using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// The Polyfork store, inside Unity: browse the catalogue, turn the same knobs the web
    /// viewer exposes, and drop the result into the project as a .glb.
    ///
    /// Knob metadata is Polyfork's own (/cdn/{id}-params.json); nothing here is invented.
    /// </summary>
    public sealed class PolyforkGalleryWindow : EditorWindow
    {
        const float DetailWidth = 340f;
        const float CardSize = 118f;
        const float CardPadding = 8f;

        [MenuItem("Polyfork/Browse Assets %#p", priority = 0)]
        // Also under Window, because that is where Unity users look for a window.
        [MenuItem("Window/Polyfork/Browse Assets", priority = 1100)]
        public static void Open()
        {
            var window = GetWindow<PolyforkGalleryWindow>();
            PolyforkBrand.ApplyTitle(window, "Polyfork");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        // ---- services -------------------------------------------------------
        PolyforkClient _client;
        PolyforkGlbLoader _loader;
        PolyforkThumbnailCache _thumbs;
        PolyforkAssetPreview _preview;
        CancellationTokenSource _cts;

        // ---- catalogue ------------------------------------------------------
        readonly List<PolyforkAsset> _all = new();
        List<PolyforkAsset> _filtered = new();
        string _status = "Connecting...";
        bool _loading;

        // ---- filters --------------------------------------------------------
        string _search = "";
        string _kit = "All kits";
        string _class = "All types";
        bool _freeOnly;
        bool _remixableOnly;
        int _maxTriangles;
        string[] _kits = { "All kits" };
        string[] _classes = { "All types" };

        // ---- selection ------------------------------------------------------
        PolyforkAsset _selected;
        PolyforkParams _schema;
        string _previewedAssetId;

        /// <summary>Slot binding for the object currently in the preview, so colour edits
        /// can be applied in place instead of re-fetching the GLB.</summary>
        PolyforkColorSlots _previewSlots;
        readonly Dictionary<string, float> _ranges = new();

        /// <summary>Structural choice and toggle knobs. The endpoint bakes these too, so
        /// they move geometry exactly like a range does - just without a slider.</summary>
        readonly Dictionary<string, string> _choices = new();
        readonly Dictionary<string, bool> _toggles = new();

        readonly Dictionary<string, Color> _slotColors = new();
        string _colorway;
        string _colorwayKnob;

        readonly PolyforkRemixHistory _history = new();

        bool _previewDirty;
        double _rebuildAt;
        bool _rebuilding;

        // ---- allowance ------------------------------------------------------
        double _rateLimitedUntil;
        bool _promptedForKey;
        readonly PolyforkRemixBudget _budget = new();

        bool IsRateLimited => EditorApplication.timeSinceStartup < _rateLimitedUntil || _budget.IsExhausted;
        string _importMessage;
        MessageType _importMessageType = MessageType.Info;
        string _importFolder = PolyforkAssetImporter.DefaultFolder;

        Vector2 _gridScroll;
        Vector2 _detailScroll;

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _client = new PolyforkClient { ApiKey = PolyforkCredentials.Resolve(null) };
            _loader = new PolyforkGlbLoader(_client);
            _thumbs = new PolyforkThumbnailCache(_client);
            _thumbs.Changed += Repaint;
            _preview = new PolyforkAssetPreview();

            PolyforkKeySettings.Changed += OnKeyChanged;
            EditorApplication.update += OnEditorUpdate;
            _ = RefreshAccessAsync();
            _ = LoadCatalogueAsync();
        }

        /// <summary>
        /// Reads the live allowance from /api/me. Keyless, so it works before sign-in and
        /// lets the window state the remaining bakes instead of surprising the user with a
        /// 429. If it cannot be reached the budget stays at its floor, which throttles
        /// speculative prewarming rather than assuming plenty.
        /// </summary>
        async Task RefreshAccessAsync()
        {
            try
            {
                _budget.SyncFrom(await _client.GetAccessAsync(_cts.Token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] could not read the remix allowance ({e.Message}).");
            }
            Repaint();
        }

        void OnDisable()
        {
            PolyforkKeySettings.Changed -= OnKeyChanged;
            EditorApplication.update -= OnEditorUpdate;
            _cts?.Cancel();
            _cts?.Dispose();
            _thumbs?.Dispose();
            _preview?.Dispose();
        }

        /// <summary>A newly saved key clears the limit and re-arms the client immediately.</summary>
        void OnKeyChanged()
        {
            _client.ApiKey = PolyforkCredentials.Resolve(null);
            _rateLimitedUntil = 0d;
            _promptedForKey = false;
            _budget.Reset();
            _status = PolyforkKeySettings.HasKey ? "API key active" : $"{_all.Count} assets";
            _ = RefreshAccessAsync();        // the new key almost certainly has a new tier
            QueuePreviewRebuild(immediate: true);
            Repaint();
        }

        /// <summary>
        /// Records a 429 and, once per session, opens the key prompt. Repeated limits after
        /// that only update the banner, so the window does not nag.
        /// </summary>
        void HandleRateLimit(PolyforkRateLimitException e)
        {
            _rateLimitedUntil = EditorApplication.timeSinceStartup + e.RetryAfter.TotalSeconds;
            _status = "Rate limited";

            if (_promptedForKey) return;
            _promptedForKey = true;
            PolyforkApiKeyWindow.OpenRateLimited(e.RetryAfter);
        }

        void OnEditorUpdate()
        {
            // Geometry rebuilds need the network, so hold them while capped rather than
            // firing requests that can only fail. Colour edits are local and unaffected.
            if (_previewDirty && !_rebuilding && !IsRateLimited &&
                EditorApplication.timeSinceStartup >= _rebuildAt)
            {
                _previewDirty = false;
                _ = RebuildPreviewAsync();
            }

            if (IsRateLimited) Repaint();   // keep the countdown ticking
        }

        // =====================================================================
        // Catalogue
        // =====================================================================

        async Task LoadCatalogueAsync()
        {
            _loading = true;
            _status = "Loading catalogue...";
            Repaint();

            try
            {
                var assets = await _client.GetAllAssetsAsync(null, _cts.Token);
                _all.Clear();
                _all.AddRange(assets);

                _kits = new[] { "All kits" }
                    .Concat(assets.Select(a => a.Kit).Where(k => !string.IsNullOrEmpty(k)).Distinct().OrderBy(k => k))
                    .ToArray();
                _classes = new[] { "All types" }
                    .Concat(assets.Select(a => a.Class).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c))
                    .ToArray();

                ApplyFilter();
                _status = $"{_all.Count} assets";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"Could not reach polyfork.dev: {e.Message}";
            }
            finally
            {
                _loading = false;
                Repaint();
            }
        }

        void ApplyFilter()
        {
            IEnumerable<PolyforkAsset> q = _all;

            if (!string.IsNullOrWhiteSpace(_search))
            {
                var needle = _search.Trim();
                q = q.Where(a =>
                    (a.Title != null && a.Title.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Id != null && a.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (a.Kit != null && a.Kit.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_kit != "All kits") q = q.Where(a => a.Kit == _kit);
            if (_class != "All types") q = q.Where(a => a.Class == _class);
            if (_freeOnly) q = q.Where(a => a.Free);
            if (_remixableOnly) q = q.Where(a => a.Remixable);
            if (_maxTriangles > 0) q = q.Where(a => a.Triangles <= _maxTriangles);

            _filtered = q.ToList();
        }

        // =====================================================================
        // Selection
        // =====================================================================

        async void Select(PolyforkAsset asset)
        {
            if (asset == null || _selected == asset) return;

            _selected = asset;
            _schema = null;
            _previewedAssetId = null;      // a new asset should be framed, not inherit zoom
            _history.Clear();              // undo is per-asset; don't step back into another
            _ranges.Clear();
            _choices.Clear();
            _toggles.Clear();
            _slotColors.Clear();
            _colorway = null;
            _colorwayKnob = null;
            _importMessage = null;
            _preview.Clear();
            Repaint();

            try
            {
                if (asset.Remixable)
                {
                    _schema = await _client.GetParamsAsync(asset.Id, _cts.Token);
                    if (_selected != asset) return;
                    ResetKnobs();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] no knob schema for {asset.Id}: {e.Message}");
            }

            QueuePreviewRebuild(immediate: true);
        }

        void ResetKnobs()
        {
            _ranges.Clear();
            _choices.Clear();
            _toggles.Clear();
            _slotColors.Clear();
            _colorway = null;
            _colorwayKnob = null;
            if (_schema == null) return;

            foreach (var knob in _schema.All)
            {
                switch (knob.Support)
                {
                    case PolyforkKnobSupport.ServerRebuild when knob.Type == PolyforkKnobType.Choice:
                        _choices[knob.Name] = knob.DefaultString ?? knob.Options.FirstOrDefault();
                        break;
                    case PolyforkKnobSupport.ServerRebuild when knob.Type == PolyforkKnobType.Toggle:
                        _toggles[knob.Name] = knob.DefaultBool;
                        break;
                    case PolyforkKnobSupport.ServerRebuild:
                        _ranges[knob.Name] = knob.DefaultFloat;
                        break;
                    case PolyforkKnobSupport.LocalRecolor when knob.Type == PolyforkKnobType.Color:
                        if (PolyforkParams.TryParseHex(knob.DefaultString, out var c)) _slotColors[knob.Name] = c;
                        break;
                    case PolyforkKnobSupport.LocalRecolor when knob.Type == PolyforkKnobType.Choice:
                        _colorwayKnob ??= knob.Name;
                        _colorway ??= knob.DefaultString;
                        break;
                }
            }
        }

        void QueuePreviewRebuild(bool immediate = false)
        {
            _previewDirty = true;
            _rebuildAt = EditorApplication.timeSinceStartup + (immediate ? 0d : 0.25d);
        }

        /// <summary>
        /// The geometry knobs to send, as the schema types them.
        ///
        /// Defaults are left out: they are what the baseline preview already is, so sending
        /// them would turn a free file into a metered variant of itself. Ranges are put on
        /// the server's grid first, so dragging converges on URLs other people have already
        /// paid to bake.
        /// </summary>
        PolyforkKnobValues BuildGeometryValues()
        {
            var values = new PolyforkKnobValues();
            if (_schema == null) return values;

            foreach (var kv in _ranges)
            {
                if (!_schema.Knobs.TryGetValue(kv.Key, out var knob)) continue;
                var snapped = knob.SnapToServerGrid(kv.Value);
                if (!Mathf.Approximately(snapped, knob.SnapToServerGrid(knob.DefaultFloat)))
                    values.SetNumber(kv.Key, snapped);
            }

            foreach (var kv in _choices)
            {
                if (!_schema.Knobs.TryGetValue(kv.Key, out var knob)) continue;
                if (kv.Value != null && kv.Value != knob.DefaultString) values.SetChoice(kv.Key, kv.Value);
            }

            foreach (var kv in _toggles)
            {
                if (!_schema.Knobs.TryGetValue(kv.Key, out var knob)) continue;
                if (kv.Value != knob.DefaultBool) values.SetBool(kv.Key, kv.Value);
            }

            return values;
        }

        async Task RebuildPreviewAsync()
        {
            if (_selected == null) return;

            _rebuilding = true;
            var asset = _selected;

            try
            {
                var payload = BuildGeometryValues();

                var url = payload.Count == 0 ? asset.PreviewGlb : _client.RemixGlbUrl(asset.Id, payload);
                if (string.IsNullOrEmpty(url)) return;

                // Only a request that actually leaves the machine can cost a bake, and only
                // a variant nobody has baked before is metered at all.
                var billable = payload.Count > 0 && !_loader.IsCached(url);
                if (billable) _budget.TryConsume();

                var bytes = await _loader.GetBytesAsync(url, _cts.Token);
                if (_selected != asset) return;

                // Re-read the real figure rather than trusting the local mirror; the variant
                // may have been baked by someone else already and cost nothing.
                if (billable) _ = RefreshAccessAsync();

                var go = await _loader.InstantiateAsync(bytes, url, null, _cts.Token);
                if (_selected != asset)
                {
                    DestroyImmediate(go);
                    return;
                }

                // Colours are applied locally: the remix endpoint does not bake them.
                // Keep the binding so later colour edits skip the network entirely.
                _previewSlots = _schema != null ? PolyforkColorSlots.Build(go, _schema) : null;
                if (_slotColors.Count > 0) _previewSlots?.Apply(_slotColors);

                // Only re-frame when the asset itself changed. A knob-driven rebuild must
                // keep the user's zoom, so a geometry change reads as the model resizing.
                var sameAsset = _previewedAssetId == asset.Id;
                _preview.SetTarget(go, frameCamera: !sameAsset);
                _previewedAssetId = asset.Id;
            }
            catch (OperationCanceledException)
            {
            }
            catch (PolyforkRateLimitException e)
            {
                HandleRateLimit(e);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Polyfork] preview failed for {asset.Id}: {e.Message}");
            }
            finally
            {
                _rebuilding = false;
                Repaint();
            }
        }

        // =====================================================================
        // GUI
        // =====================================================================

        // =====================================================================
        // Undo / redo
        // =====================================================================

        PolyforkRemixSnapshot Snapshot() => new()
        {
            Ranges = new Dictionary<string, float>(_ranges),
            Choices = new Dictionary<string, string>(_choices),
            Toggles = new Dictionary<string, bool>(_toggles),
            SlotColors = new Dictionary<string, Color>(_slotColors),
            Colorway = _colorway
        };

        /// <summary>Call immediately before mutating knob state.</summary>
        void RecordUndo(string opKey) => _history.Record(Snapshot(), opKey);

        void RestoreSnapshot(PolyforkRemixSnapshot state)
        {
            if (state == null) return;

            var geometryChanged = state.GeometryDiffers(Snapshot());

            _ranges.Clear();
            foreach (var kv in state.Ranges) _ranges[kv.Key] = kv.Value;

            _choices.Clear();
            foreach (var kv in state.Choices) _choices[kv.Key] = kv.Value;

            _toggles.Clear();
            foreach (var kv in state.Toggles) _toggles[kv.Key] = kv.Value;

            _slotColors.Clear();
            foreach (var kv in state.SlotColors) _slotColors[kv.Key] = kv.Value;

            _colorway = state.Colorway;

            // Only pay for a rebuild when the mesh actually differs; a colour-only undo is
            // applied in place and stays instant.
            if (geometryChanged) QueuePreviewRebuild(immediate: true);
            else ApplyColorsToPreview();

            Repaint();
        }

        void PerformUndo()
        {
            var restored = _history.Undo(Snapshot());
            if (restored != null) RestoreSnapshot(restored);
        }

        void PerformRedo()
        {
            var restored = _history.Redo(Snapshot());
            if (restored != null) RestoreSnapshot(restored);
        }

        /// <summary>
        /// Claims the editor's Undo/Redo commands while this window has focus, so Ctrl+Z
        /// here edits the remix rather than the user's scene.
        /// </summary>
        void HandleUndoCommands()
        {
            var e = Event.current;
            if (e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) return;

            var isUndo = e.commandName == "Undo";
            var isRedo = e.commandName == "Redo";
            if (!isUndo && !isRedo) return;

            // Only claim it when there is something of ours to undo; otherwise let the
            // command fall through to Unity so global undo still works from this window.
            if (isUndo && !_history.CanUndo) return;
            if (isRedo && !_history.CanRedo) return;

            if (e.type == EventType.ValidateCommand)
            {
                e.Use();
                return;
            }

            if (isUndo) PerformUndo();
            else PerformRedo();
            e.Use();
        }

        void OnGUI()
        {
            HandleUndoCommands();

            PolyforkBrand.DrawHeader(
                _all.Count > 0 ? $"{_all.Count} assets" : "Browse, remix and import",
                () =>
                {
                    if (GUILayout.Button("Account", EditorStyles.miniButton, GUILayout.Width(64f)))
                        Application.OpenURL(PolyforkKeySettings.AccountUrl);
                });

            DrawToolbar();
            DrawRateLimitBanner();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawGrid();
                DrawDetail();
            }

            DrawStatusBar();
        }

        void DrawRateLimitBanner()
        {
            if (IsRateLimited)
            {
                var seconds = _rateLimitedUntil - EditorApplication.timeSinceStartup;
                var wait = seconds > 60d ? $"{seconds / 60d:0} min"
                    : seconds > 0d ? $"{seconds:0} s"
                    : "shortly";

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        $"Out of remix bakes - resets in {wait}. Colour knobs still work; new geometry needs allowance.",
                        EditorStyles.wordWrappedMiniLabel);

                    if (GUILayout.Button("Get more", GUILayout.Width(96f), GUILayout.Height(20f)))
                        PolyforkApiKeyWindow.Open();
                }
                return;
            }

            // Warn while there is still room to act, rather than at the wall.
            if (!_budget.IsLow) return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var note = _budget.Access?.UpgradeNote;
                EditorGUILayout.LabelField(
                    $"{_budget.Describe()}. {(_budget.Access?.Authenticated == true ? "" : note)}".Trim(),
                    EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Add API key", GUILayout.Width(96f), GUILayout.Height(20f)))
                    PolyforkApiKeyWindow.Open();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();

                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180f));

                var kitIndex = Mathf.Max(0, Array.IndexOf(_kits, _kit));
                kitIndex = EditorGUILayout.Popup(kitIndex, _kits, EditorStyles.toolbarPopup, GUILayout.Width(150f));
                _kit = _kits[Mathf.Clamp(kitIndex, 0, _kits.Length - 1)];

                var classIndex = Mathf.Max(0, Array.IndexOf(_classes, _class));
                classIndex = EditorGUILayout.Popup(classIndex, _classes, EditorStyles.toolbarPopup, GUILayout.Width(110f));
                _class = _classes[Mathf.Clamp(classIndex, 0, _classes.Length - 1)];

                _freeOnly = GUILayout.Toggle(_freeOnly, "Free only", EditorStyles.toolbarButton, GUILayout.Width(70f));
                _remixableOnly = GUILayout.Toggle(_remixableOnly, "Remixable", EditorStyles.toolbarButton, GUILayout.Width(80f));

                GUILayout.Label("Max tris", EditorStyles.miniLabel, GUILayout.Width(52f));
                _maxTriangles = EditorGUILayout.IntPopup(
                    _maxTriangles,
                    new[] { "Any", "500", "1000", "2000", "5000" },
                    new[] { 0, 500, 1000, 2000, 5000 },
                    EditorStyles.toolbarPopup, GUILayout.Width(60f));

                if (EditorGUI.EndChangeCheck()) ApplyFilter();

                GUILayout.FlexibleSpace();

                var keyed = PolyforkKeySettings.HasKey || !string.IsNullOrEmpty(_client?.ApiKey);
                if (GUILayout.Button(keyed ? "Key active" : "Add API key", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                    PolyforkApiKeyWindow.Open();

                using (new EditorGUI.DisabledScope(_loading))
                {
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                        _ = LoadCatalogueAsync();
                }
            }
        }

        void DrawGrid()
        {
            using var scope = new EditorGUILayout.VerticalScope();
            _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll);

            if (_filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(_loading ? "Loading..." : "No assets match these filters.", MessageType.Info);
            }
            else
            {
                var available = Mathf.Max(CardSize, position.width - DetailWidth - 28f);
                var columns = Mathf.Max(1, Mathf.FloorToInt(available / (CardSize + CardPadding)));

                for (var i = 0; i < _filtered.Count; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (var c = 0; c < columns && i + c < _filtered.Count; c++)
                            DrawCard(_filtered[i + c]);
                        GUILayout.FlexibleSpace();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawCard(PolyforkAsset asset)
        {
            var rect = GUILayoutUtility.GetRect(CardSize, CardSize + 26f, GUILayout.Width(CardSize),
                GUILayout.Height(CardSize + 26f));

            var selected = _selected == asset;
            if (Event.current.type == EventType.Repaint)
            {
                var accent = PolyforkBrand.Accent;
                var bg = selected
                    ? new Color(accent.r, accent.g, accent.b, 0.45f)
                    : new Color(0f, 0f, 0f, 0.16f);
                EditorGUI.DrawRect(rect, bg);
            }

            var imageRect = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.width - 6f);
            var tex = _thumbs.Get(asset.Thumbnail);
            if (tex != null) GUI.DrawTexture(imageRect, tex, ScaleMode.ScaleToFit);
            else EditorGUI.DrawRect(imageRect, new Color(1f, 1f, 1f, 0.04f));

            var labelRect = new Rect(rect.x + 4f, rect.yMax - 24f, rect.width - 8f, 14f);
            GUI.Label(labelRect, asset.Title ?? asset.Id, EditorStyles.miniLabel);

            var metaRect = new Rect(rect.x + 4f, rect.yMax - 12f, rect.width - 8f, 12f);
            var badge = asset.Free ? "free" : "pro";
            if (asset.Remixable) badge += " - remix";
            GUI.Label(metaRect, $"{asset.Triangles} tri - {badge}", EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Select(asset);
                Event.current.Use();
            }
        }

        void DrawDetail()
        {
            using var scope = new EditorGUILayout.VerticalScope(GUILayout.Width(DetailWidth));

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Select an asset to remix it.", MessageType.None);
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorGUILayout.LabelField(_selected.Title ?? _selected.Id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{_selected.Triangles} tri - {_selected.Class} - {(_selected.Free ? "free" : $"${_selected.PriceUsdOrZero()}")}",
                EditorStyles.miniLabel);

            var previewRect = GUILayoutUtility.GetRect(DetailWidth - 12f, 200f);
            _preview.Draw(previewRect, _rebuilding);

            EditorGUILayout.Space(4f);
            DrawKnobs();

            EditorGUILayout.Space(8f);
            DrawImportSection();

            EditorGUILayout.EndScrollView();
        }

        void DrawKnobs()
        {
            if (_schema == null)
            {
                EditorGUILayout.HelpBox(
                    _selected.Remixable ? "Loading knobs..." : "This asset has no remix knobs.",
                    MessageType.None);
                return;
            }

            var knobs = _schema.Remixable.ToList();
            if (knobs.Count == 0)
            {
                EditorGUILayout.HelpBox("No knobs on this asset can be applied here.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Remix", EditorStyles.boldLabel);

            foreach (var knob in knobs)
            {
                switch (knob.Type)
                {
                    case PolyforkKnobType.Choice when knob.Support == PolyforkKnobSupport.LocalRecolor:
                        DrawColorwayKnob(knob);
                        break;
                    case PolyforkKnobType.Choice: DrawChoiceKnob(knob); break;
                    case PolyforkKnobType.Toggle: DrawToggleKnob(knob); break;
                    case PolyforkKnobType.Color: DrawColorKnob(knob); break;
                    case PolyforkKnobType.Range: DrawRangeKnob(knob); break;
                }
            }

            var hidden = _schema.All.Count(k => !k.IsSupported);
            if (hidden > 0)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(
                    $"{hidden} knob{(hidden == 1 ? "" : "s")} hidden - Polyfork does not bake {(hidden == 1 ? "it" : "them")} " +
                    "from a GLB. The asset's own module does; see the Local Baking sample.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_history.CanUndo))
                {
                    if (GUILayout.Button(new GUIContent("Undo", "Ctrl/Cmd + Z"), GUILayout.Width(60f)))
                        PerformUndo();
                }

                using (new EditorGUI.DisabledScope(!_history.CanRedo))
                {
                    if (GUILayout.Button(new GUIContent("Redo", "Ctrl/Cmd + Y, or Ctrl/Cmd + Shift + Z"),
                            GUILayout.Width(60f)))
                        PerformRedo();
                }

                if (GUILayout.Button("Reset to defaults"))
                {
                    RecordUndo("reset");
                    ResetKnobs();
                    QueuePreviewRebuild(immediate: true);
                }
            }
        }

        void DrawColorwayKnob(PolyforkKnob knob)
        {
            EditorGUILayout.LabelField(knob.Label, EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var option in knob.Options)
                {
                    // The default option often has no preset: it IS the asset's authored
                    // colours, which every colour knob already carries its own share of.
                    // Skipping it would leave no way back to the model as published.
                    var isAuthored = _schema.IsDefaultColorway(knob, option);
                    if (!isAuthored && !_schema.TryGetPreset(option, out _)) continue;

                    var swatch = SwatchFor(knob, option, isAuthored);

                    var rect = GUILayoutUtility.GetRect(26f, 20f, GUILayout.Width(26f));
                    EditorGUI.DrawRect(rect, swatch);

                    if (_colorway == option)
                        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), PolyforkBrand.Accent);

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        ApplyColorway(knob.Name, option);
                        Event.current.Use();
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        /// <summary>
        /// The chip shown for a colourway option: its most prominent slot colour, or for the
        /// authored default, that knob's own published hex.
        /// </summary>
        Color SwatchFor(PolyforkKnob knob, string option, bool isAuthored)
        {
            if (isAuthored)
            {
                var defaults = _schema.DefaultSlotColors();
                foreach (var slot in defaults)
                {
                    if (_schema.Knobs.TryGetValue(slot.Key, out var k) && k.Type == PolyforkKnobType.Color)
                        return slot.Value;
                }
                return Color.gray;
            }

            if (_schema.TryGetPreset(option, out var slots))
            {
                foreach (var kv in slots)
                {
                    if (PolyforkParams.TryParseHex(kv.Value, out var c)) return c;
                }
            }
            return Color.gray;
        }

        void ApplyColorway(string knobName, string option)
        {
            var knob = _schema.Knobs.TryGetValue(knobName, out var k) ? k : null;
            var isAuthored = _schema.IsDefaultColorway(knob, option);

            if (!isAuthored && !_schema.TryGetPreset(option, out _)) return;

            RecordUndo($"colorway:{option}");

            if (isAuthored)
            {
                // Back to the model as published: drop every override rather than writing
                // the authored hexes back in, so the GLB's own colours show through.
                _slotColors.Clear();
                foreach (var kv in _schema.DefaultSlotColors()) _slotColors[kv.Key] = kv.Value;
            }
            else if (_schema.TryGetPreset(option, out var slots))
            {
                foreach (var kv in slots)
                {
                    if (PolyforkParams.TryParseHex(kv.Value, out var c)) _slotColors[kv.Key] = c;
                }
            }

            _colorway = option;
            _colorwayKnob = knobName;
            ApplyColorsToPreview();
        }

        /// <summary>
        /// Recolours the object already on screen. No download, so this keeps working while
        /// rate limited and stays instant.
        /// </summary>
        void ApplyColorsToPreview()
        {
            if (_previewSlots != null && _previewSlots.HasSlots) _previewSlots.Apply(_slotColors);
            else QueuePreviewRebuild(immediate: true);   // nothing bound yet
            Repaint();
        }

        void DrawColorKnob(PolyforkKnob knob)
        {
            _slotColors.TryGetValue(knob.Name, out var current);

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ColorField(
                new GUIContent(knob.Label, knob.Describe), current, showEyedropper: true, showAlpha: false, hdr: false);

            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo($"color:{knob.Name}");
            _slotColors[knob.Name] = next;
            _colorway = null;                    // no longer a curated colourway
            ApplyColorsToPreview();
        }

        /// <summary>
        /// A structural choice: piece, layout, tower height. Polyfork bakes these, so they
        /// cost a round trip and an allowance exactly like a slider does.
        ///
        /// The option is sent as the literal string the schema published, never parsed into
        /// a number. Options read "12"/"15"/"18" on plenty of assets and the server compares
        /// them strictly, so a helpfully-converted 12 matches nothing and quietly returns
        /// the default mesh.
        /// </summary>
        void DrawChoiceKnob(PolyforkKnob knob)
        {
            if (knob.Options.Count == 0) return;

            _choices.TryGetValue(knob.Name, out var current);
            var index = Mathf.Max(0, knob.Options.ToList().IndexOf(current ?? knob.DefaultString));

            using var disabled = new EditorGUI.DisabledScope(IsRateLimited);

            var label = new GUIContent(
                knob.Label,
                IsRateLimited ? "Rate limited - add an API key to keep remixing geometry." : knob.Describe);

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Popup(label, index, knob.Options.Select(o => new GUIContent(o)).ToArray());
            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo($"choice:{knob.Name}");
            _choices[knob.Name] = knob.Options[next];
            QueuePreviewRebuild(immediate: true);   // one click, not a drag: no point debouncing
        }

        /// <summary>A structural toggle. Baked server-side, same as a choice.</summary>
        void DrawToggleKnob(PolyforkKnob knob)
        {
            if (!_toggles.TryGetValue(knob.Name, out var current)) current = knob.DefaultBool;

            using var disabled = new EditorGUI.DisabledScope(IsRateLimited);

            var label = new GUIContent(
                knob.Label,
                IsRateLimited ? "Rate limited - add an API key to keep remixing geometry." : knob.Describe);

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.Toggle(label, current);
            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo($"toggle:{knob.Name}");
            _toggles[knob.Name] = next;
            QueuePreviewRebuild(immediate: true);
        }

        void DrawRangeKnob(PolyforkKnob knob)
        {
            _ranges.TryGetValue(knob.Name, out var current);

            // Geometry is rebuilt server-side, so these are the only controls a rate limit
            // actually blocks. Grey them out rather than letting drags silently do nothing.
            using var disabled = new EditorGUI.DisabledScope(IsRateLimited);

            var label = new GUIContent(
                knob.Label,
                IsRateLimited ? "Rate limited - add an API key to keep remixing geometry." : knob.Describe);

            EditorGUI.BeginChangeCheck();
            float next;

            if (knob.IsIntegral)
            {
                next = EditorGUILayout.IntSlider(
                    label, Mathf.RoundToInt(current), Mathf.RoundToInt(knob.Min), Mathf.RoundToInt(knob.Max));
            }
            else
            {
                next = EditorGUILayout.Slider(label, current, knob.Min, knob.Max);
            }

            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo($"range:{knob.Name}");    // coalesced, so a whole drag is one step
            _ranges[knob.Name] = next;
            QueuePreviewRebuild();               // debounced: geometry needs a round trip
        }

        void DrawImportSection()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            _importFolder = EditorGUILayout.TextField("Folder", _importFolder);

            using (new EditorGUI.DisabledScope(_rebuilding))
            {
                if (GUILayout.Button("Import GLB to project", GUILayout.Height(24f)))
                    _ = ImportAsync();
            }

            if (GUILayout.Button("Open on polyfork.dev", EditorStyles.miniButton))
                Application.OpenURL(_selected.Page ?? "https://polyfork.dev");

            if (!string.IsNullOrEmpty(_importMessage))
                EditorGUILayout.HelpBox(_importMessage, _importMessageType);
        }

        async Task ImportAsync()
        {
            var asset = _selected;
            _importMessage = "Importing...";
            _importMessageType = MessageType.Info;
            Repaint();

            var result = await PolyforkAssetImporter.ImportAsync(
                _client, _loader, asset, _schema, BuildGeometryValues(), _slotColors, _importFolder, _cts.Token);

            if (result.Success)
            {
                _importMessage = result.ColorsBaked
                    ? $"Imported to {result.AssetPath} with your colours baked in."
                    : $"Imported to {result.AssetPath}.";
                _importMessageType = MessageType.Info;

                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(result.AssetPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            else
            {
                _importMessage = $"Import failed: {result.Error}";
                _importMessageType = MessageType.Error;

                if (result.RateLimited)
                    HandleRateLimit(new PolyforkRateLimitException("import", result.RetryAfter));
            }

            Repaint();
        }

        void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_loading ? "Loading..." : $"{_filtered.Count} of {_all.Count} shown", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (_budget.Synced)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    if (_budget.IsLow) style.normal.textColor = new Color(0.95f, 0.72f, 0.35f);
                    GUILayout.Label(
                        new GUIContent(_budget.Describe(),
                            "Only new geometry is metered. Variants anyone has already baked are free."),
                        style);
                    GUILayout.Space(10f);
                }

                GUILayout.Label(_status, EditorStyles.miniLabel);
            }
        }
    }

    internal static class PolyforkAssetExtensions
    {
        public static string PriceUsdOrZero(this PolyforkAsset asset) => asset.Free ? "0" : "1-3";
    }
}
