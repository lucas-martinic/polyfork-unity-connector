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

            /// <summary>The prefab carrying the knob values, or null if one could not be
            /// written. This is the thing to drag into a scene.</summary>
            public string PrefabPath;

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
            IPolyforkBaker baker = null,
            CancellationToken ct = default)
        {
            var result = new Result();
            GameObject staging = null;

            try
            {
                var payload = StripDefaults(schema, geometry);

                /* If something here can build the asset without asking polyfork.dev, use it.
                 *
                 * Importing a remixed FREE asset used to demand a server bake and could be
                 * refused for want of allowance - on an asset the editor was, at that moment,
                 * rebuilding locally and for nothing on every slider move. The mesh in the
                 * preview and the mesh being imported are the same mesh; only one of them was
                 * being metered. */
                if (baker is { ConsumesAllowance: false } && baker.CanBake(asset, schema))
                {
                    var local = await ImportFromBakerAsync(baker, asset, schema, geometry, folder, ct);
                    if (local != null) return local;

                    // Fell through: the local bake could not produce it, so buy it as usual.
                }

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

                        // Same reason as the local path: the recoloured mesh IS its colours.
                        var export = new GameObjectExport(
                            new ExportSettings
                            {
                                Format = GltfFormat.Binary,
                                Deterministic = true,
                                PreservedVertexAttributes = VertexAttributeUsage.Color
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
                result.PrefabPath = SavePrefab(assetPath, asset, geometry);
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
        /// Saves a prefab beside the .glb carrying a PolyforkAssetLink, and returns its path.
        ///
        /// A .glb lands in the project as an imported model, and an imported model is not
        /// something a component can be added to - Unity rebuilds it from the file on every
        /// import, so anything attached is discarded. A prefab wrapping it is the standard
        /// answer, and it is the prefab that carries the knob values, which is what makes an
        /// asset still editable after it has been dropped into a scene.
        ///
        /// Best effort: a failure here costs the Inspector knobs, not the import.
        /// </summary>
        static string SavePrefab(
            string glbPath, PolyforkAsset asset, PolyforkKnobValues values)
        {
            GameObject instance = null;
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
                if (model == null) return null;

                instance = UnityEngine.Object.Instantiate(model);
                instance.name = model.name;

                var link = instance.AddComponent<PolyforkAssetLink>();
                link.assetId = asset.Id;
                link.title = asset.Title;
                link.page = asset.Page;
                link.knobValues = values?.ToString() ?? "{}";

                var prefabPath = Path.ChangeExtension(glbPath, ".prefab");
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out var ok);
                return ok && saved != null ? prefabPath : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] could not write a prefab for {asset.Id} ({e.Message}); " +
                                 "the .glb imported fine, it just has no knobs in the Inspector.");
                return null;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Writes the asset straight out of a local bake, or null if it could not.
        ///
        /// The module honours colour as well as geometry, so what it returns is already the
        /// finished thing - no download to fetch and no slots to repaint afterwards.
        /// </summary>
        static async Task<Result> ImportFromBakerAsync(
            IPolyforkBaker baker,
            PolyforkAsset asset,
            PolyforkParams schema,
            PolyforkKnobValues values,
            string folder,
            CancellationToken ct)
        {
            GameObject staging = null;
            try
            {
                staging = await baker.BakeAsync(new PolyforkBakeRequest(asset, schema, values), ct);
                if (staging == null) return null;

                Directory.CreateDirectory(folder);
                var fileName = BuildFileName(asset, StripDefaults(schema, values), null, schema);
                var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.Combine(folder, fileName).Replace('\\', '/'));

                /* PreservedVertexAttributes is not optional here.
                 *
                 * glTFast drops vertex attributes it judges unused, and it judges by the
                 * material: "vertex colors are discarded when the assigned material(s) do not
                 * use them". A Polyfork asset keeps its ENTIRE appearance in COLOR_0, and the
                 * material carrying it is our own shader, which glTFast has never heard of -
                 * so the exporter helpfully threw away the only thing that made the model
                 * look like anything, and the import arrived white. */
                var export = new GameObjectExport(new ExportSettings
                {
                    Format = GltfFormat.Binary,
                    Deterministic = true,
                    PreservedVertexAttributes = VertexAttributeUsage.Color
                });
                export.AddScene(new[] { staging }, asset.Title ?? asset.Id);

                if (!await export.SaveToFileAndDispose(assetPath, ct)) return null;

                AssetDatabase.ImportAsset(assetPath);

                return new Result
                {
                    Success = true,
                    AssetPath = assetPath,
                    ColorsBaked = true,
                    PrefabPath = SavePrefab(assetPath, asset, values)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] local import of {asset.Id} failed ({e.Message}); using the server.");
                return null;
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
