using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Polyfork
{
    public enum PolyforkKnobType
    {
        Unknown = 0,
        Color,
        Range,
        Choice,
        Toggle
    }

    /// <summary>
    /// How a knob's value can actually be realised on this client.
    /// </summary>
    public enum PolyforkKnobSupport
    {
        /// <summary>Polyfork rebuilds the mesh server-side: refetch the remix GLB.</summary>
        ServerRebuild,

        /// <summary>Applied locally by recolouring vertex-colour slots. No network.</summary>
        LocalRecolor,

        /// <summary>
        /// Cannot be honoured: the remix GLB endpoint ignores it and it changes topology,
        /// so it cannot be emulated locally either. Hidden from remix UI rather than
        /// shown as a control that does nothing.
        /// </summary>
        Unsupported
    }

    /// <summary>
    /// One typed knob exactly as published in https://polyfork.dev/cdn/{id}-params.json.
    /// Every field here is Polyfork's own metadata; the connector never invents a parameter.
    /// </summary>
    public sealed class PolyforkKnob
    {
        public string Name { get; private set; }
        public PolyforkKnobType Type { get; private set; }
        public string Label { get; private set; }
        public string Describe { get; private set; }

        /// <summary>"geometry", "colors", or null.</summary>
        public string Affects { get; private set; }

        /// <summary>
        /// True when Polyfork rebuilds geometry for this knob.
        ///
        /// The server reads a missing "affects" as "colors" (inc/remix.php,
        /// remix_geo_params), so an unlabelled knob is NOT baked however numeric it looks.
        /// Only geometry knobs reach the baker; everything else is applied here.
        /// </summary>
        public bool AffectsGeometry =>
            string.Equals(Affects, "geometry", StringComparison.OrdinalIgnoreCase);

        public string Icon { get; private set; }

        public JToken DefaultValue { get; private set; }

        // range only
        public bool HasRange { get; private set; }
        public float Min { get; private set; }
        public float Max { get; private set; }
        public float Step { get; private set; }

        /// <summary>True when min/max/default are all whole numbers, e.g. "facets" 8..15.</summary>
        public bool IsIntegral { get; private set; }

        // choice only
        public IReadOnlyList<string> Options { get; private set; } = Array.Empty<string>();

        public PolyforkKnobSupport Support { get; internal set; } = PolyforkKnobSupport.Unsupported;

        public bool IsSupported => Support != PolyforkKnobSupport.Unsupported;

        public float DefaultFloat => DefaultValue?.Type is JTokenType.Integer or JTokenType.Float
            ? DefaultValue.Value<float>()
            : 0f;

        public bool DefaultBool => DefaultValue?.Type == JTokenType.Boolean && DefaultValue.Value<bool>();

        public string DefaultString => DefaultValue?.Type == JTokenType.String ? DefaultValue.Value<string>() : null;

        /// <summary>
        /// Snaps a range value onto the grid the server bakes on.
        ///
        /// This mirrors remix_snap() in inc/remix.php exactly, and the match matters for
        /// money rather than looks: the server canonicalises before it keys its cache, so a
        /// value off the grid is baked as its snapped neighbour but requested under a URL
        /// nobody else will ever ask for. The bake is shared; the cache hit is not. Snapping
        /// here means two people who drag to "about the same place" send the same URL, which
        /// is what makes a variant free the second time anyone wants it.
        /// </summary>
        public float SnapToServerGrid(float value)
        {
            if (!HasRange || Max <= Min) return Min;

            value = Mathf.Clamp(value, Min, Max);

            /* A count-style range (portholes, windows, steps) only has integer geometry.
             * Mirrors remix_snap on the server, which is the authority.
             *
             * A span of 1 is NOT a count. 0..1 is how a fraction is declared - reef health,
             * wear, openness - and both endpoints being whole made it look like a two-value
             * count, so the slider had two positions and nothing between them could be baked.
             * A real count needs three values to be worth counting; two states are a toggle. */
            var isCount = IsWhole(Min) && IsWhole(Max) && Max - Min <= 8f && Max - Min >= 2f;
            /* An authored Step wins, mirroring remix_snap. Most knobs that declare one
             * declare far fewer values than the 40-step fallback, so honouring it means
             * fewer rebuilds AND bakes that land on keys other people have already asked
             * for - an off-step value is a cache miss by construction. */
            var step = Step > 0f && Step <= Max - Min
                ? Step
                : (isCount ? 1d : (Max - Min) / 40d);

            /* Away from zero, not to even. PHP rounds 10.5 up and .NET rounds it down to
             * the even neighbour, so the default MidpointRounding would put the client one
             * step off the server on exact half-steps - which integer count knobs land on
             * all the time. */
            var steps = Math.Round((value - Min) / step, MidpointRounding.AwayFromZero);
            var snapped = Min + steps * step;
            return (float)Math.Round(snapped, 4, MidpointRounding.AwayFromZero);
        }

        internal static PolyforkKnob Parse(string name, JObject o)
        {
            if (o == null) return null;

            var knob = new PolyforkKnob
            {
                Name = name,
                Label = (string)o["label"] ?? name,
                Describe = (string)o["describe"],
                Affects = (string)o["affects"],
                Icon = (string)o["icon"],
                DefaultValue = o["default"],
                Type = ParseType((string)o["type"])
            };

            var min = o["min"];
            var max = o["max"];
            if (min != null && max != null && min.Type != JTokenType.Null && max.Type != JTokenType.Null)
            {
                knob.HasRange = true;
                knob.Min = min.Value<float>();
                knob.Max = max.Value<float>();
                knob.Step = o["step"] != null && o["step"].Type != JTokenType.Null ? o["step"].Value<float>() : 0f;
                knob.IsIntegral = IsWhole(knob.Min) && IsWhole(knob.Max) && IsWhole(knob.DefaultFloat);
            }

            if (o["options"] is JArray opts)
                knob.Options = opts.Select(t => (string)t).Where(s => s != null).ToArray();

            return knob;
        }

        static bool IsWhole(float v) => Mathf.Approximately(v, Mathf.Round(v));

        static PolyforkKnobType ParseType(string raw) => raw switch
        {
            "color" => PolyforkKnobType.Color,
            "range" => PolyforkKnobType.Range,
            "choice" => PolyforkKnobType.Choice,
            "toggle" => PolyforkKnobType.Toggle,
            _ => PolyforkKnobType.Unknown
        };
    }

    /// <summary>
    /// The full parameter schema for one asset: its knobs plus the curated presets
    /// that back colourway-style choice knobs.
    /// </summary>
    public sealed class PolyforkParams
    {
        public string AssetId { get; private set; }
        public long Rev { get; private set; }

        readonly Dictionary<string, PolyforkKnob> _knobs = new();

        /// <summary>presetName -> (colorKnobName -> hex).</summary>
        readonly Dictionary<string, Dictionary<string, string>> _presets = new();

        public IReadOnlyDictionary<string, PolyforkKnob> Knobs => _knobs;

        public IEnumerable<PolyforkKnob> All => _knobs.Values;

        /// <summary>Knobs that can be honoured exactly, in a stable display order.</summary>
        public IEnumerable<PolyforkKnob> Remixable => _knobs.Values
            .Where(k => k.IsSupported)
            .OrderBy(k => k.Support == PolyforkKnobSupport.LocalRecolor ? 0 : 1)
            .ThenBy(k => k.Type == PolyforkKnobType.Choice ? 0 : 1)
            .ThenBy(k => k.Name, StringComparer.Ordinal);

        public bool TryGetPreset(string presetName, out Dictionary<string, string> slots)
            => _presets.TryGetValue(presetName, out slots);

        public IReadOnlyCollection<string> PresetNames => _presets.Keys;

        /// <summary>
        /// Parses a -params.json payload and classifies each knob.
        ///
        /// Classification is derived from the payload itself, and matches the verified
        /// behaviour of https://polyfork.dev/cdn/{id}-remix.glb?p={...}:
        ///   * affects: geometry -> the endpoint rebuilds the mesh, whatever the type.
        ///                          range, choice and toggle are all baked.
        ///   * anything else     -> the endpoint drops it (a missing "affects" reads as
        ///                          "colors" server-side), so it is applied here or not
        ///                          at all.
        ///   * color             -> recoloured locally by rewriting the vertex-colour slot
        ///                          whose default hex matches this knob's default.
        ///   * colourway choice  -> local: selecting one writes several colour slots at once.
        ///
        /// This is the SERVER path's view. A local baker runs the asset's own module and
        /// honours everything, which is why IPolyforkBaker.Supports is what UI should ask.
        /// </summary>
        public static PolyforkParams Parse(string assetId, string json)
        {
            var root = JObject.Parse(json);
            var result = new PolyforkParams { AssetId = assetId };

            if (root["rev"] != null && root["rev"].Type != JTokenType.Null)
                result.Rev = root["rev"].Value<long>();

            if (root["presets"] is JObject presets)
            {
                foreach (var p in presets.Properties())
                {
                    if (p.Value is not JObject slotObj) continue;
                    var slots = new Dictionary<string, string>();
                    foreach (var s in slotObj.Properties())
                    {
                        var hex = (string)s.Value;
                        if (!string.IsNullOrEmpty(hex)) slots[s.Name] = hex;
                    }
                    result._presets[p.Name] = slots;
                }
            }

            if (root["params"] is JObject ps)
            {
                foreach (var prop in ps.Properties())
                {
                    var knob = PolyforkKnob.Parse(prop.Name, prop.Value as JObject);
                    if (knob != null) result._knobs[prop.Name] = knob;
                }
            }

            foreach (var knob in result._knobs.Values)
                knob.Support = result.Classify(knob);

            return result;
        }

        PolyforkKnobSupport Classify(PolyforkKnob knob)
        {
            // A colourway is decided before anything else: it is a choice knob that resolves
            // to colours, so it is reproducible here and must never cost a bake.
            if (knob.Type == PolyforkKnobType.Choice && IsColorway(knob))
                return PolyforkKnobSupport.LocalRecolor;

            // Geometry is the server's job, and the only thing it will act on.
            if (knob.AffectsGeometry)
            {
                return knob.Type switch
                {
                    PolyforkKnobType.Range => knob.HasRange
                        ? PolyforkKnobSupport.ServerRebuild
                        : PolyforkKnobSupport.Unsupported,

                    // Sent as the exact option string; the server compares strictly.
                    PolyforkKnobType.Choice => knob.Options.Count > 0
                        ? PolyforkKnobSupport.ServerRebuild
                        : PolyforkKnobSupport.Unsupported,

                    PolyforkKnobType.Toggle => PolyforkKnobSupport.ServerRebuild,

                    _ => PolyforkKnobSupport.Unsupported
                };
            }

            // Needs a default hex to identify which vertex-colour slot it owns.
            if (knob.Type == PolyforkKnobType.Color && IsHex(knob.DefaultString))
                return PolyforkKnobSupport.LocalRecolor;

            return PolyforkKnobSupport.Unsupported;
        }

        /// <summary>
        /// A choice knob whose options name curated colour presets.
        ///
        /// The default option is frequently absent from "presets": it is the asset's
        /// authored colours, which are already carried by each colour knob's own default,
        /// so there is nothing to publish. Requiring every option to name a preset therefore
        /// hid the colourway control on exactly the assets that only ship alternatives.
        /// </summary>
        bool IsColorway(PolyforkKnob knob)
        {
            if (knob.Options.Count == 0 || _presets.Count == 0) return false;

            return knob.Options.Any(o => _presets.ContainsKey(o)) &&
                   knob.Options.All(o => _presets.ContainsKey(o) || o == knob.DefaultString);
        }

        /// <summary>The authored colours a colourway option restores, or null if it names a preset.</summary>
        public bool IsDefaultColorway(PolyforkKnob knob, string option)
            => knob != null && option != null && option == knob.DefaultString && !_presets.ContainsKey(option);

        /// <summary>Accepts both #RRGGBB and the #RGB shorthand the catalogue uses.</summary>
        internal static bool IsHex(string s) =>
            !string.IsNullOrEmpty(s) && s[0] == '#' && (s.Length == 7 || s.Length == 4);

        /// <summary>
        /// The default colour of every recolourable slot, as authored by Polyfork.
        /// These hexes are exactly the distinct COLOR_0 values in the asset's GLB,
        /// which is what makes slot identification exact rather than approximate.
        /// </summary>
        public Dictionary<string, Color> DefaultSlotColors()
        {
            var map = new Dictionary<string, Color>();
            foreach (var knob in _knobs.Values)
            {
                if (knob.Type != PolyforkKnobType.Color) continue;
                if (TryParseHex(knob.DefaultString, out var c)) map[knob.Name] = c;
            }
            return map;
        }

        public static bool TryParseHex(string hex, out Color color)
        {
            color = default;
            if (!IsHex(hex)) return false;

            var digits = hex.Substring(1);

            // Expand #RGB to #RRGGBB: each digit doubles, so #479 is #447799.
            if (digits.Length == 3)
                digits = new string(new[] { digits[0], digits[0], digits[1], digits[1], digits[2], digits[2] });

            if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return false;

            color = new Color(
                ((v >> 16) & 0xFF) / 255f,
                ((v >> 8) & 0xFF) / 255f,
                (v & 0xFF) / 255f,
                1f);
            return true;
        }
    }
}
