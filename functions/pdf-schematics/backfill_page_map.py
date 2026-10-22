import argparse
import json
import os
from pathlib import Path


def load_local_settings() -> None:
    settings_path = Path(__file__).with_name("local.settings.json")
    if not settings_path.exists():
        return

    values = json.loads(settings_path.read_text(encoding="utf-8")).get("Values", {})
    for name, value in values.items():
        if isinstance(value, str):
            os.environ.setdefault(name, value)

    for name in (
        "AZURE_SUBSCRIPTION_ID",
        "FOUNDRY_RESOURCE_GROUP",
        "FOUNDRY_RESOURCE_NAME",
        "FOUNDRY_LOCATION",
    ):
        os.environ.setdefault(name, "unused-by-page-map-backfill")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Crée ou réutilise page-map.json pour un PDF déjà ingéré."
    )
    parser.add_argument(
        "source_blob",
        help="Chemin du PDF source relatif au conteneur documents.",
    )
    args = parser.parse_args()

    load_local_settings()
    from function_app import backfill_page_map

    result = backfill_page_map(args.source_blob)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()