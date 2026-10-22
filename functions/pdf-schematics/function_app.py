import base64
import json
import logging
import os
import re
import time
import unicodedata
import uuid
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from urllib.parse import urlparse

import azure.functions as func
import fitz  # PyMuPDF
import requests
from anthropic import AnthropicFoundry
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    FileSearchToolboxTool,
)
from azure.core import MatchConditions
from azure.core.exceptions import (
    HttpResponseError,
    ResourceExistsError,
    ResourceModifiedError,
    ResourceNotFoundError,
)
from azure.identity import DefaultAzureCredential
from azure.mgmt.cognitiveservices import CognitiveServicesManagementClient
from azure.storage.blob import (
    BlobServiceClient,
    ContentSettings,
)
from PIL import Image

# ============================================================
# CONFIGURATION
# ============================================================

app = func.FunctionApp()

STORAGE_CONNECTION = os.environ["AzureWebJobsStorage"]
AZURE_SUBSCRIPTION_ID = os.environ["AZURE_SUBSCRIPTION_ID"]
FOUNDRY_RESOURCE_GROUP = os.environ["FOUNDRY_RESOURCE_GROUP"]
FOUNDRY_RESOURCE_NAME = os.environ["FOUNDRY_RESOURCE_NAME"]
FOUNDRY_LOCATION = os.environ["FOUNDRY_LOCATION"]

SCHEMATICS_CONTAINER = "documents"
PDF_INGESTION_QUEUE = "pdf-ingestion"
PDF_INGESTION_BATCH_SIZE = 10
PAGE_MAP_GENERATOR_NAME = "native-pdf-page-map"
PAGE_MAP_GENERATOR_VERSION = 1
PAGE_MAP_EDGE_RATIO = 0.15
PAGE_MAP_MIN_SEQUENCE_LENGTH = 3

_VECTOR_STORE_LOCK_STALE_SECONDS = 600
_VECTOR_STORE_LOCK_POLL_SECONDS = 1
_VECTOR_STORE_LOCK_WAIT_TIMEOUT_SECONDS = 30

_TOOLBOX_LOCK_STALE_SECONDS = 120
_TOOLBOX_LOCK_POLL_SECONDS = 1
_TOOLBOX_LOCK_WAIT_TIMEOUT_SECONDS = 30

_FILE_SEARCH_LOCK_STALE_SECONDS = 600
_FILE_SEARCH_LOCK_POLL_SECONDS = 1
_FILE_SEARCH_LOCK_WAIT_TIMEOUT_SECONDS = 60

FILE_SEARCH_MAX_RESULTS = 5

# Résolution des images utilisées par Claude
FULL_PAGE_DPI = 195
TILE_DPI = 390

# Découpage
GRID_ROWS = 3
GRID_COLS = 3
OVERLAP_RATIO = 0.25

# Cadrage : détection de la zone de contenu réelle
# (pour retirer les marges blanches/vides autour du plan)
CONTENT_DETECTION_DPI = 150
CONTENT_WHITE_THRESHOLD = 245
CONTENT_PADDING_RATIO = 0.01

# Tuile considérée comme vide si moins de 0.1% de ses pixels
# ne sont pas blancs (seuil bas : mieux vaut garder une tuile
# avec juste un repère/numéro de borne que la perdre)
TILE_MIN_CONTENT_RATIO = 0.001

# ============================================================
# UTILITAIRES
# ============================================================


def safe_name(name: str) -> str:
    """
    Transforme un nom de fichier en identifiant propre.
    """

    stem = Path(name).stem.lower()

    stem = re.sub(r"[^\w\-]+", "-", stem, flags=re.UNICODE)
    stem = re.sub(r"-+", "-", stem)

    return stem.strip("-")


def ascii_safe_metadata_value(value: str) -> str:
    """Réduit une valeur en ASCII pur pour les métadonnées Azure Blob."""
    normalized = unicodedata.normalize("NFKD", value)
    return normalized.encode("ascii", "ignore").decode("ascii")


def build_page_asset_prefix(document_id: str, page_number: int) -> str:
    """
    Préfixe stable pour tous les fichiers générés d'une page.
    """

    return f"{document_id}_page-{page_number:05d}"


def parse_blob_path(blob_name: str) -> tuple[str, str, str]:
    """
    Extrait le chemin de blob attendu : documents/{entreprise}/{machine}/{fichier}.
    """

    blob_path = str(blob_name).strip()
    if not blob_path:
        raise ValueError("Chemin de blob vide.")

    pure_path = PurePosixPath(blob_path)
    parts = list(pure_path.parts)

    if parts and parts[0] == SCHEMATICS_CONTAINER:
        parts = parts[1:]

    if len(parts) != 3:
        raise ValueError(f"Chemin de blob invalide : {blob_name!r}")

    entreprise, machine, file_name = parts
    if not entreprise or not machine or not file_name:
        raise ValueError(f"Chemin de blob invalide : {blob_name!r}")

    return entreprise, machine, file_name


def normalize_agent_name(machine_name: str) -> str:
    """Normalise le nom de l'agent machine en un nom Foundry valide."""

    base_name = f"Assistant-Technique-{machine_name}"
    normalized = re.sub(r"[^A-Za-z0-9-]+", "-", base_name)
    normalized = re.sub(r"-+", "-", normalized).strip("-")
    normalized = normalized[:63].strip("-")

    while normalized and not normalized[0].isalnum():
        normalized = normalized[1:]
    while normalized and not normalized[-1].isalnum():
        normalized = normalized[:-1]

    if not normalized:
        normalized = "Assistant-Technique"

    return normalized


def normalize_vector_store_name(machine: str) -> str:
    """Normalise le nom du vector store machine de façon déterministe."""

    base_name = f"VS-{machine}"
    normalized = re.sub(r"[^A-Za-z0-9-]+", "-", base_name)
    normalized = re.sub(r"-+", "-", normalized).strip("-")
    return normalized


def normalize_toolbox_name(machine: str) -> str:
    """Normalise le nom de la Toolbox File Search d'une machine."""

    base_name = f"TB-{machine}"
    normalized = re.sub(r"[^A-Za-z0-9-]+", "-", base_name)
    normalized = re.sub(r"-+", "-", normalized).strip("-")
    normalized = normalized[:63].strip("-")
    return normalized or "TB"


def build_full_image_name(document_id: str, page_number: int) -> str:
    return f"{build_page_asset_prefix(document_id, page_number)}-full.png"


def build_tile_image_name(
    document_id: str, page_number: int, row: int, col: int
) -> str:
    return (
        f"{build_page_asset_prefix(document_id, page_number)}"
        f"-tile-r{row:02d}-c{col:02d}.png"
    )


def build_description_file_name(document_id: str, page_number: int) -> str:
    return f"{build_page_asset_prefix(document_id, page_number)}-description.txt"


def upload_blob(
    container_client,
    blob_name: str,
    data: bytes,
    content_type: str,
    metadata: dict = None,
):
    """
    Envoie ou remplace un blob.
    """

    container_client.upload_blob(
        name=blob_name,
        data=data,
        overwrite=True,
        content_settings=ContentSettings(content_type=content_type),
        metadata=metadata,
    )


def try_acquire_blob_lock(lock_blob_client) -> bool:
    """Tente d'obtenir un lock de type blob atomique."""

    try:
        lock_blob_client.upload_blob(data=b"", overwrite=False)
        return True
    except ResourceExistsError:
        return False
    except Exception:
        logging.exception(
            "Impossible d'acquérir le lock blob %s.", lock_blob_client.blob_name
        )
        return False


def release_blob_lock(lock_blob_client) -> None:
    """Supprime le lock blob si présent."""

    try:
        lock_blob_client.delete_blob()
    except Exception:
        logging.debug(
            "Suppression du lock blob ignorée : %s", lock_blob_client.blob_name
        )


def reclaim_stale_blob_lock_and_acquire(lock_blob_client, stale_seconds: int) -> bool:
    """Supprime un lock expiré puis tente un nouvel acquisition atomique."""

    try:
        properties = lock_blob_client.get_blob_properties()
        last_modified = getattr(properties, "last_modified", None)
        if last_modified is not None:
            age_seconds = (
                datetime.now(timezone.utc) - last_modified.astimezone(timezone.utc)
            ).total_seconds()
            if age_seconds > stale_seconds:
                logging.warning(
                    "Lock blob expiré détecté pour %s ; tentative de récupération.",
                    lock_blob_client.blob_name,
                )
                try:
                    lock_blob_client.delete_blob()
                except Exception:
                    pass
    except ResourceNotFoundError:
        pass
    except Exception:
        logging.exception(
            "Vérification du lock blob %s impossible.", lock_blob_client.blob_name
        )

    return try_acquire_blob_lock(lock_blob_client)


def run_with_blob_lock(
    lock_blob_client,
    marker_check,
    action,
    *,
    stale_seconds,
    poll_seconds,
    timeout_seconds,
    timeout_message,
):
    """Exécute action() sous lock blob avec poll/stale timeout."""

    deadline = time.time() + timeout_seconds

    while True:
        try:
            if marker_check():
                return True
        except Exception:
            logging.exception("Échec de la vérification du marker pendant le lock.")

        if time.time() >= deadline:
            raise TimeoutError(timeout_message)

        if reclaim_stale_blob_lock_and_acquire(lock_blob_client, stale_seconds):
            try:
                if marker_check():
                    return True
                action()
                return True
            finally:
                release_blob_lock(lock_blob_client)

        if time.time() >= deadline:
            raise TimeoutError(timeout_message)

        time.sleep(poll_seconds)


