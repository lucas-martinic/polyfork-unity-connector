using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Polyfork
{
    /// <summary>
    /// A complete set of knob values for one asset, of any knob type.
    ///
    /// The connector originally kept ranges and colours in separate dictionaries, because
    /// the remix endpoint only bakes ranges and colours had to be applied locally. That
    /// split is a property of one baker, not of the asset: a baker that runs the asset's
    /// own module honours every knob from a single set. This is that single set.
    /// </summary>
    public sealed class PolyforkKnobValues : IEnumerable<KeyValuePair<string, object>>
    {
        readonly Dictionary<string, object> _values = new();

        public int Count => _values.Count;

        public IEnumerable<string> Names => _values.Keys;

        public bool TryGet(string knob, out object value) => _values.TryGetValue(knob, out value);

        public bool Contains(string knob) => _values.ContainsKey(knob);

        public void Remove(string knob) => _values.Remove(knob);

        public void Clear() => _values.Clear();

        public void SetNumber(string knob, float value) => _values[knob] = value;

        public void SetBool(string knob, bool value) => _values[knob] = value;

        public void SetChoice(string knob, string option) => _values[knob] = option;

        /// <summary>Stored as a hex string, which is the form the schema and the module use.</summary>
        public void SetColor(string knob, Color value) => _values[knob] = ToHex(value);

        public float GetNumber(string knob, float fallback = 0f)
            => _values.TryGetValue(knob, out var v) && v is float f ? f : fallback;

        public bool GetBool(string knob, bool fallback = false)
            => _values.TryGetValue(knob, out var v) && v is bool b ? b : fallback;

        public string GetString(string knob, string fallback = null)
            => _values.TryGetValue(knob, out var v) && v is string s ? s : fallback;

        public bool TryGetColor(string knob, out Color color)
        {
            color = default;
            return _values.TryGetValue(knob, out var v)
                   && v is string s
                   && PolyforkParams.TryParseHex(s, out color);
        }

        public PolyforkKnobValues Clone()
        {
            var copy = new PolyforkKnobValues();
            foreach (var kv in _values) copy._values[kv.Key] = kv.Value;
            return copy;
        }

        /// <summary>
        /// Every knob at the value the schema publishes as its default.
        /// </summary>
        public static PolyforkKnobValues Defaults(PolyforkParams schema)
        {
            var values = new PolyforkKnobValues();
            if (schema == null) return values;

            foreach (var knob in schema.All)
            {
                switch (knob.Type)
                {
                    case PolyforkKnobType.Range:
                        values.SetNumber(knob.Name, knob.DefaultFloat);
                        break;
                    case PolyforkKnobType.Toggle:
                        values.SetBool(knob.Name, knob.DefaultBool);
                        break;
                    case PolyforkKnobType.Choice:
                    case PolyforkKnobType.Color:
                        if (knob.DefaultString != null) values._values[knob.Name] = knob.DefaultString;
                        break;
                }
            }
            return values;
        }

        /// <summary>
        /// Drops anything still at its published default, so a request carries only what
        /// the user actually changed. Smaller payloads are also better cache keys.
        /// </summary>
        public PolyforkKnobValues WithoutDefaults(PolyforkParams schema)
        {
            var trimmed = new PolyforkKnobValues();
            if (schema == null) return trimmed;

            foreach (var kv in _values)
            {
                if (!schema.Knobs.TryGetValue(kv.Key, out var knob)) continue;

                var isDefault = kv.Value switch
                {
                    float f => Mathf.Approximately(f, knob.DefaultFloat),
                    bool b => b == knob.DefaultBool,
                    string s => s == knob.DefaultString,
                    _ => false
                };

                if (!isDefault) trimmed._values[kv.Key] = kv.Value;
            }
            return trimmed;
        }

        /// <summary>
        /// Only the knobs a given baker can actually honour. Sending a value a baker
        /// ignores just changes the cache key without changing the result.
        /// </summary>
        public PolyforkKnobValues Filter(PolyforkParams schema, IPolyforkBaker baker)
        {
            var filtered = new PolyforkKnobValues();
            if (schema == null || baker == null) return filtered;

            foreach (var kv in _values)
            {
                if (schema.Knobs.TryGetValue(kv.Key, out var knob) &&
                    baker.Supports(knob) == PolyforkKnobSupport.ServerRebuild)
                {
                    filtered._values[kv.Key] = kv.Value;
                }
            }
            return filtered;
        }

        /// <summary>
        /// Reads back what ToJson wrote.
        ///
        /// The round trip is what lets a value set outlive the window that made it - stored
        /// on an imported prefab, reopened a month later and turned again. Types come from
        /// the JSON itself rather than from a schema, so a choice stays the string it was
        /// published as, which is the distinction the remix endpoint compares on.
        /// </summary>
        public static PolyforkKnobValues FromJson(string json)
        {
            var values = new PolyforkKnobValues();
            if (string.IsNullOrWhiteSpace(json)) return values;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception) { return values; }   // a corrupt record reads as "no overrides"

            foreach (var p in root.Properties())
            {
                switch (p.Value.Type)
                {
                    case JTokenType.Integer:
                    case JTokenType.Float:
                        values.SetNumber(p.Name, p.Value.Value<float>());
                        break;
                    case JTokenType.Boolean:
                        values.SetBool(p.Name, p.Value.Value<bool>());
                        break;
                    case JTokenType.String:
                        values.SetChoice(p.Name, p.Value.Value<string>());
                        break;
                }
            }
            return values;
        }

        /// <summary>The `p=` payload: a JSON object of knob name to value.</summary>
        public JObject ToJson()
        {
            var obj = new JObject();
            foreach (var kv in _values)
            {
                obj[kv.Key] = kv.Value switch
                {
                    float f => new JValue(f),
                    bool b => new JValue(b),
                    string s => new JValue(s),
                    _ => JValue.CreateNull()
                };
            }
            return obj;
        }

        public override string ToString() => ToJson().ToString(Newtonsoft.Json.Formatting.None);

        static string ToHex(Color c) =>
            "#" + Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255).ToString("X2", CultureInfo.InvariantCulture)
                + Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255).ToString("X2", CultureInfo.InvariantCulture)
                + Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255).ToString("X2", CultureInfo.InvariantCulture);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
