import json
from pathlib import Path


def load_json(path, default=None):
    """
    Load a JSON file.

    • Supports UTF-8 with or without BOM.
    • Automatically creates the file if it doesn't exist.
    """

    path = Path(path)

    if default is None:
        default = {}

    if not path.exists():

        save_json(path, default)

        return default.copy()

    with open(
        path,
        "r",
        encoding="utf-8-sig"
    ) as f:

        return json.load(f)


def save_json(path, data):
    """
    Save JSON using UTF-8.
    """

    path = Path(path)

    path.parent.mkdir(
        parents=True,
        exist_ok=True
    )

    with open(
        path,
        "w",
        encoding="utf-8"
    ) as f:

        json.dump(
            data,
            f,
            indent=4,
            ensure_ascii=False
        )