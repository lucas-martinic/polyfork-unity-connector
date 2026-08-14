#!/usr/bin/env python3
"""Checks this package's .meta files without needing Unity.

Unity pairs every asset with a .meta file. In a package installed from a git URL the
folder is immutable, so Unity cannot write a missing one itself: it prints

    Asset Packages/dev.polyfork.unity-connector/<path> has no meta file, but it's in an
    immutable folder. The asset will be ignored.

and skips the asset. "Ignored" is the dangerous part - a missing .meta on a script or an
asmdef silently removes it from the compilation rather than failing loudly.

The mirror image is an orphaned .meta, describing something that has since been deleted or
moved. That is what shipped in 0.2.0: Runtime/Resources/Polyfork.meta outlived the folder
it described when local baking moved to Samples~/LocalBaking, leaving a Resources directory
that contained nothing but a stale meta and had no meta of its own.

Unity ignores paths that are dot-prefixed or tilde-suffixed, so Samples~, Documentation~,
Tools~ and .github are all skipped here for the same reason Unity skips them.

Exits non-zero on any problem, so CI can gate on it.
"""

import os
import sys

SKIP = lambda name: name.startswith(".") or name.endswith("~")


def audit(root="."):
    missing, orphans = [], []

    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if not SKIP(d))

        for d in dirnames:
            path = os.path.join(dirpath, d)
            if not os.path.exists(path + ".meta"):
                missing.append(os.path.relpath(path, root) + "/")

        for f in sorted(filenames):
            if f.startswith("."):
                continue
            path = os.path.join(dirpath, f)
            rel = os.path.relpath(path, root)

            if f.endswith(".meta"):
                if not os.path.exists(path[: -len(".meta")]):
                    orphans.append(rel)
            elif not os.path.exists(path + ".meta"):
                missing.append(rel)

    return missing, orphans


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    missing, orphans = audit(root)

    for rel in missing:
        print(f"::error file={rel}::no .meta file; Unity will ignore this in an "
              f"immutable package")
    for rel in orphans:
        print(f"::error file={rel}::orphaned .meta; the asset it describes is gone")

    if missing or orphans:
        print(f"\n{len(missing)} missing, {len(orphans)} orphaned.")
        return 1

    print("meta files are consistent.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
