using System;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Prompt for a Polyfork API key.
    ///
    /// Shown automatically the first time a session is rate limited, and available any time
    /// from the gallery toolbar. A utility window rather than EditorUtility.DisplayDialog
    /// because the user needs somewhere to type.
    /// </summary>
    public sealed class PolyforkApiKeyWindow : EditorWindow
    {
        string _key = "";
        string _message;
        MessageType _messageType = MessageType.None;
        TimeSpan _retryAfter;
        bool _wasRateLimited;

        /// <summary>Opens the prompt in its rate-limited framing.</summary>
        public static void OpenRateLimited(TimeSpan retryAfter)
        {
            var window = Open();
            window._wasRateLimited = true;
            window._retryAfter = retryAfter;
        }

        /// <summary>
        /// Where the key in force actually came from.
        ///
        /// A key resolves from the environment, this window's EditorPrefs entry, or a
        /// polyfork.key file, and EditorPrefs is shared by every project on the machine - so
        /// a key typed once, anywhere, silently applies everywhere afterwards. Finding
        /// yourself already signed in with no memory of doing it is unsettling rather than
        /// convenient, so the window says which one it is.
        /// </summary>
        static void DrawActiveKeySource()
        {
            var key = PolyforkCredentials.Resolve(null, out var source);
            if (string.IsNullOrEmpty(key)) return;

            var where = source switch
            {
                PolyforkCredentials.Source.Environment =>
                    $"the {PolyforkCredentials.EnvironmentVariable} environment variable",
                PolyforkCredentials.Source.EditorSettings =>
                    "this editor's saved key (EditorPrefs, shared across all your projects)",
                PolyforkCredentials.Source.StreamingAssets => $"StreamingAssets/{PolyforkCredentials.KeyFileName}",
                PolyforkCredentials.Source.PersistentData => $"persistentDataPath/{PolyforkCredentials.KeyFileName}",
                _ => "the component inspector"
            };

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField($"A key is already active, from {where}.",
                EditorStyles.wordWrappedMiniLabel);
        }

        [MenuItem("Tools/Polyfork/API Key…", priority = 1)]
        [MenuItem("Window/Polyfork/API Key…", priority = 1101)]
        public static PolyforkApiKeyWindow Open()
        {
            var window = GetWindow<PolyforkApiKeyWindow>(utility: true, title: "Polyfork API key", focus: true);
            PolyforkBrand.ApplyTitle(window, "Polyfork API key");
            window.minSize = new Vector2(420f, 274f);
            window.maxSize = new Vector2(420f, 284f);
            window._key = PolyforkKeySettings.Get();
            window.ShowUtility();
            return window;
        }

        void OnGUI()
        {
            PolyforkBrand.DrawHeader("Lifts the remix cap and unlocks paid downloads");
            EditorGUILayout.Space(6f);

            if (_wasRateLimited)
            {
                /* The anonymous allowance is monthly, so Retry-After is genuinely days -
                 * and "resets in about 26027 minutes" is a true sentence nobody can read.
                 * Past a couple of hours the useful answer is a date. */
                var wait = _retryAfter.TotalHours >= 36
                    ? $"on {DateTime.Now.Add(_retryAfter):d MMMM}"
                    : _retryAfter.TotalHours >= 2
                        ? $"in about {_retryAfter.TotalHours:0} hours"
                        : _retryAfter.TotalMinutes >= 1
                            ? $"in about {_retryAfter.TotalMinutes:0} minute{(_retryAfter.TotalMinutes >= 2 ? "s" : "")}"
                            : $"in {_retryAfter.TotalSeconds:0} seconds";

                EditorGUILayout.HelpBox(
                    $"Polyfork limits remixes on unauthenticated connections, and this one has hit the cap. " +
                    $"It resets {wait}.\n\n" +
                    "Adding an API key lifts the limit and unlocks downloads for paid assets.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Browsing and free assets work without a key. A key lifts the remix rate limit " +
                    "and unlocks downloads for paid assets.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("API key", EditorStyles.boldLabel);
            _key = EditorGUILayout.PasswordField(_key);

            EditorGUILayout.LabelField(
                "Stored in EditorPrefs on this machine only — never written into your scene or repo.",
                EditorStyles.miniLabel);

            /* Below the field, and that placement is load-bearing.
             *
             * This note only draws once a key resolves, so pasting one made it appear on the
             * next repaint - and anything appearing ABOVE a text field shifts every control
             * id under it. IMGUI keys the text editor's selection by control id, so the
             * editor woke up holding a selection that belonged to a different control and
             * Unity's paste path cut a Substring with stale indices:
             *
             *   ArgumentOutOfRangeException: startIndex cannot be larger than length of string
             *     at TextEditingUtilities.DeleteSelection ... PasswordField
             *
             * It threw on the first paste and worked on the second, because by then the note
             * was already there and the layout had stopped moving. Nothing that comes and
             * goes belongs above a field someone is typing into. */
            DrawActiveKeySource();

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                /* "Create an API Key", not "Create an account": in a window whose whole job
                 * is to be given a key, the useful button is the one that produces one. The
                 * account is a step on the way, not the thing being asked for. */
                if (GUILayout.Button("Create an API Key", GUILayout.Height(22f)))
                    Application.OpenURL(PolyforkKeySettings.AccountUrl);

                if (GUILayout.Button("See plans", GUILayout.Height(22f)))
                    Application.OpenURL(PolyforkKeySettings.PricingUrl);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!PolyforkKeySettings.HasKey))
                {
                    if (GUILayout.Button("Remove key", GUILayout.Height(24f)))
                    {
                        PolyforkKeySettings.Clear();
                        _key = "";
                        _message = "Key removed.";
                        _messageType = MessageType.Info;
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Close", GUILayout.Width(70f), GUILayout.Height(24f)))
                {
                    Close();
                    return;
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_key)))
                {
                    if (GUILayout.Button("Save key", GUILayout.Width(90f), GUILayout.Height(24f)))
                    {
                        PolyforkKeySettings.Set(_key);
                        _message = $"Saved {PolyforkCredentials.Redact(_key)}. The gallery will use it right away.";
                        _messageType = MessageType.Info;
                        _wasRateLimited = false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_message, _messageType);
            }
        }
    }
}
