# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-08-04

First release. Unverified against Unity on macOS and Linux — see *Known limitations*.

### Added

- **Gallery window** (`Window ▸ Polyfork ▸ Browse Assets`, `Ctrl/Cmd + Shift + P`).
  Thumbnail grid over the catalogue, disk-cached after first load; orbitable preview;
  live knob editing; import to project as `.glb` with colours baked in. Undo/redo and a
  zoom level that survives a rebuild.
- **Schema-driven controls.** Every label, range, step, option and palette entry is read
  from the asset's published `/cdn/{id}-params.json`. Nothing is invented client-side, so
  a knob added on polyfork.dev appears here without a package update.
- **Knob support classification.** Knobs are sorted into server-rebuild, local-recolour
  and unsupported, so controls that the endpoint would silently ignore are not drawn.
- **Runtime API** — `PolyforkCatalog`, `PolyforkSpawner`, `PolyforkRemixable` — for
  streaming and remixing at play time, with prefetch and a remix budget that degrades to
  the nearest cached variant rather than stalling on a 429.
- **Vertex morphing** for topology-preserving range knobs: lerps between two bakes at
  ~0.05 ms instead of a ~120 ms round trip. 14 of 32 range knobs qualified when measured
  against the live catalogue.
- **Local colour application.** Colour knobs are applied to vertex colours in-process by
  matching authored hexes to slots, so colourways are instant and cost no quota.
- **API key handling** that keeps secrets out of the scene: environment variable, ignored
  key file, or `EditorPrefs`. A 429 opens a dialog explaining the quota with a link to
  create an account.
- **Samples.** *Runtime API* (spawn and remix from script) and *Local Baking* (optional
  offline geometry rebuilds via PuerTS/QuickJS).
- Tests: 30 offline, plus live tests marked `[Category("Network")]` so CI can exclude them.

### Known limitations

- **Structural knobs are not drawn.** `choice`/`toggle` knobs marked `affects: geometry`
  are ignored by the remix endpoint and cannot be emulated locally, so the gallery hides
  them rather than showing a control that does nothing.
- **Linear colour space required**, so vertex-colour maths matches the authored hexes.
- **Developed and tested on Windows.** The package uses no platform-specific APIs and the
  optional QuickJS binary is a macOS universal build, but macOS and Linux are unverified.
