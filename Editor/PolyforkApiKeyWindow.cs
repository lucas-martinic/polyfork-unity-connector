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

        [MenuItem("Window/Polyfork/API Key…", priority = 1101)]
        public static PolyforkApiKeyWindow Open()
        {
            var window = GetWindow<PolyforkApiKeyWindow>(utility: true, title: "Polyfork API key", focus: true);
            window.minSize = new Vector2(420f, 250f);
            window.maxSize = new Vector2(420f, 260f);
            window._key = PolyforkKeySettings.Get();
            window.ShowUtility();
            return window;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6f);

            if (_wasRateLimited)
            {
                var wait = _retryAfter.TotalMinutes >= 1
                    ? $"{_retryAfter.TotalMinutes:0} minute{(_retryAfter.TotalMinutes >= 2 ? "s" : "")}"
                    : $"{_retryAfter.TotalSeconds:0} seconds";

                EditorGUILayout.HelpBox(
                    $"Polyfork limits remixes on unauthenticated connections, and this one has hit the cap. " +
                    $"It resets in about {wait}.\n\n" +
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

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create an account", GUILayout.Height(22f)))
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
