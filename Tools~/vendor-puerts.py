#!/usr/bin/env python3
"""Vendors PuerTS into the package as editor-only desktop code.

WHY THIS EXISTS

Local baking runs asset modules on QuickJS through PuerTS. Until now the connector asked
the user to install PuerTS themselves and offered a one-button installer that called
`UnityEditor.PackageManager.Client.Add`. The Asset Store forbids that outright:

    2.5.1.e  "Offerings must not programmatically add, update, or remove packages in user
              projects, except for packages included in the offering's own Asset Store
              product."
    5.2.c    "Packages may only include dependencies on Unity packages or other packages
              already included in the same published product."

Note what 2.5.1.e does NOT say: nothing about consent. The consent qualifier belongs to
2.5.1.d, which is about redirecting the user out of the editor. A button press is not a
defence here.

Meta's XR SDK looks like a counterexample and is not one. Its All-in-One package pulls in
eight dependencies and every one of them is `com.meta.*` - Meta's own packages, all part of
the same Asset Store offering, declared in package.json and resolved by Package Manager. No
Meta editor script calls Client.Add. The Meta XR Simulator is the tell: it is the one piece
that is NOT a dependency and must be installed separately, precisely because it is not in
that offering.

So the rule is: declare, don't install - and you may only declare Unity's packages or your
own. PuerTS is Tencent's, which closes both doors. The remaining door is the one this script
walks through: make PuerTS part of our own product.

WHAT IS TAKEN, AND WHAT IS LEFT

PuerTS is built to ship a JS engine to players. We want the exact opposite: an engine that
runs in the editor and never reaches a build. So:

  taken    Runtime/Src from core and quickjs, verbatim. Not one line is edited - PuerTS
           resolves its own backends by string (`TypeUtils.GetType("Puerts.BackendQuickJS")`)
           and its JS bootstrap calls `CS.Puerts.Utils`, so renaming the namespace would
           break the bridge at run time rather than at compile time. The ASSEMBLY is renamed
           instead, which collides with nothing and needs no edits.
  taken    Runtime/Resources/puerts/*.mjs - the bootstrap, loaded through Resources.Load by
           PuerTS's own DefaultLoader. Load-bearing despite us evaluating every one of our
           own scripts from a string.
  taken    Desktop x64 natives only: PuertsCore and PapiQuickjs for Windows, macOS and Linux.
           The macOS .bundle is a universal Mach-O, so Apple Silicon is covered.
  left     Android, iOS, WebGL and OpenHarmony natives. 37 MB of binaries for platforms an
           editor-only feature cannot run on.
  left     WSPPAddon, the websocketpp addon: 3.5 MB, and nothing outside its own P/Invoke
           declaration references it. The declaration stays; DllImport binds on first call,
           and that call never happens.
  left     core/Editor entirely. It is the IL2CPP wrapper generator, which matters only when
           shipping PuerTS to a player, plus ScriptedImporters that would claim .mjs, .cjs
           and .lua project-wide for every Polyfork user whether or not they bake locally.

The assembly is `Polyfork.Puerts`, not `com.tencent.puerts.core`, and it is `autoReferenced:
false`. Both matter for the same reason: a user may already have the real PuerTS packages
installed, from the installer this replaces. Different assembly names mean no duplicate-name
error; not being auto-referenced means the user's own scripts never see two `Puerts.JsEnv`
types at once and so never hit CS0433. Our binding references this one explicitly.

GUIDs are md5 of the destination path: stable across re-runs, so bumping the PuerTS version
does not churn every meta, and guaranteed not to collide with Tencent's own.

    python3 Tools~/vendor-puerts.py [--version 3.0.2]
"""

import argparse
import hashlib
import io
import shutil
import sys
import tarfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEST = ROOT / "Editor" / "Puerts" / "Vendor"
CACHE = Path("/tmp/puerts-vendor-cache")

RELEASE = "https://github.com/Tencent/puerts/releases/download/Unity_v{v}/PuerTS_{n}_{v}.tar.gz"

# tarball name -> root directory inside it
PARTS = [("Core", "core"), ("Quickjs", "quickjs")]

