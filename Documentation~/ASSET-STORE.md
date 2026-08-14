# Asset Store submission

Everything needed to submit this package. The pricing and licensing questions are settled
(free, pack kept); one technical blocker remains and is handled by a build script.

---

## 1. Blockers

### 1a. Programmatic package installation — **must be removed**

> *"Submissions do not contain any scripts that, upon import and at any other point,
> automatically and/or without user consent redirect users outside the Unity Editor [or]
> programmatically add, update, or remove packages in user projects, except for packages
> included in the offering's own Asset Store product."*
> — [Submission Guidelines](https://assetstore.unity.com/publishing/submission-guidelines)

Two features break this, and both are correct behaviour for a git-installed package:

| Feature | What it does | Why it fails |
| --- | --- | --- |
| *Install PuerTS for me* | `Client.AddAndRemove` on two third-party packages | Adds packages that are not part of this product |
| `Polyfork ▸ Update Package` | Rewrites `packages-lock.json`, re-adds this package | Manipulates packages; the store handles updates anyway |

**Handled.** `Tools~/make-store-package.py` produces the store variant from this source:
it drops those files, cuts the regions marked `// <store-strip>`, then *searches the result*
for `Client.Add`, `Client.AddAndRemove`, `packages-lock.json` and any orphaned reference the
strip left behind, and exits non-zero if it finds one. Run it and read the output — a build
that merely believes it complied is worth nothing when the cost of being wrong is a two-week
review round trip.

```bash
python3 "Tools~/make-store-package.py" ../polyfork-store-build
```

The setup window survives, minus the button: it still explains what PuerTS gives you and
opens the releases page when clicked, which is user-initiated and allowed.

### 1b. The animation clips — **decided: keep them, ship free**

The character animation feature downloads `polyfork.dev/anim/xbot.glb` into the user's
project. Those clips are Mixamo animations that reached polyfork.dev via three.js's example
models rather than an Adobe account.

**Decision (Lucas, 2026-08-14): keep the pack, and the package is free.** Recorded here
rather than argued: Mixamo's terms restrict redistribution regardless of price, so charging
nothing reduces the exposure without erasing it. If a complaint ever arrives, the fix is
already scoped — the retargeting code does not care where clips come from, only that the bone
names are Mixamo-compatible, so a CC0 locomotion set drops in without touching anything else.

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
