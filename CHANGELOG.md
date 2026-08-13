# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-13

Re-verified against the live API. The endpoint had gained the ability to bake structural
knobs since 0.1.0, and this release stops hiding them.

### Added

- **Structural `choice` and `toggle` knobs are now editable.** Anything marked
  `affects: geometry` is baked by Polyfork, whatever its type. Verified by hashing
  responses: on `brick-church-6cf1af`, `towerHeight` `"12"`/`"18"` and `rose=false` each
  return a distinct GLB. The gallery draws them as a popup and a checkbox, and
  `PolyforkRemixable` gained `SetChoice` / `SetToggle`. They always rebuild rather than
  morph, since they change topology by definition.
- **Polyfork mark and accent** in the editor windows: title-bar icon, a header strip and
  brand blue on selection. The mark is embedded as PNG bytes, so it renders identically
  regardless of a consumer project's texture import defaults.

### Fixed

- **Colourways no longer disappear when the default option has no preset.** Assets that
  publish only the *alternative* schemes (`regolith-terrain-blob-33148e`,
  `field-console-a92adc`) had their whole colourway control hidden, because every option
  was required to name a preset. The default option restores the authored colours, which
  each colour knob already carries.
- **Range values are snapped to the grid the server bakes on** (`remix_snap`, 40 steps, or
  1 for a count-style knob). The server canonicalises *after* keying its cache, so an
  off-grid request paid for a bake that an on-grid one gets free. Rounding is
  away-from-zero to match PHP rather than .NET's round-half-to-even.
- **Non-geometry knobs are no longer sent.** A missing `affects` reads as `colors`
  server-side, so an unlabelled `range` knob was being sent and silently dropped.
- **Remix URLs order their keys**, so one variant is one URL and one cache entry instead
  of one per session.

### Changed

- **The package has its own repo.** It used to live in a subfolder of
  `lucas-martinic/polyfork-unity`, whose root was a Unity project, so installing meant
  `…/polyfork-unity.git?path=/Packages/com.polyfork.connector`. It is now
  `https://github.com/lucas-martinic/polyfork-unity-connector.git`, with no query string.
  The old URL will stop resolving: that repo is private and archived.
- `PolyforkAssetImporter.ImportAsync` takes `PolyforkKnobValues` instead of a
  `Dictionary<string, float>`, so it can carry choice and toggle values.
- `PolyforkServerBaker.Supports` now defers to `PolyforkParams`' classification instead of
  restating it. Keeping two copies is what let them drift apart.

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
  them rather than showing a control that does nothing. *(No longer true as of 0.2.0: the
  endpoint bakes them.)*
- **Linear colour space required**, so vertex-colour maths matches the authored hexes.
- **Developed and tested on Windows.** The package uses no platform-specific APIs and the
  optional QuickJS binary is a macOS universal build, but macOS and Linux are unverified.
