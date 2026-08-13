using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// The front door: shown once after the package is installed, and any time from
    /// <c>Polyfork ▸ Welcome</c>.
    ///
    /// It exists because the first thing a new user needs to know is that they do not need
    /// an account. Browsing, previewing and importing all work with no key at all; a key
    /// raises the remix allowance and unlocks paid downloads. Left undiscovered, that reads
    /// the other way round - a store that wants signing up to before it shows anything.
    ///
    /// The allowance quoted here is read from GET /api/me rather than written into this
    /// file, for the same reason the knobs are: the numbers are the server's to change, and
    /// a hardcoded "40 an hour" becomes a lie the first time pricing moves.
    /// </summary>
    public sealed class PolyforkWelcomeWindow : EditorWindow
    {
        /// <summary>Bumped only when the window has something new to say.</summary>
        const int Revision = 1;

        /// <summary>
        /// Per-project, because "just installed" is a fact about this project, while
        /// EditorPrefs is shared across every project on the machine.
        ///
        /// The project is identified by a hand-rolled FNV-1a rather than string.GetHashCode,
        /// which .NET is free to randomise per process. If it ever were randomised the key
        /// would differ on every launch, and a one-time welcome would greet the user forever.
        /// </summary>
        static string SeenKey => $"Polyfork.Welcome.Seen.{Fnv1a(Application.dataPath):X8}";

        static uint Fnv1a(string s)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in s) hash = (hash ^ c) * 16777619u;
                return hash;
            }
        }

        PolyforkAccess _access;
        string _status = "Checking what this connection can do...";
        CancellationTokenSource _cts;

        [MenuItem("Polyfork/Welcome", priority = 2)]
        public static void Open()
        {
            var window = GetWindow<PolyforkWelcomeWindow>(utility: true, title: "Polyfork", focus: true);
            PolyforkBrand.ApplyTitle(window, "Polyfork");
            window.minSize = new Vector2(460f, 366f);
            window.maxSize = new Vector2(460f, 376f);
            window.ShowUtility();
        }

        /// <summary>
        /// Shows the window the first time this project sees this revision of the package.
        ///
        /// Deliberately quiet in batch mode: a CI run that opens a modal window and waits
        /// for a click is a hung build, and this is exactly the kind of nicety that causes
        /// one. Also delayed by a frame, because the editor is still importing when
        /// InitializeOnLoad runs and a window opened there can come up blank.
        /// </summary>
        [InitializeOnLoadMethod]
        static void MaybeShowOnFirstImport()
        {
            if (Application.isBatchMode) return;
            if (EditorPrefs.GetInt(SeenKey, 0) >= Revision) return;

            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetInt(SeenKey, 0) >= Revision) return;
                EditorPrefs.SetInt(SeenKey, Revision);
                Open();
            };
        }

        void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _ = LoadAccessAsync();
        }

        void OnDisable()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        async Task LoadAccessAsync()
        {
            try
            {
                var client = new PolyforkClient { ApiKey = PolyforkCredentials.Resolve(null) };
                _access = await client.GetAccessAsync(_cts.Token);
                _status = null;
            }
            catch (OperationCanceledException)
            {
                // The window closed while the request was in flight. Nothing to report.
            }
            catch (Exception e)
            {
                // Never block the front door on the network: the buttons below still work.
                _status = $"Could not reach polyfork.dev ({e.Message}). You can still browse once connected.";
            }

            if (this != null) Repaint();
        }

        void OnGUI()
        {
            PolyforkBrand.DrawHeader("Parametric low-poly assets, inside the editor");

            EditorGUILayout.Space(10f);

            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(14, 14, 0, 0) }))
            {
                EditorGUILayout.LabelField(
                    "Browse the polyfork.dev catalogue, turn the same knobs the web viewer " +
                    "exposes, and import the result as a .glb with your colours baked in.",
                    EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space(10f);
                DrawAllowance();
                EditorGUILayout.Space(12f);
                DrawActions();
            }
        }

        /// <summary>
        /// What this connection can do right now, in the server's own numbers.
        /// </summary>
        void DrawAllowance()
        {
            if (PolyforkKeySettings.HasKey && _access is { Authenticated: true })
            {
                EditorGUILayout.HelpBox(
                    $"Signed in. {_access.Describe()}.", MessageType.Info);
                return;
            }

            if (_status != null)
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
                return;
            }

            var free = "No account needed. Browsing, previewing and importing all work as you are.";
            if (_access != null)
            {
                free += $"\n\nRight now: {_access.Describe()}.";
                if (!string.IsNullOrEmpty(_access.UpgradeNote)) free += $"\n{_access.UpgradeNote}";
            }

            EditorGUILayout.HelpBox(free, MessageType.Info);
        }

        void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add an API key", GUILayout.Height(30f)))
                {
                    Close();
                    PolyforkApiKeyWindow.Open();
                }

                if (GUILayout.Button("Continue free", GUILayout.Height(30f)))
                {
                    Close();
                    PolyforkGalleryWindow.Open();
                }
            }

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create a free account", EditorStyles.miniButton))
                    Application.OpenURL(PolyforkKeySettings.AccountUrl);

                if (GUILayout.Button("Pricing", EditorStyles.miniButton))
                    Application.OpenURL(PolyforkKeySettings.PricingUrl);

                if (GUILayout.Button("Docs", EditorStyles.miniButton))
                    Application.OpenURL("https://github.com/lucas-martinic/polyfork-unity-connector");
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Reopen any time from Polyfork ▸ Welcome.", EditorStyles.centeredGreyMiniLabel);
        }
    }
}