def build_description_prompt(
    stage: str,
    has_extractable_text: bool,
    available_tile_ids: list[str] | None = None,
) -> str:
    """Construit le prompt d'analyse adaptative d'une page technique."""

    if stage == "final":
        return """À partir de ton analyse initiale de la page complète et des tuiles demandées, produis maintenant une seule description technique finale.

La description doit être complète, précise, structurée pour File Search et sans répétitions inutiles. Intègre les informations confirmées ou complétées par les tuiles sans mentionner le processus d'analyse ni les demandes de tuiles. Ne supprime aucune référence, valeur, unité, connexion ou relation technique utile. N'invente rien ; indique explicitement ce qui demeure incertain ou illisible. Ne demande aucune autre tuile."""

    if stage != "initial":
        raise ValueError(f"Étape de description non prise en charge : {stage}")

    tile_ids = available_tile_ids or []
    available_tiles = ", ".join(tile_ids) if tile_ids else "aucune tuile stockée"
    if has_extractable_text:
        mode_instructions = """Du texte est extractible depuis le PDF original et sera indexé séparément. Ne retranscris donc pas inutilement ses paragraphes.

Décris de manière exhaustive les tableaux, schémas électriques, hydrauliques ou pneumatiques, vues éclatées, illustrations, références, composants, connexions, relations et autres informations techniques visuelles. Extrais aussi fidèlement les textes intégrés aux images ou schémas qui risquent de ne pas être accessibles par l'extraction PDF."""
    else:
        mode_instructions = """Aucun texte exploitable n'est extractible depuis le PDF original. Extrais fidèlement l'ensemble des textes visibles, consignes, procédures, tableaux, références, valeurs et unités.

Décris également tous les éléments graphiques et techniques avec le même niveau de précision : schémas électriques, hydrauliques ou pneumatiques, vues éclatées, illustrations, composants, connexions et relations."""

    return f"""Analyse minutieusement l'image FULL de cette page de documentation technique.

{mode_instructions}

La page existe aussi sous la forme d'une grille logique de 3 × 3 tuiles haute résolution avec chevauchement. Les identifiants suivent le format `r01-c01` à `r03-c03`. Tuiles effectivement disponibles pour cette page : {available_tiles}.

Commence par examiner toute la FULL afin de comprendre le contexte général. Recherche aussi les détails techniques importants qui pourraient passer inaperçus à l'échelle globale. Si un détail est ambigu, difficilement lisible ou nécessite une confirmation, appelle l'outil `request_page_tiles` une seule fois avec uniquement la ou les tuiles nécessaires. Évite d'en demander plusieurs lorsqu'une seule suffit. Lorsqu'un schéma ou un trajet traverse plusieurs zones, demande seulement les tuiles indispensables.

Si aucune tuile n'est nécessaire, n'appelle pas l'outil et fournis directement une unique description technique finale, complète, précise, structurée pour File Search et sans répétitions inutiles. Ne supprime aucune référence, valeur, unité, connexion ou relation technique utile.

Décris exclusivement les informations présentes dans le document. N'invente aucune connexion, relation causale, explication ou conclusion technique. En cas d'ambiguïté, vérifie les tuiles disponibles ; si le doute persiste, signale-le sans faire de supposition. Conserve fidèlement les valeurs et références contradictoires."""


def describe_image_with_claude(
    messages: list[dict],
    image_name: str,
    *,
    tools: list[dict] | None = None,
    tool_choice: dict | None = None,
    max_tokens: int = 8192,
):
    """
    Exécute un tour d'analyse Claude et retourne la réponse Anthropic complète.
    """

    try:
        client = AnthropicFoundry(
            api_key=os.environ["FOUNDRY_ANTHROPIC_API_KEY"],
            base_url=os.environ["FOUNDRY_ANTHROPIC_ENDPOINT"],
        )
        model = os.environ["FOUNDRY_CLAUDE_DEPLOYMENT"]

        request = dict(
            model=model,
            max_tokens=max_tokens,
            messages=messages,
        )
        if tools is not None:
            request["tools"] = tools
        if tool_choice is not None:
            request["tool_choice"] = tool_choice

        return client.messages.create(**request)

    except (KeyError, ValueError, TypeError):
        logging.exception(
            "Configuration ou réponse Claude invalide pour l'image %s.",
            image_name,
        )
    except Exception:
        logging.exception("Échec de la description Claude pour l'image %s.", image_name)

    return None


def log_claude_vision_call(
    response,
    document_id: str,
    page_number: int,
    call_name: str,
    duration_seconds: float,
) -> dict[str, int]:
    """Journalise les métriques non sensibles d'un appel Claude Vision."""

    usage = getattr(response, "usage", None)

    def token_count(field_name: str) -> int:
        value = getattr(usage, field_name, 0) if usage is not None else 0
        return value if isinstance(value, int) else 0

    metrics = {
        "input_tokens": token_count("input_tokens"),
        "output_tokens": token_count("output_tokens"),
        "cache_creation_input_tokens": token_count(
            "cache_creation_input_tokens"
        ),
        "cache_read_input_tokens": token_count("cache_read_input_tokens"),
    }

    logging.info(
        "Claude Vision : document=%s page=%s appel=%s durée_secondes=%.3f "
        "input_tokens=%s output_tokens=%s cache_creation_input_tokens=%s "
        "cache_read_input_tokens=%s",
        document_id,
        page_number,
        call_name,
        duration_seconds,
        metrics["input_tokens"],
        metrics["output_tokens"],
        metrics["cache_creation_input_tokens"],
        metrics["cache_read_input_tokens"],
    )
    return metrics


def assemble_page_description(
    document_id: str,
    page_number: int,
    full_image: dict,
    uploaded_tiles: list[dict],
) -> str:
    """
    Assemble le fichier texte de descriptions d'une page.
    """

    image_records = [full_image, *uploaded_tiles]
    lines = [
        f"Document ID: {document_id}",
        f"Page number: {page_number}",
        "",
    ]

    for image in image_records:
        lines.extend(
            [
                f"PNG source: {image['name']}",
                f"Type: {image['kind']}",
                (
                    f"Tile row: {image['row']}"
                    if image["kind"] == "tile"
                    else "Tile row: not applicable"
                ),
                (
                    f"Tile column: {image['column']}"
                    if image["kind"] == "tile"
                    else "Tile column: not applicable"
                ),
                f"Blob path: {image['blob_path']}",
                "Claude description:",
                image["description"]
                or "Description indisponible : l'analyse Claude a échoué pour cette image.",
                "",
            ]
        )

    return "\n".join(lines)


def wait_for_foundry_project_ready(
    project_endpoint: str,
    timeout_seconds: int = 60,
    poll_seconds: int = 2,
) -> None:
    """Attend la propagation data-plane d'un projet Foundry tout juste créé côté ARM."""

    deadline = time.time() + timeout_seconds
    project_client = AIProjectClient(
        endpoint=project_endpoint,
        credential=DefaultAzureCredential(),
    )

    try:
        while True:
            try:
                # Sonde au niveau projet (jamais un agent) : un 404 ici signifie
                # que le projet lui-même est introuvable côté data-plane.
                next(iter(project_client.connections.list()), None)
                logging.info(
                    "Projet Foundry disponible dans le data-plane: %s",
                    project_endpoint,
                )
                return
            except ResourceNotFoundError:
                if time.time() >= deadline:
                    raise TimeoutError(
                        f"Le projet Foundry {project_endpoint} n'est pas devenu disponible "
                        f"dans le data-plane après {timeout_seconds} secondes."
                    )
                logging.info(
                    "Projet Foundry pas encore disponible — nouvelle tentative..."
                )
                time.sleep(poll_seconds)
    finally:
        project_client.close()


def get_or_create_foundry_project(entreprise: str) -> str:
    """Récupère ou crée le sous-projet Foundry scalaire de l'entreprise."""

    if not (2 <= len(entreprise) <= 64):
        raise ValueError(f"Nom de projet Foundry invalide : {entreprise!r}")
    if not entreprise[0].isalnum():
        raise ValueError(
            f"Le projet Foundry doit commencer par un caractère alphanumérique : {entreprise!r}"
        )
    if any(not (ch.isalnum() or ch in "_.-") for ch in entreprise[1:]):
        raise ValueError(f"Nom de projet Foundry invalide : {entreprise!r}")

    client = CognitiveServicesManagementClient(
        credential=DefaultAzureCredential(),
        subscription_id=AZURE_SUBSCRIPTION_ID,
    )

    project_endpoint = f"https://{FOUNDRY_RESOURCE_NAME}.services.ai.azure.com/api/projects/{entreprise}"
    project_just_created = False

    try:
        try:
            client.projects.get(
                resource_group_name=FOUNDRY_RESOURCE_GROUP,
                account_name=FOUNDRY_RESOURCE_NAME,
                project_name=entreprise,
            )
            logging.info("Projet Foundry existant: %s", entreprise)
        except ResourceNotFoundError:
            try:
                client.projects.begin_create(
                    resource_group_name=FOUNDRY_RESOURCE_GROUP,
                    account_name=FOUNDRY_RESOURCE_NAME,
                    project_name=entreprise,
                    project={
                        "location": FOUNDRY_LOCATION,
                        "identity": {"type": "SystemAssigned"},
                        "properties": {},
                    },
                ).result()
                logging.info("Projet Foundry créé: %s", entreprise)
                project_just_created = True
            except ResourceExistsError:
                logging.warning(
                    "Conflit de création projet Foundry pour %s, relecture en cours.",
                    entreprise,
                )
                client.projects.get(
                    resource_group_name=FOUNDRY_RESOURCE_GROUP,
                    account_name=FOUNDRY_RESOURCE_NAME,
                    project_name=entreprise,
                )
            except HttpResponseError:
                logging.exception(
                    "Échec de création du sous-projet Foundry %s.", entreprise
                )
                raise
    finally:
        client.close()

    if project_just_created:
        wait_for_foundry_project_ready(project_endpoint)

    return project_endpoint


def finalize_machine_claude_direct_config(
    entreprise: str,
    machine: str,
    project_endpoint: str,
    vector_store_id: str,
) -> None:
    """Finalise la machine via l'API DiagLink authentifiée par identité managée."""

    blob_prefix = f"{entreprise}/{machine}"
    base_url = os.environ.get("DIAGLINK_BACKEND_BASE_URL", "").strip().rstrip("/")
    audience = os.environ.get("DIAGLINK_BACKEND_AUDIENCE", "").strip().rstrip("/")
    parsed_base_url = urlparse(base_url)
    if (
        parsed_base_url.scheme != "https"
        or not parsed_base_url.netloc
        or parsed_base_url.path not in {"", "/"}
        or parsed_base_url.params
        or parsed_base_url.query
        or parsed_base_url.fragment
    ):
        raise RuntimeError("DIAGLINK_BACKEND_BASE_URL doit être une origine HTTPS.")
    if not audience.startswith("api://") or any(character.isspace() for character in audience):
        raise RuntimeError("DIAGLINK_BACKEND_AUDIENCE est invalide.")

    credential = DefaultAzureCredential()
    try:
        access_token = credential.get_token(f"{audience}/.default").token
    finally:
        credential.close()

    url = f"{base_url}/api/internal/document-ingestion/finalize"
    payload = {
        "blobPrefix": blob_prefix,
        "projectEndpoint": project_endpoint,
        "vectorStoreId": vector_store_id,
    }
    headers = {
        "Authorization": f"Bearer {access_token}",
        "Content-Type": "application/json",
    }
    transient_statuses = {408, 425, 429, 500, 502, 503, 504}

    for attempt in range(1, 4):
        try:
            response = requests.post(
                url,
                json=payload,
                headers=headers,
                timeout=(5, 30),
            )
        except requests.RequestException as exc:
            if attempt == 3:
                raise RuntimeError(
                    "La finalisation DiagLink est inaccessible après trois tentatives."
                ) from exc
            time.sleep(2 ** (attempt - 1))
            continue

        if response.status_code == 200:
            try:
                status = response.json().get("status")
            except (ValueError, AttributeError) as exc:
                raise RuntimeError(
                    "La réponse de finalisation DiagLink est invalide."
                ) from exc
            if status not in {"updated", "already_finalized"}:
                raise RuntimeError(
                    "La réponse de finalisation DiagLink contient un statut inattendu."
                )
            logging.info("Finalisation DiagLink confirmée (statut=%s).", status)
            return

        if response.status_code in transient_statuses and attempt < 3:
            time.sleep(2 ** (attempt - 1))
            continue

        raise RuntimeError(
            "La finalisation DiagLink a échoué "
            f"(HTTP {response.status_code}, tentative {attempt}/3)."
        )

    raise RuntimeError("La finalisation DiagLink n'a pas été confirmée.")


