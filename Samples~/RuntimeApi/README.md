# Runtime API sample

Streams a Polyfork asset into a running scene and remixes it live.

## Run it

1. Empty GameObject → add **Polyfork Catalog** and **Polyfork Runtime Sample**.
2. Press Play.

The catalogue loads, one asset spawns, and its first range knob cycles between its
published minimum and maximum so you can watch the geometry rebuild.

## When to use this instead of the gallery

| | |
| --- | --- |
| **Gallery** (`Window ▸ Polyfork ▸ Browse Assets`) | You want the asset *in your project*, as a `.glb` on disk, remixed once at author time. This is what most projects want. |
| **Runtime API** (this sample) | You want assets streamed and remixed while the game runs — procedural levels, player customisation, a live storefront. |

## Costs worth knowing before you build on this

- **Geometry rebuilds are network calls.** Every `SetRange` hits the remix endpoint, which
  is quota-limited per hour. `knobInterval` is deliberately 2.5 s. Don't drive a slider's
  `onValueChanged` straight into `SetRange` — debounce it, or pre-measure with
  `MeasureMorphableKnobsAsync` and let the connector morph between bakes locally.
- **Colour changes are free.** They're applied to vertex colours in-process, no round trip.
  See the main README for why.
- **Nothing is scaled for you.** Assets arrive at real-world metres with the origin on the
  ground. `PolyforkSpawner.FitToSize` is a presentation choice this sample makes; the
  connector never makes it for you.
