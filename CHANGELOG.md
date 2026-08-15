# Changelog

All notable changes to this package are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.15.1] - 2026-08-14

### Fixed

- **The model only rebuilt when you let go of the slider.** The rebuild was kicked from
  `EditorApplication.update`, and dragging an IMGUI control runs a drag loop that starves the
  editor tick — so nothing started until the drag ended. The bake was never the problem: it
  measures 20 to 140 ms end to end. It simply was not being asked for until too late. `OnGUI`
  now kicks a pending rebuild as well, so the geometry follows the slider.

- **Camera damping was frame-rate dependent**, and the editor is not 60fps. A fixed fraction
  per editor tick made a glide that ran for as long as the ticks took, which read as the whole
  preview having got slower. It is exponential smoothing against real elapsed time now, so it
  settles in about a sixth of a second whatever the tick rate.

## [0.15.0] - 2026-08-14

### Fixed

- **Rigged assets cast no shadow.** The shark had none and the sea anemone did, and the
  difference was the rig rather than anything about either model: the planar shadow was applied
  by walking `MeshRenderer`, and a rigged asset draws through a `SkinnedMeshRenderer`, which is
  a `Renderer` but not a `MeshRenderer`. Every character and fish in the catalogue was skipped.

- The same blind spot leaked a mesh per rebuild for those assets: the cleanup freed meshes
  found through `MeshFilter`, and a rigged asset keeps its mesh on the renderer.

### Changed

- **The preview camera now moves like the one on polyfork.dev.** `viewer.js` runs OrbitControls
  with `enableDamping`, and this moved the camera instantly and stopped dead, which is most of
  why dragging a model felt different in the editor.

  Input now sets a target the camera eases towards, driven from the window's editor tick so the
  glide costs repaints only while it is gliding. A drag of the preview's full height is a full
  revolution, as OrbitControls does it, rather than a fixed degrees-per-pixel that felt
  different in a small preview from a large one. Zoom is clamped to 0.35x–2.5x the framing
  distance, which is what `viewer.js` sets `minDistance` and `maxDistance` to.

## [0.14.3] - 2026-08-14

### Fixed

- **The shader fallback chain used `??`, which Unity's null does not answer to.** Unity
  overloads `==` so a destroyed object reports as null; `??` does not use that overload. A
  `Shader.Find` coming back destroyed-but-not-null therefore won the chain and produced a
  material whose shader has no `ShadowCaster` pass — an object that renders and casts nothing.
  The same trap turned an `AddComponent` into a prefab that never got written earlier today.

  It now tests each candidate with `==`, and says so in the Console when it falls back, because
  losing the package's own vertex-colour shader costs both the colours and the shadow and
  should not be silent.

## [0.14.2] - 2026-08-14

### Fixed

- **The gallery opened empty until you pressed Refresh.** `OnEnable` starts the catalogue
  load, and a window that Unity creates and immediately re-parents runs `OnDisable` between
  its two `OnEnable`s — which cancels that request through `_cts`. The cancellation is caught
  and ignored, correctly, and then nothing ever asked again.

  Rather than chase every way a first attempt can be lost, the window now tracks whether the
  catalogue has been *answered*, success or failure, and `OnGUI` asks again while it has not
  been. A real failure counts as settled, so a network that is down does not become a retry
  loop; Refresh is still there for when it comes back.

### Changed

- **The bake timing was measuring a tenth of the wait.** The status bar showed the baker's own
  figure, which covers running the module and decoding its payload — tens of milliseconds — and
  everything after that was unmeasured: building the meshes, swapping the preview target,
  freeing the previous ones. So a rebuild that took about a second still read as "21 ms", which
  is worse than showing nothing.

  It now reports the end-to-end time, with the split in the tooltip where it answers the next
  question instead of being mistaken for the answer.

## [0.14.1] - 2026-08-14

### Fixed

- **The dependency check cried wolf on a working install.** It compared AppDomain assembly
  names against the names our asmdefs reference, which is a different question with a different
  answer: `com.unity.nuget.newtonsoft-json` ships its code as a precompiled `Newtonsoft.Json`
  assembly, so the asmdef reference `Unity.Nuget.Newtonsoft-Json` resolves at compile time while
  no assembly by that name is ever loaded. A correctly installed project was told it was broken,
  which is worse than not checking at all.

  It asks the package manager now, for the package names it tells you to install, and says
  nothing when no packages are registered yet.

