#!/usr/bin/env python3
"""Builds the Asset Store variant of this package, and proves it is one.

Two of the Asset Store's submission rules bite this package as it ships on GitHub:

  "Submissions ... do not contain any scripts that, upon import and at any other point,
   automatically and/or without user consent redirect users outside the Unity Editor
   [or] programmatically add, update, or remove packages in user projects, except for
   packages included in the offering's own Asset Store product."

  "Packages may only include dependencies on Unity packages or other packages already
   included in the same published product."

The one-button PuerTS installer adds a third-party package, and `Polyfork > Update Package`
rewrites `packages-lock.json` and re-adds this one. Both are the right behaviour for a
package installed from a git URL and both are disqualifying on the store, where updates
arrive through the store itself.

So rather than maintain two codebases, this produces the store variant from the one:
files listed below are dropped, regions between `// <store-strip>` and `// </store-strip>`
are cut, and the result is SEARCHED for the API calls that would fail review. It exits
non-zero if any survive, which is the point - a build that merely believes it complied is
worth nothing to somebody about to wait two weeks for a rejection.

    python3 Tools~/make-store-package.py [output-dir]
"""

import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Whole files that exist only to update a git-installed package.
DROP_FILES = [
    "Editor/PolyforkUpdate.cs",
    "Editor/PolyforkUpdate.cs.meta",
    # Only ever unpacked what the installer downloaded.
    "Editor/PolyforkTar.cs",
    "Editor/PolyforkTar.cs.meta",
]

# Never allowed to survive into the submission.
FORBIDDEN = [
    (r"\bClient\.Add\b", "adds a package programmatically"),
    (r"\bClient\.AddAndRemove\b", "adds/removes packages programmatically"),
    (r"\bClient\.Remove\b", "removes a package programmatically"),
    (r"packages-lock\.json", "edits the project's package lock"),
]

SKIP_DIRS = {".git", "Library", "Temp", "obj"}


def strip_regions(text: str) -> str:
    """Removes every <store-strip> … </store-strip> block."""
    return re.sub(
        r"[ \t]*//\s*<store-strip>.*?//\s*</store-strip>[ \t]*\r?\n?",
        "",
        text,
        flags=re.DOTALL,
    )


def build(out: Path) -> int:
    if out.exists():
        shutil.rmtree(out)

    shutil.copytree(
        ROOT, out,
        ignore=shutil.ignore_patterns(*SKIP_DIRS, "*.tmp"),
    )

    for rel in DROP_FILES:
        target = out / rel
        if target.exists():
            target.unlink()
            print(f"  dropped  {rel}")

    stripped = 0
    for path in out.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if "<store-strip>" not in text:
            continue

        path.write_text(strip_regions(text), encoding="utf-8")
        stripped += 1
        print(f"  stripped {path.relative_to(out)}")

    print(f"\n{stripped} file(s) stripped, {len(DROP_FILES) // 2} file(s) dropped\n")

    # ---- prove it, rather than assume it -------------------------------------
    problems = []
    for path in out.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        for pattern, why in FORBIDDEN:
            for match in re.finditer(pattern, text):
                line = text[: match.start()].count("\n") + 1
                problems.append(f"{path.relative_to(out)}:{line}  {why}")

    # Anything the strip left dangling would not compile, which is a worse rejection
    # than a policy one because it wastes the review slot.
    orphans = ["InstallAsync", "PollAdd", "_addRequest", "_installing", "PolyforkTar", "Unpack("]
    for path in out.rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        for name in orphans:
            if name in text:
                line = text[: text.index(name)].count("\n") + 1
                problems.append(f"{path.relative_to(out)}:{line}  orphaned reference to {name}")

    if problems:
        print("STILL PRESENT — this would be rejected:")
        for p in problems:
            print(f"  {p}")
        return 1

    print("clean: no package-manipulating calls remain")
    print(f"\nstore package at: {out}")
    return 0


if __name__ == "__main__":
    destination = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / "polyfork-store-build"
    sys.exit(build(destination))
