# Local Baking (optional)

Runs an asset's own `createAsset()` module in-process instead of calling the remix
endpoint, so geometry changes need no network and no quota.

**Most projects should not import this.** Server baking is the default for good reasons —
read the trade-offs below before adding a JavaScript engine to your build.

## Install

1. Add the PuerTS core and QuickJS packages to your project.
2. Import this sample.
3. Tick **Enable Local Baking** on the `PolyforkCatalog` component.

Without step 1 the sample's assembly does not compile at all — it is gated on
`com.tencent.puerts.quickjs` being present — and the connector silently keeps using the
server. Without step 2 the scripts are missing and the connector logs one warning, then
keeps using the server. Neither is a failure state.

## What you get

- **No quota.** The remix endpoint is limited per hour; local baking is not.
- **Offline.** Once the `.mjs` module is cached, geometry changes need no connection.
- **No round trip.** Server bakes cost a request; local bakes cost CPU.

## What it costs

- **~41.5 ms per bake on a Quest 3** (QuickJS, measured). At 72 Hz that is three dropped
  frames — a visible hitch if you bake during interaction. For comparison the same module
  in Node takes 0.36 ms; the gap is the interpreter, not the work.
- **A trimmed three.js build**, ~343 KB, shipped in your player. Asset modules are authored
  against the three.js geometry API, so evaluating one needs the library it imports. This
  payload is the reason local baking is a sample and not part of the package: importing it
  is opt-in, so projects that never bake locally never carry it.
- **Native binaries** for every platform you ship, via PuerTS.

## The middle option

If you want responsive sliders without a JS engine, use **vertex morphing** instead. The
connector measures whether a range knob is topology-preserving, and when it is, lerps
between two server bakes at about 0.05 ms — no engine, no payload, no quota per frame.
Measured on the live catalogue, 14 of 32 range knobs qualified.

```csharp
await remixable.MeasureMorphableKnobsAsync();
if (remixable.IsMorphable("width")) {
    // SetRange now interpolates locally instead of calling out
}
```

## Licensing

The bundled three.js build is MIT, © 2010-2026 three.js authors. Full notice in the
package's `Third Party Notices.md`. PuerTS is not redistributed here — you install it
yourself.