### Changed

- **The submission compiles clean.** Vendored PuerTS carries two warnings of its own, an unused
  catch variable in `JsEnv` and an unused field in `PathHelper`, and the Asset Store expects a
  submission without them. A `csc.rsp` beside the vendored asmdef silences them for **that
  assembly only**, so our own code keeps every warning it had. The alternative was editing
  vendored source, which is the first step towards a fork nobody signed up to maintain.

- Our two `JsEnv` obsolete warnings are suppressed at the two lines that raise them, and the
  reason is written next to them: `ScriptEnv` is not a rename. `JsEnv`'s constructor checks the
  native papi version against what the managed code expects and calls
  `PuertsNative.SetLogCallback`, which is what puts a JS `console.log` or exception into Unity's
  Console. Constructing a `ScriptEnv` directly would trade a warning for silent JS errors.

## [0.14.0] - 2026-08-14

### Changed

- **The package id is now `dev.polyfork.unity-connector`**, was `dev.polyfork.connector`, to
  match the product namespace actually claimed in the Publisher Portal.

  The bare segment `dev.polyfork.unity` is refused with *"This namespace is already in use by
  another product"* — impossible under a publisher-scoped namespace, so it is a reserved word
  rather than a collision. **`dev.polyfork.unity-connector` is accepted.** So the reservation
  is on the exact segment, not on any segment containing the word, which is the narrower and
  correct reading of rules 5.1.b and 2.5.a.

### Upgrading

- **Remove the package and add the git URL again**, from `com.polyfork.connector` (0.12.x and
  earlier) or `dev.polyfork.connector` (0.13.0). Unity keys a manifest entry by the package's
  own name, so neither resolves this one and `Polyfork ▸ Update Package` cannot cross a rename.

  Assembly names, C# namespaces and every asset GUID are untouched, so scenes, prefabs and
  script references survive it. The repository keeps its name, so the git URL is unchanged.

## [0.13.0] - 2026-08-14

### Changed

- **The package id is now `dev.polyfork.connector`**, was `com.polyfork.connector`.

  Not a preference. Asset Store UPM publishing derives the publisher namespace from the domain
  you verified, so `polyfork.dev` gives `dev.polyfork`, and the uploader rejects a package whose
  `name` does not match the reserved technical name. `com.polyfork.*` was always a small lie
  anyway: it claims a domain we do not own.

- Not `dev.polyfork.unity`: the portal rejects the bare `unity` segment.

  Renaming everywhere rather than only in the store build, because a store build that differs
  from the normal one in something load-bearing has already cost this project two review cycles.

### Upgrading

- **Remove the package and add the git URL again.** Unity keys a manifest entry by the
  package's own name, so an entry under `com.polyfork.connector` cannot resolve a package that
  now calls itself `dev.polyfork.connector`, and `Polyfork ▸ Update Package` cannot carry you
  across it.

  Nothing else moves. Assembly names, C# namespaces and every asset GUID are untouched, so
  scenes, prefabs and script references survive the rename.

## [0.12.3] - 2026-08-14

### Fixed

- **The test assembly tried to compile in consumer projects.** `Polyfork.Connector.Tests`
  references `UnityEngine.TestRunner`, `UnityEditor.TestRunner` and `nunit.framework.dll`, all of
  which come from `com.unity.test-framework`. With no define constraint it compiled wherever the
  package was installed, so a project without Test Framework got unresolved references from a
  package it never asked to test. It now carries the standard `UNITY_INCLUDE_TESTS` constraint,
  which Unity sets only for a package listed in the project's `testables` - so the tests still
  run for us and never compile for a buyer.

  Only ever bit UPM and git installs; `Tests/` was already excluded from the `.unitypackage`.

### Added

- `Tools~/make-upm-zip.py`, which packs a store build as a package folder for the Asset Store's
  UPM uploader. That uploader takes a package in the project rather than a file, so the artifact
  to hand over is something that drops into `<project>/Packages/`.

### Changed

- The store build no longer carries `.github` or `.gitignore`. Repo furniture, and a shipped
  package is not a repo.

## [0.12.2] - 2026-08-14

### Added

