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

> **Tools ▸ Polyfork ▸ Browse Assets                           Ctrl/Cmd + Shift + P
Tools ▸ Polyfork ▸ API Key…
Tools ▸ Polyfork ▸ Welcome
Tools ▸ Polyfork ▸ Setup                                   is local baking running, and why not
Tools ▸ Polyfork ▸ Update Package                          pull the newest version from GitHub
Tools ▸ Polyfork ▸ Diagnostics ▸ Smoke-test local baking   bake one model, print timings
```
https://github.com/lucas-martinic/polyfork-unity-connector.git
```

That resolves both dependencies for you, because Package Manager reads them from the manifest.

Or grab **`Polyfork.unitypackage`** from
[Releases](https://github.com/lucas-martinic/polyfork-unity-connector/releases) and
double-click it. The git URL updates in place from `Tools ▸ Polyfork ▸ Update Package`; the
`.unitypackage` is the one-file version for handing to somebody.

> **A `.unitypackage` carries no dependency information**, so install these two first —
> *Window ▸ Package Manager ▸ + ▸ Install package by name*. Both are free, from Unity's own
> registry. Skip it and every Polyfork assembly fails to compile against references it cannot
> find; the Console then names the missing package and why it is needed.
>
> ```
> com.unity.cloud.gltfast
> com.unity.nuget.newtonsoft-json
> ```

Or by hand, which also lets you pin a version with `#v0.18.2`:

```jsonc
// Packages/manifest.json
"dev.polyfork.unity-connector": "https://github.com/lucas-martinic/polyfork-unity-connector.git",
"com.unity.cloud.gltfast": "6.19.0",
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

Unity 6000.0 or newer. The two dependencies are resolved from Unity's own registry.

## Updating

> **Installed before 0.14.0?** The package id is now **`dev.polyfork.unity-connector`**. It was
> `com.polyfork.connector` up to 0.12.x and `dev.polyfork.connector` in 0.13.0. The
> `dev.polyfork` half is not a choice: the Asset Store derives the publisher namespace from the
> verified `polyfork.dev` domain, and the rest has to match the claimed product namespace.
>
> Unity keys a manifest entry by the package's own name, so an entry under either old id cannot
> resolve this one and **Update Package cannot carry you across**. Remove the package in Package
> Manager and add the git URL again. Nothing else changes: assembly names, C# namespaces and
> asset GUIDs are all untouched, so scenes and prefabs keep working.
>
> The repository keeps its name, so the git URL is unchanged.

A package installed from a git URL does not update on its own. **Tools ▸ Polyfork ▸ Update Package**
checks the published version, tells you if you are already on it, and pulls the newest one if
not.

It has to clear this package's entry from `Packages/packages-lock.json` to do it: UPM records
the exact commit it resolved and honours it afterwards, so re-adding the same URL resolves to
the same commit and nothing appears to happen. Unity's own advice is to remove the package and
install it again, which is the same operation with more steps.

## Menu

```
Tools ▸ Polyfork ▸ Browse Assets                          Ctrl/Cmd + Shift + P
Tools ▸ Polyfork ▸ API Key…
Tools ▸ Polyfork ▸ Welcome
Tools ▸ Polyfork ▸ Setup                                  is local baking running, and why not
Tools ▸ Polyfork ▸ Update Package                         pull the newest version from GitHub
Tools ▸ Polyfork ▸ Diagnostics ▸ Smoke-test local baking   bake one model, print timings
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
| any type with `affects: geometry` | Rebuilt from the asset's own module, in the editor | ~20-140 ms |
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

The asset's own module honours all of them, and the editor runs that module directly, so a
local rebuild is not limited this way.

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
    remixable.SetRange("tallness", 1.12f);               // rebuild
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
| **Local bake** (default in the editor) | ~20-140 ms | Nothing — the engine ships with the package | Editing. It spends no allowance and needs no network once the module is fetched. |
| **Vertex morph** | ~0.05 ms | Two bakes up front, measured once per knob | A slider that tracks the hand. Only knobs that preserve topology qualify, and that has to be measured rather than assumed. |
| **Server bake** | One request, ~120 ms | Network, quota | Player builds, rigged assets, and anything the local module cannot build. |

Morphing is the one most projects overlook:

```csharp
await remixable.MeasureMorphableKnobsAsync();
if (remixable.IsMorphable("tallness")) {
    // SetRange now interpolates between two bakes locally, per frame if you like
}
```

Local baking runs the asset's own `createAsset()` module in-process on QuickJS, in the
editor. It needs no setup: the engine ships inside the package, editor-only, so it never
reaches a player build.

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

**There is nothing to install.** The engine is PuerTS on QuickJS, vendored into the package
under `Editor/Puerts/Vendor/` (BSD 3-Clause; see `Third Party Notices.md`). Drag a slider and
the geometry rebuilds as you drag. **Tools ▸ Polyfork ▸ Setup** reports whether it started and what to
check if it did not.

It is editor-only and desktop-only: Windows, macOS (universal, so Apple Silicon included) and
Linux on x64. The assembly declares `includePlatforms: ["Editor"]` and every native library is
marked Editor-only, so a player build gets none of it — which is the point, since a shipped
game keeps using the server baker.

The gallery picks the local baker up automatically and stops reporting a remaining-bakes count
once it has: with a local engine the allowance only governs assets whose module this connection
cannot fetch, which are the ones you could not import anyway.

> **Install it one way, not two.** The `.unitypackage` is built from these same files and its
> assets carry the same GUIDs as a git-URL install, so a project holding both ends up with two
> copies of every assembly and every native plugin, which will not compile. Remove one before
> adding the other.

> **Upgrading from 0.11 or earlier?** Remove `com.tencent.puerts.core` and
> `com.tencent.puerts.quickjs` from your project, and delete the `PuerTS` folder beside
> `Assets` if an older setup window left one. Unity refuses to import two native plugins with
> the same file name, so the project will not compile while both copies are present.
> **Tools ▸ Polyfork ▸ Setup** detects this and says so.

Two things worth knowing:

**It is editor-only, by construction.** The engine assembly declares
`includePlatforms: ["Editor"]`, every native library is marked Editor-only, and the 734 KB
three.js runtime lives under `Editor/` rather than in a `Resources` folder — Unity copies
`Resources` into every build whether anything references it or not. So none of it reaches a
player, and a shipped game always uses the server baker.

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

## Rigs and animation

Some assets carry more than geometry, and what survives an import depends on what you did to
it first.

**Imported unchanged, you get the authored file**: named parts, real materials, and any baked
`AnimationClip` the asset ships. `forest-rabbit-ea2da0` arrives with its `Walk`; self-animating
props like `steam-plume-f06841` arrive with their `tick`.

**Imported with knobs turned or colours changed, you get a baked mesh.** A remix is rebuilt
geometry, and a rebuild has no clips to carry — the connector logs a warning when it is about
to drop one rather than letting it vanish quietly.

**Characters are rigged, not animated.** They ship a skeleton and named joints, and the
catalogue publishes the handles: `rigged_parts` gives each part an axis and a range, e.g.
`LeftArm` rotating on `z` from 0 to -55 degrees. Nothing is missing when a character imports
without clips — there were none.

**So the connector supplies them.** Import a rigged asset and it arrives with an `Animator`,
a `PolyforkCharacterAnimation` component, a set of clips bound to its own skeleton, and
**idle playing by default**. The Inspector shows a dropdown to try the others; from script it
is `anim.Play("walk")`.

The clips come from a pack polyfork.dev publishes, fetched once per project into
`Assets/Polyfork/Animations` rather than shipped in the package — 2.8 MB of Mixamo clips is a
lot to put in every project, most of which import no characters at all.

**Only rotations are bound.** A Mixamo clip carries a position curve for every bone, and
those values *are* the source skeleton's proportions — where its elbow sits relative to its
shoulder. Applied to a rig with different bone lengths they yank every joint to a position
belonging to a different body, and the character comes apart rather than animating wrongly. A
joint *angle* means the same thing on any skeleton with the same topology, so that is what
transfers. Characters therefore animate **in place**, which suits the `applyRootMotion = false`
the importer sets: you move the character, the clip poses it.

They are bound rather than retargeted, because they cannot be retargeted. A Humanoid avatar
is Unity's answer to playing a Mixamo clip on another rig, and **glTFast has no Humanoid
import** — its maintainers say those importer settings *"would basically have to be
rewritten"*. What makes binding work instead is that the two skeletons are the same skeleton:
the packs use Mixamo's names with the `mixamorig:` prefix and the characters use them without,
so each curve is simply re-pointed at the bone this character actually has. Measured on the
live catalogue, every one of `naval-officer`'s 22 bones is driven by the idle clip and none is
missing; the 45 leftover curves are fingers, eyes and toes a reduced rig does not have.

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
