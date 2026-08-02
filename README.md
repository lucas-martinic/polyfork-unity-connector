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
"com.polyfork.connector": "https://github.com/<you>/polyfork-unity.git?path=/Packages/com.polyfork.connector",
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

The same client works at play time — this is what the XR sample uses.

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

## Licence

The integration is yours to relicense. Asset files remain under the
[Polyfork licence](https://polyfork.dev/licensing).
