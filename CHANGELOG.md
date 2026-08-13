# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.3] - 2026-08-13

### Fixed

- **0.3.2 did not compile.** The setup window awaited `SendWebRequestAsync`, which lives in
  an `internal` class in the runtime assembly and is therefore invisible from the editor
  assembly. It has its own small awaiter now, which suits it: that download talks to
  github.com and deliberately shares no transport with the Polyfork client. Widening the
  runtime's API, or granting the editor assembly blanket access to every internal, would
  both have been larger changes than the twenty lines it took.
- The setup window's package-manager poll is now removed when the window closes, instead of
  repainting a destroyed window every tick for the rest of the session.

### Changed

- `Polyfork ▸ Make Bakes Instant…` is now **`Polyfork ▸ Setup`**, and the gallery's status
  bar button matches. The explanation moved to the tooltip.

## [0.3.2] - 2026-08-13

### Fixed

- **Opening the gallery from the welcome screen showed an empty window** until you hit
  Refresh. The welcome window closed itself and opened the gallery inside the same OnGUI
  pass, so the gallery was created through a dying window and came up blank; the catalogue
  had loaded, nothing had repainted. Both windows now hand over on the next editor tick.

### Added

- **A one-button PuerTS install** in `Polyfork ▸ Make Bakes Instant…`. It resolves the
  newest PuerTS release, downloads the core and QuickJS tarballs **from that same release**,
  and adds both in a single `AddAndRemove` call. Taking both from one release is what makes
  the version mismatch structurally impossible rather than something to be careful about.

  It confirms before doing anything, naming the version, the size and the source, because it
  downloads third-party native plugins and edits the project manifest. The manual steps stay
  in the window for anyone who would rather not. Tarballs are saved to `Packages/PuerTS/`
  rather than a temp folder, since the manifest stores the path.
- **The API key window says where the active key came from** — environment variable,
  EditorPrefs, or a key file. EditorPrefs is shared by every project on the machine, so a key
  entered once anywhere silently applies everywhere; arriving at a project already signed in
  with no memory of doing it is unsettling rather than convenient.

## [0.3.1] - 2026-08-13

### Fixed

- **The instructions for enabling local baking were not followable.** 0.3.0 said "install the
  PuerTS core and QuickJS packages" without saying from where. They are not in Unity's
  registry, and the route most people would try — OpenUPM — carries only
  `com.tencent.puerts.core`, at a version the QuickJS backend does not accept. The obvious
  path installs cleanly and then never works, with nothing on screen explaining why.

### Added

- **`Polyfork ▸ Make Bakes Instant…`**, a setup window that gives the real steps (both
  packages from the same GitHub release, added as tarballs), reads the installed versions
  back from the Package Manager, and calls out a version mismatch explicitly. It flips to a
  confirmation on its own once an engine registers.
- **A bake-path indicator in the gallery's status bar.** It reads `local bakes` when an
  engine is running, and offers *Make bakes instant* when one is not — so the difference
  between a metered 120 ms round trip and a free instant rebuild is visible rather than
  something you had to read the README to discover.

## [0.3.0] - 2026-08-13

### Added

- **Instant, unmetered bakes in the editor.** The gallery now goes through
  `PolyforkBakerRegistry` instead of calling the remix endpoint directly, which it had never
  done — local baking existed but only ever affected the runtime component, so every editor
  preview was a ~120 ms round trip against your allowance. With a JS engine installed the
  editor runs the asset's own `createAsset()` module: no request, no quota, no wait.

  Setup is installing the PuerTS core and QuickJS packages. Nothing else. Without them the
  binding assembly is not compiled at all and the server path runs exactly as before.

### Changed

- **Local baking moved out of `Samples~` and into the package, editor-only.** The engine
  binding declares `includePlatforms: ["Editor"]` and the ~336 KB three.js bundle now lives
  under `Editor/JS/`, so neither can reach a player build. A shipped game always uses the
  server baker.

  The bundle previously sat in a `Resources` folder, and Unity copies `Resources` into every
  player build whether anything references it or not — that payload was the whole reason
  local baking had been kept at arm's length as an opt-in sample. Editor-only removes the
  reason rather than working around it, so the *Local Baking* sample is gone; it is a
  feature now.