def get_or_create_machine_vector_store(
    project_endpoint: str,
    entreprise: str,
    machine: str,
) -> str:
    """Récupère ou crée le vector store machine dédié et le marque dans Blob Storage."""

    vector_store_name = normalize_vector_store_name(machine)
    marker_blob_path = f"{entreprise}/{machine}/.foundry/vector-store.json"
    lock_blob_path = f"{entreprise}/{machine}/.foundry/vector-store.lock"

    container_client = BlobServiceClient.from_connection_string(
        STORAGE_CONNECTION
    ).get_container_client(SCHEMATICS_CONTAINER)
    marker_client = container_client.get_blob_client(marker_blob_path)
    lock_client = container_client.get_blob_client(lock_blob_path)

    try:
        try:
            marker_bytes = marker_client.download_blob().readall()
            marker_payload = json.loads(marker_bytes.decode("utf-8"))
            vector_store_id = marker_payload.get("vector_store_id")
            if isinstance(vector_store_id, str) and vector_store_id.startswith("vs_"):
                logging.info(
                    "Vector store déjà marqué pour %s/%s: %s",
                    entreprise,
                    machine,
                    vector_store_id,
                )
                return vector_store_id
        except (ResourceNotFoundError, ValueError, TypeError, json.JSONDecodeError):
            pass

        project_client = AIProjectClient(
            endpoint=project_endpoint,
            credential=DefaultAzureCredential(),
        )

        def marker_ready() -> bool:
            try:
                marker_bytes = marker_client.download_blob().readall()
                marker_payload = json.loads(marker_bytes.decode("utf-8"))
                vector_store_id = marker_payload.get("vector_store_id")
                return isinstance(vector_store_id, str) and vector_store_id.startswith(
                    "vs_"
                )
            except Exception:
                return False

        def create_vector_store():
            openai_client = project_client.get_openai_client()
            try:
                existing_vector_stores = list(openai_client.vector_stores.list())
                for candidate in existing_vector_stores:
                    if getattr(candidate, "name", None) == vector_store_name:
                        vector_store_id = getattr(candidate, "id", None)
                        if vector_store_id and str(vector_store_id).startswith("vs_"):
                            marker_client.upload_blob(
                                json.dumps(
                                    {
                                        "vector_store_id": vector_store_id,
                                        "vector_store_name": vector_store_name,
                                    }
                                ).encode("utf-8"),
                                overwrite=True,
                                content_settings=ContentSettings(
                                    content_type="application/json"
                                ),
                            )
                            return vector_store_id
                created_store = openai_client.vector_stores.create(
                    name=vector_store_name
                )
                vector_store_id = getattr(created_store, "id", None)
                if not vector_store_id or not str(vector_store_id).startswith("vs_"):
                    raise ValueError(
                        f"Vector store sans identifiant valide pour {machine!r}."
                    )
                marker_client.upload_blob(
                    json.dumps(
                        {
                            "vector_store_id": vector_store_id,
                            "vector_store_name": vector_store_name,
                        }
                    ).encode("utf-8"),
                    overwrite=True,
                    content_settings=ContentSettings(content_type="application/json"),
                )
                return vector_store_id
            finally:
                openai_client.close()

        try:
            run_with_blob_lock(
                lock_client,
                marker_ready,
                create_vector_store,
                stale_seconds=_VECTOR_STORE_LOCK_STALE_SECONDS,
                poll_seconds=_VECTOR_STORE_LOCK_POLL_SECONDS,
                timeout_seconds=_VECTOR_STORE_LOCK_WAIT_TIMEOUT_SECONDS,
                timeout_message=(
                    f"Timeout attente vector store machine pour {entreprise}/{machine}."
                ),
            )
            marker_bytes = marker_client.download_blob().readall()
            marker_payload = json.loads(marker_bytes.decode("utf-8"))
            vector_store_id = marker_payload.get("vector_store_id")
            if not isinstance(vector_store_id, str) or not vector_store_id.startswith(
                "vs_"
            ):
                raise ValueError(
                    f"Vector store machine invalide après création pour {entreprise}/{machine}."
                )
            logging.info("Vector store Foundry prêt: %s", vector_store_id)
            return vector_store_id
        finally:
            project_client.close()
    finally:
        try:
            container_client.close()
        except Exception:
            pass


def get_or_create_machine_toolbox(
    project_endpoint: str,
    entreprise: str,
    machine: str,
    vector_store_id: str,
) -> dict[str, str]:
    """Récupère ou crée la Toolbox File Search liée au vector store machine."""

    if not vector_store_id or not vector_store_id.startswith("vs_"):
        raise ValueError(
            "vector_store_id invalide pour la Toolbox : doit commencer par 'vs_'."
        )

    toolbox_name = normalize_toolbox_name(machine)
    marker_blob_path = f"{entreprise}/{machine}/.foundry/toolbox.json"
    lock_blob_path = f"{entreprise}/{machine}/.foundry/toolbox.lock"

    container_client = BlobServiceClient.from_connection_string(
        STORAGE_CONNECTION
    ).get_container_client(SCHEMATICS_CONTAINER)
    marker_client = container_client.get_blob_client(marker_blob_path)
    lock_client = container_client.get_blob_client(lock_blob_path)
    project_client = AIProjectClient(
        endpoint=project_endpoint,
        credential=DefaultAzureCredential(),
    )
    result: dict[str, str] = {}

    def build_toolbox_payload(version: str) -> dict[str, str]:
        version = str(version)
        return {
            "toolbox_name": toolbox_name,
            "toolbox_version": version,
            "vector_store_id": vector_store_id,
            "mcp_endpoint": (
                f"{project_endpoint.rstrip('/')}/toolboxes/{toolbox_name}/"
                f"versions/{version}/mcp?api-version=v1"
            ),
        }

    def toolbox_version_matches(toolbox_version) -> bool:
        tools = list(getattr(toolbox_version, "tools", []) or [])
        if len(tools) != 1:
            return False

        tool = tools[0]

        return (
            (
                isinstance(tool, FileSearchToolboxTool)
                or getattr(tool, "type", None) == "file_search"
            )
            and [str(item) for item in (getattr(tool, "vector_store_ids", []) or [])]
            == [vector_store_id]
            and getattr(tool, "max_num_results", None) == FILE_SEARCH_MAX_RESULTS
        )

    def read_marker() -> dict | None:
        try:
            marker_bytes = marker_client.download_blob().readall()
            payload = json.loads(marker_bytes.decode("utf-8"))
        except (ResourceNotFoundError, ValueError, TypeError, json.JSONDecodeError):
            return None

        if not isinstance(payload, dict):
            return None
        if (
            payload.get("toolbox_name") != toolbox_name
            or payload.get("vector_store_id") != vector_store_id
            or not str(payload.get("toolbox_version", "")).strip()
        ):
            return None
        return payload

    def save_marker(payload: dict[str, str]) -> None:
        marker_client.upload_blob(
            json.dumps(payload, ensure_ascii=False, indent=2).encode("utf-8"),
            overwrite=True,
            content_settings=ContentSettings(content_type="application/json"),
        )

    def reuse_marker_if_valid() -> bool:
        payload = read_marker()
        if payload is None:
            return False

        version = str(payload["toolbox_version"])
        try:
            toolbox_version = project_client.toolboxes.get_version(
                name=toolbox_name,
                version=version,
            )
        except ResourceNotFoundError:
            return False

        if not toolbox_version_matches(toolbox_version):
            return False

        result.update(build_toolbox_payload(version))
        logging.info(
            "[Foundry] Toolbox réutilisée - entreprise=%s machine=%s "
            "name=%s version=%s vector_store_id=%s mcp_endpoint=%s",
            entreprise,
            machine,
            toolbox_name,
            version,
            vector_store_id,
            result["mcp_endpoint"],
        )
        return True

    def find_or_create_toolbox_version() -> None:
        try:
            versions = project_client.toolboxes.list_versions(name=toolbox_name)
            for candidate in versions:
                if toolbox_version_matches(candidate):
                    version = str(getattr(candidate, "version", "")).strip()
                    if not version:
                        continue
                    payload = build_toolbox_payload(version)
                    save_marker(payload)
                    result.update(payload)
                    logging.info(
                        "[Foundry] Toolbox trouvée - entreprise=%s machine=%s "
                        "name=%s version=%s vector_store_id=%s mcp_endpoint=%s",
                        entreprise,
                        machine,
                        toolbox_name,
                        version,
                        vector_store_id,
                        payload["mcp_endpoint"],
                    )
                    return
        except ResourceNotFoundError:
            pass

        created_toolbox = project_client.toolboxes.create_version(
            name=toolbox_name,
            description=f"File Search technique pour la machine {machine}",
            tools=[
                FileSearchToolboxTool(
                    vector_store_ids=[vector_store_id],
                    max_num_results=FILE_SEARCH_MAX_RESULTS,
                )
            ],
        )
        version = str(getattr(created_toolbox, "version", "")).strip()
        if not version:
            raise ValueError(f"Toolbox {toolbox_name!r} créée sans version valide.")

        payload = build_toolbox_payload(version)
        save_marker(payload)
        result.update(payload)
        logging.info(
            "[Foundry] Toolbox créée - entreprise=%s machine=%s "
            "name=%s version=%s vector_store_id=%s mcp_endpoint=%s",
            entreprise,
            machine,
            toolbox_name,
            version,
            vector_store_id,
            payload["mcp_endpoint"],
        )

    try:
        run_with_blob_lock(
            lock_client,
            reuse_marker_if_valid,
            find_or_create_toolbox_version,
            stale_seconds=_TOOLBOX_LOCK_STALE_SECONDS,
            poll_seconds=_TOOLBOX_LOCK_POLL_SECONDS,
            timeout_seconds=_TOOLBOX_LOCK_WAIT_TIMEOUT_SECONDS,
            timeout_message=(
                f"Timeout création Toolbox machine pour {entreprise}/{machine}."
            ),
        )

        if not result and not reuse_marker_if_valid():
            raise RuntimeError(
                f"Toolbox introuvable après verrouillage pour {entreprise}/{machine}."
            )
        return result
    finally:
        project_client.close()
        try:
            container_client.close()
        except Exception:
            pass