- **The Console now says which dependency is missing**, instead of leaving a wall of
  unresolved-reference errors. `Editor/Bootstrap/` is a tiny assembly that references
  **nothing**, which makes it the only part of the connector that still compiles when glTFast or
  Newtonsoft JSON is absent, and therefore the only part left that can explain why the rest did
  not. It reports and stops there; installing them itself would be the store's 2.5.1.e.

  This only ever fires for a `.unitypackage` install. That format carries no dependency
  information at all - it is a bag of files, not a manifest - so nothing resolves them. Package
  Manager installs, from the git URL or from the Asset Store, read `package.json` and fetch both
  before any of our code runs.

### Changed

- `Documentation~/ASSET-STORE.md` now leads with **submitting as a UPM package rather than a
  `.unitypackage`**, which is what makes declared dependencies install themselves for a buyer.
  UPM publishing is open to all tools, extensions and SDKs, `package.json` is already
  submission-ready, and the technical name to reserve is `com.polyfork.connector`.

## [0.12.1] - 2026-08-14

### Fixed

- **The `.unitypackage` could not be imported at all.** Unity's import dialog died before
  drawing anything:

  ```
  NullReferenceException
  UnityEditor.PackageImportTreeView.RecursiveComputeEnabledStateForFolders
  UnityEditor.PackageImportTreeView.ComputeEnabledStateForFolders
  ```

  `Tools~/make-unitypackage.py` wrote the three members of each asset - `asset`, `asset.meta`,
  `pathname` - but not the **directory member** for the GUID folder containing them. Unity's
  exporter writes one; a package Unity exported has an `isdir()` entry per GUID and ours had
  none.

  It hid because Python's tarfile creates parent directories implicitly on extract, so every
  check that read the package back agreed it was fine: parents present, GUIDs unique and
  matching their metas, no childless folders, no stray characters, every member readable.
  Rebuilding Unity's own tree-construction from its source and running it over the entry list
  also found nothing, because the fault was never in the entries. Only diffing against a
  package Unity actually exported showed it.

  Member order and file modes now follow that reference too. Verified structurally against it
  rather than against a round trip, which was the mistake the first time.

- A wrong diagnosis reached the README, the manual and the release notes: that the crash came
  from importing over an existing git-URL install. It did not - a fresh project crashed the
  same way. Installing both ways is still a bad idea, because the GUIDs match and the project
  ends up with two copies of every assembly and native plugin, but that is a compile problem
  and not this one.

### Added

- `Tools~/make-store-zip.py`, which turns a built package back into a plain folder tree.
  Unzip into `Assets/`, then let Unity export the `.unitypackage` itself, or point Asset Store
  Tools at `Assets/Polyfork` and upload from the project. Derived from the `.unitypackage` so
  the two cannot disagree about their contents.

## [0.12.0] - 2026-08-14

### Changed

- **Local baking needs no setup: the JavaScript engine ships inside the package.** PuerTS on
  QuickJS is vendored into `Editor/Puerts/Vendor/` by `Tools~/vendor-puerts.py`. Import the
  package and instant rebuilds are on.

  This started as an Asset Store problem. Rule 2.5.1.e reads *"Offerings must not
  programmatically add, update, or remove packages in user projects, except for packages
  included in the offering's own Asset Store product"* — and unlike 2.5.1.d next to it, it
  carries no exception for user consent, so a button the user chose to press was never a
  defence. The one-button installer had to go either way.

  Meta's XR SDK looks like a counterexample and is the opposite of one. Its All-in-One package
  pulls in eight dependencies and every one is `com.meta.*`: Meta's own packages, all in the
  same offering, **declared** in `package.json` rather than installed by a script. The tell is
  the Meta XR Simulator, the one piece that is not a dependency and must be installed
  separately, precisely because it is not part of that offering. The rule is declare, don't
  install, and you may only declare Unity's packages or your own. So PuerTS became ours.

  What is vendored is deliberately less than the whole engine, because PuerTS is built to ship
  a runtime to players and this one must never reach a build:

  | | |
  | --- | --- |
  | Managed source | verbatim, unedited — PuerTS resolves its own backends by string (`GetType("Puerts.BackendQuickJS")`) and its bootstrap calls `CS.Puerts.Utils`, so a namespace rename would break at run time rather than at compile time. The **assembly** is renamed to `Polyfork.Puerts` instead, which needs no edits. |
  | Natives | desktop x64 only: Windows, Linux, and a universal macOS build that covers Apple Silicon. Every one marked Editor-only. |
  | Dropped | Android, iOS, WebGL and OpenHarmony binaries (37 MB for platforms an editor-only feature cannot run on), the WSPPAddon websocket library (3.5 MB, referenced by nothing but its own P/Invoke declaration), and the IL2CPP wrapper generator with the ScriptedImporters that would have claimed `.mjs`, `.cjs` and `.lua` project-wide for every user. |

  Net 4.4 MB of native, 4.9 MB in total.

