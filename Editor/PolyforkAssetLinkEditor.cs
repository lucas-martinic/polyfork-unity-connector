using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Puts an imported asset's knobs back in the Inspector, so a model already placed in a
    /// scene can still be changed.
    ///
    /// The alternative, which is what this replaces, is that importing freezes a model: to
    /// make the fence one section longer you find the asset again, guess what the sliders
    /// were, import a second copy and swap it by hand, leaving the first one orphaned in the
    /// project. The values were never lost, only unwritten - PolyforkAssetLink writes them
    /// down, and this reads them back.
    ///
    /// Rebuilding replaces the meshes on the instance in place, so transforms, parenting,
    /// colliders and anything else attached to the object survive a knob change.
    /// </summary>
    [CustomEditor(typeof(PolyforkAssetLink))]
    public sealed class PolyforkAssetLinkEditor : UnityEditor.Editor
    {
        PolyforkClient _client;
        PolyforkGlbLoader _loader;
        PolyforkBakerRegistry _bakers;
        IPolyforkJsRuntime _js;
        CancellationTokenSource _cts;

        PolyforkParams _schema;
        PolyforkKnobValues _values;
        PolyforkAsset _asset;

        string _status;
        bool _loading;
        bool _dirty;
        bool _rebuilding;

        /// <summary>When the pending change should be built, or -1 for nothing pending.</summary>
        double _rebuildAt = -1d;

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
            EditorApplication.update += Tick;
            _client = new PolyforkClient { ApiKey = PolyforkCredentials.Resolve(null) };
            _loader = new PolyforkGlbLoader(_client);

            _bakers = new PolyforkBakerRegistry();
            _bakers.Register(new PolyforkServerBaker(_client, _loader));

            // Same engine the gallery uses, when one is installed: a knob turned in the
            // Inspector should cost no more than a knob turned in the window.
            _js = PolyforkJsRuntimeProvider.TryCreate();
            if (_js != null) _bakers.Register(new PolyforkLocalBaker(_js, _client));

            _ = LoadAsync();
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _js?.Dispose();
            _js = null;
        }

        async Task LoadAsync()
        {
            var link = (PolyforkAssetLink)target;
            if (link == null || string.IsNullOrEmpty(link.assetId)) return;

            _loading = true;
            _status = "Reading the asset...";
            Repaint();

            try
            {
                _asset = await _client.GetAssetAsync(link.assetId, _cts.Token);
                _schema = await _client.GetParamsAsync(link.assetId, _cts.Token);
                _values = link.Values;
                _status = null;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                _status = $"Could not reach polyfork.dev ({e.Message}).";
            }

            _loading = false;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            var link = (PolyforkAssetLink)target;

            PolyforkBrand.DrawHeader(link.title ?? link.assetId);
            EditorGUILayout.Space(6f);

            if (string.IsNullOrEmpty(link.assetId))
            {
                EditorGUILayout.HelpBox("No asset id on this component.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(link.assetId, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(link.page) && GUILayout.Button("Open on polyfork.dev", EditorStyles.miniButton))
                    Application.OpenURL(link.page);
            }

            if (_status != null)
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
                return;
            }

            if (_loading || _schema == null || _values == null)
            {
                EditorGUILayout.LabelField("Loading knobs...", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space(4f);
            DrawKnobs();

            EditorGUILayout.Space(8f);
            DrawActions(link);
        }

        void DrawKnobs()
        {
            var drawn = 0;

            foreach (var knob in _schema.All.OrderBy(k => k.Type == PolyforkKnobType.Color ? 1 : 0)
                         .ThenBy(k => k.Name, StringComparer.Ordinal))
            {
                // What the baker that would serve this asset can honour, not what the server
                // alone can: with a local engine that is a wider set.
                var support = _bakers.Supports(_asset, _schema, knob);
                if (support == PolyforkKnobSupport.Unsupported) continue;

                drawn++;
                var label = new GUIContent(knob.Label, knob.Describe);

                EditorGUI.BeginChangeCheck();

                switch (knob.Type)
                {
                    case PolyforkKnobType.Range when knob.HasRange:
                    {
                        var current = _values.GetNumber(knob.Name, knob.DefaultFloat);
                        var next = knob.IsIntegral
                            ? EditorGUILayout.IntSlider(label, Mathf.RoundToInt(current),
                                Mathf.RoundToInt(knob.Min), Mathf.RoundToInt(knob.Max))
                            : EditorGUILayout.Slider(label, current, knob.Min, knob.Max);

                        if (EditorGUI.EndChangeCheck()) Schedule(() => _values.SetNumber(knob.Name, next));
                        continue;
                    }

                    case PolyforkKnobType.Toggle:
                    {
                        var next = EditorGUILayout.Toggle(label, _values.GetBool(knob.Name, knob.DefaultBool));
                        if (EditorGUI.EndChangeCheck()) Schedule(() => _values.SetBool(knob.Name, next));
                        continue;
                    }

                    case PolyforkKnobType.Choice when knob.Options.Count > 0:
                    {
                        var options = knob.Options.ToList();
                        var index = Mathf.Max(0, options.IndexOf(_values.GetString(knob.Name, knob.DefaultString)));
                        var next = EditorGUILayout.Popup(label, index,
                            options.Select(o => new GUIContent(o)).ToArray());

                        if (EditorGUI.EndChangeCheck()) Schedule(() => _values.SetChoice(knob.Name, options[next]));
                        continue;
                    }

                    case PolyforkKnobType.Color:
                    {
                        PolyforkParams.TryParseHex(knob.DefaultString, out var authored);
                        _values.TryGetColor(knob.Name, out var current);
                        if (current == default) current = authored;

                        var next = EditorGUILayout.ColorField(label, current);
                        if (EditorGUI.EndChangeCheck()) Schedule(() => _values.SetColor(knob.Name, next));
                        continue;
                    }
                }

                EditorGUI.EndChangeCheck();
            }

            if (drawn == 0)
                EditorGUILayout.LabelField("This asset has no knobs that can be applied here.", EditorStyles.miniLabel);
        }

        void Schedule(Action apply)
        {
            apply();
            _dirty = true;

            /* Debounced only when the rebuild leaves the machine. A local bake is neither
             * metered nor a request, so waiting buys nothing and costs the immediacy the
             * whole point of turning a knob in the Inspector is - you want to see it. */
            var metered = _bakers.Resolve(_asset, _schema)?.ConsumesAllowance ?? true;
            _rebuildAt = EditorApplication.timeSinceStartup + (metered ? 0.35d : 0d);
        }

        /// <summary>
        /// Builds whatever the last change asked for, once it has settled.
        ///
        /// A dragged slider fires a change per frame, so this coalesces them: the button it
        /// replaces made you ask for a result you had already described, which is a step that
        /// only ever existed because nothing was watching for the change.
        /// </summary>
        void Tick()
        {
            if (_rebuildAt < 0d || _rebuilding) return;
            if (EditorApplication.timeSinceStartup < _rebuildAt) return;

            _rebuildAt = -1d;

            if (target is PolyforkAssetLink link) _ = RebuildAsync(link);
        }

        void DrawActions(PolyforkAssetLink link)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    _rebuilding ? "Rebuilding..." : _dirty ? "Building..." : " ",
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_rebuilding || link.IsDefault && !_dirty))
                {
                    if (GUILayout.Button("Reset to published", EditorStyles.miniButton, GUILayout.Width(140f)))
                    {
                        _values = new PolyforkKnobValues();
                        Schedule(() => { });
                    }
                }
            }

            EditorGUILayout.LabelField(
                "Changes rebuild the meshes on this object. Its transform, children and any " +
                "components you added stay as they are.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// Rebuilds and swaps the geometry in place.
        ///
        /// Children are replaced rather than the object itself, so whatever the scene has
        /// attached to it - transform, colliders, scripts, its place in a hierarchy, its
        /// prefab connection - is untouched by a knob change.
        /// </summary>
        async Task RebuildAsync(PolyforkAssetLink link)
        {
            _rebuilding = true;
            Repaint();

            GameObject built = null;
            try
            {
                var baker = _bakers.Resolve(_asset, _schema);
                if (baker == null)
                {
                    _status = "Nothing here can rebuild this asset.";
                    return;
                }

                built = await baker.BakeAsync(
                    new PolyforkBakeRequest(_asset, _schema, _values), _cts.Token);

                if (built == null)
                {
                    _status = "The rebuild produced nothing; the object is unchanged.";
                    return;
                }

                /* Keep whatever material the object is already wearing.
                 *
                 * A bake hands back meshes dressed in the baker's own material, and the local
                 * baker's is Polyfork/Vertex Color - a preview shader that does its own
                 * lighting and ignores the scene's. Dropping that onto an object in a real
                 * scene is why a rebuilt model suddenly looked unlit: it was, and it had
                 * stopped being the glTFast material the import gave it.
                 *
                 * Reading it off the object rather than reconstructing it also means a
                 * material the user assigned themselves survives a knob change, which is the
                 * behaviour they would expect without being told. */
                var existing = link.GetComponentInChildren<Renderer>(true)?.sharedMaterial;

                Undo.RegisterFullObjectHierarchyUndo(link.gameObject, "Polyfork rebuild");

                foreach (Transform child in link.transform.Cast<Transform>().ToList())
                    Undo.DestroyObjectImmediate(child.gameObject);

                foreach (Transform child in built.transform.Cast<Transform>().ToList())
                {
                    child.SetParent(link.transform, worldPositionStays: false);
                    Undo.RegisterCreatedObjectUndo(child.gameObject, "Polyfork rebuild");
                }

                if (existing != null)
                {
                    foreach (var r in link.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterial = existing;
                }

                link.knobValues = _values.ToString();
                EditorUtility.SetDirty(link);

                _dirty = false;
                _status = null;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"Rebuild failed: {e.Message}";
                Debug.LogWarning($"[Polyfork] rebuild of {link.assetId} failed: {e}");
            }
            finally
            {
                if (built != null) DestroyImmediate(built);
                _rebuilding = false;
                Repaint();
            }
        }
    }
}