def upload_file_to_file_search(
    file_bytes: bytes,
    file_name: str,
    content_type: str,
    project_endpoint: str,
    vector_store_id: str,
) -> str | None:
    """Ajoute un fichier au vector store Foundry machine et attend son indexation."""

    project_client = None

    try:
        if not vector_store_id or not vector_store_id.startswith("vs_"):
            raise ValueError(
                "vector_store_id invalide pour l'upload File Search : doit commencer par 'vs_'."
            )

        project_client = AIProjectClient(
            endpoint=project_endpoint,
            credential=DefaultAzureCredential(),
        )
        openai_client = project_client.get_openai_client()
        openai_client.vector_stores.retrieve(vector_store_id)

        logging.info(
            "Upload File Search vers vector store machine: %s",
            vector_store_id,
        )

        vector_store_file = openai_client.vector_stores.files.upload_and_poll(
            vector_store_id=vector_store_id,
            file=(file_name, file_bytes, content_type),
        )

        file_id = getattr(vector_store_file, "id", None)
        status = getattr(vector_store_file, "status", None)

        if str(status).lower() != "completed" or not file_id:
            logging.error(
                "File Search non terminé avec succès pour %s : file_id=%s, statut=%s.",
                file_name,
                file_id,
                status,
            )
            return None

        logging.info(
            "Fichier indexé dans File Search : file_id=%s, nom=%s, statut=%s",
            file_id,
            file_name,
            status,
        )

        return file_id

    except ValueError:
        logging.exception(
            "Configuration File Search invalide pour le fichier %s.",
            file_name,
        )
    except Exception:
        logging.exception(
            "Échec de l'upload File Search pour le fichier %s ; le blob est conservé.",
            file_name,
        )
    finally:
        if project_client is not None:
            project_client.close()

    return None


def index_file_in_file_search_once(
    container_client,
    marker_blob_path: str,
    load_file_bytes,
    file_name: str,
    content_type: str,
    project_endpoint: str,
    vector_store_id: str,
) -> str | None:
    """Indexe un fichier dans File Search une seule fois par marker JSON et par vector store."""

    lock_blob_path = f"{marker_blob_path}.lock"
    lock_blob_client = container_client.get_blob_client(lock_blob_path)
    marker_client = container_client.get_blob_client(marker_blob_path)

    def marker_ready() -> str | None:
        try:
            payload_bytes = marker_client.download_blob().readall()
            payload_text = payload_bytes.decode("utf-8", errors="replace").strip()
        except ResourceNotFoundError:
            return None

        if payload_text in {"indexed", ""}:
            return None

        try:
            payload = json.loads(payload_text)
        except (TypeError, ValueError, json.JSONDecodeError):
            return None

        if not isinstance(payload, dict):
            return None

        file_search_id = payload.get("file_search_id")
        stored_vector_store_id = payload.get("vector_store_id")
        if (
            stored_vector_store_id == vector_store_id
            and isinstance(file_search_id, str)
            and bool(file_search_id.strip())
        ):
            return file_search_id
        return None

    def upload_once() -> str | None:
        existing = marker_ready()
        if existing:
            return existing

        file_bytes = load_file_bytes()
        file_search_id = upload_file_to_file_search(
            file_bytes=file_bytes,
            file_name=file_name,
            content_type=content_type,
            project_endpoint=project_endpoint,
            vector_store_id=vector_store_id,
        )

        if not file_search_id:
            return None

        marker_client.upload_blob(
            json.dumps(
                {
                    "file_search_id": file_search_id,
                    "vector_store_id": vector_store_id,
                }
            ).encode("utf-8"),
            overwrite=True,
            content_settings=ContentSettings(content_type="application/json"),
        )
        return file_search_id

    try:
        current = marker_ready()
        if current:
            logging.info(
                "Marker File Search déjà présent pour %s : %s",
                marker_blob_path,
                current,
            )
            return current

        run_with_blob_lock(
            lock_blob_client,
            marker_ready,
            upload_once,
            stale_seconds=_FILE_SEARCH_LOCK_STALE_SECONDS,
            poll_seconds=_FILE_SEARCH_LOCK_POLL_SECONDS,
            timeout_seconds=_FILE_SEARCH_LOCK_WAIT_TIMEOUT_SECONDS,
            timeout_message=(
                f"Timeout File Search sur {marker_blob_path} pour vector store {vector_store_id}."
            ),
        )
        return marker_ready()
    finally:
        try:
            lock_blob_client.delete_blob()
        except Exception:
            pass


def generate_page_description(
    container_client,
    document_id: str,
    page_number: int,
    page_folder: str,
    full_image: dict,
    uploaded_tiles: list[dict],
    has_extractable_text: bool,
):
    """
    Analyse la FULL, fournit à la demande les tuiles utiles, puis enregistre un seul texte.
    Retourne le chemin du fichier description généré.
    """

    mode = (
        "texte PDF extractible"
        if has_extractable_text
        else "texte PDF non extractible"
    )
    logging.info(
        "Claude Vision : document=%s page=%s mode=%s",
        document_id,
        page_number,
        mode,
    )
    page_usage = {
        "input_tokens": 0,
        "output_tokens": 0,
        "cache_creation_input_tokens": 0,
        "cache_read_input_tokens": 0,
    }
    claude_call_count = 0

    tile_by_id = {
        f"r{tile['row']:02d}-c{tile['column']:02d}": tile
        for tile in uploaded_tiles
    }
    available_tile_ids = sorted(tile_by_id)
    initial_message = {
        "role": "user",
        "content": [
            {
                "type": "image",
                "source": {
                    "type": "base64",
                    "media_type": "image/png",
                    "data": base64.b64encode(full_image["bytes"]).decode("ascii"),
                },
            },
            {
                "type": "text",
                "text": build_description_prompt(
                    "initial", has_extractable_text, available_tile_ids
                ),
            },
        ],
    }
    tools = None
    if available_tile_ids:
        tools = [
            {
                "name": "request_page_tiles",
                "description": (
                    "Demande en une seule fois les tuiles haute résolution strictement "
                    "nécessaires pour vérifier ou compléter l'analyse de la page FULL."
                ),
                "input_schema": {
                    "type": "object",
                    "properties": {
                        "tiles": {
                            "type": "array",
                            "items": {
                                "type": "string",
                                "enum": available_tile_ids,
                            },
                            "minItems": 1,
                            "uniqueItems": True,
                            "description": (
                                "Identifiants des tuiles nécessaires, par exemple r01-c01."
                            ),
                        }
                    },
                    "required": ["tiles"],
                    "additionalProperties": False,
                },
            }
        ]

    first_call_started = time.perf_counter()
    first_response = describe_image_with_claude(
        [initial_message],
        full_image["name"],
        tools=tools,
        max_tokens=8192,
    )
    first_call_duration = time.perf_counter() - first_call_started
    claude_call_count += 1
    first_usage = log_claude_vision_call(
        first_response,
        document_id,
        page_number,
        "analyse_full",
        first_call_duration,
    )
    for metric_name, metric_value in first_usage.items():
        page_usage[metric_name] += metric_value
    if first_response is None:
        raise RuntimeError(
            f"Description Claude absente pour l'image {full_image['name']}."
        )
    if getattr(first_response, "stop_reason", None) == "max_tokens":
        raise RuntimeError(
            f"Réponse Claude tronquée pour l'image {full_image['name']}."
        )

    first_content = getattr(first_response, "content", [])
    tool_uses = [
        block for block in first_content if getattr(block, "type", None) == "tool_use"
    ]

    if not tool_uses:
        logging.info(
            "Claude Vision : document=%s page=%s Aucune tuile demandée",
            document_id,
            page_number,
        )
        final_description = "\n".join(
            block.text
            for block in first_content
            if getattr(block, "type", None) == "text"
        ).strip()
    else:
        if len(tool_uses) != 1 or tool_uses[0].name != "request_page_tiles":
            raise RuntimeError("Claude a émis une demande d'outil invalide.")

        requested_tile_ids = getattr(tool_uses[0], "input", {}).get("tiles")
        if (
            not isinstance(requested_tile_ids, list)
            or not requested_tile_ids
            or any(not isinstance(tile_id, str) for tile_id in requested_tile_ids)
            or len(set(requested_tile_ids)) != len(requested_tile_ids)
            or any(tile_id not in tile_by_id for tile_id in requested_tile_ids)
        ):
            raise RuntimeError("Claude a demandé des identifiants de tuiles invalides.")

        logging.info(
            "Claude Vision : document=%s page=%s nombre_tuiles_demandées=%s "
            "tuiles_demandées=%s",
            document_id,
            page_number,
            len(requested_tile_ids),
            ",".join(requested_tile_ids),
        )

        tool_result_content = [
            {
                "type": "text",
                "text": "Tuiles haute résolution demandées, dans l'ordre indiqué : "
                + ", ".join(requested_tile_ids),
            }
        ]
        for tile_id in requested_tile_ids:
            tile = tile_by_id[tile_id]
            tool_result_content.extend(
                [
                    {"type": "text", "text": f"Tuile {tile_id} :"},
                    {
                        "type": "image",
                        "source": {
                            "type": "base64",
                            "media_type": "image/png",
                            "data": base64.b64encode(tile["bytes"]).decode("ascii"),
                        },
                    },
                ]
            )

        second_call_started = time.perf_counter()
        second_response = describe_image_with_claude(
            [
                initial_message,
                {"role": "assistant", "content": first_content},
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "tool_result",
                            "tool_use_id": tool_uses[0].id,
                            "content": tool_result_content,
                        },
                        {
                            "type": "text",
                            "text": build_description_prompt(
                                "final", has_extractable_text, available_tile_ids
                            ),
                        },
                    ],
                },
            ],
            full_image["name"],
            tools=tools,
            tool_choice={"type": "none"},
            max_tokens=8192,
        )
        second_call_duration = time.perf_counter() - second_call_started
        claude_call_count += 1
        second_usage = log_claude_vision_call(
            second_response,
            document_id,
            page_number,
            "analyse_finale_après_tuiles",
            second_call_duration,
        )
        for metric_name, metric_value in second_usage.items():
            page_usage[metric_name] += metric_value
        if second_response is None:
            raise RuntimeError(
                f"Description Claude finale absente pour l'image {full_image['name']}."
            )
        if getattr(second_response, "stop_reason", None) == "max_tokens":
            raise RuntimeError(
                f"Réponse Claude finale tronquée pour l'image {full_image['name']}."
            )
        second_content = getattr(second_response, "content", [])
        if any(
            getattr(block, "type", None) == "tool_use" for block in second_content
        ):
            raise RuntimeError("Claude a demandé des tuiles plus d'une fois.")
        final_description = "\n".join(
            block.text
            for block in second_content
            if getattr(block, "type", None) == "text"
        ).strip()

    if not final_description:
        raise RuntimeError(
            f"Description Claude finale vide pour l'image {full_image['name']}."
        )

    total_tokens = sum(page_usage.values())
    logging.info(
        "Claude Vision : document=%s page=%s nombre_appels=%s total_tokens=%s "
        "total_input_tokens=%s total_output_tokens=%s "
        "total_cache_creation_input_tokens=%s total_cache_read_input_tokens=%s",
        document_id,
        page_number,
        claude_call_count,
        total_tokens,
        page_usage["input_tokens"],
        page_usage["output_tokens"],
        page_usage["cache_creation_input_tokens"],
        page_usage["cache_read_input_tokens"],
    )

    description_path = (
        f"{page_folder}/{build_description_file_name(document_id, page_number)}"
    )
    description_text = "\n".join(
        [
            f"Document ID: {document_id}",
            f"Page number: {page_number}",
            "Native PDF text available: "
            + ("yes" if has_extractable_text else "no"),
            "",
            "Claude technical description:",
            final_description,
        ]
    )

    upload_blob(
        container_client,
        description_path,
        description_text.encode("utf-8"),
        "text/plain; charset=utf-8",
    )

    logging.info(
        "Description de page enregistrée : %s",
        description_path,
    )

    return description_path


