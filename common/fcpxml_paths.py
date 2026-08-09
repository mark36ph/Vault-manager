"""Helpers for keeping Resolve FCPXML media paths valid when projects move."""
from __future__ import annotations

from pathlib import Path
from urllib.parse import unquote, urlparse
import xml.etree.ElementTree as ET


def _path_from_file_uri(value: str) -> Path | None:
    parsed = urlparse(str(value or ""))
    if parsed.scheme != "file":
        return None

    path = unquote(parsed.path)
    if len(path) >= 3 and path[0] == "/" and path[2] == ":":
        path = path[1:]
    return Path(path)


def rebase_fcpxml_media_paths(
    project_folder: str | Path,
    old_project_folder: str | Path,
    new_project_folder: str | Path,
) -> int:
    """Rewrite absolute file URIs in project FCPXML files after a folder move.

    Only media paths that were inside the old project folder are changed.
    Returns the number of asset paths rewritten.
    """
    project_folder = Path(project_folder)
    old_root = Path(old_project_folder).resolve()
    new_root = Path(new_project_folder).resolve()

    if not project_folder.is_dir() or old_root == new_root:
        return 0

    rewritten = 0

    for fcpxml_path in project_folder.rglob("*.fcpxml"):
        try:
            tree = ET.parse(fcpxml_path)
        except (OSError, ET.ParseError):
            continue

        changed = False
        for asset in tree.getroot().findall("./resources/asset"):
            source = _path_from_file_uri(asset.attrib.get("src", ""))
            if source is None:
                continue

            try:
                relative = source.resolve().relative_to(old_root)
            except (OSError, ValueError):
                continue

            asset.attrib["src"] = (new_root / relative).resolve().as_uri()
            rewritten += 1
            changed = True

        if changed:
            ET.indent(tree, space="  ")
            tree.write(fcpxml_path, encoding="utf-8", xml_declaration=True)

    return rewritten


__all__ = ["rebase_fcpxml_media_paths"]