- `PolyforkJsRuntimeProvider` takes its scripts from a `ScriptSource` hook, set by the editor
  assembly, falling back to the old `Resources` path so an existing project keeps working.

## [0.2.3] - 2026-08-13

### Fixed

- **The welcome window told signed-in users they had no account.** It decided that from
  `PolyforkKeySettings.HasKey`, which only reads `EditorPrefs` — so a key supplied through
  `POLYFORK_API_KEY` or a `polyfork.key` file did not count, and a Founders user was shown
  the anonymous pitch directly above their own "900 bakes left this hour". Sign-in state
  now comes from the server's `authenticated`, which is the only thing that actually knows.
- The window no longer opens taller than its content, and reads a good deal warmer.

### Added

- **Locked assets are marked as locked.** `PolyforkAsset` now reads `owned` and `plan` from
  the catalogue, which it previously ignored entirely — it knew only `free`, so it could not
  tell an asset you had licensed from one you had not. Paid assets you do not own are dimmed
  in the grid, badged `locked`, and offer *Unlock with Pro* instead of *Import*.

  They still preview and still remix. The public preview GLB is what makes the catalogue
  browsable, so the line is drawn at **writing a file into `Assets/`**, not at looking.

### Changed

- Asset detail shows what you may do with an asset (`free` / `owned` / `included in Pro`)
  rather than a price. `price_usd` is retired server-side and now returns null for every
  paid asset, so the old `$1-3` label was inventing a number.

## [0.2.2] - 2026-08-13

### Fixed

- **The menu made the package look broken.** The gallery lived under
  `Window ▸ Polyfork ▸ Browse Assets`, while the local-baking smoke test created its own
  top-level `Polyfork` menu — so the only entry under `Polyfork` was
  `4. Smoke-test local baking`, step 4 of a numbered workflow whose steps 1-3 lived in the
  XR showcase and left with it. Everything now sits under one `Polyfork` menu:

  ```
  Polyfork ▸ Browse Assets            (Ctrl/Cmd + Shift + P, also under Window ▸ Polyfork)
  Polyfork ▸ API Key…
  Polyfork ▸ Welcome
  Polyfork ▸ Diagnostics ▸ Smoke-test local baking
  ```

  The smoke test is greyed out unless a JS engine is actually installed, rather than
  offering to test something the project cannot do.

### Added

- **A welcome window**, shown once per project on first import and reopenable from
  `Polyfork ▸ Welcome`. It answers the question a new user actually has — *do I need an
  account?* — with **Add an API key** and **Continue free** side by side, since browsing,
  previewing and importing all work with no key at all.

  The allowance it quotes is read live from `GET /api/me`, not written into the window, for
  the same reason the knobs are read from the schema: those numbers belong to the server,
  and a hardcoded "40 an hour" becomes wrong the first time pricing moves. It stays quiet in
  batch mode, so a CI run cannot hang on a modal nobody is there to click.

## [0.2.1] - 2026-08-13

### Fixed

- **Two assets were being ignored on import.** Unity cannot write a missing `.meta` inside
  an immutable package, so it skips the asset and says so:
  `… has no meta file, but it's in an immutable folder. The asset will be ignored.`
  - `Runtime/Resources/` held nothing but `Polyfork.meta`, an orphan left behind when local
    baking moved to `Samples~/LocalBaking`. The folder it described was already gone. Both
    are now removed; the JS payload lives in the sample, which is where it belongs, and
    `PolyforkJsRuntimeProvider` already falls back to server baking when it is absent.
  - `HANDOFF.md` moved to `Documentation~/`. It is a maintainer document and has no business
    being imported into a consumer's project as a `TextAsset`; the trailing `~` is how Unity
    is told to leave a folder alone.

### Added

- **A CI check for exactly this** (`Tools~/check-package.py`, run by
  `.github/workflows/package-check.yml`). It walks the package the way Unity does, skipping
  dot- and tilde-paths, and fails on any missing or orphaned `.meta`, plus a `package.json`
  whose declared sample paths do not resolve. No Unity licence needed.

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