- `Polyfork ▸ Setup` is a status page rather than an installer: whether the engine started,
  and what to check when it did not.

### Removed

- The one-button PuerTS installer, the tarball reader that supported it, and roughly 400 lines
  of download, unpack and retry handling. There is nothing left to install.

### Upgrading

- **Remove `com.tencent.puerts.core` and `com.tencent.puerts.quickjs`** from any project that
  has them, along with a `PuerTS` folder beside `Assets` if an older setup window left one.
  Unity refuses to import two native plugins sharing a file name, so a project carrying both
  copies will not compile. `Polyfork ▸ Setup` detects this and says exactly what to remove.

### Fixed

- `Third Party Notices.md` gave PuerTS's licence as MIT. It is **BSD 3-Clause**. The full text
  is now reproduced there and shipped verbatim at `Editor/Puerts/Vendor/LICENSE-PuerTS.txt`,
  which is what clause 2 asks of a binary redistribution.

## [0.11.6] - 2026-08-14

### Fixed

- **`Play()` did not compile.** `PlayableGraph.Destroy()` tears down the whole graph and takes
  no argument, so passing the stale clip to it was a compile error rather than a wrong thing
  freed at run time. Destroying one node is `DestroyPlayable`.

- **Asset Store validation still failed on demo scenes and documentation**, because 0.11.5 put
  both behind `StoreExtras~/` and unpacked them only in the store build. The two artifacts then
  differed in exactly the files validation looks for, so whichever one you happened to import
  decided the result. `Demo/` and `Documentation/` are ordinary package folders now, present in
  the git install and in both `.unitypackage` builds.

  Reading the validator rather than guessing at them a second time also turned up what the
  documentation check actually wants: any `.txt`, `.pdf`, `.html`, `.rtf` or `.md` file in the
  paths you select, which either ends in `.pdf` or contains the word "documentation" somewhere
  in its text. The manual said "Manual" throughout and so failed a check it was written to
  pass. It now ships as `Polyfork-Manual.pdf` beside the HTML, and a PDF is accepted outright.

  The demo scene needed no change: the check accepts any scene whose root object count is not
  exactly an untouched camera-and-light pair, and this one has three.

- `Demo/` carries its own assembly definitions. Scripts in a UPM package that sit outside one
  are not compiled at all, so without them the scene's component would have been missing.

## [0.11.5] - 2026-08-14

### Added

- **A demo scene and an offline manual, for Asset Store validation.** The validator failed the
  submission on both: no demo scene found, and no documentation file in the accepted formats.

  `Demo/Polyfork Demo.unity` has a camera, a key light and an object whose Inspector lists the
  four setup steps with a button that opens the gallery — which is what the guidance asks of an
  editor extension. It ships without models on purpose: Polyfork browses a catalogue that lives
  online, so the thing that belongs in the scene is whatever the user imports into it.

  `Documentation/Polyfork-Manual.html` is twelve numbered sections with a table of contents.

  Both live under `StoreExtras~/` and are unpacked only by `make-store-package.py`. A git-URL
  install has no business getting a demo scene and a manual dropped into the project.

## [0.11.4] - 2026-08-14

### Added

- **Asset Store submission kit** in `Documentation~/ASSET-STORE.md`: the blockers, listing
  copy, image specs and a checklist.
- **`Tools~/make-store-package.py`**, which builds the store variant from this source and
  then *proves* it is one. Two shipped features are disqualifying on the store — the
  one-button PuerTS install and `Polyfork ▸ Update Package` both manipulate packages in a
  user's project, which submissions may not do. The script drops those files, cuts the
  regions marked `// <store-strip>`, then searches the result for the forbidden calls and for
  anything the strip left dangling, and fails if it finds either.

## [0.11.3] - 2026-08-14

### Fixed

