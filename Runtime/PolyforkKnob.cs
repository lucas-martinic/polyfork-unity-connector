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
        ///   * range  -> the endpoint rebuilds the mesh (and clamps to min/max).
        ///   * color  -> the endpoint ignores it; applied locally by recolouring the
        ///               vertex-colour slot whose default hex matches this knob's default.
        ///   * choice -> local only when its options are exactly the keys of "presets",
        ///               in which case selecting one writes several colour slots at once.
        ///               Structural choices (piece, layout, ...) are unsupported.
        ///   * toggle -> unsupported: ignored by the endpoint and topology-changing.
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
            switch (knob.Type)
            {
                case PolyforkKnobType.Range:
                    // Verified: numeric ranges are the only knobs the remix endpoint bakes.
                    return knob.HasRange ? PolyforkKnobSupport.ServerRebuild : PolyforkKnobSupport.Unsupported;

                case PolyforkKnobType.Color:
                    // Needs a default hex to identify which vertex-colour slot it owns.
                    return IsHex(knob.DefaultString)
                        ? PolyforkKnobSupport.LocalRecolor
                        : PolyforkKnobSupport.Unsupported;

                case PolyforkKnobType.Choice:
                    // A colourway iff every option names a preset.
                    return knob.Options.Count > 0 && knob.Options.All(o => _presets.ContainsKey(o))
                        ? PolyforkKnobSupport.LocalRecolor
                        : PolyforkKnobSupport.Unsupported;

                default:
                    return PolyforkKnobSupport.Unsupported;
            }
        }

        internal static bool IsHex(string s) =>
            !string.IsNullOrEmpty(s) && s.Length == 7 && s[0] == '#';

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
            if (!int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
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
