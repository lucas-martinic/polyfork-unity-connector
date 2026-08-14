# Asset Store submission

Everything needed to submit this package. Pricing and licensing are settled (free, pack kept),
and the policy blockers are resolved: the JavaScript engine is vendored rather than installed,
and the one remaining menu item that touches packages is stripped by a build script that then
proves it stripped it.

---

## 1. Blockers

### 1a. Programmatic package installation — **handled**

Quote the two clauses separately, because running them together is how this was misread the
first time:

> **2.5.1.d** *"Submissions do not contain any scripts that, upon import and at any other
> point, automatically and/or without user consent redirect users outside the Unity Editor,
> such as a website or other hyperlinks/deep links."*
>
> **2.5.1.e** *"Offerings must not programmatically add, update, or remove packages in user
> projects, except for packages included in the offering's own Asset Store product."*
> — [Submission Guidelines](https://assetstore.unity.com/publishing/submission-guidelines)

"Automatically and/or without user consent" qualifies **d**, not **e**. A button the user
chose to press is a fine answer to d and no answer at all to e.

**Meta's XR SDK is not the counterexample it looks like.** Its All-in-One package pulls in
eight dependencies — Core, Audio, Haptics, Interaction Essentials, Interaction, Platform,
Voice, MR Utility Kit — and every one is `com.meta.*`: Meta's own packages, all part of the
same Asset Store offering, **declared** in `package.json` and resolved by Package Manager. No
Meta editor script calls the Package Manager API. The tell is the Meta XR Simulator, the one
piece that is *not* a dependency and must be installed separately, precisely because it is not
in that offering.

So the rule is **declare, don't install**, and 5.2.c limits what you may declare to *"Unity
packages or other packages already included in the same published product"*. PuerTS is
Tencent's, which closes both doors — and leaves the third: make it ours.

| Feature | Resolution |
| --- | --- |
| *Install PuerTS for me* | **Gone.** The engine is vendored into the package (`Tools~/vendor-puerts.py`), so there is nothing to install and nothing to ask permission for. |
| `Polyfork ▸ Update Package` | **Stripped from the store build.** Wrong twice over there: the store delivers its own updates, and a store install lands in `Assets/Polyfork/` where there is no package to update, so it would fetch a second copy alongside the imported files. |

`Tools~/make-store-package.py` drops that file, cuts any region marked `// <store-strip>`,
then *searches the result* for `Client.Add`, `Client.AddAndRemove`, `packages-lock.json` and
any orphaned reference the strip left behind, and exits non-zero if it finds one. Run it and
read the output — a build that merely believes it complied is worth nothing when the cost of
being wrong is a two-week review round trip.

```bash
python3 "Tools~/make-store-package.py" ../polyfork-store-build
```

What is vendored is deliberately less than the whole engine: desktop x64 natives only (4.4 MB,
all marked Editor-only), managed source verbatim, and none of the Android/iOS/WebGL binaries,
the websocket addon, or the IL2CPP generator. `Third Party Notices.md` carries the BSD 3-Clause
notice, which is what clause 2 asks of a binary redistribution.

### 1b. The animation clips — **decided: keep them, ship free**

The character animation feature downloads `polyfork.dev/anim/xbot.glb` into the user's
project. Those clips are Mixamo animations that reached polyfork.dev via three.js's example
models rather than an Adobe account.

**Decision (Lucas, 2026-08-14): keep the pack, and the package is free.** Recorded here
rather than argued: Mixamo's terms restrict redistribution regardless of price, so charging
nothing reduces the exposure without erasing it. If a complaint ever arrives, the fix is
already scoped — the retargeting code does not care where clips come from, only that the bone
names are Mixamo-compatible, so a CC0 locomotion set drops in without touching anything else.

### 1d. Validation: demo scene and offline documentation — **handled**

The Asset Store Tools validator failed the first submission on two counts:

> *Could not find any valid Demo Scenes in the selected validation paths.*
> *The following files have been found to match the documentation file format, but may not be
> documentation in content.*

**Read the validator before satisfying it.** Its source ships with the tools, and the two rules
are narrower and dumber than the wording suggests:

- `CheckDemoScenes` collects every `.unity` file under the paths you selected and accepts a
  scene whose root-object count is **anything other than zero, or exactly an untouched camera
  plus an untouched light**. Content is never inspected. Three roots passes.
- `CheckDocumentation` collects every `.txt`, `.pdf`, `.html`, `.rtf` and `.md` under those
  paths, and accepts one that either ends in **`.pdf`** or has the literal word
  **"documentation"** somewhere in its text. Nothing else is read. A manual that calls itself a
  Manual throughout fails — which is exactly what happened, and it reports as a *warning* about
  files that "may not be documentation in content" rather than as a missing file.

The first fix attempt put both under `StoreExtras~/` and unpacked them only in the store build,
on the reasoning that a git-URL install has no business getting a demo scene. That cost a second
review cycle: it made the two artifacts differ in precisely the files validation looks at, so
whichever one you had open decided the outcome. They are ordinary package folders now.

- **`Demo/Polyfork Demo.unity`** — camera, key light, and an object called
  *START HERE - Polyfork* whose Inspector lists the four steps with a button that opens the
  gallery. Three root objects, so it passes. The guidance allows exactly this for an editor
  extension: *"a demo scene showcasing the asset or showing setup steps in the scene"*. It ships
  without models deliberately — Polyfork browses a catalogue that lives online, so what belongs
  in the scene is whatever the user picks; importing one puts it there, which is the demo.
  `Demo/` carries its own asmdefs, without which its scripts would not compile in a UPM package
  and the scene's component would be missing.
- **`Documentation/Polyfork-Manual.pdf`** and **`.html`** — twelve numbered sections with a
  table of contents, covering install, the demo scene, browsing, remixing, importing,
  re-editing, characters, local rebuilds, keys, scripting and troubleshooting. The PDF is
  generated from the HTML (`--headless --print-to-pdf`) and is accepted outright, no text scan.

**When validating, select `Assets/Polyfork` itself**, not a subfolder. Both checks only ever see
the paths you add, so pointing them at `Runtime/` fails on a package that would otherwise pass.

### 1c. Free on GitHub — **settled: the package is free**

MIT and public, and the store listing is free too, so there is no gap between what a buyer
pays and what GitHub gives away. Say plainly in the description that the source is on GitHub;
it reads as confidence rather than a caveat.

---

## 2. What is already compliant

- **Dependencies.** `com.unity.cloud.gltfast` and `com.unity.nuget.newtonsoft-json` are both
  Unity Registry packages, correctly declared in `package.json` — which is what the rules
  require. They must *also* be named in the store description.
- **Third-party notices.** `Third Party Notices.md` carries three.js's MIT notice in full. Add
  the animation clips to it, since the pack is being kept.
- **Licence file.** `LICENSE.md`, MIT.
- **Minimum editor version.** `package.json` says `6000.0`, above the 2021.3 LTS floor.
- **Size.** Under a megabyte, against a 700 MB UPM ceiling.
- **Documentation.** `README.md` covers install, the gallery, knobs, local baking, rigs and
  animation, and FBX export.

---

## 3. Listing copy

**Title** (keep under 50 characters)

```
Polyfork — 3D Asset Browser & Remixer
```

**Summary / short description**

```
Browse hundreds of low-poly 3D assets inside Unity, turn each model's parameters, and
import it with your colours baked in. One draw call per model.
```

**Description**

```
Polyfork puts a 3D asset catalogue inside the Unity editor — and the models are programs
rather than frozen meshes.

Open Window > Polyfork > Browse Assets to search the catalogue, preview any model in an
orbitable 3D view, then open it to remix: drag a slider and the geometry rebuilds, pick a
colourway and every part recolours at once. Import writes a .glb into your project and drops
it into the scene, with a component that keeps the knobs editable afterwards — change your
mind a month later without hunting for the asset again.

WHAT YOU GET
• An editor window over the whole polyfork.dev catalogue, searchable and filterable
• Live parameter editing: sliders, options, toggles and per-part colours
• Import as .glb at real-world scale, origin on the ground
• A component that keeps a placed model editable in the Inspector
• A runtime API for spawning and remixing at play time
• Rigged characters import with an Animator and a clip dropdown

BUILT FOR REAL-TIME
Every model is flat-shaded vertex colour on a single material with no textures, so it draws
in one call — and because they share that material, a set of them merges into one draw call
for the lot. Kilobytes per model, not megabytes.

FREE TIER
About half the catalogue is free forever: no account needed to browse, preview or import, no
attribution, commercial use allowed. An API key raises the remix allowance and unlocks the
paid catalogue.

REQUIREMENTS
Unity 6000.0 or newer. Depends on glTFast (com.unity.cloud.gltfast) and Newtonsoft JSON
(com.unity.nuget.newtonsoft-json), both from Unity's own registry. Works with the built-in
render pipeline and URP.

Remixing and importing use the polyfork.dev web API, so an internet connection is required.
```

**Category**: `Tools ▸ Modeling` (alternative: `Tools ▸ Utilities`)

**Keywords**: `3d models`, `low poly`, `asset browser`, `parametric`, `procedural`,
`glb`, `gltf`, `editor tool`, `prototyping`, `vertex color`

---

## 3b. The .unitypackage

`Tools~/make-unitypackage.py` builds one without Unity — a .unitypackage is a gzipped tar
laid out by GUID, which is a format, not a ritual. Paths are rewritten to `Assets/Polyfork/…`
and `~` folders are dropped, since Unity ignores those wherever they land.

```bash
python3 "Tools~/make-unitypackage.py" Polyfork.unitypackage
```

It is attached to each GitHub release, which is the answer to "where do I get the Unity
package" for anyone who does not want the git URL.

Each release carries **two**, and the difference is only the store's policy strip:

| File | For | Contains |
| --- | --- | --- |
| `Polyfork.unitypackage` | everyone | everything, including `Polyfork ▸ Update Package` |
| `Polyfork-AssetStore.unitypackage` | the submission | the same, minus that one menu item |

Both carry `Demo/` and `Documentation/`, so validating either gives the same answer. Upload the
`-AssetStore` one; the policy strip is the whole reason it exists.

## 4. Images

Generated into `Documentation~/store/`, at the sizes the store asks for:

| File | Size | Where it shows |
| --- | --- | --- |
| `icon-160x160.png` | 160 × 160 | Icon grid |
| `card-420x280.png` | 420 × 280 | Search results |
| `cover-1950x1300.png` | 1950 × 1300 | Product page header |

All three are on `#eceae6`, which is not a style choice: it is the exact background every
asset render on polyfork.dev is photographed against, and the one the connector clears its
own preview to. Listing art on the brand's dark ink matched the logo and nothing the product
shows anyone.

**Screenshots are still needed, and they are the part that sells it.** Marketing images get
rejected for poor quality, excessive text or unattractive design, so:

1. The gallery with the grid populated and a model in the preview
2. The remix screen mid-edit, showing sliders beside a large model
3. A model in a scene with the Inspector open on its knobs
4. A character with the animation dropdown open

Take them at 1920 × 1080 or larger, on the dark editor skin, and crop to the tool rather
than photographing the whole editor. Avoid Unity's default skybox in any scene shots.

---

## 5. Submission checklist

- [x] **1b settled** — pack kept, package ships free
- [x] **Price settled** — free
- [x] **Demo scene and offline documentation** — in every build, validated against the
      validator's own rules
- [ ] Publisher account created at [publisher.unity.com](https://publisher.unity.com), profile
      filled in (name, description, logo, contact)
- [ ] `python3 "Tools~/make-store-package.py"` run, output **clean**
- [ ] Store build opened in a fresh Unity 6000.0 project and compiled with no errors or
      warnings
- [ ] Every feature exercised once in that project: browse, remix, import, place, re-edit
- [ ] Four screenshots taken
- [ ] Description names both Unity Registry dependencies and the internet requirement
- [ ] `Third Party Notices.md` current
- [ ] Version tagged in git so the submission maps to a commit