def detect_content_rect(page):
    """
    Détecte la zone réelle du plan sur la page en excluant
    les marges blanches/vides autour du contenu.

    Rendu basse résolution en niveaux de gris, puis recherche
    de la boîte englobante des pixels plus sombres que le blanc
    (traits, texte, hachures du schéma).
    """

    page_rect = page.rect

    pix = page.get_pixmap(
        dpi=CONTENT_DETECTION_DPI,
        colorspace=fitz.csGRAY,
        alpha=False,
    )

    image = Image.frombytes("L", (pix.width, pix.height), pix.samples)

    mask = image.point(lambda value: 255 if value < CONTENT_WHITE_THRESHOLD else 0)

    bbox = mask.getbbox()

    if bbox is None:
        return page_rect

    scale = 72.0 / CONTENT_DETECTION_DPI

    x0 = page_rect.x0 + bbox[0] * scale
    y0 = page_rect.y0 + bbox[1] * scale
    x1 = page_rect.x0 + bbox[2] * scale
    y1 = page_rect.y0 + bbox[3] * scale

    pad_x = (x1 - x0) * CONTENT_PADDING_RATIO
    pad_y = (y1 - y0) * CONTENT_PADDING_RATIO

    x0 = max(page_rect.x0, x0 - pad_x)
    y0 = max(page_rect.y0, y0 - pad_y)
    x1 = min(page_rect.x1, x1 + pad_x)
    y1 = min(page_rect.y1, y1 + pad_y)

    return fitz.Rect(x0, y0, x1, y1)


# ============================================================
# DÉCOUPAGE DE PAGE
# ============================================================


def make_tiles(page, content_rect, document_id: str, page_number: int):
    """
    Découpe la zone de contenu d'une page (hors marges blanches)
    en grille 3x3 avec recouvrement.
    """

    cell_width = content_rect.width / GRID_COLS
    cell_height = content_rect.height / GRID_ROWS

    overlap_x = cell_width * OVERLAP_RATIO
    overlap_y = cell_height * OVERLAP_RATIO

    tiles = []

    for row in range(GRID_ROWS):

        for col in range(GRID_COLS):

            base_x0 = content_rect.x0 + col * cell_width
            base_y0 = content_rect.y0 + row * cell_height

            base_x1 = base_x0 + cell_width
            base_y1 = base_y0 + cell_height

            x0 = max(
                content_rect.x0,
                base_x0 - overlap_x,
            )

            y0 = max(
                content_rect.y0,
                base_y0 - overlap_y,
            )

            x1 = min(
                content_rect.x1,
                base_x1 + overlap_x,
            )

            y1 = min(
                content_rect.y1,
                base_y1 + overlap_y,
            )

            clip = fitz.Rect(
                x0,
                y0,
                x1,
                y1,
            )

            # Rend seulement la zone nécessaire
            # Rendu de la tuile à la résolution dédiée.
            pix = page.get_pixmap(
                dpi=TILE_DPI,
                clip=clip,
                alpha=False,
                colorspace=fitz.csRGB,
            )

            # Ratio de pixels non-blancs, pour détecter les tuiles vides
            gray = Image.frombytes("RGB", (pix.width, pix.height), pix.samples).convert(
                "L"
            )
            mask = gray.point(
                lambda value: 255 if value < CONTENT_WHITE_THRESHOLD else 0
            )
            content_pixels = mask.histogram()[255]
            content_ratio = content_pixels / (pix.width * pix.height)

            tile_name = build_tile_image_name(
                document_id,
                page_number,
                row + 1,
                col + 1,
            )

            normalized_bbox = [
                round(
                    (x0 - content_rect.x0) / content_rect.width,
                    6,
                ),
                round(
                    (y0 - content_rect.y0) / content_rect.height,
                    6,
                ),
                round(
                    (x1 - content_rect.x0) / content_rect.width,
                    6,
                ),
                round(
                    (y1 - content_rect.y0) / content_rect.height,
                    6,
                ),
            ]

            tiles.append(
                {
                    "name": tile_name,
                    "bytes": pix.tobytes("png"),
                    "row": row + 1,
                    "column": col + 1,
                    "bbox": normalized_bbox,
                    "width": pix.width,
                    "height": pix.height,
                    "contentRatio": round(content_ratio, 6),
                }
            )

    return tiles


# ============================================================
# AZURE FUNCTION
# ============================================================


def resolve_document_paths(blob_name: str) -> tuple[str, str, str]:
    """
    Calcule l'identifiant du document, le nom du PDF source et le
    dossier de sortie (en conservant le sous-dossier d'origine du blob).
    """

    document_id = safe_name(blob_name)
    source_pdf_name = Path(blob_name).name

    relative_blob_path = PurePosixPath(blob_name)
    if relative_blob_path.parts and relative_blob_path.parts[0] == SCHEMATICS_CONTAINER:
        relative_blob_path = relative_blob_path.relative_to(SCHEMATICS_CONTAINER)
    document_subfolder = relative_blob_path.parent.as_posix()
    document_folder = (
        document_id
        if document_subfolder in ("", ".")
        else f"{document_subfolder}/{document_id}"
    )

    return document_id, source_pdf_name, document_folder


def normalize_source_blob_name(blob_name: str) -> str:
    """Retourne le chemin du PDF relatif au conteneur documents."""

    source_path = PurePosixPath(str(blob_name).strip())
    if source_path.parts and source_path.parts[0] == SCHEMATICS_CONTAINER:
        source_path = source_path.relative_to(SCHEMATICS_CONTAINER)
    return source_path.as_posix()


def build_processing_blob_path(document_folder: str) -> str:
    return f"{document_folder}/processing.json"


def build_page_map_blob_path(document_folder: str) -> str:
    return f"{document_folder}/page-map.json"


def _roman_to_int(value: str) -> int | None:
    normalized = value.upper()
    if not re.fullmatch(
        r"M{0,4}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})",
        normalized,
    ):
        return None
    if not normalized:
        return None

    values = {"I": 1, "V": 5, "X": 10, "L": 50, "C": 100, "D": 500, "M": 1000}
    total = 0
    previous = 0
    for character in reversed(normalized):
        current = values[character]
        if current < previous:
            total -= current
        else:
            total += current
            previous = current
    return total


def _parse_page_number_candidate(value: str) -> tuple[str, str, int] | None:
    candidate = value.strip().strip("()[]{}.,;:")
    if not candidate:
        return None

    if re.fullmatch(r"\d{1,6}", candidate):
        return "arabic", "", int(candidate)

    prefixed = re.fullmatch(r"([A-Za-z]{1,8}[-./])(\d{1,6})", candidate)
    if prefixed:
        return "prefixed", prefixed.group(1).casefold(), int(prefixed.group(2))

    roman_value = _roman_to_int(candidate)
    if roman_value is not None:
        return "roman", "", roman_value
    return None


def _extract_edge_candidates(doc) -> list[list[dict]]:
    candidates_by_page = []
    for page_index in range(doc.page_count):
        page = doc.load_page(page_index)
        page_height = float(page.rect.height)
        page_candidates = []
        seen = set()

        for word in page.get_text("words", sort=True) or []:
            if len(word) < 5:
                continue
            _, y0, _, y1, raw_value = word[:5]
            zone = None
            if float(y1) <= page_height * PAGE_MAP_EDGE_RATIO:
                zone = "header"
            elif float(y0) >= page_height * (1 - PAGE_MAP_EDGE_RATIO):
                zone = "footer"
            if zone is None:
                continue

            display_value = str(raw_value).strip().strip("()[]{}.,;:")
            parsed = _parse_page_number_candidate(display_value)
            if parsed is None:
                continue
            family, prefix, numeric_value = parsed
            candidate_key = (zone, display_value, family, prefix, numeric_value)
            if candidate_key in seen:
                continue
            seen.add(candidate_key)
            page_candidates.append(
                {
                    "zone": zone,
                    "display": display_value,
                    "family": family,
                    "prefix": prefix,
                    "numeric": numeric_value,
                }
            )
        candidates_by_page.append(page_candidates)
    return candidates_by_page


def _find_recurrent_text_page_numbers(doc) -> list[str | None]:
    candidates_by_page = _extract_edge_candidates(doc)
    tracks = {}
    for page_index, candidates in enumerate(candidates_by_page):
        pdf_page = page_index + 1
        for candidate in candidates:
            track_key = (
                candidate["zone"],
                candidate["family"],
                candidate["prefix"],
                candidate["numeric"] - pdf_page,
            )
            tracks.setdefault(track_key, {})[pdf_page] = candidate["display"]

    accepted_by_zone = {
        "footer": [set() for _ in range(doc.page_count)],
        "header": [set() for _ in range(doc.page_count)],
    }
    for track_key, values_by_page in tracks.items():
        zone = track_key[0]
        ordered_pages = sorted(values_by_page)
        segment = []
        for pdf_page in ordered_pages:
            if segment and pdf_page != segment[-1] + 1:
                if len(segment) >= PAGE_MAP_MIN_SEQUENCE_LENGTH:
                    for accepted_page in segment:
                        accepted_by_zone[zone][accepted_page - 1].add(
                            values_by_page[accepted_page]
                        )
                segment = []
            segment.append(pdf_page)
        if len(segment) >= PAGE_MAP_MIN_SEQUENCE_LENGTH:
            for accepted_page in segment:
                accepted_by_zone[zone][accepted_page - 1].add(
                    values_by_page[accepted_page]
                )

    results = []
    for page_index in range(doc.page_count):
        footer_values = accepted_by_zone["footer"][page_index]
        values = footer_values or accepted_by_zone["header"][page_index]
        results.append(next(iter(values)) if len(values) == 1 else None)
    return results


def _page_labels_are_informative(doc) -> bool:
    try:
        rules = doc.get_page_labels() or []
    except (AttributeError, RuntimeError, ValueError):
        return False

    return rules != [
        {
            "startpage": 0,
            "prefix": "",
            "firstpagenum": 1,
            "style": "D",
        }
    ] and bool(rules)