- **`Cannot connect output 0, it is already connected`** when switching animation. The
  outgoing playable was being attached to mixer slot 0 while still attached to slot 1 — a
  playable's output is singular, so it has to be freed first. Both inputs now come off before
  either goes back on, and the clip from two switches ago is destroyed rather than left in
  the graph.
- **Poses no longer loop.** The pack ships cycles *and* poses, and `sad_pose` / `sneak_pose`
  animate **into** a pose from rest — so looping one snapped the character back to rest and
  started again, forever. The retarget marks poses as non-looping and playback honours the
  mark. Cycles still loop.

### Changed

- Dropped the component's `loop` field. Whether a clip loops is a fact about the clip, not a
  setting on the thing playing it, and one switch for a list containing both cycles and poses
  is wrong for half of them whichever way it is set.

## [0.11.2] - 2026-08-14

### Fixed

- **0.11.1 did not compile.** `CS0104: 'Object' is an ambiguous reference` — 0.11.1 added
  `using System;` to `PolyforkClipRetarget` for `StringComparison`, which put `System.Object`
  in scope alongside `UnityEngine.Object` and made the one bare `Object.DestroyImmediate`
  ambiguous. Qualified.

## [0.11.1] - 2026-08-14

### Fixed

- **Characters came apart in play mode.** The rebound clips carried every curve the source
  had, including an `m_LocalPosition` for each bone — and those values *are* the source
  skeleton's proportions, where xbot's elbow sits relative to its shoulder. Applied to a rig
  with different bone lengths they drag every joint to a position belonging to a different
  body, so the character does not animate wrongly, it shatters.

  Only rotations are bound now. A joint angle means the same thing on any skeleton with the
  same topology, which is precisely why it transfers and a position does not — and it is the
  work a Humanoid avatar would have done, if glTFast could produce one. Characters animate in
  place, which is what `applyRootMotion = false` wants anyway.

## [0.11.0] - 2026-08-14

### Added

- **`Polyfork ▸ Update Package`.** Checks the published version first and says so when you
  are already on it, rather than costing a domain reload to reinstall an identical commit.

  Updating clears this package's entry from `Packages/packages-lock.json` before re-adding
  the URL. That entry is why a git package goes stale: UPM records the exact commit it
  resolved and keeps using it, so re-adding the same URL resolves to the same commit and
  nothing appears to happen. Unity's own advice — remove the package and install it again —
  is the same operation with more steps and less certainty that you are allowed to.

## [0.10.1] - 2026-08-14

### Fixed

- **Importing a character wrote no prefab.** `GetComponent<Animator>() ?? AddComponent<...>()`
  is the classic Unity trap: `==` is overloaded to report a missing object as null, `??` is
  not, so the coalesce kept a component that exists only as far as C# is concerned and the
  next line threw `There is no 'Animator' attached`. The throw took the whole prefab with it,
  so the asset imported with no knobs and no animation.
- **The model appeared in the scene and hung around before vanishing.** The staging instance
  was `HideInHierarchy`, which hides it from the Hierarchy window while the Scene view draws
  it anyway — and the 2.8 MB clip pack was being downloaded while it sat there. The pack is
  now fetched before anything is instantiated, and the instance is created **inactive**
  rather than merely hidden.
- **`Light.shadowResolution is compatible only with the Built-In Render Pipeline`** on every
  preview under URP. Removed; the shadow is drawn geometry now, so the light's own shadow
  quality settles nothing.
- **Rigged assets no longer attempt a local bake.** Two independent failures, both on rigged
  models — `field-console-a92adc` returned a hierarchy with no meshes, `village-engineer-a44949`
  threw `TypeError: not a function at buildSkeleton`. The trimmed three.js bundle omits the
  skinning classes, so a module that builds a skeleton has nothing to build it with. They go
  straight to the server, which bakes them properly.

### Changed

- The API key window's first button reads **Create an API Key** rather than "Create an
  account": in a window whose job is to be given a key, the useful button is the one that
  produces one.
- **The "a key is already active" note moved below the field.** It only drew once a key
  resolved, so pasting one made it appear on the next repaint — and anything appearing above
  a text field shifts every control id under it, which is why the first paste threw
  `ArgumentOutOfRangeException` out of Unity's own paste handler and the second worked.

## [0.10.0] - 2026-08-14

### Added

