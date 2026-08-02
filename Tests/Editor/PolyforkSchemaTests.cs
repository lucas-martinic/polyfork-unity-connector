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
                    $"'{name}' is a range knob and is the only kind the remix endpoint bakes.");
            }
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

        [Test]
        public void StructuralKnobsAreHiddenRatherThanDead()
        {
            var schema = Road();

            // The endpoint ignores these and they change topology, so they cannot be
            // emulated locally either. Showing them would be a control that does nothing.
            Assert.AreEqual(PolyforkKnobSupport.Unsupported, schema.Knobs["piece"].Support);
            Assert.AreEqual(PolyforkKnobSupport.Unsupported, schema.Knobs["lines"].Support);

            var remixable = schema.Remixable.Select(k => k.Name).ToList();
            CollectionAssert.DoesNotContain(remixable, "piece");
            CollectionAssert.DoesNotContain(remixable, "lines");
            CollectionAssert.Contains(remixable, "patchCount");
            CollectionAssert.Contains(remixable, "colorway");
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
        public void RemixUrlCarriesOnlyRangeKnobs()
        {
            var client = new PolyforkClient();
            var url = client.RemixGlbUrl("plastic-drum-da992f",
                new System.Collections.Generic.Dictionary<string, float> { ["tallness"] = 1.12f });

            StringAssert.Contains("plastic-drum-da992f-remix.glb", url);
            StringAssert.Contains("tallness", url);
        }

        [Test]
        public void RemixUrlWithNoParamsIsTheBareEndpoint()
        {
            var url = new PolyforkClient().RemixGlbUrl("x", null);
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
