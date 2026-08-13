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

        [MenuItem("Polyfork/Setup", priority = 3)]
        public static void Open()
        {
            var window = GetWindow<PolyforkLocalBakingWindow>(utility: true, title: "Polyfork", focus: true);
            PolyforkBrand.ApplyTitle(window, "Polyfork setup");
            var size = new Vector2(520f, 400f);
            window.minSize = size;
            window.maxSize = size;
            window.ShowUtility();
        }

        Vector2 _scroll;

        /// <summary>
        /// Whether an engine actually registered itself. The binding assembly compiles only
        /// when QuickJS is present, so a factory being set means everything downstream works.
        /// </summary>
        static bool EngineReady => PolyforkJsRuntimeProvider.IsAvailable;

        /// <summary>
        /// Whether the project has ASKED for the packages, read from the manifest.
        ///
        /// Adding a package triggers a domain reload, which wipes every field on this window,
        /// so anything remembered in one cannot survive its own success. That is why the
        /// first version offered to install again after installing: it had forgotten. The
        /// manifest is the one thing that outlives the reload.
        /// </summary>
        static bool PackagesRequested()
        {
            try
            {
                var manifest = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? ".", "Packages", "manifest.json");

                if (!File.Exists(manifest)) return false;

                var text = File.ReadAllText(manifest);
                return text.Contains(CorePackage) && text.Contains(QuickJsPackage);
            }
            catch (Exception)
            {
                return false;   // unreadable manifest is not a reason to hide the button
            }
        }

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
                else if (PackagesRequested()) DrawInstalledButNotRunning();
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

        /// <summary>
        /// Installed, but no engine yet. Almost always "Unity has not finished compiling",
        /// which is a wait rather than a problem - but it needs saying, because the previous
        /// version showed the install button again here and left you with no way to tell a
        /// finished install from one that never started.
        /// </summary>
        void DrawInstalledButNotRunning()
        {
            EditorGUILayout.LabelField(
                SessionState.GetBool(InstallRequestedKey, false)
                    ? "PuerTS installed"
                    : "PuerTS is installed",
                EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField(
                "Both packages are in this project, so there is nothing more to install. " +
                "Local baking switches on as soon as Unity finishes compiling them.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8f);
            DrawDetected();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "Still not running after the spinner stops? Check the Console for errors from " +
                "PuerTS — that is where a platform-specific native plugin failure shows up.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Package Manager", EditorStyles.miniButton))
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");

                if (GUILayout.Button("Recompile", EditorStyles.miniButton))
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
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

            EditorGUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(_installing))
            {
                var label = _installing ? "Installing…" : "Install PuerTS  ·  14 MB, about a minute";
                if (GUILayout.Button(label, PrimaryButton, GUILayout.Height(44f)))
                    _ = InstallAsync();
            }

            if (_installStatus != null)
                EditorGUILayout.LabelField(_installStatus, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Or do it by hand", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "Take both from the same release. The QuickJS backend pins an exact core version, " +
                "and OpenUPM carries only the core, at a version that does not match.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
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

        /// <summary>The one action worth clicking, sized and coloured to say so.</summary>
        internal static GUIStyle PrimaryButton
        {
            get
            {
                var style = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                style.normal.textColor = PolyforkBrand.Accent;
                style.hover.textColor = PolyforkBrand.Accent;
                style.focused.textColor = PolyforkBrand.Accent;
                style.active.textColor = PolyforkBrand.Accent;
                return style;
            }
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

        const string InstallRequestedKey = "Polyfork.PuertsInstallRequested";

        bool _installing;
        string _installStatus;
        AddAndRemoveRequest _addRequest;

        const string PackageDirName = "PuerTS";

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath) ?? ".";

        /// <summary>
        /// Where the unpacked packages live: a folder in the project, beside Assets.
        ///
        /// Unpacked rather than handed over as tarballs, because Unity's "add from tarball"
        /// expects npm's layout - one `package/` folder at the archive root - and PuerTS
        /// ships archives rooted at `core/` and `quickjs/`. Given one of those, Unity unpacks
        /// it to a temp directory, finds no package.json at the top, and reports the temp
        /// path in the error, which reads as a broken download rather than a wrong shape.
        ///
        /// Inside the project because the manifest stores the path: a package unpacked into
        /// the system temp folder stops existing, and takes the project with it next resolve.
        /// </summary>
        static string PackageDir => Path.Combine(ProjectRoot, PackageDirName);

        /// <summary>Clears the tarballs an older version left in Packages/, which Unity never
        /// managed to install and which are 14 MB of nothing.</summary>
        static void CleanUpStaleTarballs()
        {
            try
            {
                var stale = Path.Combine(ProjectRoot, "Packages", "PuerTS");
                if (!Directory.Exists(stale)) return;

                foreach (var f in Directory.GetFiles(stale, "*.tar.gz")) File.Delete(f);
                if (Directory.GetFileSystemEntries(stale).Length == 0) Directory.Delete(stale);
            }
            catch (Exception)
            {
                // Tidying is not worth failing an install over.
            }
        }

        /// <summary>
        /// Unpacks one archive and returns the folder holding its package.json.
        ///
        /// Staged inside the project rather than the system temp folder so the final move
        /// stays on one volume: Directory.Move cannot cross drives, and on Windows the temp
        /// folder frequently is one.
        /// </summary>
        static string Unpack(byte[] archive)
        {
            var staging = Path.Combine(PackageDir, ".staging");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            try
            {
                PolyforkTar.ExtractTarGz(archive, staging);

                var manifest = Directory.GetFiles(staging, "package.json", SearchOption.AllDirectories)
                    .OrderBy(f => f.Length)          // the shallowest is the package root
                    .FirstOrDefault();

                if (manifest == null) throw new Exception("the archive contains no package.json");

                var root = Path.GetDirectoryName(manifest);
                var name = (string)JObject.Parse(File.ReadAllText(manifest))["name"]
                           ?? Path.GetFileName(root);

                var target = Path.Combine(PackageDir, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);

                Directory.Move(root, target);
                return target;
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

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

                Directory.CreateDirectory(PackageDir);
                CleanUpStaleTarballs();

                var assets = new[] { core, quickjs };
                var installed = new string[assets.Length];

                for (var i = 0; i < assets.Length; i++)
                {
                    _installStatus = $"Downloading {assets[i].name}...";
                    Repaint();

                    var bytes = await DownloadAsync(assets[i].url);

                    _installStatus = $"Unpacking {assets[i].name}...";
                    Repaint();

                    installed[i] = Unpack(bytes);
                }

                _installStatus = "Adding the packages to the project...";
                Repaint();

                // Relative to the Packages folder, which is how Unity resolves a file: path.
                /* Adding a package reloads the domain, which kills this window's fields and
                 * this callback with them. The flag is what lets the window come back knowing
                 * an install was in flight rather than starting from scratch. */
                SessionState.SetBool(InstallRequestedKey, true);

                _addRequest = Client.AddAndRemove(new[]
                {
                    $"file:../{PackageDirName}/{Path.GetFileName(installed[0])}",
                    $"file:../{PackageDirName}/{Path.GetFileName(installed[1])}"
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
         * every request it makes, and these requests go to github.com.
         *
         * Its own awaiter too: the one in PolyforkClient sits in an internal class, so it is
         * invisible across the assembly boundary. Widening it, or granting the editor
         * assembly blanket access to every internal, would both be larger changes than the
         * twenty lines below. */
        static async Task<byte[]> DownloadAsync(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "polyfork-unity-connector");   // GitHub rejects requests without one
            req.timeout = 300;

            await SendAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"{url}: {req.error}");

            return req.downloadHandler.data;
        }

        static Task SendAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var op = request.SendWebRequest();

            // Already finished from cache: completed callbacks do not fire for those.
            if (op.isDone) tcs.TrySetResult(true);
            else op.completed += _ => tcs.TrySetResult(true);

            return tcs.Task;
        }

        static async Task<string> DownloadStringAsync(string url)
            => Encoding.UTF8.GetString(await DownloadAsync(url));

        void OnDisable()
        {
            /* The install keeps going if the window is closed - Unity owns the request once
             * it is made - but this poll must stop, or it calls Repaint on a destroyed
             * window every editor tick for the rest of the session. */
            EditorApplication.update -= PollAdd;
        }

        void OnInspectorUpdate() => Repaint();   // so it flips to green without being poked
    }
}
