# Before running the sample:
#    pip install azure-ai-projects>=2.1.0

import json

from azure.identity import DefaultAzureCredential
from azure.ai.projects import AIProjectClient

endpoint = "https://claude-assistant-techni-resource.services.ai.azure.com/api/projects/claude-assistant-technique"

project_client = AIProjectClient(
    endpoint=endpoint,
    credential=DefaultAzureCredential(),
)

my_agent = "assistant-technique-claude-opus"
my_version = "40"

openai_client = project_client.get_openai_client()

messages = []
technical_summary = ""
conversation_state = {"document_id": None, "pages": {}}


def update_summary(old_summary, old_messages):
    if not old_messages:
        return old_summary

    summary_prompt = [
        {
            "role": "user",
            "content": f"""
Résume uniquement le contexte technique utile de cette conversation.

Résumé précédent :
{old_summary}

Messages à condenser :
{old_messages}

Conserve uniquement :
- faits techniques confirmés ;
- pages / folios utiles ;
- repères, fils, bornes, composants et références ;
- résultats de contrôles ;
- points encore non résolus.

Conserve toujours exactement les identifiants techniques utilisés par les outils, notamment document_id, sans les raccourcir, reformuler ou remplacer par un nom plus lisible.
Si un document_id exact a déjà été trouvé, conserve-le tel quel dans le résumé.

Conserve un résumé technique compact de 500 mots maximum. Remplace les anciennes informations par leur version mise à jour au lieu de les répéter.
""",
        }
    ]

    response = openai_client.responses.create(
        input=summary_prompt,
        extra_body={
            "agent_reference": {
                "name": my_agent,
                "version": my_version,
                "type": "agent_reference",
            }
        },
    )

    return response.output_text


while True:
    user_message = input("\nVous : ").strip()

    if user_message.lower() in {"quit", "exit"}:
        break

    messages.append(
        {
            "role": "user",
            "content": user_message,
        }
    )

    if len(messages) > 6:
        old_messages = messages[:-6]
        technical_summary = update_summary(technical_summary, old_messages)
        messages = messages[-6:]

    response = openai_client.responses.create(
        input=[
            {
                "role": "user",
                "content": (
                    f"document_id exact à utiliser pour les outils : "
                    f"{conversation_state['document_id']}"
                    if conversation_state["document_id"]
                    else "Aucun document_id exact confirmé pour le moment."
                ),
            },
            {
                "role": "user",
                "content": (
                    "Mémoire conversationnelle technique. "
                    "Utilise ce résumé pour comprendre le contexte général. "
                    "Pour tout appel d'outil, base-toi sur les résultats actuels "
                    "de Recherche de fichier et les paramètres structurés confirmés.\n\n"
                    f"{technical_summary}"
                ),
            },
            *messages[-6:],
        ],
        extra_body={
            "agent_reference": {
                "name": my_agent,
                "version": my_version,
                "type": "agent_reference",
            }
        },
    )

    assistant_message = response.output_text

    print("\nAgent :", assistant_message)

    messages.append(
        {
            "role": "assistant",
            "content": assistant_message,
        }
    )
