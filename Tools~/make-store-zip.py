#!/usr/bin/env python3
"""Turns a built .unitypackage back into a plain folder tree, zipped.

For the Asset Store, and for anyone who would rather Unity built the package than trust one
assembled off-editor. Drop the folder into `Assets/`, let it compile, then
**Assets > Export Package...** - Unity writes a canonical archive and there is no question of
whether a hand-rolled tar matched what its importer expects. Asset Store Tools uploads from a
project folder anyway, so this is the shape a submission wants.

Derived from the .unitypackage rather than from the source tree on purpose: two lists of what
to include is one list too many, and this way the zip and the package cannot disagree about
their contents.

    python3 Tools~/make-store-zip.py Polyfork-AssetStore.unitypackage [Polyfork.zip]
"""

import collections
import sys
import tarfile
import zipfile
from pathlib import Path

# Assets/Polyfork/Editor/... -> Polyfork/Editor/...
STRIP = "Assets/"


def build(package: Path, out: Path) -> int:
    assets = {}
    with tarfile.open(package) as tar:
        grouped = collections.defaultdict(dict)
        for member in tar.getmembers():
            if member.isdir() or "/" not in member.name:
                continue
            guid, _, leaf = member.name.partition("/")
            grouped[guid][leaf] = member

        for guid, parts in grouped.items():
            if "pathname" not in parts:
                continue
            path = tar.extractfile(parts["pathname"]).read().decode("utf-8").strip()
            assets[path] = (
                tar.extractfile(parts["asset"]).read() if "asset" in parts else None,
                tar.extractfile(parts["asset.meta"]).read(),
            )

    if not assets:
        print(f"ERROR: no assets found in {package}")
        return 1

    folders = files = 0
    out.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for path, (blob, meta) in sorted(assets.items()):
            if not path.startswith(STRIP):
                print(f"ERROR: unexpected path outside {STRIP}: {path}")
                return 1

            rel = path[len(STRIP):]

            # A folder still needs its .meta beside it; without one Unity re-imports the
            # folder with a fresh GUID, and every reference into it goes stale.
            if blob is None:
                zf.writestr(f"{rel}/", b"")
                folders += 1
            else:
                zf.writestr(rel, blob)
                files += 1

            zf.writestr(f"{rel}.meta", meta)

    size = out.stat().st_size
    print(f"{files} file(s), {folders} folder(s), {files + folders} meta(s)")
    print(f"{out}  ({size // 1024} KB)")
    print(f"\nUnzip into your project's Assets/ so the tree lands at Assets/{list(assets)[0].split('/')[1]}/")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)

    src = Path(sys.argv[1])
    dest = Path(sys.argv[2]) if len(sys.argv) > 2 else src.with_suffix(".zip")
    sys.exit(build(src, dest))
