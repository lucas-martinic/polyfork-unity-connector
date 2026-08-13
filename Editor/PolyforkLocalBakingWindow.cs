using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

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
                // Deferred for the same reason the welcome window defers: a window opened
                // from inside a closing window's OnGUI comes up blank.
                EditorApplication.delayCall += () => { Close(); PolyforkGalleryWindow.Open(); };
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

            EditorGUILayout.Space(6f);

            using (new EditorGUI.DisabledScope(_installing))
            {
                if (GUILayout.Button(
                        _installing ? "Installing…" : "Install PuerTS for me",
                        GUILayout.Height(32f)))
                {
                    _ = InstallAsync();
                }
            }

            if (_installStatus != null)
                EditorGUILayout.LabelField(_installStatus, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Or do it by hand", EditorStyles.miniBoldLabel);
            Step(1, "Download PuerTS_Core_<version> and PuerTS_Quickjs_<version> from the releases page.");
            Step(2, "Window ▸ Package Manager ▸ + ▸ Add package from tarball…, and add each one.");
            Step(3, "Come back here — this window turns green on its own once the engine registers.");

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open releases page", EditorStyles.miniButton))
                    Application.OpenURL(ReleasesUrl);

                if (GUILayout.Button("Copy package names", EditorStyles.miniButton))
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

        // =====================================================================
        // One-button install
        // =====================================================================

        bool _installing;
        string _installStatus;
        AddAndRemoveRequest _addRequest;

        /// <summary>Where the tarballs are kept. Inside the project, because the manifest
        /// stores the path: a tarball in a temp folder breaks the package on the next
        /// resolve, and a relative path under Packages/ still works on a teammate's
        /// machine.</summary>
        static string TarballDir =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Packages", "PuerTS");

        /// <summary>
        /// Downloads a matched pair of PuerTS packages and adds both to the project.
        ///
        /// Taking both from ONE release is the point: that is what makes the version trap
        /// structurally impossible rather than something the user has to be careful about.
        /// They are added in a single AddAndRemove call for the same reason - QuickJS
        /// depends on the core, so resolving them together is the only ordering that works.
        /// </summary>
        async Task InstallAsync()
        {
            _installing = true;
            _installStatus = "Looking up the latest PuerTS release...";
            Repaint();

            try
            {
                var (tag, core, quickjs) = await FindReleaseAsync();
                if (core == null || quickjs == null)
                {
                    _installStatus = "Could not find a release with both packages. Use the manual steps below.";
                    return;
                }

                var mb = (core.size + quickjs.size) / 1048576f;

                /* Confirmed at the moment of the click, not warned about in advance: this
                 * downloads third-party native plugins from the internet and edits the
                 * project manifest, which is not something to do because a window was open. */
                if (!EditorUtility.DisplayDialog(
                        "Install PuerTS?",
                        $"Downloads PuerTS {tag} ({mb:0.#} MB) from github.com/Tencent/puerts:\n\n" +
                        $"  {core.name}\n  {quickjs.name}\n\n" +
                        $"They are saved to Packages/PuerTS/ and added to this project. Both come " +
                        $"from the same release, so their versions match.\n\n" +
                        "PuerTS is MIT-licensed and includes native plugins.",
                        "Install", "Cancel"))
                {
                    _installStatus = null;
                    return;
                }

                Directory.CreateDirectory(TarballDir);

                foreach (var asset in new[] { core, quickjs })
                {
                    _installStatus = $"Downloading {asset.name}...";
                    Repaint();

                    var bytes = await DownloadAsync(asset.url);
                    File.WriteAllBytes(Path.Combine(TarballDir, asset.name), bytes);
                }

                _installStatus = "Adding the packages to the project...";
                Repaint();

                // Relative to the Packages folder, which is how Unity resolves a file: path.
                _addRequest = Client.AddAndRemove(new[]
                {
                    $"file:PuerTS/{core.name}",
                    $"file:PuerTS/{quickjs.name}"
                });

                EditorApplication.update += PollAdd;
            }
            catch (Exception e)
            {
                _installing = false;
                _installStatus = $"Install failed: {e.Message}. The manual steps below still work.";
                Debug.LogWarning($"[Polyfork] PuerTS install failed: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        void PollAdd()
        {
            if (_addRequest is not { IsCompleted: true }) return;

            EditorApplication.update -= PollAdd;
            _installing = false;

            _installStatus = _addRequest.Status == StatusCode.Success
                ? "Installed. Unity will recompile, and this window turns green once the engine registers."
                : $"Unity could not add the packages: {_addRequest.Error?.message}. Try the manual steps below.";

            _addRequest = null;
            Repaint();
        }

        /// <summary>The newest Unity release carrying both packages.</summary>
        static async Task<(string tag, ReleaseAsset core, ReleaseAsset quickjs)> FindReleaseAsync()
        {
            var json = await DownloadStringAsync("https://api.github.com/repos/Tencent/puerts/releases?per_page=20");

            foreach (var release in JArray.Parse(json))
            {
                var tag = (string)release["tag_name"] ?? "";
                if (!tag.StartsWith("Unity_v", StringComparison.Ordinal)) continue;

                ReleaseAsset core = null, quickjs = null;
                foreach (var a in release["assets"] ?? (JToken)new JArray())
                {
                    var name = (string)a["name"] ?? "";
                    if (!name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) continue;

                    var item = new ReleaseAsset
                    {
                        name = name,
                        url = (string)a["browser_download_url"],
                        size = a["size"]?.Value<long>() ?? 0
                    };

                    if (name.StartsWith("PuerTS_Core_", StringComparison.OrdinalIgnoreCase)) core = item;
                    else if (name.StartsWith("PuerTS_Quickjs_", StringComparison.OrdinalIgnoreCase)) quickjs = item;
                }

                if (core != null && quickjs != null) return (tag, core, quickjs);
            }

            return (null, null, null);
        }

        sealed class ReleaseAsset
        {
            public string name;
            public string url;
            public long size;
        }

        /* Its own transport, deliberately. PolyforkClient attaches the Polyfork API key to
         * every request it makes, and these requests go to github.com. */
        static async Task<byte[]> DownloadAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "polyfork-unity-connector");   // GitHub rejects requests without one
            req.timeout = 300;

            await req.SendWebRequestAsync();

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"{url}: {req.error}");

            return req.downloadHandler.data;
        }

        static async Task<string> DownloadStringAsync(string url)
            => Encoding.UTF8.GetString(await DownloadAsync(url));

        void OnInspectorUpdate() => Repaint();   // so it flips to green without being poked
    }
}
