# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.5.1] - 2026-08-13

### Added

- **Real-time shadows and a ground plane**, matching the store viewer: the model casts a soft
  shadow onto a pale plane sitting at its base. The shader gained a `ShadowCaster` pass —
  without one a mesh is never drawn into the shadow map, whatever the light is told to do,
  and URP looks for the same tag so one pass serves both pipelines. The plane deliberately
  uses whichever stock lit shader the project's pipeline ships, because *receiving* a shadow
  is the one thing that does not port between built-in and URP.

### Fixed

- **Knobs a local bake could honour were still hidden.** The gallery read
  `PolyforkKnob.Support`, which describes the *server* — and the server bakes only knobs
  marked `affects: geometry`, treating a missing `affects` as `colors`. A local baker runs
  the asset's own module and honours whatever that module declares.

  Large Coastal Boulder's `dampLine` is exactly this: a range knob with no `affects`, which
  the endpoint will not bake and the module turns perfectly well. The UI now asks the baker
  that would actually serve the asset, which is what `IPolyforkBaker.Supports` was for.

### Changed

- **The gallery panel lists what can be changed instead of offering to change it.** Live
  sliders beside the grid were a trap: every drag is a rebuild, shown in a panel too narrow
  to judge, while the thing you were doing was browsing. The knobs live on the remix screen.

## [0.5.0] - 2026-08-13

### Added

- **A remix screen.** Clicking *Remix this asset* gives the model the whole window with its
  controls beside it, and leaves the catalogue behind rather than squeezed alongside.
  Browsing and remixing are different jobs: a 340px column shared with a thumbnail grid
  served neither. The panel beside the grid stays a preview, as it should be.

### Changed

- **The preview matches the store viewer.** Same background (`#eceae6`), same key and rim
  lights, same 38° lens, taken from `public/viewer.js` on polyfork.dev — with soft shadows.
  It was a dark studio before, so an asset changed colour and mood between its store page and
  the editor, which invites the question of which one is the real asset.
- **Knobs respond immediately when bakes are local.** The 250 ms debounce exists to stop a
  slider drag becoming forty metered HTTP requests. A local bake is neither metered nor a
  request, so the wait bought nothing and cost exactly the smoothness the local path was
  installed for. The server path keeps its debounce.

## [0.4.1] - 2026-08-13

### Fixed

- **Locally baked models rendered grey.** All of a Polyfork asset's colour lives in `COLOR_0`
  — one material, no textures — and Unity's stock shaders discard vertex colour. `URP/Lit`,
  `URP/Simple Lit` and `Standard` all do. The `.glb` path looked right because glTFast
  supplies its own vertex-colour material; the local path had nothing equivalent, so it fell
  back to a shader that threw the colour away.

  The package now ships `Polyfork/Vertex Color`, a plain vertex/fragment shader that draws
  under both the built-in pipeline and URP. It stays out of player builds like the rest of
  local baking. The stock shaders remain a fallback, and now log a warning saying the model
  will look grey rather than leaving you to work it out.
- **Local bakes were slow.** The base64 bridge appended to a string four times per three
  bytes — tens of thousands of appends for a mesh, which an interpreter without rope strings
  charges full price for. It builds through a chunked array now.
- Bakes slower than 120 ms log the split between engine time and decode time, so the next
  slow one says which half to look at. Silent otherwise.

## [0.4.0] - 2026-08-13

### Fixed

- **An exhausted server allowance froze a window that did not need the server.** The preview
  rebuild was gated on `!IsRateLimited`, and the knobs were disabled by the same flag — so
  running out of remote bakes stopped the gallery showing anything or letting you touch a
  control, on a machine that could rebuild every free asset locally and instantly.

  Gating now asks what would actually happen to *this* asset: the resolved baker's
  `ConsumesAllowance`. A local bake spends nothing, so nothing is blocked.
- **The allowance is no longer reported when it does not govern.** With a local engine
  running, the status bar reads `local bakes - unmetered` instead of a remaining-bakes count,
  and the rate-limit banner does not appear at all. The number was true and irrelevant, which
  is worse than absent: it read as the reason the window was stuck.
- **A baker that produced nothing left an empty preview.** If the local baker cannot fetch an
  asset's module, or its bake yields no mesh, the server now gets a turn before giving up —
  slower and metered, but it always works. Both the fallback and a total failure are logged
  rather than silently showing nothing.

## [0.3.6] - 2026-08-13

### Fixed

- **A failing JS runtime reported almost nothing.** The warning logged `e.Message` and
  discarded the stack, so a real report read only

  ```
  QuickJS runtime failed to start (String reference not set to an instance of a String.
  Parameter name: s)
  ```

  which names neither the script nor the step. That message is
  `Encoding.UTF8.GetBytes(chunk)` inside PuerTS's `ScriptEnv.Eval` — something evaluated a
  **null script** — but nothing said which one. The full exception is logged now, and
  `PolyforkPuertsRuntime.Initialise` labels each step, so a failure names it: creating the
  environment, evaluating three.js, binding `__polyfork.bake`, and so on, with the script
  lengths included.
- Dropped the `UsingFunc`/`UsingAction` pre-registration calls, which are empty methods in
  PuerTS 3.x and only mattered for IL2CPP ahead-of-time wrappers on device.

## [0.3.5] - 2026-08-13

### Fixed

- **The one-button install could not work.** It handed Unity the release tarballs directly,
  and Unity's *add from tarball* expects npm's layout: one `package/` folder at the archive
  root. PuerTS ships archives rooted at `core/` and `quickjs/`, so Unity unpacked each to a
  temp directory, found no `package.json` at the top, and reported

  ```
  The file [C:\Users\…\Temp\.tmp-47508-WYY2f8GPZdJO\package.json] cannot be found
  ```

  which reads as a broken download rather than a wrong shape. PuerTS's own documentation
  says to extract first and add from disk; the installer now does that.

  It unpacks each archive into `<project>/PuerTS/<package-name>` and adds those folders.
  Inside the project because the manifest stores the path — a package unpacked into the
  system temp folder stops existing and takes the project with it on the next resolve — and
  staged on the same volume, because `Directory.Move` cannot cross drives and on Windows the
  temp folder frequently is one. Tarballs an earlier version left in `Packages/PuerTS/` are
  cleaned up.

### Added

- `PolyforkTar`, a minimal tar reader. Unity's runtime predates `System.Formats.Tar`, so
  gzip is available and tar is not. It is scoped to what these archives actually contain —
  files and directories, no links, every path short enough to need no long-name record — and
  refuses any entry that resolves outside the destination. The parsing was checked against
  both real archives before shipping: 122 and 25 files, every size matching.

## [0.3.4] - 2026-08-13

### Fixed

- **The setup window offered to install PuerTS again after installing it.** Adding a package
  triggers a domain reload, which wipes every field on an `EditorWindow` — so the window
  could not survive its own success, and reset to its opening state with no way to tell a
  finished install from one that never started. It now reads the project's
  `Packages/manifest.json` instead of remembering, and has a third state: *installed, waiting
  for Unity to finish compiling*, with the Console pointed at as the place a native-plugin
  failure would show.
- **The setup link on the welcome screen was styled as a `miniLabel`**, so it rendered as
  grey text in a corner and read as a footnote. It is a proper button now, and the window is
  taller when it is shown.

### Changed

- `Install PuerTS for me` is now a single large primary action stating the download size, and
  the yellow warning above it is gone: with both tarballs taken from one release the version
  mismatch it warned about cannot happen. The caveat survives as plain text under the manual
  steps, which is the only path it still applies to.

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
