using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Explains how to make bakes instant, and reports whether they already are.
    ///
    /// This exists because the instruction it replaces ("install the PuerTS core and QuickJS
    /// packages") was not actionable. Those packages are not in Unity's registry, and the
    /// route most people would try - OpenUPM - only carries the core, at a version that does
    /// not match the QuickJS backend. Following the obvious path leaves you with a
    /// mismatched pair and no local baking, with nothing on screen saying why.
    /// </summary>
    public sealed class PolyforkLocalBakingWindow : EditorWindow
    {
        const string CorePackage = "com.tencent.puerts.core";
        const string QuickJsPackage = "com.tencent.puerts.quickjs";
        const string ReleasesUrl = "https://github.com/Tencent/puerts/releases";

        [MenuItem("Polyfork/Make Bakes Instant…", priority = 3)]
        public static void Open()
        {
            var window = GetWindow<PolyforkLocalBakingWindow>(utility: true, title: "Polyfork", focus: true);
            PolyforkBrand.ApplyTitle(window, "Instant bakes");
            var size = new Vector2(520f, 400f);
            window.minSize = size;
            window.maxSize = size;
            window.ShowUtility();
        }

        Vector2 _scroll;

        /// <summary>
        /// Whether an engine actually registered itself. This is the only honest signal:
        /// the binding assembly compiles only when QuickJS is present, so if the factory is
        /// set, everything downstream of it works.
        /// </summary>
        static bool EngineReady => PolyforkJsRuntimeProvider.IsAvailable;

        void OnGUI()
        {
            PolyforkBrand.DrawHeader(EngineReady
                ? $"Running on {PolyforkJsRuntimeProvider.EngineName}"
                : "Bakes currently go to the server");

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            using (new EditorGUILayout.VerticalScope(
                       new GUIStyle { padding = new RectOffset(16, 16, 12, 12) }))
            {
                if (EngineReady) DrawReady();
                else DrawSetup();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawReady()
        {
            EditorGUILayout.HelpBox(
                $"Local baking is active on {PolyforkJsRuntimeProvider.EngineName}.\n\n" +
                "Knob changes run the asset's own module here in the editor: no request, no " +
                "allowance, no waiting. Nothing ships with your game — the engine binding and " +
                "its three.js bundle are editor-only.",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Assets whose module you cannot fetch — paid ones you do not own — still " +
                "preview from their public GLB and still remix on the server.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Browse assets", GUILayout.Height(26f)))
            {
                Close();
                PolyforkGalleryWindow.Open();
            }
        }

        void DrawSetup()
        {
            EditorGUILayout.LabelField(
                "Right now every knob change asks polyfork.dev to rebuild the mesh: about " +
                "120 ms, and it counts against your hourly allowance.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Add a JavaScript engine and the editor runs the asset's own program instead. " +
                "Sliders become instant and cost nothing. The engine is editor-only: it never " +
                "reaches a player build.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Installing PuerTS", EditorStyles.boldLabel);

            /* The version trap, stated first because it is the one that wastes an afternoon.
             * The QuickJS package pins the core to its exact version, and OpenUPM carries
             * only the core - at a different version - so the obvious route half-works. */
            EditorGUILayout.HelpBox(
                "Take both packages from the SAME release. The QuickJS backend depends on an " +
                "exact core version, and OpenUPM carries only the core, at a version that does " +
                "not match. Mixing them installs cleanly and then never works.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);
            Step(1, $"Download PuerTS_Core_<version> and PuerTS_Quickjs_<version> from the releases page.");
            Step(2, "Window ▸ Package Manager ▸ + ▸ Add package from tarball…, and add each one.");
            Step(3, "Come back here — this window turns green on its own once the engine registers.");

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open releases page", GUILayout.Height(26f)))
                    Application.OpenURL(ReleasesUrl);

                if (GUILayout.Button("Copy package names", GUILayout.Height(26f)))
                {
                    EditorGUIUtility.systemCopyBuffer = $"{CorePackage}\n{QuickJsPackage}";
                    ShowNotification(new GUIContent("Copied"));
                }
            }

            EditorGUILayout.Space(10f);
            DrawDetected();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Prefer not to? Nothing breaks. The server keeps baking, and topology-preserving " +
                "sliders are already smoothed by interpolation.",
                EditorStyles.wordWrappedMiniLabel);
        }

        static void Step(int n, string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{n}.", GUILayout.Width(16f));
                EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
            }
        }

        /// <summary>
        /// What is actually installed, so a half-finished install is visible rather than
        /// silently inert. Asked of the Package Manager rather than inferred.
        /// </summary>
        void DrawDetected()
        {
            var core = Installed(CorePackage);
            var quickjs = Installed(QuickJsPackage);

            if (core == null && quickjs == null)
            {
                EditorGUILayout.LabelField("Neither package is installed yet.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"{CorePackage}: {core ?? "not installed"}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{QuickJsPackage}: {quickjs ?? "not installed"}", EditorStyles.miniLabel);

            if (core != null && quickjs != null && core != quickjs)
            {
                EditorGUILayout.HelpBox(
                    $"Versions differ ({core} and {quickjs}). Install both from the same release.",
                    MessageType.Error);
            }
            else if (core != null && quickjs != null && !EngineReady)
            {
                EditorGUILayout.HelpBox(
                    "Both packages are present but no engine has registered. Unity may still be " +
                    "compiling; if this persists, check the Console for errors from PuerTS.",
                    MessageType.Warning);
            }
        }

        /// <summary>Installed version of a package, or null. Never throws: this is decoration.</summary>
        static string Installed(string id)
        {
            try
            {
                var list = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                return list?.FirstOrDefault(p => p.name == id)?.version;
            }
            catch (Exception)
            {
                return null;
            }
        }

        void OnInspectorUpdate() => Repaint();   // so it flips to green without being poked
    }
}