# Native libraries we keep, and the editor platform each is for.
# (source path inside the tarball, destination, CPU, OS)
NATIVES = [
    ("core/Plugins/x86_64/PuertsCore.dll",       "Plugins/Windows/PuertsCore.dll",     "x86_64", "Windows"),
    ("core/Plugins/x86_64/libPuertsCore.so",     "Plugins/Linux/libPuertsCore.so",     "x86_64", "Linux"),
    ("core/Plugins/macOS/PuertsCore.bundle",     "Plugins/macOS/PuertsCore.bundle",    "AnyCPU", "OSX"),
    ("quickjs/Plugins/x86_64/PapiQuickjs.dll",   "Plugins/Windows/PapiQuickjs.dll",    "x86_64", "Windows"),
    ("quickjs/Plugins/x86_64/libPapiQuickjs.so", "Plugins/Linux/libPapiQuickjs.so",    "x86_64", "Linux"),
    ("quickjs/Plugins/macOS/PapiQuickjs.bundle", "Plugins/macOS/PapiQuickjs.bundle",   "AnyCPU", "OSX"),
]

# Managed source and the JS bootstrap.
#
# The bootstrap does NOT go to a Resources folder, which is where upstream keeps it and how
# Puerts's own DefaultLoader finds it. Two reasons, and either alone is enough:
#
#   Resources is copied into every player build whether anything references it or not, which
#   is the exact trap that once exiled local baking to a sample. A Resources folder under
#   Editor/ escapes that but is then only reachable through EditorGUIUtility.Load, not
#   Resources.Load, so DefaultLoader would not find it either.
#
#   `.mjs` is not a file type Unity imports. Upstream registers a ScriptedImporter for it in
#   core/Editor, which is dropped here, so a vendored `.mjs` would be a DefaultAsset and load
#   as null.
#
# So each script gains a `.txt` suffix - guaranteed to import as a TextAsset - and
# PolyforkPuertsLoader reads them by explicit asset path. Upstream suggests the same rename
# for old Unity versions, so the shape is one Puerts already expects.
TREES = [
    ("core/Runtime/Src", "Src"),
    ("quickjs/Runtime/Src", "Src"),
    ("core/Runtime/Resources", "JS"),
]

# Extensions Unity will not import, which the loader asks for by their original name.
RETEXT = (".mjs", ".cjs")

ASMDEF = """{
    "name": "Polyfork.Puerts",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
"""


def guid_for(rel: str) -> str:
    """Stable per-path GUID. Re-running must not rewrite every meta."""
    return hashlib.md5(f"polyfork.puerts.vendor/{rel}".encode()).hexdigest()


def write_meta(path: Path, body: str):
    Path(str(path) + ".meta").write_text(body, encoding="utf-8")


def default_meta(path: Path, rel: str, folder: bool):
    write_meta(path, "fileFormatVersion: 2\n"
                     f"guid: {guid_for(rel)}\n"
                     + ("folderAsset: yes\n" if folder else "")
                     + "DefaultImporter:\n  externalObjects: {}\n  userData:\n"
                       "  assetBundleName:\n  assetBundleVariant:\n")


def script_meta(path: Path, rel: str):
    write_meta(path, "fileFormatVersion: 2\n"
                     f"guid: {guid_for(rel)}\n"
                     "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
                     "  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n"
                     "  userData:\n  assetBundleName:\n  assetBundleVariant:\n")