def build_page_map(
    doc,
    document_id: str,
    source_blob: str,
    source_etag: str,
) -> dict:
    text_page_numbers = _find_recurrent_text_page_numbers(doc)
    labels_are_informative = _page_labels_are_informative(doc)
    pages = []

    for page_index, text_page_number in enumerate(text_page_numbers):
        display_page = None
        source = "Unknown"
        if text_page_number is not None:
            display_page = text_page_number
            source = "ExtractedText"
        elif labels_are_informative:
            label = str(doc.load_page(page_index).get_label() or "").strip()
            if label:
                display_page = label
                source = "PdfPageLabel"

        pages.append(
            {
                "pdfPage": page_index + 1,
                "displayPage": display_page,
                "source": source,
            }
        )

    return {
        "schemaVersion": 1,
        "documentId": document_id,
        "sourceBlob": source_blob,
        "sourceETag": source_etag,
        "pageCount": doc.page_count,
        "generator": {
            "name": PAGE_MAP_GENERATOR_NAME,
            "version": PAGE_MAP_GENERATOR_VERSION,
            "pymupdfVersion": fitz.VersionBind,
        },
        "pages": pages,
    }


def page_map_matches_source(
    payload: dict,
    document_id: str,
    source_etag: str,
    page_count: int,
) -> bool:
    generator = payload.get("generator") if isinstance(payload, dict) else None
    pages = payload.get("pages") if isinstance(payload, dict) else None
    return bool(
        payload.get("schemaVersion") == 1
        and payload.get("documentId") == document_id
        and payload.get("sourceETag") == source_etag
        and payload.get("pageCount") == page_count
        and isinstance(generator, dict)
        and generator.get("name") == PAGE_MAP_GENERATOR_NAME
        and generator.get("version") == PAGE_MAP_GENERATOR_VERSION
        and isinstance(pages, list)
        and len(pages) == page_count
    )


def load_page_map(page_map_blob_client) -> tuple[dict, str]:
    download = page_map_blob_client.download_blob()
    payload = json.loads(download.readall().decode("utf-8"))
    if not isinstance(payload, dict):
        raise ValueError(f"Page map invalide : {page_map_blob_client.blob_name}.")
    return payload, str(download.properties.etag)


def ensure_page_map(
    doc,
    schematics_container,
    document_id: str,
    document_folder: str,
    source_blob: str,
    source_etag: str,
) -> tuple[dict, str, bool]:
    page_map_client = schematics_container.get_blob_client(
        build_page_map_blob_path(document_folder)
    )
    generated_payload = None

    for _ in range(3):
        existing_payload = None
        existing_etag = None
        try:
            existing_payload, existing_etag = load_page_map(page_map_client)
        except (ResourceNotFoundError, ValueError, TypeError, json.JSONDecodeError):
            pass

        if existing_payload is not None and page_map_matches_source(
            existing_payload, document_id, source_etag, doc.page_count
        ):
            return existing_payload, existing_etag, False

        if generated_payload is None:
            generated_payload = build_page_map(
                doc, document_id, source_blob, source_etag
            )
        upload_kwargs = {
            "data": json.dumps(
                generated_payload, ensure_ascii=False, indent=2
            ).encode("utf-8"),
            "overwrite": existing_etag is not None,
            "content_settings": ContentSettings(content_type="application/json"),
        }
        if existing_etag is not None:
            upload_kwargs["etag"] = existing_etag
            upload_kwargs["match_condition"] = MatchConditions.IfNotModified

        try:
            result = page_map_client.upload_blob(**upload_kwargs)
            result_etag = result.get("etag") if hasattr(result, "get") else None
            if not result_etag:
                result_etag = page_map_client.get_blob_properties().etag
            return generated_payload, str(result_etag), True
        except (ResourceExistsError, ResourceModifiedError):
            continue

    winner_payload, winner_etag = load_page_map(page_map_client)
    if page_map_matches_source(
        winner_payload, document_id, source_etag, doc.page_count
    ):
        return winner_payload, winner_etag, False
    raise RuntimeError("Conflit persistant pendant la création de page-map.json.")


def build_ingestion_batches(
    page_count: int, batch_size: int = PDF_INGESTION_BATCH_SIZE
) -> list[dict]:
    if page_count < 1:
        raise ValueError("Le PDF doit contenir au moins une page.")
    if batch_size < 1:
        raise ValueError("La taille de lot doit être supérieure à zéro.")

    return [
        {
            "start_page": start_page,
            "end_page": min(start_page + batch_size - 1, page_count),
            "status": "pending",
            "owner_work_id": None,
        }
        for start_page in range(1, page_count + 1, batch_size)
    ]


def build_processing_state(
    document_id: str,
    source_blob: str,
    source_etag: str,
    page_count: int,
) -> dict:
    return {
        "schema_version": 1,
        "document_id": document_id,
        "source_blob": source_blob,
        "source_etag": source_etag,
        "page_count": page_count,
        "batch_size": PDF_INGESTION_BATCH_SIZE,
        "status": "processing",
        "batches": build_ingestion_batches(page_count),
        "page_map_ready": False,
        "page_map_etag": None,
        "source_pdf_indexed": False,
        "last_error": None,
    }


def load_processing_state(processing_blob_client) -> tuple[dict, str]:
    download = processing_blob_client.download_blob()
    state = json.loads(download.readall().decode("utf-8"))
    if not isinstance(state, dict) or state.get("schema_version") != 1:
        raise ValueError(
            f"État d'ingestion invalide : {processing_blob_client.blob_name}."
        )
    return state, str(download.properties.etag)


def save_processing_state(
    processing_blob_client,
    state: dict,
    *,
    etag: str | None = None,
    overwrite: bool = True,
) -> str:
    kwargs = {
        "data": json.dumps(state, ensure_ascii=False, indent=2).encode("utf-8"),
        "overwrite": overwrite,
        "content_settings": ContentSettings(content_type="application/json"),
    }
    if etag is not None:
        kwargs["etag"] = etag
        kwargs["match_condition"] = MatchConditions.IfNotModified

    result = processing_blob_client.upload_blob(**kwargs)
    result_etag = result.get("etag") if hasattr(result, "get") else None
    if result_etag:
        return str(result_etag)
    return str(processing_blob_client.get_blob_properties().etag)


def initialize_processing_state(
    processing_blob_client,
    document_id: str,
    source_blob: str,
    source_etag: str,
    page_count: int,
) -> tuple[dict, str]:
    try:
        state, etag = load_processing_state(processing_blob_client)
    except ResourceNotFoundError:
        state = build_processing_state(
            document_id, source_blob, source_etag, page_count
        )
        try:
            etag = save_processing_state(processing_blob_client, state, overwrite=False)
            return state, etag
        except ResourceExistsError:
            state, etag = load_processing_state(processing_blob_client)

    if state.get("source_etag") != source_etag:
        raise RuntimeError(
            "Un processing.json existe déjà pour une autre version du PDF. "
            "La migration des remplacements de PDF est hors du périmètre V1."
        )
    if int(state.get("page_count", 0)) != page_count:
        raise RuntimeError(
            "Le nombre de pages de processing.json ne correspond plus au PDF."
        )
    return state, etag


def find_batch(state: dict, start_page: int, end_page: int) -> dict | None:
    for batch in state.get("batches", []):
        if (
            int(batch.get("start_page", 0)) == start_page
            and int(batch.get("end_page", 0)) == end_page
        ):
            return batch
    return None


def find_next_incomplete_batch(state: dict) -> dict | None:
    for batch in state.get("batches", []):
        if batch.get("status") != "completed":
            return batch
    return None


def build_ingestion_queue_message(state: dict, batch: dict | None) -> str:
    payload = {
        "schema_version": 1,
        "document_id": state["document_id"],
        "source_blob": state["source_blob"],
        "source_etag": state["source_etag"],
        "work_id": str(uuid.uuid4()),
    }
    if batch is not None:
        payload["start_page"] = int(batch["start_page"])
        payload["end_page"] = int(batch["end_page"])
    return json.dumps(payload, ensure_ascii=False)


def set_next_ingestion_message(queue_output, state: dict) -> None:
    next_batch = find_next_incomplete_batch(state)
    if next_batch is not None:
        if next_batch.get("status") == "pending":
            queue_output.set(build_ingestion_queue_message(state, next_batch))
        return
    if state.get("status") != "completed":
        queue_output.set(build_ingestion_queue_message(state, None))


def validate_ingestion_configuration() -> None:
    if os.environ.get("ENABLE_IMAGE_DESCRIPTION", "").strip().lower() != "true":
        raise RuntimeError(
            "Configuration invalide : ENABLE_IMAGE_DESCRIPTION doit être true."
        )
    if os.environ.get("DESCRIPTION_TEST_PAGE", "").strip():
        raise RuntimeError(
            "Configuration invalide : DESCRIPTION_TEST_PAGE doit être vide en ingestion normale."
        )
    if not os.environ.get("DIAGLINK_BACKEND_BASE_URL", "").strip():
        raise RuntimeError(
            "Configuration invalide : DIAGLINK_BACKEND_BASE_URL est obligatoire."
        )
    if not os.environ.get("DIAGLINK_BACKEND_AUDIENCE", "").strip():
        raise RuntimeError(
            "Configuration invalide : DIAGLINK_BACKEND_AUDIENCE est obligatoire."
        )


def claim_batch_for_work(
    processing_blob_client,
    state: dict,
    state_etag: str,
    start_page: int,
    end_page: int,
    work_id: str,
) -> tuple[str, dict, str, dict]:
    """Prend atomiquement un lot ou constate qu'un autre work_id le possède."""

    while True:
        batch = find_batch(state, start_page, end_page)
        if batch is None:
            raise ValueError("Le lot du message Queue est absent de processing.json.")

        status = batch.get("status")
        owner_work_id = batch.get("owner_work_id")
        if status == "completed":
            return "completed", state, state_etag, batch
        if status == "processing" and owner_work_id == work_id:
            return "owned", state, state_etag, batch
        if status == "processing" and owner_work_id not in {None, work_id}:
            return "concurrent", state, state_etag, batch
        if status not in {"pending", "processing"}:
            raise RuntimeError(
                f"Statut de lot invalide pour {start_page}-{end_page} : {status!r}."
            )

        batch["status"] = "processing"
        batch["owner_work_id"] = work_id
        state["status"] = "processing"
        state["last_error"] = None
        try:
            state_etag = save_processing_state(
                processing_blob_client, state, etag=state_etag
            )
            return "owned", state, state_etag, batch
        except ResourceModifiedError:
            state, state_etag = load_processing_state(processing_blob_client)