- **Rigged assets arrive animated.** Importing a character now gives it an `Animator`, a
  `PolyforkCharacterAnimation` component and a set of clips bound to its own skeleton, with
  **idle playing by default** and a dropdown in the Inspector to try the others. No sample to
  import and nothing to configure.

  The clips are fetched once per project into `Assets/Polyfork/Animations`, rather than
  shipped in the package: 2.8 MB of Mixamo clips is a lot to put in every consumer's project,
  most of which import no characters.

  **They are bound, not retargeted, because they cannot be retargeted.** A Humanoid avatar is
  Unity's mechanism for playing a Mixamo clip on another rig, and glTFast has no Humanoid
  import — its maintainers say those importer settings *"would basically have to be
  rewritten"*. Binding works instead because the two skeletons are the same one: the packs use
  Mixamo's names with the `mixamorig:` prefix and the characters use them without, so each
  curve is re-pointed at the bone the character has. Verified against the live catalogue
  before building it: all 22 of `naval-officer`'s bones are driven by xbot's idle clip, none
  is missing, and the 45 leftover curves are fingers, eyes and toes.

  Idle is chosen by name, not index — the packs disagree on capitalisation and ordering, so
  anything positional starts a different animation depending on which pack was used.



### Added

- It plays through a `PlayableGraph` rather than an `AnimatorController`, so nothing has to
  author a controller asset and wire states before a character moves.

## [0.9.0] - 2026-08-14

### Fixed

- **An unmodified import now fetches the authored GLB, not the preview.** The catalogue is
  explicit that the preview is not the whole asset — *"hierarchy joined and names removed"* —
  and joined is what costs the rig, since posing a rigged part means finding it by name.
  Baked `AnimationClip`s are not in the preview at all. So when nothing has been changed and
  the caller may fetch the real file, it fetches the real file: named parts, real materials,
  and the clips.

  Measured on the live catalogue: character previews carry a skin and **zero** animations,
  while `forest-rabbit-ea2da0`'s download carries `Walk` and self-animating props carry
  `tick`.

### Added

- **A warning when a recolour is about to drop animation.** Recolouring re-exports the mesh
  and an export of an instantiated hierarchy carries no clips, so the import now counts them
  in the source file and says so rather than handing back a rabbit that has stopped walking.

## [0.8.2] - 2026-08-14

### Fixed

- **Rebuilding in the scene swapped the material for a preview shader.** A bake returns
  meshes wearing the baker's own material, and the local baker's is `Polyfork/Vertex Color`,
  which does its own lighting and ignores the scene's — so a rebuilt model went unlit and
  stopped being the glTFast material the import gave it. Rebuilds now keep whatever material
  the object is already wearing, which also means one you assigned yourself survives a knob
  change.

### Changed

- **Import puts the model in the scene**, in front of the scene view, selected and
  undoable. The model used to appear only as a side effect of the export staging it, which
  meant the one moment it was visible was the moment before it was thrown away. Placing it
  deliberately is both the obvious thing to want after pressing Import and the honest version
  of what was already happening.

## [0.8.1] - 2026-08-14

### Fixed

- **The model flashed into the scene during an import.** Exporting needs a real GameObject to
  export, a bake builds one in the open scene, and it was only destroyed after the export
  finished awaiting — so for a frame or two the model appeared in your scene and vanished,
  which looks like the import failed at the exact moment it succeeded. Staging objects are
  hidden the instant they exist now.

### Changed

- **The Inspector rebuilds on change; the Rebuild button is gone.** It only ever existed
  because nothing was watching for the change — it asked you to request a result you had
  already described. Slider drags are coalesced, and the wait is skipped entirely when the
  bake is local, since there is nothing to be gentle with.

## [0.8.0] - 2026-08-14

### Added

- **Imported assets stay editable in the scene.** Importing now also writes a prefab beside
  the `.glb` carrying a `PolyforkAssetLink`: the asset id and the knob values, as JSON. Drag
  the prefab in and the Inspector shows the knobs, with a **Rebuild** button that changes the
  model in place.

  Rebuilding replaces the meshes on the object, so its transform, children, colliders and
  anything else attached survive a knob change. It uses the same baker as the gallery, so
  with a local engine installed an Inspector rebuild costs nothing either.

  A prefab rather than the `.glb` because an imported model is rebuilt from its file on every
  import and a component added to it is discarded — the prefab is the only thing that can
  carry state. Previously an import froze a model: changing your mind meant finding the asset
  again, guessing the slider positions, importing a second copy and swapping it by hand.