def plugin_meta(path: Path, rel: str, cpu: str, os_name: str):
    """Editor-only native plugin.

    `Any: enabled: 0` makes it compatible with no build target at all; the Editor entry
    then turns it on for one editor OS. That combination is what keeps 4.4 MB of engine
    out of every player build, which is the whole promise of an editor-only feature.
    """
    write_meta(path, "fileFormatVersion: 2\n"
                     f"guid: {guid_for(rel)}\n"
                     "PluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
                     "  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n"
                     "  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n"
                     "  validateReferences: 1\n  platformData:\n"
                     "  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {}\n"
                     "  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n"
                     f"      settings:\n        CPU: {cpu}\n        DefaultValueInitialized: true\n"
                     f"        OS: {os_name}\n"
                     "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def fetch(version: str) -> Path:
    """Downloads and extracts both tarballs into a cache dir."""
    work = CACHE / version
    if (work / "core" / "package.json").exists() and (work / "quickjs" / "package.json").exists():
        print(f"  cached   {work}")
        return work

    work.mkdir(parents=True, exist_ok=True)
    for name, _ in PARTS:
        url = RELEASE.format(v=version, n=name)
        print(f"  fetching {url}")
        with urllib.request.urlopen(url, timeout=180) as r:
            data = r.read()
        with tarfile.open(fileobj=io.BytesIO(data)) as t:
            t.extractall(work)
        print(f"           {len(data) // 1048576} MB")
    return work


def build(version: str) -> int:
    src = fetch(version)

    for part, root in PARTS:
        if not (src / root / "package.json").exists():
            print(f"ERROR: {root} missing from the {part} tarball")
            return 1

    if DEST.exists():
        shutil.rmtree(DEST)
    DEST.mkdir(parents=True)

    # The vendor root needs its own meta as much as anything inside it: a folder Unity has
    # no GUID for is re-imported with a fresh one, which in an immutable package it cannot
    # write back.
    default_meta(DEST, "", folder=True)

    made_dirs = set()

    def ensure_dirs(rel: str):
        """A folder without a .meta is re-imported with a fresh GUID by Unity."""
        parts = Path(rel).parts[:-1]
        for i in range(1, len(parts) + 1):
            d = "/".join(parts[:i])
            if d in made_dirs:
                continue
            made_dirs.add(d)
            (DEST / d).mkdir(parents=True, exist_ok=True)
            default_meta(DEST / d, d, folder=True)

    copied = 0

    # ---- managed source and the JS bootstrap, verbatim -----------------------
    for tree, into in TREES:
        base = src / tree
        for f in sorted(base.rglob("*")):
            if f.is_dir() or f.name.endswith(".meta") or f.suffix == ".asmdef":
                continue
            rel = f"{into}/{f.relative_to(base).as_posix()}"
            if f.suffix in RETEXT:
                rel += ".txt"
            ensure_dirs(rel)
            shutil.copy2(f, DEST / rel)
            if f.suffix == ".cs":
                script_meta(DEST / rel, rel)
            else:
                default_meta(DEST / rel, rel, folder=False)
            copied += 1

    # ---- desktop natives -----------------------------------------------------
    native_bytes = 0
    for rel_src, rel_dst, cpu, os_name in NATIVES:
        f = src / rel_src
        if not f.exists():
            print(f"ERROR: expected native missing: {rel_src}")
            return 1
        ensure_dirs(rel_dst)
        shutil.copy2(f, DEST / rel_dst)
        plugin_meta(DEST / rel_dst, rel_dst, cpu, os_name)
        native_bytes += f.stat().st_size
        copied += 1

    # ---- assembly definition and licence ------------------------------------
    (DEST / "Polyfork.Puerts.asmdef").write_text(ASMDEF, encoding="utf-8")
    write_meta(DEST / "Polyfork.Puerts.asmdef",
               "fileFormatVersion: 2\n"
               f"guid: {guid_for('Polyfork.Puerts.asmdef')}\n"
               "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData:\n"
               "  assetBundleName:\n  assetBundleVariant:\n")

    # BSD 3-Clause clause 2: binary redistribution must reproduce the notice.
    shutil.copy2(src / "core" / "LICENSE", DEST / "LICENSE-PuerTS.txt")
    default_meta(DEST / "LICENSE-PuerTS.txt", "LICENSE-PuerTS.txt", folder=False)

    (DEST / "VERSION.txt").write_text(
        f"PuerTS {version}\n"
        f"https://github.com/Tencent/puerts/releases/tag/Unity_v{version}\n\n"
        "Vendored by Tools~/vendor-puerts.py - do not edit by hand.\n"
        "Managed source is verbatim; only the assembly name and platform settings differ.\n",
        encoding="utf-8")
    default_meta(DEST / "VERSION.txt", "VERSION.txt", folder=False)

    total = sum(f.stat().st_size for f in DEST.rglob("*") if f.is_file())
    print(f"\n  {copied} file(s) vendored into {DEST.relative_to(ROOT)}")
    print(f"  natives {native_bytes / 1048576:.1f} MB, total {total / 1048576:.1f} MB")
    return 0


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="3.0.2")
    sys.exit(build(ap.parse_args().version))
