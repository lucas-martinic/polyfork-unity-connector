# Polyfork for Unity

The [Polyfork](https://polyfork.dev) store, inside the editor. Browse the catalogue, turn
the same knobs the web viewer exposes, watch the model rebuild, and drop it into your
project as a `.glb` — with your colours baked in.

> **Window ▸ Polyfork ▸ Browse Assets** &nbsp;·&nbsp; `Ctrl/Cmd + Shift + P`

Nothing here invents a parameter. Every label, range, option and palette entry is read from
the asset's published schema at `/cdn/{id}-params.json`.

## Install

```jsonc
// Packages/manifest.json
"com.polyfork.connector": "https://github.com/lucas-martinic/polyfork-unity.git?path=/Packages/com.polyfork.connector",
"com.unity.cloud.gltfast": "6.19.0",
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

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

| Type | Count\* | Path | Cost |
| --- | --- | --- | --- |
| `range` | 112 | Server rebuild via `-remix.glb?p={…}` | ~120 ms |
| `color` | 239 | Local vertex-colour slot remap | instant |
| `choice` (colourway) | ~40 | Local, expands a preset across slots | instant |
| `choice`/`toggle` with `affects: geometry` | ~35 | **Hidden** — see below | — |

<sub>\* across a 40-asset sample of the remixable assets.</sub>

### Why colours are local

The remix endpoint honours **only numeric `range` knobs**. Colour, choice and toggle values
are accepted and silently ignored — the GLB comes back byte-identical:

```
{}                        6D103CFE…  45096 b
{"body":"#FF6600"}        6D103CFE…  45096 b   ← ignored
{"tallness":1.12}         3A1FA1AC…  53160 b   ← rebuilt
```

So colours are applied client-side, and the mapping is exact rather than approximate. A
Polyfork asset is one mesh with baked `COLOR_0` vertex colours, and the set of distinct
vertex colours **is** the set of default hexes declared by its colour knobs. For
`plastic-drum-da992f`: `#8FB4C9`×1386 = `body`, `#1B1D20`×336 = `bung`, `#4E5459`×126 =
`lid`. Recolouring is a slot lookup, not a guess.

On import, the recoloured mesh is re-exported through glTFast, so the saved `.glb` carries
your colours in `COLOR_0` and stays correct outside Unity.

### Why some knobs are hidden

`choice`/`toggle` knobs marked `affects: geometry` (`piece`, `layout`, `lines`) change
topology. The endpoint ignores them and they cannot be emulated locally, so they are not
drawn rather than shown as controls that do nothing. The gallery says how many were hidden.

**If the endpoint starts baking non-range knobs, no client change is needed** — reclassify
in `PolyforkParams.Classify` and they appear.

### Endpoint behaviour worth knowing

- Values clamp to `min`/`max` (`patchCount=99` == `patchCount=10`).
- Unknown knobs, malformed JSON and non-range types fall back to the baseline GLB rather
  than erroring, so a bad request looks like "nothing happened".
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

## Samples

Import from the package page in **Window ▸ Package Manager**.

| Sample | What it does |
| --- | --- |
| **Runtime API** | Spawns an asset at play time and drives a knob from script |
| **Local Baking** | Offline geometry rebuilds via PuerTS/QuickJS. Optional — read its README first |

## Licence

The integration code is **MIT** — see [`LICENSE.md`](LICENSE.md).

3D assets served by polyfork.dev are separate works under the
[Polyfork asset licence](https://polyfork.dev/licensing); the MIT grant here does not
extend to them.

Redistributed third-party components are listed in
[`Third Party Notices.md`](Third%20Party%20Notices.md).