- `PolyforkKnobValues.FromJson`, the inverse of `ToJson`. The round trip is what lets a value
  set outlive the window that made it.

## [0.7.1] - 2026-08-13

### Fixed

- **A locally imported remix arrived white.** glTFast's exporter drops vertex attributes it
  judges unused, and it judges by the material: *"vertex colors are discarded when the
  assigned material(s) do not use them."* A Polyfork asset keeps its entire appearance in
  `COLOR_0`, and the material carrying it is our own shader, which glTFast has never heard
  of — so the export threw away the only thing making the model look like anything, and the
  `.glb` landed in the project as untinted geometry.

  Both export paths now set `PreservedVertexAttributes = VertexAttributeUsage.Color`. The
  recolour path was exposed to the same rule and is fixed with it.

## [0.7.0] - 2026-08-13

### Changed

- **Thumbnails load smoothly while scrolling.** The downloads were always asynchronous; what
  was not paced was everything they triggered on arrival, all of it on the main thread:

  - **One repaint per thumbnail.** A repaint re-renders the 3D preview too, so twenty
    thumbnails landing meant twenty forced full redraws. Repaints are now coalesced to at
    most one every 80 ms.
  - **Every PNG decoded the moment it landed.** `LoadImage` decodes on the main thread, so a
    batch finishing together dropped a frame. Textures are now built two per editor tick —
    about 120 a second, faster than anyone scrolls and never a visible stall.
  - **No cap on concurrent downloads.** A flick down the catalogue opened a request per card,
    which makes nothing arrive sooner; it just puts the thumbnail you are looking at behind
    ninety you have already passed. Six at a time now, and the queue is a stack, so the most
    recently requested — the ones on screen — are served first.

## [0.6.3] - 2026-08-13

### Fixed

- **Importing a remixed free asset asked for an API key it did not need.** The import always
  bought its mesh from the remix endpoint, so it could be refused for want of allowance — on
  an asset the editor was, at that moment, rebuilding locally and for free on every slider
  move. The mesh in the preview and the mesh being imported are the same mesh; only one of
  them was metered.

  Import now goes through the same baker as the preview. When that is the local one, the
  module builds the asset here and glTFast writes it straight out: no download, no allowance,
  and colour already baked in because the module honours it. The server path is unchanged and
  still takes over if the local bake cannot produce the asset.
- **"It resets in about 26027 minutes."** True — the anonymous allowance is monthly, so
  `Retry-After` really is days — and unreadable. Past 36 hours it now names the date, past
  two hours it says hours.

## [0.6.2] - 2026-08-13

### Fixed

- **Some assets never previewed at all — "No preview", forever.** Two causes, both ours.

  A local bake that *threw* ended the preview. Only a `null` return fell back to the server;
  an exception propagated, got logged, and left an empty viewport. Rigged assets are the case
  that found this — `field-console-a92adc` has a `screen-head` rig, and the bridge returns its
  hierarchy without geometry, so the bake threw "produced no meshes" and the asset simply
  never appeared. Both failure modes now get the same second chance on the server.

  Separately, the rebuild was gated on the allowance even when the rebuild would not spend
  any. An asset at its defaults is a plain file fetch of the public preview GLB, so running
  out of bakes was stopping the gallery from displaying things that were free to display.
  The gate now asks whether *this* rebuild would be metered.

- **The local baker no longer retries an asset it has already failed on.** Otherwise every
  knob change on such an asset pays twice — a bake that cannot work, then the fetch that
  does — turning one bad asset into a permanently sluggish one. Session-scoped, so a fresh
  window gets to find out for itself.

## [0.6.1] - 2026-08-13

### Changed

- **The model is properly shaded, dark on the side away from the light.** The shader used a
  wrap term — `dot(n, l) * 0.5 + 0.5` — which maps the whole sphere into `[0,1]` and never
  lets anything go properly dark. Every face landed within a hair of every other and the
  model read as flat. It now uses real clamped `N·L`, so the far side falls to the
  hemisphere term alone: measured, the multiplier runs 1.28 facing the key light down to
  0.44 facing away, against 1.12 to 0.62 before.

  The three lights are the store viewer's, written into the shader rather than sampled from
  the scene, since the preview is an isolated utility scene with its own lighting and the
  point is for an asset to look the same here as on its store page.

