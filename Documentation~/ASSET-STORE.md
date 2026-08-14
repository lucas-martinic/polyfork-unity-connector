# Asset Store submission

Everything needed to submit this package, and the three things that must be settled first.

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

### 1b. The animation clips — **needs a decision, and it is not a technical one**

The character animation feature downloads `polyfork.dev/anim/xbot.glb` into the user's
project. Those clips are Mixamo animations, and they reached polyfork.dev via three.js's
example models rather than an Adobe account.

Mixamo's terms license content **to the account holder** for use in their own projects. They
do not grant the right to redistribute animation data as an asset, which is what shipping it
inside a commercial Asset Store product amounts to — the download makes it no less a
distribution. three.js's own MIT licence covers the library's source, not the example models,
and their repository states no licence for them.

Three ways out, in order of how quickly they close it:

1. **Ship without the pack.** Characters import rigged and still, and the docs say so. Costs
   the nicest demo in the product.
2. **Source clips you can redistribute.** CC0 locomotion sets exist; a small idle/walk/run set
   is a day's work to find and verify. The retargeting code is indifferent to where the clips
   came from — it only needs Mixamo-compatible bone names.
3. **Get the licence.** A Mixamo/Adobe account whose terms permit the use, confirmed in
   writing before submitting.

**Do not submit on the current pack.** A licensing complaint after launch is worse than a
rejection before it.

### 1c. Free on GitHub — **a pricing decision, not a rule**

The package is MIT and public. That is permitted, and plenty of store assets are also on
GitHub, but two things follow: buyers can obtain it free, and a reviewer may ask what the
paid version adds. Either price it as convenience-and-support, or hold something back for
the paid build. Decide before writing the description, because the description has to be
honest about it.

---

## 2. What is already compliant

- **Dependencies.** `com.unity.cloud.gltfast` and `com.unity.nuget.newtonsoft-json` are both
  Unity Registry packages, correctly declared in `package.json` — which is what the rules
  require. They must *also* be named in the store description.
- **Third-party notices.** `Third Party Notices.md` carries three.js's MIT notice in full. Add
  the animation clips to it if 1b is resolved by licensing rather than removal.
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

## 4. Images

Generated into `Documentation~/store/`, at the sizes the store asks for:

| File | Size | Where it shows |
| --- | --- | --- |
| `icon-160x160.png` | 160 × 160 | Icon grid |
| `card-420x280.png` | 420 × 280 | Search results |
| `cover-1950x1300.png` | 1950 × 1300 | Product page header |

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

- [ ] **1b settled** — clips removed, relicensed, or replaced
- [ ] Decide the price, given the package is free on GitHub
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