def complete_batch_for_work(
    processing_blob_client,
    start_page: int,
    end_page: int,
    work_id: str,
) -> tuple[dict, str]:
    """Termine le lot seulement si le work_id courant en est toujours propriétaire."""

    state, state_etag = load_processing_state(processing_blob_client)
    batch = find_batch(state, start_page, end_page)
    if batch is None:
        raise ValueError("Le lot à terminer est absent de processing.json.")
    if batch.get("status") == "completed":
        if batch.get("owner_work_id") != work_id:
            raise RuntimeError("Un autre work_id a déjà terminé ce lot.")
        return state, state_etag
    if batch.get("status") != "processing" or batch.get("owner_work_id") != work_id:
        raise RuntimeError("Le work_id courant ne possède plus le lot.")

    batch["status"] = "completed"
    state["last_error"] = None
    state_etag = save_processing_state(processing_blob_client, state, etag=state_etag)
    return state, state_etag


def mark_page_map_ready(
    processing_blob_client,
    state: dict,
    state_etag: str,
    page_map_etag: str,
) -> tuple[dict, str]:
    while True:
        if state.get("page_map_ready") and state.get("page_map_etag") == page_map_etag:
            return state, state_etag

        state["page_map_ready"] = True
        state["page_map_etag"] = page_map_etag
        try:
            state_etag = save_processing_state(
                processing_blob_client, state, etag=state_etag
            )
            return state, state_etag
        except ResourceModifiedError:
            previous_state = state
            state, state_etag = load_processing_state(processing_blob_client)
            if (
                state.get("document_id") != previous_state.get("document_id")
                or state.get("source_etag") != previous_state.get("source_etag")
                or state.get("page_count") != previous_state.get("page_count")
            ):
                raise RuntimeError(
                    "processing.json a changé de source pendant la création du page map."
                )


def backfill_page_map(
    source_blob: str,
    storage_connection: str | None = None,
) -> dict:
    normalized_source_blob = normalize_source_blob_name(source_blob)
    parse_blob_path(normalized_source_blob)
    document_id, _, document_folder = resolve_document_paths(normalized_source_blob)

    blob_service = BlobServiceClient.from_connection_string(
        storage_connection or STORAGE_CONNECTION
    )
    schematics_container = blob_service.get_container_client(SCHEMATICS_CONTAINER)
    try:
        processing_blob_client = schematics_container.get_blob_client(
            build_processing_blob_path(document_folder)
        )
        state, _ = load_processing_state(processing_blob_client)
        if state.get("status") != "completed":
            raise RuntimeError(
                "Le backfill ciblé exige un document dont l'ingestion est completed."
            )
        if state.get("document_id") != document_id:
            raise RuntimeError("Le document_id de processing.json ne correspond pas.")
        if state.get("source_blob") != normalized_source_blob:
            raise RuntimeError("Le source_blob de processing.json ne correspond pas.")

        source_etag = str(state.get("source_etag", ""))
        source_blob_client = schematics_container.get_blob_client(
            normalized_source_blob
        )
        current_source_etag = str(source_blob_client.get_blob_properties().etag)
        if current_source_etag != source_etag:
            raise RuntimeError(
                "Le PDF source a changé depuis la fin de l'ingestion."
            )

        pdf_bytes = source_blob_client.download_blob(
            etag=source_etag,
            match_condition=MatchConditions.IfNotModified,
        ).readall()
        doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        try:
            if doc.page_count != int(state.get("page_count", 0)):
                raise RuntimeError(
                    "Le nombre de pages du PDF ne correspond pas à processing.json."
                )
            payload, page_map_etag, created = ensure_page_map(
                doc,
                schematics_container,
                document_id,
                document_folder,
                normalized_source_blob,
                source_etag,
            )
        finally:
            doc.close()

        return {
            "pageMapBlob": build_page_map_blob_path(document_folder),
            "pageMapETag": page_map_etag,
            "created": created,
            "pageCount": payload["pageCount"],
            "determinedPageCount": sum(
                page.get("displayPage") is not None for page in payload["pages"]
            ),
        }
    finally:
        try:
            schematics_container.close()
        except Exception:
            pass
        blob_service.close()


def generate_page_images(
    doc,
    schematics_container,
    document_id: str,
    document_folder: str,
    page_count: int,
    start_page: int = 1,
    end_page: int | None = None,
) -> None:
    """
    PHASE 1 : rend et téléverse l'image pleine page et les tuiles de chaque page.
    """

    if end_page is None:
        end_page = page_count
    if start_page < 1 or end_page < start_page or end_page > page_count:
        raise ValueError(
            f"Plage de pages invalide : {start_page}-{end_page} pour {page_count} page(s)."
        )

    for page_number in range(start_page, end_page + 1):

        page_index = page_number - 1
        page = doc.load_page(page_index)
        page_folder = f"{document_folder}/page-{page_number:05d}"

        logging.info(
            "Phase images : page %s/%s",
            page_number,
            page_count,
        )

        page_parent_id = f"{document_id}-page-{page_number:05d}"
        common_metadata = {
            "document_id": ascii_safe_metadata_value(document_id),
            "page_number": f"{page_number:05d}",
            "page_parent_id": ascii_safe_metadata_value(page_parent_id),
        }

        content_rect = detect_content_rect(page)
        full_path = f"{page_folder}/{build_full_image_name(document_id, page_number)}"

        full_pix = page.get_pixmap(
            dpi=FULL_PAGE_DPI,
            clip=content_rect,
            alpha=False,
            colorspace=fitz.csRGB,
        )
        full_image_bytes = full_pix.tobytes("png")

        if not schematics_container.get_blob_client(full_path).exists():
            upload_blob(
                schematics_container,
                full_path,
                full_image_bytes,
                "image/png",
                metadata={
                    **common_metadata,
                    "image_type": "full",
                },
            )

        for tile in make_tiles(page, content_rect, document_id, page_number):

            if tile["contentRatio"] < TILE_MIN_CONTENT_RATIO:
                logging.info(
                    "Tuile vide ignorée : %s (page %s)",
                    tile["name"],
                    page_number,
                )
                continue

            tile_path = f"{page_folder}/{tile['name']}"

            if not schematics_container.get_blob_client(tile_path).exists():
                upload_blob(
                    schematics_container,
                    tile_path,
                    tile["bytes"],
                    "image/png",
                    metadata={
                        **common_metadata,
                        "image_type": "tile",
                        "tile_row": str(tile["row"]),
                        "tile_col": str(tile["column"]),
                    },
                )

    logging.info("Phase images terminée pour les pages %s-%s.", start_page, end_page)


def describe_pages(
    schematics_container,
    document_id: str,
    document_folder: str,
    page_count: int,
    start_page: int = 1,
    end_page: int | None = None,
    *,
    doc=None,
) -> list[tuple[int, str]]:
    """
    PHASE 2 : décrit chaque page éligible avec Claude et enregistre le texte résultant.
    Retourne les (numéro de page, chemin de description) à indexer en phase 3.
    """

    image_description_enabled = (
        os.environ.get("ENABLE_IMAGE_DESCRIPTION", "").strip().lower() == "true"
    )
    description_test_page = os.environ.get("DESCRIPTION_TEST_PAGE", "").strip()
    description_blob_paths_to_index = []

    if end_page is None:
        end_page = page_count
    if start_page < 1 or end_page < start_page or end_page > page_count:
        raise ValueError(
            f"Plage de pages invalide : {start_page}-{end_page} pour {page_count} page(s)."
        )

    for page_number in range(start_page, end_page + 1):

        page_folder = f"{document_folder}/page-{page_number:05d}"
        description_blob_path = (
            f"{page_folder}/{build_description_file_name(document_id, page_number)}"
        )
        description_blob_client = schematics_container.get_blob_client(
            description_blob_path
        )

        should_describe_page = image_description_enabled

        if description_test_page:
            try:
                should_describe_page = should_describe_page and page_number == int(
                    description_test_page
                )
            except ValueError:
                logging.error(
                    "DESCRIPTION_TEST_PAGE invalide (%s) : aucune page ne sera analysée.",
                    description_test_page,
                )
                should_describe_page = False

        if not should_describe_page:
            logging.info(
                "Page %s : aucune description nécessaire",
                page_number,
            )
            continue

        if description_blob_client.exists():
            logging.info(
                "Page %s : description déjà présente — skip Claude",
                page_number,
            )
            description_blob_paths_to_index.append((page_number, description_blob_path))
            continue

        if doc is None:
            raise ValueError(
                "Le document PyMuPDF est requis pour détecter le texte extractible."
            )
        page = doc.load_page(page_number - 1)
        has_extractable_text = bool(page.get_text("text", sort=True).strip())

        full_path = f"{page_folder}/{build_full_image_name(document_id, page_number)}"
        full_image_bytes = (
            schematics_container.get_blob_client(full_path).download_blob().readall()
        )
        uploaded_tiles = []

        for blob in schematics_container.list_blobs(
            name_starts_with=(
                f"{page_folder}/"
                f"{build_page_asset_prefix(document_id, page_number)}-tile-"
            )
        ):
            tile_match = re.search(r"-tile-r(\d+)-c(\d+)\.png$", blob.name)
            if tile_match is None:
                continue

            tile_path = blob.name
            uploaded_tiles.append(
                {
                    "name": tile_path.rsplit("/", 1)[-1],
                    "blob_path": tile_path,
                    "bytes": schematics_container.get_blob_client(tile_path)
                    .download_blob()
                    .readall(),
                    "kind": "tile",
                    "row": int(tile_match.group(1)),
                    "column": int(tile_match.group(2)),
                }
            )

        uploaded_tiles.sort(key=lambda tile: (tile["row"], tile["column"]))

        description_blob_path = generate_page_description(
            container_client=schematics_container,
            document_id=document_id,
            page_number=page_number,
            page_folder=page_folder,
            full_image={
                "name": build_full_image_name(document_id, page_number),
                "blob_path": full_path,
                "bytes": full_image_bytes,
                "kind": "full",
            },
            uploaded_tiles=uploaded_tiles,
            has_extractable_text=has_extractable_text,
        )

        description_blob_paths_to_index.append((page_number, description_blob_path))

    return description_blob_paths_to_index


def index_page_descriptions(
    schematics_container,
    description_blob_paths_to_index: list[tuple[int, str]],
    project_endpoint: str,
    machine_vector_store_id: str,
) -> None:
    """
    PHASE 3 : indexe dans File Search les descriptions générées en phase 2.
    """

    for page_number, description_blob_path in description_blob_paths_to_index:
        description_file_name = description_blob_path.rsplit("/", 1)[-1]
        description_indexed_marker = f"{description_blob_path}.file-search-indexed"

        file_search_id = index_file_in_file_search_once(
            schematics_container,
            description_indexed_marker,
            lambda description_blob_path=description_blob_path: (
                schematics_container.get_blob_client(description_blob_path)
                .download_blob()
                .readall()
            ),
            description_file_name,
            "text/plain",
            project_endpoint,
            machine_vector_store_id,
        )

        if file_search_id:
            logging.info(
                "Page %s : File Search réussi (vector store machine %s)",
                page_number,
                machine_vector_store_id,
            )
        else:
            raise RuntimeError(
                f"Indexation File Search non confirmée pour la page {page_number}."
            )