## [0.6.0] - 2026-08-13

### Changed

- **The shadow is hard and shaped like the model.** The soft blob was a radial gradient — it
  always drew, but it was not the shape of anything. It is a planar projection now: the
  vertex shader flattens the mesh onto the ground along the light direction, so the
  silhouette is the model's own and the edge is as crisp as its geometry. Still drawn rather
  than cast, because `PreviewRenderUtility`'s real shadows cannot be relied on, and still
  pipeline-independent because it is only a mesh with a material.

  A stencil test keeps each pixel to a single draw. Without it a projected mesh blends
  against itself wherever the silhouette self-covers, and the shadow becomes a patchwork of
  darker blotches instead of one flat shape.

### Fixed

- **The camera started underneath the model.** The default orbit pitch was negative, and
  pitch is applied as `Euler(pitch, yaw, 0)`, so the camera began below looking up. It now
  starts slightly above, the way a product shot is framed.
- **The camera no longer drifts while you turn knobs.** It orbited `bounds.center`, read
  live, and bounds move whenever a knob changes the silhouette — so the camera chased its
  subject on every rebuild, which reads as the camera moving rather than the model changing.
  Framing is captured when an asset is opened and not touched again.

### Added

- **Escape returns to the catalogue** from the remix screen.

## [0.5.5] - 2026-08-13

### Fixed

- **The ground plane could not match the background, so it is gone.** It used a lit shader,
  and what a lit surface shows is albedo times lighting, never a flat colour — so setting its
  albedo to the sky colour still left a visible horizon under the key light. Unlit and
  exactly the sky colour, a plane is indistinguishable from no plane, so that is what this
  is: same picture, one fewer object, material and pipeline question. The contact shadow is
  what says the model is standing on something.

## [0.5.4] - 2026-08-13

### Fixed

- **The editor got heavier the longer the window stayed open.** Every bake creates a fresh
  `Mesh` per part and a `Material`, and destroying a `GameObject` destroys its components but
  not the assets they point at — so each rebuild leaked both. At 30-60 ms a bake with a
  slider being dragged, that is dozens of leaked objects a second. The preview now frees what
  it generated, guarded by `EditorUtility.IsPersistent` so nothing saved in the project is
  ever touched.
- **The window repainted on every editor tick while rate limited**, purely to advance a
  countdown — and a repaint re-renders the 3D preview, so it burned a core continuously, and
  hardest exactly when the allowance was spent and there was least to show. Four times a
  second now.
- **The contact shadow read as an orange disc.** It was warm-tinted, which turns orange when
  blended over the cream background, and its falloff held a flat plateau before fading, which
  drew a visible rim. Pure black now, falling off from the centre with no flat core.

### Added

- **Double-clicking an asset opens the remix screen**, which is what opening a thing means
  everywhere else in the editor. Single click still just selects and previews.

## [0.5.3] - 2026-08-13

### Fixed

- **Opening the gallery started a download for every thumbnail in the catalogue.** IMGUI lays
  out every row in a scroll view whether it is on screen or not, so 480 cards meant 480
  fetches at once — and the thumbnails you were looking at queued behind the ones you were
  not. Off-screen rows are still laid out, so the scrollbar stays honest, but they no longer
  ask for anything.
- **The shadow no longer depends on `PreviewRenderUtility` casting one.** It renders into its
  own scene and its shadow support is unreliable, producing none at all under a scriptable
  pipeline whatever the light says. The model now sits in a drawn contact shadow: a radial
  falloff computed in the fragment shader, no texture and no light, which draws the same on
  every machine. The `ShadowCaster` pass stays for wherever real shadows do work.
- **The ground is the colour of the background.** A ground that contrasts with the sky draws
  a line across the frame and turns a product shot into a diorama; matching them leaves only
  the shadow to say the model is standing on something.

## [0.5.2] - 2026-08-13

### Changed

- **The last bake's time is in the status bar**, reading `local · 34 ms`, with the split
  between running the module and decoding its output in the tooltip. 0.4.1 only logged this
  above 120 ms, which is useless for the actual complaint: "slower than it should feel" is
  not a threshold, and a bake sitting at 80 ms would say nothing at all. The warning stays
  for the genuinely slow ones.

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
