#!/usr/bin/env python3
"""Builds a .unitypackage from this package, without Unity.

A .unitypackage is not a Unity-only format, it is a gzipped tar laid out by GUID: one
directory per asset, named for the GUID in its .meta, containing the file as `asset`, its
meta as `asset.meta`, and the destination path as `pathname`. Folders get a directory with
`asset.meta` and `pathname` and no `asset`. That is the whole specification, which is why
this can be produced on a machine with no editor on it.

Why it exists: the git URL is the better way to install this and the worse way to hand it to
somebody. A .unitypackage is one file, it double-clicks, and it is what a person expects when
they are told "here is the Unity package" - including the Asset Store, which wants content
under Assets/ rather than a UPM layout.

Paths are rewritten to Assets/Polyfork/…, and `~`-suffixed folders are dropped because Unity
ignores them wherever they land, so shipping them would be shipping files nobody can open.

    python3 Tools~/make-unitypackage.py [output.unitypackage]
"""

import io
import re
import sys
import tarfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PREFIX = "Assets/Polyfork"

# Everything a consumer needs; nothing that only makes sense as a UPM package.
#
# Demo and Documentation are here because Asset Store validation looks for a scene and for
# offline docs inside the paths you hand it, and finds neither in a folder that does not
# contain them. They are in the git package too - the two builds must not differ in the
# files that decide whether a submission passes.
INCLUDE_DIRS = ["Runtime", "Editor", "Demo", "Documentation"]
INCLUDE_FILES = ["README.md", "LICENSE.md", "Third Party Notices.md", "CHANGELOG.md"]

GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


def guid_of(meta: Path) -> str | None:
    m = GUID_RE.search(meta.read_text(encoding="utf-8", errors="replace"))
    return m.group(1) if m else None


# Fixed so the same input produces a byte-identical file. Not 0: epoch-zero timestamps
# are the kind of thing a strict reader is entitled to dislike, and nothing is gained.
MTIME = 1704067200                           # 2024-01-01T00:00:00Z


def add(tar: tarfile.TarFile, guid: str, pathname: str, meta_bytes: bytes, asset_bytes=None):
    """Writes one asset, in the shape Unity's own exporter writes it.

    The GUID DIRECTORY MEMBER is load-bearing and its absence is not benign. Python's
    tarfile creates parent directories implicitly on extract, so a package without one
    round-trips perfectly here and still breaks Unity: its extractor leaves the item list
    malformed, and the import dialog dies building its tree, before drawing anything, with

        NullReferenceException
        UnityEditor.PackageImportTreeView.RecursiveComputeEnabledStateForFolders

    which names nothing about the package and sends you looking at your own assets. Compare
    against a package Unity exported (`tarfile.getmembers()`, count the `isdir()` entries)
    rather than against whether Python can read it back.

    Member order and modes follow the same reference: dir, asset, asset.meta, pathname.
    """
    def entry(name: str, data: bytes = b"", *, directory: bool = False, mode: int = 0o644):
        info = tarfile.TarInfo(guid if directory else f"{guid}/{name}")
        info.type = tarfile.DIRTYPE if directory else tarfile.REGTYPE
        info.mode = 0o755 if directory else mode
        info.size = 0 if directory else len(data)
        info.mtime = MTIME
        tar.addfile(info, None if directory else io.BytesIO(data))

    entry("", directory=True)
    if asset_bytes is not None:
        entry("asset", asset_bytes, mode=0o775)
    entry("asset.meta", meta_bytes)
    entry("pathname", pathname.encode("utf-8"))


def build(out: Path) -> int:
    targets = []

    for name in INCLUDE_DIRS:
        base = ROOT / name
        if not base.exists():
            continue
        targets.append(base)
        targets.extend(p for p in base.rglob("*") if not any(
            part.endswith("~") or part.startswith(".") for part in p.relative_to(ROOT).parts))

    for name in INCLUDE_FILES:
        p = ROOT / name
        if p.exists():
            targets.append(p)

    written = skipped = 0
    out.parent.mkdir(parents=True, exist_ok=True)

    with tarfile.open(out, "w:gz") as tar:
        for path in sorted(set(targets)):
            if path.suffix == ".meta":
                continue

            meta = Path(str(path) + ".meta")
            if not meta.exists():
                # Without a meta there is no GUID, and without a GUID Unity re-imports the
                # file with a new one - which breaks every reference to it.
                print(f"  SKIP (no .meta) {path.relative_to(ROOT)}")
                skipped += 1
                continue

            guid = guid_of(meta)
            if guid is None:
                print(f"  SKIP (no guid)  {path.relative_to(ROOT)}")
                skipped += 1
                continue

            pathname = f"{PREFIX}/{path.relative_to(ROOT).as_posix()}"
            add(tar, guid,
                pathname,
                meta.read_bytes(),
                None if path.is_dir() else path.read_bytes())
            written += 1

    size = out.stat().st_size
    print(f"\n{written} asset(s), {skipped} skipped")
    print(f"{out}  ({size // 1024} KB)")
    return 1 if skipped else 0


if __name__ == "__main__":
    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "Polyfork.unitypackage"
    sys.exit(build(dest))
