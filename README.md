<p align="center">
  <img src="Documentation~/polyfork-mark.png" width="88" height="88" alt="Polyfork">
</p>

<h1 align="center">Polyfork for Unity</h1>

<p align="center">
  <a href="https://polyfork.dev">polyfork.dev</a>
  &nbsp;·&nbsp; <a href="CHANGELOG.md">Changelog</a>
  &nbsp;·&nbsp; <a href="LICENSE.md">MIT</a>
  &nbsp;·&nbsp; Unity 6000.0+
</p>

The [Polyfork](https://polyfork.dev) store, inside the editor. Browse the catalogue, turn
the same knobs the web viewer exposes, watch the model rebuild, and drop it into your
project as a `.glb` — with your colours baked in.

> **Polyfork ▸ Browse Assets** &nbsp;·&nbsp; `Ctrl/Cmd + Shift + P`

Nothing here invents a parameter. Every label, range, option and palette entry is read from
the asset's published schema at `/cdn/{id}-params.json`.

## Install

Unity ▸ **Window ▸ Package Manager ▸ + ▸ Add package from git URL**:

```
https://github.com/lucas-martinic/polyfork-unity-connector.git
```

Or by hand, which also lets you pin a version with `#v0.2.0`:

```jsonc
// Packages/manifest.json
"com.polyfork.connector": "https://github.com/lucas-martinic/polyfork-unity-connector.git",
"com.unity.cloud.gltfast": "6.19.0",
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

Unity 6000.0 or newer. The two dependencies are resolved from Unity's own registry.

## Menu

```
Polyfork ▸ Browse Assets                          Ctrl/Cmd + Shift + P
Polyfork ▸ API Key…
Polyfork ▸ Welcome
Polyfork ▸ Setup                                  install a JS engine for instant bakes
Polyfork ▸ Diagnostics ▸ Smoke-test local baking  (needs a JS engine)
```

The gallery is also under **Window ▸ Polyfork**, where Unity users tend to look for a
window. The welcome screen opens by itself the first time a project imports the package;
you do not need a key to get past it, and it says so.

## The gallery

| | |
| --- | --- |
| **Browse** | Thumbnail grid over the whole catalogue, cached to disk after first load |
| **Filter** | Search, kit, class, free-only, remixable-only, triangle budget |
| **Preview** | Orbitable 3D view of the real GLB — drag to rotate, scroll to zoom |
| **Remix** | Colourway chips, per-slot colour pickers, and range sliders, straight from the schema |
| **Import** | Writes `Assets/Polyfork/<name>.glb`; glTFast turns it into a prefab automatically |

Variants are named after what you changed — `Plastic-Drum_tallness-1.12_recoloured.glb` —
so several versions of one asset coexist without collisions.

## API key

Public previews cover the entire catalogue, so the gallery works signed out. A key unlocks
paid downloads and lifts the unauthenticated remix cap.

**Never type it into the component field** — it is a `[SerializeField]` and gets serialised
into the scene, then committed. Use one of these instead:

| Priority | Source | Good for |
| --- | --- | --- |
| 1 | `POLYFORK_API_KEY` env var | editor, CI |
| 2 | `StreamingAssets/polyfork.key` | device builds |
| 3 | `persistentDataPath/polyfork.key` | side-loading onto a headset |
| 4 | inspector field | quick tests only — warns on load |

`polyfork.key` is gitignored.

## How each knob is honoured

Polyfork publishes four knob types, and the remix endpoint does **not** treat them alike.

| Type | Path | Cost |
| --- | --- | --- |
| any type with `affects: geometry` | Server rebuild via `-remix.glb?p={…}` | ~120 ms |
| `color` | Local vertex-colour slot remap | instant |
| `choice` (colourway) | Local, expands a preset across slots | instant |
| anything else | Not sent — see below | — |

**What decides the path is `affects`, not the type.** A knob marked `affects: geometry` is
rebuilt by Polyfork whether it is a `range`, a `choice` or a `toggle`. Everything else is
applied here or not at all, because the server reads a missing `affects` as `colors`.

### Why colours are local

Colour values are accepted by the endpoint and silently ignored — the GLB comes back
byte-identical, while a geometry knob of any type rebuilds it:

```
{}                          6D103CFE…  45096 b
{"body":"#FF6600"}          6D103CFE…  45096 b   ← ignored, colour is local
{"tallness":1.12}           3A1FA1AC…  53160 b   ← rebuilt (range)
{"towerHeight":"12"}        4299F327…  65960 b   ← rebuilt (choice)
{"rose":false}              75FF9147…  62648 b   ← rebuilt (toggle)
```

Note the quotes on `"12"`. Options are compared strictly, so sending the number `12` for an
option published as the string `"12"` matches nothing and returns the baseline mesh.

So colours are applied client-side, and the mapping is exact rather than approximate. A
Polyfork asset is one mesh with baked `COLOR_0` vertex colours, and the set of distinct
vertex colours **is** the set of default hexes declared by its colour knobs. For
`plastic-drum-da992f`: `#8FB4C9`×1386 = `body`, `#1B1D20`×336 = `bung`, `#4E5459`×126 =
`lid`. Recolouring is a slot lookup, not a guess.

On import, the recoloured mesh is re-exported through glTFast, so the saved `.glb` carries
your colours in `COLOR_0` and stays correct outside Unity.

### Why some knobs are hidden

A knob that does not declare `affects: geometry` and is not a colour cannot be honoured
from a GLB: the endpoint drops it, and there is no local equivalent. Those are not drawn,
rather than shown as controls that do nothing, and the gallery says how many there were.

The asset's own module honours all of them, which is what the *Local Baking* sample is for.

Classification lives in one place, `PolyforkParams.Classify`. If the platform's behaviour
changes again, that is the only thing to edit — `PolyforkServerBaker` reads its verdict
rather than keeping a second copy.

### Range values are snapped

Range values are put on the same grid the server bakes on (40 steps across `min`..`max`, or
whole numbers for a count-style knob). The server canonicalises *after* it keys its cache,
so an off-grid value is baked as its snapped neighbour but requested under a URL nobody
else will ever ask for: the bake is shared, the cache hit is not.

### Endpoint behaviour worth knowing

- Values clamp to `min`/`max` (`patchCount=99` == `patchCount=10`).
- Unknown knobs, malformed JSON, non-geometry knobs and choice values that match no option
  all fall back to the baseline GLB rather than erroring, so a bad request looks like
  "nothing happened".
- The `x-remix` response header reports `exact` vs `fallback`; the client surfaces it.

## Runtime API

The same client works at play time. `Samples~/RuntimeApi` is a runnable version of this.

```csharp
var catalog = FindFirstObjectByType<PolyforkCatalog>();
catalog.Loaded += async () =>
{
    var remixable = await PolyforkSpawner.SpawnAsync(catalog, catalog.Next());
    remixable.SetColorway("colorway", "kerosene-red");   // instant
    remixable.SetRange("tallness", 1.12f);               // rebuild, ~120 ms
};
```

Knobs combine, so the variant space is a product — `plastic-drum` alone is
`tallness(7) × facets(8) × taper(7)` = 392 GLBs. `PrewarmAsync` therefore walks **one axis
at a time around the current values** (linear, 5–15 requests) rather than prefetching
combinations, and re-warms after each rebuild. `PolyforkRemixBudget` keeps an
unauthenticated session under the remix cap and degrades to the nearest cached variant
instead of stalling on a 429.

## Where geometry gets rebuilt

Turning a range knob has to re-run the asset's generator. There are three ways to pay for
that, and the connector ships all three because they suit different projects.

| | Cost | Needs | Use when |
| --- | --- | --- | --- |
| **Server bake** (default) | One request, ~120 ms | Network, quota | Almost always. Nothing to install, exact results. |
| **Vertex morph** | ~0.05 ms | Two server bakes up front | You want a slider to track the hand. Only works on topology-preserving knobs — 14 of 32 measured on the live catalogue. |
| **Local bake** | ~41.5 ms on Quest 3 | A JS engine + ~343 KB payload | Offline, or you're past the hourly quota. |

Morphing is the one most projects overlook:

```csharp
await remixable.MeasureMorphableKnobsAsync();
if (remixable.IsMorphable("tallness")) {
    // SetRange now interpolates between two bakes locally, per frame if you like
}
```

Local baking runs the asset's own `createAsset()` module in-process via PuerTS/QuickJS.
It is shipped as the optional **Local Baking** sample rather than in the package, because
it carries a trimmed three.js build that would otherwise land in every consumer's player
build whether or not they use it. Import it only if you need it — see that sample's README
for the full trade-off.

## Types

| Type | Role |
| --- | --- |
| `PolyforkGalleryWindow` | The editor gallery |
| `PolyforkAssetImporter` | Import-to-project with colour baking |
| `PolyforkAssetPreview` | Orbitable preview via `PreviewRenderUtility` |
| `PolyforkClient` | Typed HTTP access |
| `PolyforkParams` / `PolyforkKnob` | Published schema + support classification |
| `PolyforkColorSlots` | Vertex-colour slot binding and recolouring |
| `PolyforkCatalog` / `PolyforkRemixable` | Runtime catalogue and live remix state |
| `PolyforkCredentials` | Key resolution that keeps secrets out of the scene |

## Notes

- Assets are authored at real-world metres with the origin on the ground (`minY = 0`).
  Don't rescale to fake a fit; pick a right-sized asset.
- Requires **Linear** colour space so vertex-colour maths matches the authored hexes.

## Instant bakes in the editor

By default a knob change is a round trip: the server rebuilds the mesh in about 120 ms and
it counts against your hourly allowance. Install a JavaScript engine and the editor runs the
asset's **own `createAsset()` module** instead — the same program the store runs — so a
slider costs CPU rather than a request.

**Polyfork ▸ Setup** has an *Install PuerTS for me* button that downloads a
matched pair and adds both to the project. It asks first, and tells you what it is about to
fetch and from where.

By hand instead:

1. From one PuerTS release, download **both** `PuerTS_Core_<version>` and
   `PuerTS_Quickjs_<version>`: <https://github.com/Tencent/puerts/releases>
2. **Window ▸ Package Manager ▸ + ▸ Add package from tarball…** for each.

> **Take both from the same release.** `com.tencent.puerts.quickjs` depends on an exact
> `com.tencent.puerts.core` version. OpenUPM carries only the core, at a different version,
> so the obvious route installs cleanly and then never works. The setup window checks the
> installed versions and says so if they disagree.

The gallery picks the local baker up automatically, and stops reporting a remaining-bakes
count once it has: with a local engine the allowance only governs assets whose module this
connection cannot fetch, which are the ones you could not import anyway. Without the engine the connector keeps
using the server exactly as before — the assembly that binds to PuerTS is gated on the
package being present, so its absence is not a broken state, just a slower one.

Two things worth knowing:

**It is editor-only, by construction.** The engine binding declares
`includePlatforms: ["Editor"]` and the ~336 KB three.js bundle lives under `Editor/`, so
neither can reach a player build. A shipped game always uses the server baker. This is the
reason local baking used to be an opt-in sample: the scripts sat in a `Resources` folder,
and Unity copies `Resources` into every build whether anything references it or not.

**It only covers assets whose module you can fetch** — every free asset, and paid ones once
you own them. Locked assets still preview from their public GLB and still remix on the
server.

For responsive sliders with no engine at all, there is a middle option: the runtime measures
whether a range knob is topology-preserving and, when it is, interpolates between two bakes
at about 0.05 ms. 14 of 32 range knobs qualified on the live catalogue.

```csharp
await remixable.MeasureMorphableKnobsAsync();
if (remixable.IsMorphable("width")) {
    // SetRange now interpolates locally instead of calling out
}
```

## Still editable after you drop it in a scene

Importing writes two files — the `.glb` and a **prefab beside it carrying a
`PolyforkAssetLink`** — and drops the prefab into the open scene in front of the camera.
Select it and the Inspector keeps the asset's knobs: move a slider and the model changes in
place as you go.

Rebuilding keeps whatever material the object is wearing, so a material you assigned
yourself survives a knob change.

Rebuilding replaces the meshes on the object, so its transform, its children, its colliders
and anything else you attached survive the change. The knob values are stored on the
component as JSON, so they outlive the window that made them: a month later the fence is
still one section away from being longer, rather than a mesh whose settings nobody wrote
down.

Why a prefab rather than the `.glb` itself: an imported model is rebuilt from the file on
every import, so a component added to it is discarded. The prefab is the thing that can
carry state. The `.glb` is still there and still a normal Unity asset if you would rather
have the frozen mesh.

At runtime the component does nothing and costs a few bytes, so it is safe to leave on a
shipped prefab.

## Exporting to FBX

There is no FBX button here on purpose. Assets import as `.glb`, which glTFast turns into a
prefab, and that prefab is a normal Unity GameObject — so Unity's own exporter already does
the job:

1. Install **FBX Exporter** (`com.unity.formats.fbx`) from the Package Manager.
2. Right-click the imported prefab ▸ **Export To FBX**.

It carries the vertex colours across, which is the part that matters for these assets: a
Polyfork model is one mesh with one material and no textures, so `COLOR_0` *is* the look.
The exporter writes them as an `FbxLayerElementVertexColor` layer whenever the mesh reports
`HasValidVertexColors()`.

Prefer the `.glb` where you have the choice. It is the file polyfork.dev actually served, so
it matches the web viewer exactly, and vertex colours are a first-class glTF concept rather
than something to verify after a conversion. Blender, Godot, three.js and Unreal 5 all read
glTF natively. Reach for FBX when a pipeline demands it — Mixamo, or a Maya-centric
studio — not by default.

## Samples

Import from the package page in **Window ▸ Package Manager**.

| Sample | What it does |
| --- | --- |
| **Runtime API** | Spawns an asset at play time and drives a knob from script |

## Licence

The integration code is **MIT** — see [`LICENSE.md`](LICENSE.md).

3D assets served by polyfork.dev are separate works under the
[Polyfork asset licence](https://polyfork.dev/licensing); the MIT grant here does not
extend to them.

Redistributed third-party components are listed in
[`Third Party Notices.md`](Third%20Party%20Notices.md).
