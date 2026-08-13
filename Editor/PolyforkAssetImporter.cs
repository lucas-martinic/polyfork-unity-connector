using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GLTFast.Export;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Saves a remixed Polyfork asset into the project as a .glb.
    ///
    /// The geometry comes from Polyfork's remix endpoint, but colour knobs are not baked
    /// server-side, so a straight download would land in the project with default colours -
    /// not what the user just dialled in. Instead the GLB is loaded, recoloured through
    /// <see cref="PolyforkColorSlots"/>, and re-exported. The result is a real .glb with the
    /// chosen colours baked into COLOR_0, usable outside Unity too.
    /// </summary>
    public static class PolyforkAssetImporter
    {
        public const string DefaultFolder = "Assets/Polyfork";

        public sealed class Result
        {
            public bool Success;
            public string AssetPath;
            public string Error;
            public bool ColorsBaked;

            /// <summary>Set when the failure was a 429, so callers can offer the key prompt.</summary>
            public bool RateLimited;
            public TimeSpan RetryAfter;
        }

        /// <summary>
        /// Downloads the asset at its current knob values and writes it into the project.
        /// </summary>
        public static async Task<Result> ImportAsync(
            PolyforkClient client,
            PolyforkGlbLoader loader,
            PolyforkAsset asset,
            PolyforkParams schema,
            PolyforkKnobValues geometry,
            IReadOnlyDictionary<string, Color> slotColors,
            string folder = DefaultFolder,
            CancellationToken ct = default)
        {
            var result = new Result();
            GameObject staging = null;

            try
            {
                var payload = StripDefaults(schema, geometry);
                var url = payload.Count == 0
                    ? asset.PreviewGlb
                    : client.RemixGlbUrl(asset.Id, payload);

                if (string.IsNullOrEmpty(url))
                {
                    result.Error = "This asset publishes no downloadable GLB.";
                    return result;
                }

                var bytes = await loader.GetBytesAsync(url, ct);

                Directory.CreateDirectory(folder);
                var fileName = BuildFileName(asset, payload, slotColors, schema);
                var assetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName).Replace('\\', '/'));

                var needsRecolour = NeedsRecolour(schema, slotColors);

                if (!needsRecolour)
                {
                    // Nothing to bake: the server's bytes are already exactly right.
                    await File.WriteAllBytesAsync(assetPath, bytes, ct);
                }
                else
                {
                    staging = await loader.InstantiateAsync(bytes, url, null, ct);
                    var slots = PolyforkColorSlots.Build(staging, schema);

                    if (!slots.HasSlots)
                    {
                        // Could not identify colour slots; save the server bytes rather than
                        // silently exporting something that lost fidelity.
                        await File.WriteAllBytesAsync(assetPath, bytes, ct);
                    }
                    else
                    {
                        slots.Apply(slotColors);

                        var export = new GameObjectExport(
                            new ExportSettings
                            {
                                Format = GltfFormat.Binary,
                                Deterministic = true
                            });

                        export.AddScene(new[] { staging }, asset.Title ?? asset.Id);

                        var saved = await export.SaveToFileAndDispose(assetPath, ct);
                        if (!saved)
                        {
                            result.Error = "glTF export failed.";
                            return result;
                        }

                        result.ColorsBaked = true;
                    }
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                result.Success = true;
                result.AssetPath = assetPath;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Error = "Cancelled.";
                return result;
            }
            catch (PolyforkRateLimitException e)
            {
                result.RateLimited = true;
                result.RetryAfter = e.RetryAfter;
                result.Error = "Rate limited. Add an API key to lift the cap.";
                return result;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                return result;
            }
            finally
            {
                if (staging != null) UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        /// <summary>
        /// Only the geometry knobs that differ from their published default.
        ///
        /// The window has usually done this already; doing it again here keeps the contract
        /// true for any other caller, and a default that slips through is not harmless - it
        /// turns the free baseline preview into a metered variant identical to it.
        /// </summary>
        static PolyforkKnobValues StripDefaults(PolyforkParams schema, PolyforkKnobValues values)
        {
            var payload = new PolyforkKnobValues();
            if (values == null || schema == null) return payload;

            foreach (var name in values.Names)
            {
                if (!schema.Knobs.TryGetValue(name, out var knob)) continue;
                if (knob.Support != PolyforkKnobSupport.ServerRebuild) continue;
                if (!values.TryGet(name, out var raw)) continue;

                switch (raw)
                {
                    case float f when !Mathf.Approximately(knob.SnapToServerGrid(f), knob.SnapToServerGrid(knob.DefaultFloat)):
                        payload.SetNumber(name, knob.SnapToServerGrid(f));
                        break;
                    case string s when s != knob.DefaultString:
                        payload.SetChoice(name, s);
                        break;
                    case bool b when b != knob.DefaultBool:
                        payload.SetBool(name, b);
                        break;
                }
            }
            return payload;
        }

        static bool NeedsRecolour(PolyforkParams schema, IReadOnlyDictionary<string, Color> slotColors)
        {
            if (schema == null || slotColors == null || slotColors.Count == 0) return false;

            foreach (var kv in slotColors)
            {
                if (!schema.Knobs.TryGetValue(kv.Key, out var knob)) continue;
                if (knob.Type != PolyforkKnobType.Color) continue;
                if (!PolyforkParams.TryParseHex(knob.DefaultString, out var authored)) continue;

                if (!Approximately(authored, kv.Value)) return true;
            }
            return false;
        }

        static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

        /// <summary>
        /// Names the file after the asset plus whatever was changed, so several variants of
        /// one asset can live side by side and stay recognisable.
        /// </summary>
        static string BuildFileName(
            PolyforkAsset asset,
            PolyforkKnobValues payload,
            IReadOnlyDictionary<string, Color> slotColors,
            PolyforkParams schema)
        {
            var sb = new StringBuilder(Sanitise(asset.Title ?? asset.Id));

            // Ordered, so the same variant always lands on the same filename rather than one
            // per import depending on which knob happened to be enumerated first.
            foreach (var name in payload.Names.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!payload.TryGet(name, out var raw)) continue;

                var text = raw switch
                {
                    float f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    bool b => b ? "on" : "off",
                    _ => Sanitise(raw?.ToString() ?? "")
                };

                sb.Append('_').Append(Sanitise(name)).Append('-').Append(text);
            }

            if (NeedsRecolour(schema, slotColors)) sb.Append("_recoloured");

            sb.Append(".glb");
            return sb.ToString();
        }

        static string Sanitise(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c is ' ' or '-' or '_') sb.Append('-');
            }
            return sb.Length == 0 ? "asset" : sb.ToString().Trim('-');
        }
    }
}
