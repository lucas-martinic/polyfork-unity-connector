using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Polyfork.Tests
{
    /// <summary>
    /// Offline tests over a real /cdn/{id}-params.json payload.
    ///
    /// These pin the rules that make the integration honest: which knobs can be applied,
    /// how, and which are deliberately hidden. They need no network and no XR packages,
    /// so they also prove the package stands alone.
    /// </summary>
    public class PolyforkSchemaTests
    {
        /// <summary>Trimmed but verbatim schema for plastic-drum-da992f.</summary>
        const string DrumParams = @"{
""params"":{
 ""colorway"":{""type"":""choice"",""default"":""chemical-blue"",""label"":""Colorway"",
   ""describe"":""Curated kit-coherent scheme."",
   ""options"":[""chemical-blue"",""kerosene-red"",""coolant-green"",""food-cream""]},
 ""body"":{""type"":""color"",""default"":""#8FB4C9"",""label"":""Body"",""describe"":""Shell albedo.""},
 ""lid"":{""type"":""color"",""default"":""#4E5459"",""label"":""Head"",""describe"":""Head disc albedo.""},
 ""bung"":{""type"":""color"",""default"":""#1B1D20"",""label"":""Bung caps"",""describe"":""Cap albedo.""},
 ""tallness"":{""type"":""range"",""default"":0.9,""label"":""Tallness"",""describe"":""Height in metres."",
   ""affects"":""geometry"",""min"":0.7,""max"":1.12},
 ""facets"":{""type"":""range"",""default"":14,""label"":""Facets"",""describe"":""Flat vertical faces."",
   ""affects"":""geometry"",""min"":8,""max"":15},
 ""taper"":{""type"":""range"",""default"":0,""label"":""Taper"",""describe"":""Wall narrowing."",
   ""affects"":""geometry"",""min"":0,""max"":0.26}},
""presets"":{
 ""chemical-blue"":{""body"":""#8FB4C9"",""lid"":""#4E5459"",""bung"":""#1B1D20""},
 ""kerosene-red"":{""body"":""#B5462F"",""lid"":""#4E5459"",""bung"":""#1B1D20""},
 ""coolant-green"":{""body"":""#3F8A5E"",""lid"":""#4E5459"",""bung"":""#1B1D20""},
 ""food-cream"":{""body"":""#E4E2DC"",""lid"":""#5B6E8C"",""bung"":""#2E3134""}},
""rev"":1785684234}";

        /// <summary>Road tile: has structural choice/toggle knobs that must stay hidden.</summary>
        const string RoadParams = @"{
""params"":{
 ""piece"":{""type"":""choice"",""default"":""straight"",""label"":""Piece"",""describe"":""Tile shape."",
   ""affects"":""geometry"",""options"":[""straight"",""corner"",""t-junction"",""crossroads"",""end""]},
 ""lines"":{""type"":""toggle"",""default"":false,""label"":""Lines"",""describe"":""Painted lines."",""affects"":""geometry""},
 ""colorway"":{""type"":""choice"",""default"":""city-asphalt"",""label"":""Colorway"",""describe"":""Scheme."",
   ""options"":[""city-asphalt"",""fresh-blacktop""]},
 ""asphalt"":{""type"":""color"",""default"":""#3C4145"",""label"":""Asphalt"",""describe"":""Road albedo.""},
 ""paint"":{""type"":""color"",""default"":""#E4E2DC"",""label"":""Paint"",""describe"":""Marking albedo.""},
 ""patchCount"":{""type"":""range"",""default"":0,""label"":""Patch count"",""describe"":""Repairs."",
   ""affects"":""geometry"",""min"":0,""max"":10}},
""presets"":{
 ""city-asphalt"":{""asphalt"":""#3C4145"",""paint"":""#E4E2DC""},
 ""fresh-blacktop"":{""asphalt"":""#2E3134"",""paint"":""#F2EFE7""}},
""rev"":1}";

        static PolyforkParams Drum() => PolyforkParams.Parse("plastic-drum-da992f", DrumParams);
        static PolyforkParams Road() => PolyforkParams.Parse("asphalt-road-tile-f6593c", RoadParams);

        [Test]
        public void ParsesEveryKnobAndPreset()
        {
            var schema = Drum();
            Assert.AreEqual(7, schema.Knobs.Count);
            Assert.AreEqual(4, schema.PresetNames.Count);
            Assert.AreEqual("Tallness", schema.Knobs["tallness"].Label);
            Assert.AreEqual("Bung caps", schema.Knobs["bung"].Label);
        }

        [Test]
        public void RangeKnobsAreServerRebuilt()
        {
            var schema = Drum();
            foreach (var name in new[] { "tallness", "facets", "taper" })
            {
                Assert.AreEqual(PolyforkKnobSupport.ServerRebuild, schema.Knobs[name].Support,
                    $"'{name}' is a geometry range knob, which the remix endpoint bakes.");
            }
        }

        /// <summary>
        /// The server reads a missing "affects" as "colors" (inc/remix.php, remix_geo_params),
        /// so a range knob that never says "geometry" is dropped. Sending it anyway would
        /// spend a bake on a URL that returns the baseline mesh.
        /// </summary>
        [Test]
        public void ARangeThatDoesNotDeclareGeometryIsNotSentToTheServer()
        {
            const string json = @"{""params"":{
 ""wobble"":{""type"":""range"",""default"":0,""label"":""Wobble"",""min"":0,""max"":1}},
""presets"":{},""rev"":1}";

            var schema = PolyforkParams.Parse("x", json);
            Assert.AreEqual(PolyforkKnobSupport.Unsupported, schema.Knobs["wobble"].Support);
            CollectionAssert.DoesNotContain(schema.Remixable.Select(k => k.Name).ToList(), "wobble");
        }

        [Test]
        public void ColorKnobsAreLocallyRecoloured()
        {
            var schema = Drum();
            foreach (var name in new[] { "body", "lid", "bung" })
                Assert.AreEqual(PolyforkKnobSupport.LocalRecolor, schema.Knobs[name].Support);
        }

        [Test]
        public void ColorwayIsLocalBecauseItsOptionsAreAllPresets()
        {
            Assert.AreEqual(PolyforkKnobSupport.LocalRecolor, Drum().Knobs["colorway"].Support);
            Assert.AreEqual(PolyforkKnobSupport.LocalRecolor, Road().Knobs["colorway"].Support);
        }

        /// <summary>
        /// Structural choice and toggle knobs are baked, and were verified against the live
        /// endpoint by hashing responses: on brick-church-6cf1af, towerHeight "12" and "18"
        /// and rose=false each return a distinct GLB. They were hidden here for a long time
        /// on the older assumption that only ranges were baked.
        /// </summary>
        [Test]
        public void StructuralChoiceAndToggleKnobsAreBakedByTheServer()
        {
            var schema = Road();

            Assert.AreEqual(PolyforkKnobSupport.ServerRebuild, schema.Knobs["piece"].Support);
            Assert.AreEqual(PolyforkKnobSupport.ServerRebuild, schema.Knobs["lines"].Support);

            var remixable = schema.Remixable.Select(k => k.Name).ToList();
            CollectionAssert.Contains(remixable, "piece");
            CollectionAssert.Contains(remixable, "lines");
            CollectionAssert.Contains(remixable, "patchCount");
            CollectionAssert.Contains(remixable, "colorway");
        }

        /// <summary>
        /// Seen live on regolith-terrain-blob-33148e and field-console-a92adc: "presets"
        /// lists only the ALTERNATIVE schemes, because the default one is the asset's
        /// authored colours and each colour knob already carries them. Demanding that every
        /// option name a preset hid the colourway entirely on those assets.
        /// </summary>
        [Test]
        public void AColorwayStaysLocalWhenItsDefaultOptionHasNoPreset()
        {
            const string json = @"{""params"":{
 ""colorway"":{""type"":""choice"",""default"":""rust-regolith"",""label"":""Colorway"",
   ""affects"":""colors"",""options"":[""rust-regolith"",""pale-dust"",""basalt-grey""]},
 ""dust"":{""type"":""color"",""default"":""#9a7b5f"",""label"":""Dust""}},
""presets"":{
 ""pale-dust"":{""dust"":""#c9b79a""},
 ""basalt-grey"":{""dust"":""#6b6b6b""}},
""rev"":1}";

            var schema = PolyforkParams.Parse("regolith-terrain-blob-33148e", json);

            Assert.AreEqual(PolyforkKnobSupport.LocalRecolor, schema.Knobs["colorway"].Support,
                "the colourway must stay usable, and must never cost a bake");
            Assert.IsTrue(schema.IsDefaultColorway(schema.Knobs["colorway"], "rust-regolith"),
                "the default option restores the authored colours rather than naming a preset");
            Assert.IsFalse(schema.IsDefaultColorway(schema.Knobs["colorway"], "pale-dust"));
        }

        [Test]
        public void IntegralRangesAreDetected()
        {
            var schema = Drum();
            Assert.IsTrue(schema.Knobs["facets"].IsIntegral, "facets is 8..15, whole numbers");
            Assert.IsFalse(schema.Knobs["tallness"].IsIntegral, "tallness is 0.7..1.12");
        }

        [Test]
        public void DefaultSlotColorsMatchThePublishedHexes()
        {
            var slots = Drum().DefaultSlotColors();

            Assert.IsTrue(PolyforkParams.TryParseHex("#8FB4C9", out var body));
            Assert.AreEqual(body, slots["body"]);
            Assert.AreEqual(3, slots.Count, "only colour knobs own a vertex-colour slot");
        }

        [Test]
        public void PresetExpandsToEverySlotItDefines()
        {
            Assert.IsTrue(Drum().TryGetPreset("food-cream", out var slots));
            Assert.AreEqual("#E4E2DC", slots["body"]);
            Assert.AreEqual("#5B6E8C", slots["lid"]);
            Assert.AreEqual("#2E3134", slots["bung"]);
        }

        [Test]
        public void RemixUrlCarriesRangeKnobs()
        {
            var client = new PolyforkClient();
            var url = client.RemixGlbUrl("plastic-drum-da992f",
                new System.Collections.Generic.Dictionary<string, float> { ["tallness"] = 1.12f });

            StringAssert.Contains("plastic-drum-da992f-remix.glb", url);
            StringAssert.Contains("tallness", url);
        }

        /// <summary>
        /// The server compares choice values with a strict in_array, so an option published
        /// as "12" does not match the number 12 - it falls through to the default and returns
        /// the baseline mesh, which on screen is indistinguishable from a broken control.
        /// </summary>
        [Test]
        public void ChoiceAndToggleKeepTheTypesTheSchemaPublished()
        {
            var values = new PolyforkKnobValues();
            values.SetChoice("towerHeight", "12");
            values.SetBool("rose", false);
            values.SetNumber("bays", 4f);

            var query = Uri.UnescapeDataString(new PolyforkClient().RemixGlbUrl("brick-church-6cf1af", values));

            StringAssert.Contains("\"towerHeight\":\"12\"", query, "a choice is sent as its literal option string");
            StringAssert.Contains("\"rose\":false", query, "a toggle is sent as a JSON boolean");
            StringAssert.Contains("\"bays\":4", query);
        }

        [Test]
        public void RemixUrlKeysAreOrderedSoTheSameVariantIsTheSameUrl()
        {
            var a = new PolyforkKnobValues();
            a.SetNumber("taper", 0.1f);
            a.SetNumber("facets", 12f);

            var b = new PolyforkKnobValues();
            b.SetNumber("facets", 12f);
            b.SetNumber("taper", 0.1f);

            var client = new PolyforkClient();
            Assert.AreEqual(client.RemixGlbUrl("x", a), client.RemixGlbUrl("x", b),
                "insertion order must not produce two URLs for one variant, or the cache misses");
        }

        /// <summary>
        /// Mirrors remix_snap() in inc/remix.php. The server snaps AFTER keying its cache,
        /// so an off-grid request pays for a bake an on-grid one would have got free.
        /// </summary>
        [Test]
        public void RangeValuesSnapToTheGridTheServerBakesOn()
        {
            var schema = Drum();

            // facets is 8..15: whole bounds, span 7, so a count-style knob with step 1.
            var facets = schema.Knobs["facets"];
            Assert.AreEqual(12f, facets.SnapToServerGrid(11.6f), 1e-4f);
            Assert.AreEqual(15f, facets.SnapToServerGrid(99f), 1e-4f, "clamped to max");
            Assert.AreEqual(8f, facets.SnapToServerGrid(-3f), 1e-4f, "clamped to min");

            // tallness is 0.7..1.12: fractional, so 40 steps of 0.0105.
            var tallness = schema.Knobs["tallness"];
            Assert.AreEqual(0.7f + 0.0105f, tallness.SnapToServerGrid(0.712f), 1e-4f);
            Assert.AreEqual(tallness.SnapToServerGrid(0.7106f), tallness.SnapToServerGrid(0.7109f),
                "two drags to nearly the same place must produce one variant, not two");
        }

        [Test]
        public void RemixUrlWithNoParamsIsTheBareEndpoint()
        {
            var url = new PolyforkClient().RemixGlbUrl("x");
            Assert.IsFalse(url.Contains("?"), "an empty param set should not add a query string");
        }

        [Test]
        public void PaletteParsesTheWeightedObjectForm()
        {
            // The catalogue publishes dominant colours as {hex, share}. An earlier shape was
            // a plain array of hex strings, and casting an object to string threw
            // "Can not convert Object to String" - which is how this was found.
            const string json = @"{
""id"":""x"",""title"":""X"",
""palette"":[{""hex"":""#479"",""share"":0.75},{""hex"":""#8FB4C9"",""share"":0.2}]}";

            var asset = PolyforkAsset.FromJson(json);

            Assert.AreEqual(2, asset.Palette.Length);
            Assert.AreEqual("#479", asset.Palette[0].Hex);
            Assert.AreEqual(0.75f, asset.Palette[0].Share, 1e-4f);
        }

        [Test]
        public void PaletteStillAcceptsThePlainStringForm()
        {
            const string json = @"{""id"":""x"",""title"":""X"",""palette"":[""#8FB4C9"",""#1B1D20""]}";

            var asset = PolyforkAsset.FromJson(json);

            Assert.AreEqual(2, asset.Palette.Length);
            Assert.AreEqual("#8FB4C9", asset.Palette[0].Hex);
        }

        [Test]
        public void DownloadFieldDecidesWhetherLocalBakingIsPossible()
        {
            // A free asset publishes its module to everyone; a paid one omits the field
            // entirely until a key is attached. HasModule is what a local baker gates on.
            const string free = @"{""id"":""a"",""free"":true,""download"":{
""glb"":""https://polyfork.dev/cdn/a.glb"",""mjs"":""https://polyfork.dev/cdn/a.mjs"",""auth"":""none""}}";
            const string paid = @"{""id"":""b"",""free"":false}";

            var freeAsset = PolyforkAsset.FromJson(free);
            Assert.IsTrue(freeAsset.HasModule);
            Assert.AreEqual("none", freeAsset.Download.Auth);

            var paidAsset = PolyforkAsset.FromJson(paid);
            Assert.IsNull(paidAsset.Download);
            Assert.IsFalse(paidAsset.HasModule, "no module means no local bake, so fall back to the server");
        }

        /// <summary>
        /// The catalogue retired price_usd: it is null on every paid asset now, with
        /// price_note pointing at `plan` instead. Ownership is what decides whether an asset
        /// can be written into a project, so it has to be read rather than guessed from a
        /// missing price.
        /// </summary>
        [Test]
        public void OwnershipComesFromTheCatalogueNotFromAPrice()
        {
            const string paid = @"{""id"":""brick-church-6cf1af"",""title"":""Brick Church"",
""free"":false,""plan"":""pro"",""price_usd"":null,""owned"":false,""remixable"":true}";

            const string owned = @"{""id"":""brick-church-6cf1af"",""title"":""Brick Church"",
""free"":false,""plan"":""pro"",""price_usd"":null,""owned"":true,""remixable"":true}";

            const string free = @"{""id"":""street-lamp-29f365"",""title"":""Street Lamp"",
""free"":true,""plan"":""free"",""owned"":false,""remixable"":true}";

            var locked = PolyforkAsset.FromJson(paid);
            Assert.AreEqual("pro", locked.Plan);
            Assert.IsFalse(locked.Owned);
            Assert.IsTrue(locked.Locked, "a paid asset nobody here has bought is not importable");

            Assert.IsFalse(PolyforkAsset.FromJson(owned).Locked, "owning it unlocks it");
            Assert.IsFalse(PolyforkAsset.FromJson(free).Locked, "a free asset is never locked");
        }

        [Test]
        public void SizeIsReadAsAVectorNotAScalar()
        {
            // size_m is {x,y,z} on the detail endpoint. Typing it as a float silently
            // dropped it; a scalar from the older shape is treated as a uniform extent.
            var boxy = PolyforkAsset.FromJson(@"{""id"":""a"",""size_m"":{""x"":4,""y"":3.6,""z"":4}}");
            Assert.IsNotNull(boxy.SizeMeters);
            Assert.AreEqual(3.6f, boxy.SizeMeters.Value.y, 1e-4f);

            var scalar = PolyforkAsset.FromJson(@"{""id"":""b"",""size_m"":2}");
            Assert.AreEqual(new Vector3(2f, 2f, 2f), scalar.SizeMeters.Value);

            Assert.IsNull(PolyforkAsset.FromJson(@"{""id"":""c""}").SizeMeters);
        }

        [Test]
        public void ModuleTransformDefersExportsToTheEnd()
        {
            // The bug this pins: assigning an export inline after its opening line lands the
            // assignment inside a multi-line object literal, and the module fails to parse.
            const string source = @"import * as THREE from 'three';
export const params = {
  tallness: { type: 'range', min: 0.7, max: 1.12 }
};
export function createAsset(p) { return null; }";

            var script = PolyforkModuleTransform.ToScript(source);
            var lines = script.Split('\n');

            var openIndex = Array.FindIndex(lines, l => l.Contains("const params = {"));
            Assert.Greater(openIndex, -1, "the declaration should survive with export stripped");

            // Search after the declaration: the preamble `var __exports = {};` also ends "};".
            var closeIndex = Array.FindIndex(lines, openIndex + 1, l => l.TrimEnd().EndsWith("};"));
            var assignIndex = Array.FindIndex(lines, l => l.Contains("__exports.params = params;"));

            Assert.Greater(closeIndex, openIndex, "the object literal should close after it opens");
            Assert.Greater(assignIndex, closeIndex,
                "the export assignment must come after the literal closes, not inside it");

            StringAssert.DoesNotContain("import ", script, "imports are dropped; THREE is injected");
            StringAssert.Contains("__exports.createAsset = createAsset;", script);
        }

        [Test]
        public void ModuleTransformHandlesExportLists()
        {
            var script = PolyforkModuleTransform.ToScript("const A = 1; const B = 2;\nexport { A, B as Renamed };");

            StringAssert.Contains("__exports.A = A;", script);
            StringAssert.Contains("__exports.Renamed = B;", script);
        }

        [Test]
        public void ModuleTransformFlagsUnsupportedImports()
        {
            // Only three is provided. Anything else should leave a trace rather than
            // silently producing a module with an unbound identifier.
            var script = PolyforkModuleTransform.ToScript("import { thing } from 'some-other-lib';\nexport const x = 1;");

            StringAssert.Contains("dropped unsupported import", script);
            StringAssert.DoesNotContain("some-other-lib", script.Replace("dropped unsupported import", ""));
        }

        [Test]
        public void MeshPayloadDecodesWhatTheBridgeProduces()
        {
            // One triangle, non-indexed, with vertex colours - the shape every Polyfork
            // asset arrives in. Buffers are base64 Float32 so a bake costs one marshal.
            var positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f };
            var colors = new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };

            string B64(float[] f)
            {
                var bytes = new byte[f.Length * 4];
                System.Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
                return System.Convert.ToBase64String(bytes);
            }

            var json = $@"{{""meshes"":[{{
""name"":""tri"",""vertexCount"":3,
""matrix"":[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1],
""positions"":""{B64(positions)}"",""colors"":""{B64(colors)}"",
""normals"":null,""indices"":null}}]}}";

            var payload = PolyforkMeshPayload.Parse(json);

            Assert.AreEqual(1, payload.Meshes.Count);
            Assert.AreEqual(3, payload.TotalVertices);
            Assert.AreEqual(1, payload.TotalTriangles, "no index buffer means one triangle per three vertices");

            var entry = payload.Meshes[0];
            Assert.AreEqual(new Vector3(1f, 0f, 0f), entry.Positions[1]);
            Assert.AreEqual(Color.green, entry.Colors[1]);
            Assert.IsNull(entry.Indices, "Polyfork geometry is non-indexed, which is what keeps facets flat");
        }

        [Test]
        public void EmptyPayloadIsHandledRatherThanThrowing()
        {
            Assert.AreEqual(0, PolyforkMeshPayload.Parse(null).Meshes.Count);
            Assert.AreEqual(0, PolyforkMeshPayload.Parse("{}").Meshes.Count);
            Assert.AreEqual(0, PolyforkMeshPayload.Parse(@"{""meshes"":[]}").Meshes.Count);
        }

        [Test]
        public void ShorthandHexExpandsCorrectly()
        {
            // #479 must mean #447799, not fail to parse. The catalogue uses this form.
            Assert.IsTrue(PolyforkParams.TryParseHex("#479", out var shorthand));
            Assert.IsTrue(PolyforkParams.TryParseHex("#447799", out var full));

            Assert.AreEqual(full.r, shorthand.r, 1e-5f);
            Assert.AreEqual(full.g, shorthand.g, 1e-5f);
            Assert.AreEqual(full.b, shorthand.b, 1e-5f);
        }

        [Test]
        public void HexParsingIsExact()
        {
            Assert.IsTrue(PolyforkParams.TryParseHex("#8FB4C9", out var c));
            Assert.AreEqual(0x8F / 255f, c.r, 1e-5f);
            Assert.AreEqual(0xB4 / 255f, c.g, 1e-5f);
            Assert.AreEqual(0xC9 / 255f, c.b, 1e-5f);

            Assert.IsFalse(PolyforkParams.TryParseHex("8FB4C9", out _), "missing #");
            Assert.IsFalse(PolyforkParams.TryParseHex(null, out _));
        }

        /// <summary>Verbatim shape of GET /api/me for an unauthenticated caller.</summary>
        const string AnonAccess = @"{
""authenticated"":false, ""plan"":""anonymous"",
""access"":{
  ""as"":""anonymous"",
  ""remix_bakes_per_hour"":40, ""remix_bakes_left_this_hour"":38,
  ""remix_bakes_per_month"":100, ""remix_bakes_left_this_month"":99,
  ""remix_allowance_resets"":""2026-09-01"",
  ""remix_allowance_note"":""A free account raises this to 300 a WEEK, and costs an email address.""}}";

        /// <summary>A free key uses a weekly window instead of a monthly one.</summary>
        const string FreeAccess = @"{
""authenticated"":true, ""plan"":""free"",
""access"":{
  ""as"":""free"",
  ""remix_bakes_per_hour"":100, ""remix_bakes_left_this_hour"":100,
  ""remix_bakes_per_week"":300, ""remix_bakes_left_this_week"":12}}";

        /// <summary>Pro publishes a rate but the longer window is the string "uncapped".</summary>
        const string ProAccess = @"{
""authenticated"":true, ""plan"":""pro"",
""access"":{ ""as"":""pro"",
  ""remix_bakes_per_hour"":900, ""remix_bakes_left_this_hour"":900,
  ""remix_bakes_per_week"":""uncapped - a free account gets a weekly allowance, Pro does not""}}";

        [Test]
        public void AccessParsesTheAnonymousTier()
        {
            var a = PolyforkAccess.Parse(AnonAccess);

            Assert.IsFalse(a.Authenticated);
            Assert.AreEqual("anonymous", a.Plan);
            Assert.AreEqual(38, a.BakesLeftThisHour);
            Assert.AreEqual("month", a.PeriodName);
            Assert.AreEqual(99, a.BakesLeftThisPeriod);
            Assert.AreEqual(38, a.Remaining, "the tighter of the two windows governs");
        }

        [Test]
        public void AccessPicksUpTheWeeklyWindowOnAFreeKey()
        {
            var a = PolyforkAccess.Parse(FreeAccess);

            Assert.AreEqual("week", a.PeriodName);
            Assert.AreEqual(12, a.Remaining, "the weekly remainder is tighter than the hourly one");
        }

        [Test]
        public void AccessTreatsAnUncappedWindowAsNoLimit()
        {
            // "uncapped" arrives as prose, not a number; it must not parse as zero.
            var a = PolyforkAccess.Parse(ProAccess);

            Assert.IsTrue(a.PeriodUncapped);
            Assert.AreEqual(900, a.Remaining, "only the hourly rate constrains Pro");
        }

        [Test]
        public void BudgetAssumesTheFloorBeforeItHasSynced()
        {
            var budget = new PolyforkRemixBudget();

            Assert.IsFalse(budget.Synced);
            Assert.AreEqual(PolyforkRemixBudget.UnknownFloor, budget.Effective,
                "an unreachable /api/me must not be read as plenty");
            Assert.AreEqual(0, budget.PrewarmAllowance,
                "nothing speculative should happen on an unknown allowance");
        }

        [Test]
        public void BudgetTracksTheServerFigureAndSpendsDown()
        {
            var budget = new PolyforkRemixBudget();
            budget.SyncFrom(PolyforkAccess.Parse(AnonAccess));

            Assert.AreEqual(38, budget.Effective);
            Assert.IsTrue(budget.TryConsume());
            Assert.AreEqual(37, budget.Effective);
        }

        [Test]
        public void BudgetReservesHeadroomForInteractiveEdits()
        {
            var budget = new PolyforkRemixBudget();
            budget.SyncFrom(PolyforkAccess.Parse(FreeAccess));   // 12 left

            Assert.AreEqual(12 - PolyforkRemixBudget.InteractiveReserve, budget.PrewarmAllowance,
                "prewarm may only use what is above the reserve");
            Assert.IsTrue(budget.IsLow, "12 is at or below the warning threshold");
        }

        [Test]
        public void BudgetRefusesWhenSpentAndAfterA429()
        {
            var budget = new PolyforkRemixBudget();
            budget.SyncFrom(PolyforkAccess.Parse(FreeAccess));   // 12 left

            for (var i = 0; i < 12; i++) Assert.IsTrue(budget.TryConsume(), $"bake {i + 1} of 12");
            Assert.IsFalse(budget.TryConsume(), "the thirteenth exceeds the allowance");
            Assert.IsTrue(budget.IsExhausted);

            var fresh = new PolyforkRemixBudget();
            fresh.SyncFrom(PolyforkAccess.Parse(ProAccess));
            Assert.IsFalse(fresh.IsExhausted);
            fresh.MarkExhausted(TimeSpan.FromMinutes(5));
            Assert.IsTrue(fresh.IsExhausted, "a server 429 outranks the local mirror");
        }

        [Test]
        public void BudgetIsUnlimitedOnlyWhenNoWindowIsPublished()
        {
            var budget = new PolyforkRemixBudget();
            budget.SyncFrom(new PolyforkAccess());               // no figures at all

            Assert.IsTrue(budget.Unlimited);
            for (var i = 0; i < 100; i++) Assert.IsTrue(budget.TryConsume());
        }

        [Test]
        public void CredentialsRedactionNeverLeaksTheKey()
        {
            const string key = "pf_live_0123456789abcdef";
            var shown = PolyforkCredentials.Redact(key);

            Assert.IsFalse(shown.Contains("0123456789"), "the middle must not be printable");
            StringAssert.StartsWith("pf_liv", shown);
            Assert.AreEqual("(none)", PolyforkCredentials.Redact(null));
        }
    }
}
