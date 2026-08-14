#!/usr/bin/env python3
"""Zips a built store package as a UPM package folder, ready for the Asset Store uploader.

The Asset Store's UPM uploader takes a PACKAGE, not a file: you point it at a package in the
project and it uploads from there. So the artifact to hand over is a folder that drops into
`<project>/Packages/dev.polyfork.unity-connector/`, not an archive Unity has to import.

Which is the whole reason UPM is the right submission format for this product: `package.json`
declares glTFast and Newtonsoft JSON, and Package Manager installs them for the buyer. A
`.unitypackage` carries no dependency information at all, so the same product delivered that
way makes every buyer install two packages by hand before anything compiles.

Run `make-store-package.py` first - this zips its output, which is the source tree minus the
menu item that re-adds this package from its git URL. Programmatic package manipulation is
2.5.1.e, and it has no exception for a user who agreed.

    python3 Tools~/make-store-package.py ../polyfork-store-build
    python3 Tools~/make-upm-zip.py ../polyfork-store-build [Polyfork-UPM.zip]
"""

import json
import sys
import zipfile
from pathlib import Path

# Never ship: repo furniture, and anything Unity would refuse to leave alone.
SKIP_PARTS = {".git", ".github", "Library", "Temp", "obj", "node_modules"}
SKIP_NAMES = {".gitignore", ".DS_Store", "Thumbs.db"}


def build(src: Path, out: Path) -> int:
    manifest = src / "package.json"
    if not manifest.is_file():
        print(f"ERROR: no package.json in {src} - run make-store-package.py first")
        return 1

    meta = json.loads(manifest.read_text(encoding="utf-8"))
    name, version = meta.get("name"), meta.get("version")
    if not name or not version:
        print("ERROR: package.json needs both name and version")
        return 1

    files = 0
    out.parent.mkdir(parents=True, exist_ok=True)

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(src.rglob("*")):
            if not path.is_file():
                continue
            rel = path.relative_to(src)
            if SKIP_PARTS & set(rel.parts) or rel.name in SKIP_NAMES:
                continue
            # Rooted at the package name, so it unzips straight into Packages/.
            zf.writestr(f"{name}/{rel.as_posix()}", path.read_bytes())
            files += 1

    print(f"{files} file(s)")
    print(f"{out}  ({out.stat().st_size // 1024} KB)")
    print(f"\n{name} {version}")
    print(f"Unzip into <project>/Packages/ so the manifest lands at Packages/{name}/package.json,")
    print("then Window > Tools > Asset Store > Uploader > UPM Packages.")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)

    source = Path(sys.argv[1])
    dest = Path(sys.argv[2]) if len(sys.argv) > 2 else source.parent / "Polyfork-UPM.zip"
    sys.exit(build(source, dest))