def index_source_pdf(
    schematics_container,
    document_folder: str,
    pdf_bytes: bytes,
    source_pdf_name: str,
    project_endpoint: str,
    machine_vector_store_id: str,
) -> str:
    """
    PHASE 4 : indexe le PDF source dans File Search (une seule fois par document).
    """

    source_pdf_indexed_marker = f"{document_folder}/source-pdf.file-search-indexed"

    file_search_id = index_file_in_file_search_once(
        schematics_container,
        source_pdf_indexed_marker,
        lambda: pdf_bytes,
        source_pdf_name,
        "application/pdf",
        project_endpoint,
        machine_vector_store_id,
    )

    if file_search_id:
        logging.info(
            "PDF source indexé dans File Search : %s (file_id=%s)",
            source_pdf_name,
            file_search_id,
        )
        return file_search_id

    raise RuntimeError(
        f"Indexation File Search non confirmée pour le PDF source {source_pdf_name}."
    )


@app.queue_output(
    arg_name="queue_message",
    queue_name=PDF_INGESTION_QUEUE,
    connection="AzureWebJobsStorage",
)
@app.blob_trigger(
    arg_name="myblob",
    path="documents/{name}",
    connection="AzureWebJobsStorage",
    source="EventGrid",
)
def BlobToSchematics(
    myblob: func.InputStream,
    queue_message: func.Out[str],
):

    logging.info(
        "Nouveau blob détecté : %s",
        myblob.name,
    )

    if not myblob.name.lower().endswith(".pdf"):
        logging.info("Fichier ignoré : ce n'est pas un PDF.")
        return

    try:
        entreprise, machine, pdf_file_name = parse_blob_path(myblob.name)
    except ValueError:
        logging.exception("Chemin de blob invalide, traitement annulé.")
        return

    pdf_bytes = myblob.read()
    document_id, source_pdf_name, document_folder = resolve_document_paths(myblob.name)
    source_blob = normalize_source_blob_name(myblob.name)

    logging.info(
        "Document ID : %s (dossier de sortie : %s)",
        document_id,
        document_folder,
    )

    blob_service = BlobServiceClient.from_connection_string(STORAGE_CONNECTION)

    try:
        schematics_container = blob_service.get_container_client(SCHEMATICS_CONTAINER)
        source_blob_client = schematics_container.get_blob_client(source_blob)
        source_etag = str(source_blob_client.get_blob_properties().etag)

        try:
            doc = fitz.open(stream=pdf_bytes, filetype="pdf")
            page_count = doc.page_count
        except Exception:
            logging.exception("Impossible d'ouvrir le PDF.")
            raise
        finally:
            if "doc" in locals():
                doc.close()

        processing_blob_client = schematics_container.get_blob_client(
            build_processing_blob_path(document_folder)
        )
        state, _ = initialize_processing_state(
            processing_blob_client,
            document_id,
            source_blob,
            source_etag,
            page_count,
        )

        if state.get("status") == "completed":
            logging.info("Document déjà terminé : %s", source_blob)
            return

        next_batch = find_next_incomplete_batch(state)
        queue_message.set(build_ingestion_queue_message(state, next_batch))
        logging.info(
            "Ingestion mise en file : %s, prochain lot=%s",
            source_blob,
            (
                f"{next_batch['start_page']}-{next_batch['end_page']}"
                if next_batch
                else "finalisation"
            ),
        )
    finally:
        blob_service.close()


def prepare_foundry_for_machine(entreprise: str, machine: str) -> tuple[str, str]:
    """Prépare les ressources Foundry nécessaires à ClaudeDirect."""

    project_endpoint = get_or_create_foundry_project(entreprise)
    vector_store_id = get_or_create_machine_vector_store(
        project_endpoint=project_endpoint,
        entreprise=entreprise,
        machine=machine,
    )
    get_or_create_machine_toolbox(
        project_endpoint=project_endpoint,
        entreprise=entreprise,
        machine=machine,
        vector_store_id=vector_store_id,
    )
    return project_endpoint, vector_store_id


@app.queue_output(
    arg_name="next_message",
    queue_name=PDF_INGESTION_QUEUE,
    connection="AzureWebJobsStorage",
)
@app.queue_trigger(
    arg_name="queue_message",
    queue_name=PDF_INGESTION_QUEUE,
    connection="AzureWebJobsStorage",
)
def ProcessPdfIngestion(
    queue_message: func.QueueMessage,
    next_message: func.Out[str],
):
    """Traite au maximum un lot de pages, puis programme la suite."""

    payload = json.loads(queue_message.get_body().decode("utf-8"))
    validate_ingestion_configuration()
    source_blob = str(payload["source_blob"])
    message_source_etag = str(payload["source_etag"])
    work_id = str(payload.get("work_id", "")).strip()
    if not work_id:
        raise ValueError("Le message Queue ne contient pas de work_id.")
    entreprise, machine, source_pdf_name = parse_blob_path(source_blob)
    document_id, _, document_folder = resolve_document_paths(source_blob)

    if payload.get("document_id") != document_id:
        raise ValueError(
            "Le document_id du message Queue ne correspond pas au PDF source."
        )

    blob_service = BlobServiceClient.from_connection_string(STORAGE_CONNECTION)
    schematics_container = blob_service.get_container_client(SCHEMATICS_CONTAINER)
    processing_blob_client = schematics_container.get_blob_client(
        build_processing_blob_path(document_folder)
    )
    target_batch = None
    owns_target_batch = False

    try:
        state, state_etag = load_processing_state(processing_blob_client)
        if str(state.get("source_etag")) != message_source_etag:
            logging.warning("Message Queue obsolète ignoré pour %s.", source_blob)
            return
        if state.get("status") == "completed":
            logging.info("Message Queue dupliqué ignoré : %s est terminé.", source_blob)
            return

        message_start = payload.get("start_page")
        message_end = payload.get("end_page")
        if message_start is not None and message_end is not None:
            start_page = int(message_start)
            end_page = int(message_end)
            claim_result, state, state_etag, target_batch = claim_batch_for_work(
                processing_blob_client,
                state,
                state_etag,
                start_page,
                end_page,
                work_id,
            )
            if claim_result == "completed":
                next_batch = find_next_incomplete_batch(state)
                if next_batch is not None:
                    set_next_ingestion_message(next_message, state)
                    return
                if target_batch.get("owner_work_id") != work_id:
                    logging.info(
                        "Lot final déjà terminé par un autre worker : %s-%s.",
                        start_page,
                        end_page,
                    )
                    return
                target_batch = None
            if claim_result == "concurrent":
                logging.info(
                    "Lot déjà traité par un autre worker : %s-%s.",
                    start_page,
                    end_page,
                )
                return
            owns_target_batch = True
        else:
            target_batch = find_next_incomplete_batch(state)
            if target_batch is not None:
                set_next_ingestion_message(next_message, state)
                return

        source_blob_client = schematics_container.get_blob_client(source_blob)
        current_source_etag = str(source_blob_client.get_blob_properties().etag)
        if current_source_etag != message_source_etag:
            logging.error(
                "Le remplacement d'un PDF sous le même chemin n'est pas supporté en V1 : %s.",
                source_blob,
            )
            raise RuntimeError(
                "Le PDF source a changé depuis la création de processing.json."
            )

        project_endpoint, machine_vector_store_id = prepare_foundry_for_machine(
            entreprise, machine
        )
        pdf_bytes = source_blob_client.download_blob(
            etag=message_source_etag,
            match_condition=MatchConditions.IfNotModified,
        ).readall()
        doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        try:
            page_count = int(state["page_count"])
            if doc.page_count != page_count:
                raise RuntimeError(
                    "Le nombre de pages du PDF ne correspond pas à processing.json."
                )

            _, page_map_etag, _ = ensure_page_map(
                doc,
                schematics_container,
                document_id,
                document_folder,
                source_blob,
                message_source_etag,
            )
            state, state_etag = mark_page_map_ready(
                processing_blob_client,
                state,
                state_etag,
                page_map_etag,
            )

            if target_batch is not None:
                start_page = int(target_batch["start_page"])
                end_page = int(target_batch["end_page"])
                generate_page_images(
                    doc,
                    schematics_container,
                    document_id,
                    document_folder,
                    page_count,
                    start_page,
                    end_page,
                )
                description_paths = describe_pages(
                    schematics_container,
                    document_id,
                    document_folder,
                    page_count,
                    start_page,
                    end_page,
                    doc=doc,
                )
                index_page_descriptions(
                    schematics_container,
                    description_paths,
                    project_endpoint,
                    machine_vector_store_id,
                )

                state, state_etag = complete_batch_for_work(
                    processing_blob_client,
                    start_page,
                    end_page,
                    work_id,
                )
                logging.info("Lot %s-%s terminé.", start_page, end_page)
        finally:
            doc.close()

        next_batch = find_next_incomplete_batch(state)
        if next_batch is not None:
            next_message.set(build_ingestion_queue_message(state, next_batch))
            return

        state["status"] = "finalizing"
        state_etag = save_processing_state(
            processing_blob_client, state, etag=state_etag
        )

        if not state.get("source_pdf_indexed"):
            index_source_pdf(
                schematics_container,
                document_folder,
                pdf_bytes,
                source_pdf_name,
                project_endpoint,
                machine_vector_store_id,
            )
            state["source_pdf_indexed"] = True
            state_etag = save_processing_state(
                processing_blob_client, state, etag=state_etag
            )

        finalize_machine_claude_direct_config(
            entreprise=entreprise,
            machine=machine,
            project_endpoint=project_endpoint,
            vector_store_id=machine_vector_store_id,
        )

        state["status"] = "completed"
        state["last_error"] = None
        save_processing_state(processing_blob_client, state, etag=state_etag)
        logging.info("Ingestion complète : %s", source_blob)
    except Exception as exc:
        logging.exception("Échec du traitement Queue pour %s.", source_blob)
        try:
            latest_state, latest_etag = load_processing_state(processing_blob_client)
            if str(latest_state.get("source_etag")) == message_source_etag:
                may_write_error = target_batch is None
                if owns_target_batch and target_batch is not None:
                    latest_batch = find_batch(
                        latest_state,
                        int(target_batch["start_page"]),
                        int(target_batch["end_page"]),
                    )
                    may_write_error = bool(
                        latest_batch
                        and latest_batch.get("status") in {"processing", "completed"}
                        and latest_batch.get("owner_work_id") == work_id
                    )
                if may_write_error:
                    latest_state["last_error"] = str(exc)[:2000]
                    save_processing_state(
                        processing_blob_client, latest_state, etag=latest_etag
                    )
        except (ResourceModifiedError, ResourceNotFoundError):
            logging.exception("Impossible d'enregistrer last_error sans conflit.")
        raise
    finally:
        blob_service.close()
