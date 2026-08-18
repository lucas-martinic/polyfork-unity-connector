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

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

"""LICENCES, and why this build carries none.

The submission was rejected twice over them:

    "it contains an independent license for your files. All packages on the Asset Store
     are protected by the Asset Store End User License Agreement."

    "You have assets in your package which require attribution or cannot be resold."

The first is our own MIT licence. The second is the vendored engine: three.js is MIT and
PuerTS is BSD 3-Clause, and both require their notice to travel with the binary, which is
attribution by definition.

So the store build loses local baking and rebuilds on polyfork.dev instead. That is what the
connector did before the engine was vendored, and it is still the path every player build
uses, so nothing about it is untested.
"""

# Whole files that exist only to update a git-installed package, plus everything that
# carries or claims a licence.
DROP_FILES = [
    "Editor/PolyforkUpdate.cs",
    "Editor/PolyforkUpdate.cs.meta",
    "LICENSE.md",
    "LICENSE.md.meta",
    "Third Party Notices.md",
    "Third Party Notices.md.meta",
    "README.md",                       # states MIT in its header
    "README.md.meta",
    "Editor/PolyforkEditorJsScripts.cs",   # feeds the engine its scripts; both are gone
    "Editor/PolyforkEditorJsScripts.cs.meta",
    "CHANGELOG.md",                    # history of a package that carried both licences
    "CHANGELOG.md.meta",
]

# Whole directories, same reason.
DROP_DIRS = [
    "Editor/Puerts",   # vendored PuerTS, BSD 3-Clause, notice required
    "Editor/JS",       # three.js runtime and the bake bridge, MIT, notice required
    "Documentation~",  # internal notes and listing art; the shipped manual is Documentation/
    "Tools~",          # build tooling, including the script that fetches PuerTS
]

# Manifest keys that assert a licence of our own.
DROP_MANIFEST_KEYS = ["license", "licensesUrl"]

# No file in the submission may look like a licence.
LICENCE_NAMES = ("license", "licence", "copying", "notice")

# Section 8 of the manual, for a build with no engine in it. The GitHub build says the
# opposite in the same place, so this is a replacement rather than an edit.
STORE_SECTION_8 = """<h2 id="s8">8. Rebuilds and the hourly allowance</h2>
<p>Every parameter change is rebuilt on polyfork.dev: roughly 120&nbsp;ms, and each one spends
part of an hourly allowance. Free use gets an allowance that is enough to browse and remix; an
API key raises it.</p>
<p>Rebuilds are coalesced while you drag a slider, so a drag costs one rebuild rather than one
per step, and the preview updates when it lands.</p>
<p>A model whose knob only moves existing vertices is interpolated in the editor instead, with
no request at all. Most shape knobs qualify; ones that change the number of parts do not.</p>
<div class="note">An optional open-source add-on runs each model&rsquo;s own program in the
editor, which makes every rebuild instant and free. It is editor-only and desktop-only, and it
lives in the GitHub build at
<a href="https://github.com/lucas-martinic/polyfork-unity-connector">github.com/lucas-martinic/polyfork-unity-connector</a>.
<strong>Tools \u25b8 Polyfork \u25b8 Setup</strong> reports whether it is running.</div>

"""

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

    for rel in DROP_DIRS:
        target = out / rel
        if target.exists():
            shutil.rmtree(target)
            meta = out / (rel + ".meta")
            if meta.exists():
                meta.unlink()
            print(f"  dropped  {rel}/")

    # The manifest must not assert a licence of its own either.
    manifest = out / "package.json"
    data = json.loads(manifest.read_text(encoding="utf-8"))
    for key in DROP_MANIFEST_KEYS:
        if data.pop(key, None) is not None:
            print(f"  dropped  package.json:{key}")
    data["documentationUrl"] = "https://polyfork.dev/unity-integration"
    manifest.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    print("  set      package.json:documentationUrl -> polyfork.dev/unity-integration")

    # The manual describes the engine and names the licence in prose, and ships as HTML plus a
    # PDF rendered from it. Both sections describe a package this build is not, so rewrite them
    # here rather than keeping a second manual in sync by hand.
    manual = out / "Documentation" / "Polyfork-Manual.html"
    if manual.exists():
        html = manual.read_text(encoding="utf-8")

        html = re.sub(
            r'<h2 id="s8">.*?(?=<h2 id="s9">)',
            STORE_SECTION_8,
            html, flags=re.DOTALL)
        html = html.replace("Instant local rebuilds", "Rebuilds and the hourly allowance")

        html = re.sub(
            r'<h2 id="s12">.*?</h2>.*?(?=<p>Source:|</div>)',
            '<h2 id="s12">12. Licence</h2>\n'
            "<p>This package is distributed under the Asset Store End User License Agreement.</p>\n"
            "<p>Models carry their own terms, stated on each model\u2019s page: free models allow\n"
            "commercial use with no attribution.</p>\n",
            html, flags=re.DOTALL)
        html = html.replace("Licence and third-party notices", "Licence")

        manual.write_text(html, encoding="utf-8")
        print("  rewrote  Documentation/Polyfork-Manual.html (sections 8 and 12)")

        pdf = out / "Documentation" / "Polyfork-Manual.pdf"
        try:
            subprocess.run(
                ["google-chrome-stable", "--headless", "--disable-gpu", "--no-sandbox",
                 "--no-pdf-header-footer", f"--print-to-pdf={pdf}", f"file://{manual}"],
                check=True, capture_output=True, timeout=120)
            print("  rebuilt  Documentation/Polyfork-Manual.pdf")
        except Exception as e:
            print(f"  WARNING: could not re-render the PDF ({e}); it still names the old licence")

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

    print(f"\n{stripped} file(s) stripped, "
          f"{len(DROP_FILES) // 2} file(s) and {len(DROP_DIRS)} folder(s) dropped\n")

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

    # A licence file is the thing this build exists to not contain, so look for one by name
    # rather than trusting the drop list to have been kept up to date.
    for path in out.rglob("*"):
        if not path.is_file():
            continue
        stem = path.stem.lower()
        if any(stem.startswith(n) or n in stem for n in LICENCE_NAMES):
            problems.append(f"{path.relative_to(out)}  looks like a licence file")

    manifest_text = (out / "package.json").read_text(encoding="utf-8")
    for key in DROP_MANIFEST_KEYS:
        if f'"{key}"' in manifest_text:
            problems.append(f"package.json  still declares {key}")

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
