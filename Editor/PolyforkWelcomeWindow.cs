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
    /// an account. Browsing, previewing and remixing all work with no key at all. Left
    /// undiscovered, that reads the other way round - a store that wants signing up to
    /// before it shows you anything.
    ///
    /// Everything it says about the account is read from GET /api/me rather than written
    /// into this file, for the same reason the knobs are read from the schema: the numbers
    /// and tier names belong to the server, and a hardcoded "40 an hour" becomes a lie the
    /// first time pricing moves.
    /// </summary>
    public sealed class PolyforkWelcomeWindow : EditorWindow
    {
        /// <summary>Bumped only when the window has something new to say.</summary>
        const int Revision = 2;

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
        bool _loading = true;
        string _error;
        CancellationTokenSource _cts;

        [MenuItem("Polyfork/Welcome", priority = 2)]
        public static void Open()
        {
            var window = GetWindow<PolyforkWelcomeWindow>(utility: true, title: "Polyfork", focus: true);
            PolyforkBrand.ApplyTitle(window, "Polyfork");

            // Fixed size: the content does not reflow, and a utility window that remembers a
            // stretched size from last time leaves a lake of grey under the buttons.
            var size = new Vector2(470f, 340f);
            window.minSize = size;
            window.maxSize = size;
            window.ShowUtility();
        }

        /// <summary>
        /// Shows the window the first time this project sees this revision of the package.
        ///
        /// Deliberately quiet in batch mode: a CI run that opens a modal and waits for a
        /// click is a hung build, and this is exactly the kind of nicety that causes one.
        /// Delayed by a frame too, because the editor is still importing when
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
            }
            catch (OperationCanceledException)
            {
                return;   // window closed mid-request; nothing to report
            }
            catch (Exception e)
            {
                _error = e.Message;
            }

            _loading = false;
            if (this != null) Repaint();
        }

        /// <summary>
        /// Whether this connection is signed in.
        ///
        /// Answered by the server, not by PolyforkKeySettings: a key can arrive from the
        /// POLYFORK_API_KEY environment variable or a polyfork.key file as well as from
        /// EditorPrefs, and reading only EditorPrefs told a signed-in Founders user that no
        /// account was needed while quoting them their 900 bakes an hour.
        /// </summary>
        bool SignedIn => _access is { Authenticated: true };

        void OnGUI()
        {
            DrawHero();

            using (new EditorGUILayout.VerticalScope(
                       new GUIStyle { padding = new RectOffset(18, 18, 0, 0) }))
            {
                EditorGUILayout.Space(12f);
                DrawPitch();
                EditorGUILayout.Space(10f);
                DrawStatus();
                EditorGUILayout.Space(12f);
                DrawActions();
            }
        }

        /// <summary>A proper hero rather than a toolbar strip: this is the one screen that
        /// gets to be a little bit of an occasion.</summary>
        void DrawHero()
        {
            var rect = GUILayoutUtility.GetRect(0f, 96f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.145f, 0.15f, 0.165f)
                : new Color(0.93f, 0.94f, 0.96f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), PolyforkBrand.Accent);

            var mark = PolyforkBrand.Mark;
            if (mark != null)
            {
                GUI.DrawTexture(new Rect(rect.x + 18f, rect.y + 20f, 56f, 56f), mark, ScaleMode.ScaleToFit);
            }

            var title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 };
            GUI.Label(new Rect(rect.x + 88f, rect.y + 24f, rect.width - 100f, 26f), "Polyfork", title);

            var sub = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11 };
            sub.normal.textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor;
            GUI.Label(new Rect(rect.x + 88f, rect.y + 50f, rect.width - 106f, 34f),
                "3D assets you can turn the knobs on, right here in the editor.", sub);
        }

        void DrawPitch()
        {
            EditorGUILayout.LabelField(
                "Every model is a little program. Pick one, drag its sliders, flip its " +
                "options, recolour it, and take the result into your project as a .glb.",
                EditorStyles.wordWrappedLabel);
        }

        /// <summary>What this particular connection can do, in the server's own words.</summary>
        void DrawStatus()
        {
            if (_loading)
            {
                EditorGUILayout.LabelField("Saying hello to polyfork.dev...", EditorStyles.miniLabel);
                return;
            }

            if (_error != null)
            {
                EditorGUILayout.LabelField(
                    $"Could not reach polyfork.dev ({_error}). Everything still works once you are online.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var style = new GUIStyle(EditorStyles.wordWrappedMiniLabel);

            if (SignedIn)
            {
                style.normal.textColor = PolyforkBrand.Accent;
                EditorGUILayout.LabelField($"Signed in — {_access.Describe()}. Have fun.", style);
                return;
            }

            var line = "No account needed. Browse, remix and import free assets as you are";
            if (_access != null) line += $" — {_access.Describe()}";
            EditorGUILayout.LabelField(line + ".", style);

            if (!string.IsNullOrEmpty(_access?.UpgradeNote))
                EditorGUILayout.LabelField(_access.UpgradeNote, EditorStyles.wordWrappedMiniLabel);
        }

        void DrawActions()
        {
            // Signed in already? Then the only useful button is the one into the catalogue.
            if (SignedIn)
            {
                if (GUILayout.Button("Browse the catalogue", GUILayout.Height(32f)))
                {
                    Close();
                    PolyforkGalleryWindow.Open();
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Start browsing — it's free", GUILayout.Height(32f)))
                    {
                        Close();
                        PolyforkGalleryWindow.Open();
                    }

                    if (GUILayout.Button("I have a key", GUILayout.Height(32f), GUILayout.Width(120f)))
                    {
                        Close();
                        PolyforkApiKeyWindow.Open();
                    }
                }
            }

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!SignedIn && GUILayout.Button("Create a free account", EditorStyles.miniButton))
                    Application.OpenURL(PolyforkKeySettings.AccountUrl);

                if (GUILayout.Button("Pricing", EditorStyles.miniButton))
                    Application.OpenURL(PolyforkKeySettings.PricingUrl);

                if (GUILayout.Button("Docs", EditorStyles.miniButton))
                    Application.OpenURL("https://github.com/lucas-martinic/polyfork-unity-connector");
            }

            // Only worth mentioning when it is not already on.
            if (!PolyforkJsRuntimeProvider.IsAvailable)
            {
                EditorGUILayout.Space(2f);
                if (GUILayout.Button("Make bakes instant and free \u2192", EditorStyles.miniLabel))
                    PolyforkLocalBakingWindow.Open();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "Polyfork ▸ Welcome brings this back.", EditorStyles.centeredGreyMiniLabel);
        }
    }
}
