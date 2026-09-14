#!/usr/bin/env python3
"""Install the licensed Feel 5.6.1 subset required by Riverworks."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import tarfile
from typing import BinaryIO


EXPECTED_PACKAGE_NAME = "Feel v5.6.1 (02 Jul 2025).unitypackage"
EXPECTED_PACKAGE_SHA256 = "36f6c61c45478216353d73d0f46e34f5089ccab6a879186dc59879ea13f77dd1"
EXPECTED_ASSET_COUNT = 1031
ROOT_FILES = {"Assets/Feel/license.txt", "Assets/Feel/readme.txt"}
ROOT_PREFIXES = ("Assets/Feel/MMFeedbacks/", "Assets/Feel/MMTools/")
ASMDEF_PATH = "Assets/Feel/MMTools/Core/MoreMountains.Tools.asmdef"
UITK_EDITOR_PATH = "Assets/Feel/MMTools/Core/Editor/MMAttributes/MMMonoBehaviourUITKEditor.cs"
ASMDEF_REFERENCES = ["UnityEngine.UI", "Unity.TextMeshPro"]
GUID_RE = re.compile(r"^[0-9a-f]{32}$")


class ImportFailure(RuntimeError):
    pass


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_archive_member(member: tarfile.TarInfo) -> tuple[str, str] | None:
    name = member.name
    if "\\" in name or name.startswith("/"):
        raise ImportFailure(f"Unsafe archive member path: {name!r}")
    parts = PurePosixPath(name).parts
    if not parts or any(part in ("", ".", "..") for part in parts):
        raise ImportFailure(f"Unsafe archive member path: {name!r}")
    if member.issym() or member.islnk() or member.isdev():
        raise ImportFailure(f"Links and device entries are not allowed: {name!r}")
    if len(parts) == 1:
        if parts[0] == ".icon.png" and member.isfile():
            return None
        if not GUID_RE.fullmatch(parts[0]):
            raise ImportFailure(f"Unexpected Unity package entry: {name!r}")
        return None
    if len(parts) != 2 or not GUID_RE.fullmatch(parts[0]):
        raise ImportFailure(f"Unexpected Unity package entry: {name!r}")
    if parts[1] not in {"asset", "asset.meta", "pathname", "preview.png"}:
        raise ImportFailure(f"Unexpected Unity package payload: {name!r}")
    return parts[0], parts[1]


def read_member_bytes(archive: tarfile.TarFile, member: tarfile.TarInfo) -> bytes:
    stream: BinaryIO | None = archive.extractfile(member)
    if stream is None:
        raise ImportFailure(f"Archive member has no file payload: {member.name!r}")
    return stream.read()


def normalize_pathname(raw: bytes, source: str) -> str:
    lines = raw.splitlines()
    if not lines:
        raise ImportFailure(f"Empty pathname payload: {source}")
    try:
        pathname = lines[0].decode("utf-8-sig", errors="strict").rstrip("\x00")
    except UnicodeDecodeError as error:
        raise ImportFailure(f"Pathname is not valid UTF-8: {source}") from error
    if any(line.rstrip(b"\x00") not in (b"", b"00") for line in lines[1:]):
        raise ImportFailure(f"Unexpected trailing pathname data: {source}")
    if not pathname or "\\" in pathname or pathname.startswith("/") or "\x00" in pathname:
        raise ImportFailure(f"Unsafe asset pathname: {pathname!r}")
    parts = PurePosixPath(pathname).parts
    if any(part in ("", ".", "..") for part in parts):
        raise ImportFailure(f"Unsafe asset pathname: {pathname!r}")
    normalized = "/".join(parts)
    if normalized != pathname or not normalized.startswith("Assets/Feel"):
        raise ImportFailure(f"Unexpected asset pathname: {pathname!r}")
    return normalized


def is_selected_asset(pathname: str) -> bool:
    if pathname in ROOT_FILES:
        return True
    if not pathname.startswith(ROOT_PREFIXES):
        return False
    # The runtime/editor libraries are retained, while vendor demo trees are not.
    return "Demos" not in PurePosixPath(pathname).parts


def patch_asset(pathname: str, payload: bytes) -> bytes:
    if pathname == ASMDEF_PATH:
        try:
            document = json.loads(payload.decode("utf-8-sig"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ImportFailure(f"Cannot parse {ASMDEF_PATH}") from error
        document["references"] = ASMDEF_REFERENCES
        return (json.dumps(document, indent=4, ensure_ascii=False) + "\n").encode("utf-8")
    if pathname == UITK_EDITOR_PATH:
        before = b"target.GetInstanceID()"
        after = b"target.GetEntityId()"
        if payload.count(before) != 1:
            raise ImportFailure(f"Expected exactly one Unity 6 compatibility site in {UITK_EDITOR_PATH}")
        return payload.replace(before, after, 1)
    return payload


def existing_is_equivalent(pathname: str, current: bytes, desired: bytes) -> bool:
    if current == desired:
        return True
    if pathname == ASMDEF_PATH:
        try:
            return json.loads(current.decode("utf-8-sig")) == json.loads(desired.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            return False
    return False


def safe_destination(project_root: Path, pathname: str) -> Path:
    destination = (project_root / Path(*PurePosixPath(pathname).parts)).resolve()
    root = project_root.resolve()
    try:
        destination.relative_to(root)
    except ValueError as error:
        raise ImportFailure(f"Destination escaped the project root: {pathname}") from error
    return destination


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Import the Riverworks subset from a user-owned Feel 5.6.1 unitypackage."
    )
    parser.add_argument("unitypackage", type=Path, help="Path to the licensed Feel 5.6.1 .unitypackage")
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Riverworks project root (defaults to the parent of Tools)",
    )
    parser.add_argument("--dry-run", action="store_true", help="Inspect and report without writing files")
    parser.add_argument("--force", action="store_true", help="Replace destination files whose bytes differ")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    package = args.unitypackage.expanduser().resolve()
    project_root = args.project_root.expanduser().resolve()
    if not package.is_file():
        raise ImportFailure(f"Package file does not exist: {package}")
    if package.suffix.lower() != ".unitypackage":
        raise ImportFailure("Expected a .unitypackage file; the exact version is verified by SHA-256.")
    if not (project_root / "Assets").is_dir() or not (project_root / "ProjectSettings").is_dir():
        raise ImportFailure(f"Not a Unity project root: {project_root}")

    package_sha = sha256_file(package)
    if package_sha != EXPECTED_PACKAGE_SHA256:
        raise ImportFailure(
            "Feel package SHA-256 mismatch; use the unmodified official Feel 5.6.1 package "
            f"(expected {EXPECTED_PACKAGE_SHA256}, got {package_sha})"
        )

    selected: list[str] = []
    outputs: dict[str, bytes] = {}
    directory_metas: dict[str, bytes] = {}
    seen_paths: set[str] = set()

    with tarfile.open(package, mode="r:*") as archive:
        current_guid: str | None = None
        current: dict[str, bytes] = {}

        def finish_group() -> None:
            if not current:
                return
            if "pathname" not in current:
                raise ImportFailure(f"Unity package group has no pathname: {current_guid}")
            pathname = normalize_pathname(current["pathname"], f"{current_guid}/pathname")
            if pathname in seen_paths:
                raise ImportFailure(f"Duplicate asset pathname: {pathname}")
            seen_paths.add(pathname)
            if "asset" not in current:
                if "asset.meta" in current:
                    directory_metas[pathname] = current["asset.meta"]
                return
            if not is_selected_asset(pathname):
                return
            if "asset.meta" not in current:
                raise ImportFailure(f"Missing metadata for selected asset: {pathname}")
            selected.append(pathname)
            outputs[pathname] = patch_asset(pathname, current["asset"])
            outputs[pathname + ".meta"] = current["asset.meta"]

        for member in archive:
            key = validate_archive_member(member)
            if key is None or member.isdir():
                continue
            guid, leaf = key
            if guid != current_guid:
                finish_group()
                current_guid = guid
                current = {}
            if leaf in current:
                raise ImportFailure(f"Duplicate Unity package payload: {member.name!r}")
            if leaf != "preview.png":
                current[leaf] = read_member_bytes(archive, member)
        finish_group()

    selected.sort()
    if len(selected) != EXPECTED_ASSET_COUNT:
        raise ImportFailure(
            f"Feel subset count mismatch: expected {EXPECTED_ASSET_COUNT}, got {len(selected)}"
        )

    for pathname in selected:
        parent = PurePosixPath(pathname).parent
        while str(parent) not in (".", "Assets"):
            parent_name = parent.as_posix()
            if parent_name not in directory_metas:
                raise ImportFailure(f"Missing parent metadata: {parent_name}.meta")
            outputs[parent_name + ".meta"] = directory_metas[parent_name]
            parent = parent.parent

    if args.dry_run:
        for pathname in selected:
            digest = hashlib.sha256(outputs[pathname]).hexdigest()
            print(f"{digest}  {pathname}")
        print(f"Package: {EXPECTED_PACKAGE_NAME}")
        print(f"Package SHA-256: {package_sha}")
        print(f"Selected assets: {len(selected)}")
        print(f"Metadata files: {sum(path.endswith('.meta') for path in outputs)}")
        print("Dry run: no files were written.")
        return 0

    created = skipped = replaced = 0
    for pathname in sorted(outputs):
        destination = safe_destination(project_root, pathname)
        desired = outputs[pathname]
        if destination.exists():
            if not destination.is_file():
                raise ImportFailure(f"Destination is not a file: {destination}")
            current = destination.read_bytes()
            if existing_is_equivalent(pathname, current, desired):
                skipped += 1
                continue
            if not args.force:
                raise ImportFailure(
                    f"Destination differs: {destination}. Review it and rerun with --force to replace it."
                )
            destination.write_bytes(desired)
            replaced += 1
            continue
        destination.parent.mkdir(parents=True, exist_ok=True)
        with destination.open("xb") as stream:
            stream.write(desired)
        created += 1

    print(f"Feel 5.6.1 import complete: created={created}, identical={skipped}, replaced={replaced}")
    print(f"Package SHA-256: {package_sha}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ImportFailure, tarfile.TarError, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
