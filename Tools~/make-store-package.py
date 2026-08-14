#!/usr/bin/env python3
"""Builds the Asset Store variant of this package, and proves it is one.

One Asset Store rule bites this package as it ships on GitHub:

  2.5.1.e  "Offerings must not programmatically add, update, or remove packages in user
            projects, except for packages included in the offering's own Asset Store
            product."

An earlier version of this header ran that sentence together with 2.5.1.d, which is the
one about redirecting the user out of the editor, and so implied that 2.5.1.e carries the
same "automatically and/or without user consent" qualifier. It does not. A button the user
chose to press is no defence, and reading it as one is how you spend a review cycle.

That cost the one-button PuerTS installer, which is gone: the engine is vendored into the
package instead (`Tools~/vendor-puerts.py`), which is the same mechanism Meta's XR SDK uses
- every package it pulls in is one of its own, declared rather than installed.

What is left is `Polyfork > Update Package`, which re-adds this package from its git URL.
It is the right behaviour for a git install and wrong twice over here: the store delivers
its own updates, and a store install lands in `Assets/Polyfork/` where there is no package
to update, so `Client.Add` would fetch a SECOND copy alongside the imported files.

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
]

# Never allowed to survive into the submission.
FORBIDDEN = [
    (r"\bClient\.Add\b", "adds a package programmatically"),
    (r"\bClient\.AddAndRemove\b", "adds/removes packages programmatically"),
    (r"\bClient\.Remove\b", "removes a package programmatically"),
    (r"packages-lock\.json", "edits the project's package lock"),
]

SKIP_DIRS = {".git", ".github", "Library", "Temp", "obj"}


def read_lossy(path: Path) -> str:
    """Reads source for SCANNING only.

    Vendored PuerTS carries one file with a GBK byte in a comment (`Src/Backends/BackendJs.cs`),
    which is how upstream ships it and how it is copied here - byte-identical, so it behaves in
    Unity exactly as the real package does. Refusing to decode it would mean the compliance scan
    below never runs over the vendored tree, which is the one place an unreviewed `Client.Add`
    could hide.
    """
    return path.read_text(encoding="utf-8", errors="replace")


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

    # Demo/ and Documentation/ used to be unpacked here from a hidden StoreExtras~ folder,
    # on the reasoning that a git-URL install has no business getting a demo scene. That was
    # wrong in a way that cost a review cycle: it made the two artifacts differ in exactly
    # the files validation checks for, so whichever one you had in front of you decided
    # whether validation passed. They are ordinary package folders now, in every build.

    stripped = 0
    for path in out.rglob("*.cs"):
        text = read_lossy(path)
        if "<store-strip>" not in text:
            continue

        # Strict here, lossy only for scanning: the marker is ours, so a file that carries
        # one is a file we wrote, and rewriting it through a lossy decode would corrupt it.
        path.write_text(strip_regions(path.read_text(encoding="utf-8")), encoding="utf-8")
        stripped += 1
        print(f"  stripped {path.relative_to(out)}")

    print(f"\n{stripped} file(s) stripped, {len(DROP_FILES) // 2} file(s) dropped\n")

    # ---- prove it, rather than assume it -------------------------------------
    problems = []
    for path in out.rglob("*.cs"):
        text = read_lossy(path)
        for pattern, why in FORBIDDEN:
            for match in re.finditer(pattern, text):
                line = text[: match.start()].count("\n") + 1
                problems.append(f"{path.relative_to(out)}:{line}  {why}")

    # Anything the strip left dangling would not compile, which is a worse rejection
    # than a policy one because it wastes the review slot.
    orphans = ["InstallAsync", "PollAdd", "_addRequest", "_installing", "PolyforkTar", "Unpack("]
    for path in out.rglob("*.cs"):
        text = read_lossy(path)
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
