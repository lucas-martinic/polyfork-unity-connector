using System;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Polyfork's mark and accent colour for the editor windows.
    ///
    /// The mark is embedded as PNG bytes rather than shipped as a texture asset. A package
    /// installed from a git URL is immutable, so its textures import with whatever settings
    /// their .meta files carry and cannot be corrected in place by the person using it;
    /// building the Texture2D here means the filtering and colour space are stated outright
    /// and look the same in every project.
    /// </summary>
    public static class PolyforkBrand
    {
        /// <summary>Polyfork blue, as used on polyfork.dev.</summary>
        public static readonly Color Blue = new(0x1f / 255f, 0x6f / 255f, 0xeb / 255f);

        /// <summary>The same blue, lifted for readability on the dark editor skin.</summary>
        public static Color Accent => EditorGUIUtility.isProSkin
            ? new Color(0.36f, 0.60f, 1f)
            : Blue;

        static Texture2D _icon;
        static Texture2D _mark;

        /// <summary>16-32 px: the window title bar and menus.</summary>
        public static Texture2D Icon => _icon != null ? _icon : _icon = Decode(Icon32);

        /// <summary>Up to 48 px: the window header.</summary>
        public static Texture2D Mark => _mark != null ? _mark : _mark = Decode(Mark96);

        /// <summary>
        /// Header strip: the mark, the product name, and whatever the window wants to say
        /// about its current state on the right.
        /// </summary>
        public static void DrawHeader(string subtitle = null, Action right = null)
        {
            var rect = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.16f, 0.17f)
                : new Color(0.85f, 0.86f, 0.88f));

            // A hairline in brand blue, so the window reads as Polyfork's at a glance
            // without tinting anything Unity draws.
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Accent);

            var mark = Mark;
            if (mark != null)
            {
                var size = 24f;
                GUI.DrawTexture(
                    new Rect(rect.x + 10f, rect.y + (rect.height - size) * 0.5f - 1f, size, size),
                    mark, ScaleMode.ScaleToFit);
            }

            var titleRect = new Rect(rect.x + 44f, rect.y + 4f, rect.width - 200f, 18f);
            GUI.Label(titleRect, "Polyfork", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(subtitle))
            {
                GUI.Label(new Rect(rect.x + 44f, rect.y + 19f, rect.width - 200f, 14f),
                    subtitle, EditorStyles.miniLabel);
            }

            if (right == null) return;

            GUILayout.BeginArea(new Rect(rect.xMax - 150f, rect.y + 9f, 140f, 20f));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                right();
            }
            GUILayout.EndArea();
        }

        /// <summary>Applies the mark to a window's title bar.</summary>
        public static void ApplyTitle(EditorWindow window, string text)
        {
            if (window == null) return;
            window.titleContent = new GUIContent(text, Icon);
        }

        static Texture2D Decode(string base64)
        {
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };

                if (tex.LoadImage(Convert.FromBase64String(base64))) return tex;

                UnityEngine.Object.DestroyImmediate(tex);
            }
            catch (Exception)
            {
                // A missing mark is a cosmetic problem; never let it take a window down.
            }
            return null;
        }

        const string Icon32 =
            "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAMAAABEpIrGAAAAUVBMVEUNGygSGiYdJTATGiYTGiZGTVY+RE4UHCidoabLz9b///+mqa9qb3d1" +
            "e4JaYGqIjJP19fYkKzf6+/zj5OUcJC8zOkSWm6F9gopVW2S9xdCvtsIHjznQAAAACHRSTlMEgNb9Tv77fzwb+HgAAAFMSURBVDjLhdOLroMg" +
            "DABQJkNpecl4yPb/H3oL6KJRd5tIDD2gaMsYxWPgFzE8WI8nFxIuQo782fMSb0I20fNKQx2NohGsmzfB2CRa3jsPCDTKV4jOJ+hinNjQNnAG" +
            "dYbFotagPBYec99iYLxZ5yowGk2BnNAW2rLOA98BB9qgEZBHAvQe18AJcAK1vQHLP4AmKyg3YKmT+ScoDfTDnoExFcgGzB3IKMf2Nc5gWYFq" +
            "oD4in3bQUOQv4DQIAgn0JVgwF0gSIYK9AnmhlKA/HryI6goYxKAC3QVJE2cgzaHW7AkofQCivK08gBDDLg/cC3EE6Mb5mw9Jvy1XR4BWbGIe" +
            "LUD28wqGtSteJfWnzKIAFS5sRfsQ28KS2qLS8vJb9uzbWLOoR8nxDZ9e0r1xdq0X4otOyM3H633rkRjX5uV0JZd4OTQvxXTd/lPN/QFyXSkA" +
            "dc37GQAAAABJRU5ErkJggg==";

        const string Mark96 =
            "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAMAAADVRocKAAAAV1BMVEUOFyYSGSUSGiYSGSUSGiYSGiYSGSYmLThTWWJpb3eFiZBITliWm6Ld" +
            "3uD///88QkzDzNccJC+prrScoqozOkTd4OX+/v7a3eF2e4NcYmq0u8N8goq7wssRwKIGAAAAB3RSTlMALHqqyv6Kr9SbgQAABSxJREFUaN69" +
            "mutiqyAMgNeuzhuteL++/3OOBKIg0LVKlz/njGI+SAJKwteXKZfr9y06LLfv6+XriVx+juve5MfHuHyHUA/y7UTQ6OMkzQ5KmsQ0C3v40vI5" +
            "uz9OyZ0V0hsXl/6cn9Muhec24QJNZRVCPUhdgr7LbvzFSeMYhmrMOYD+vA2n//FowUw3PX6Kvf6us56ymjrudVrbbLEEDij73e9D0eRMV3jP" +
            "knwcJr1TJkxdzp6Z38vVDbC+dv6t0EtRs7mFqQBPVn1dLlvK2k2oYcXRBHLzN9IWxTSxal2la9dka3JPIldTAA+YpkRtGQcNsWoCi858ybfJ" +
            "wgDTtm9wHM4AX5QX7AlkognmzYSh2pb61gqd8hZ8MwrjdJIjZHARCgwksBCzAA3824sB91zo6wCJ/ihhuXPet514uhANXJmp6G0AQxtd6VkD" +
            "kGIoCQBHGUQnGlSMLX0pO7XkCMc+0IvmK8RQ/LAAgwlIqRM4QTZFahQ4fPREaq2cGOJIrOJk1z4LF5uAUdpDhs5CgIwANZ+dZkrACTQSTQYN" +
            "ILUlFAiCFE3QshCglC1V41AkJh4BYH4CQG2wAefrIyuAKTPgKBZHLM0KkNlgZgESYkcVtEy0IgjAHYDsBQBq42L+4/oINlUEaMjtYQBMB3AV" +
            "uGEA6QqoDYDoU54HxJ8D1B5ArQFiAsznAbUL0JwBMHy4pOmHAQwK0P0rYDYB4Iz+BCAjQGYCqqeA7DBg7fS/gLsCFJ8CtCEAs7H5IwD9ngUH" +
            "sP8BTKEAMwEGE7DogG4PYOcBnAAzARL5pXQYkNL7sQ4LGP4fUK2ATH3rlZ8A9EEAMwHEw6MHIN4Z5WMPqM8D7hpg/Vo9DHjYgDYIYNgDYIOY" +
            "wgJSAiQEWEzAqA4p5wCjBpDHuC5S25MTUB0A5CbgEQQwEyD1A5JQgML4RsHzxqA+6IvDADoEgq2aHSBWi7sIAsgUgG2ARkVuQ2eeU4BafTln" +
            "9I2C+hL1pRQAwNWJLN0AiZzUEp0BMAJA1BesgoNZpBIewBqqOg8EGOkcv+7Ey9oSS68UxwDyRIbZCkM/Jfy2XNI5wKOrAJHo6Z4+NfIGzQFA" +
            "tQGA0dqpumpLQemA6RDgD/k4INYAy4sA/g4g0gD7JGcIQHcE0L8BaI8AOjtH5RXQW7wLEJt+/GomG1OsRfUmILeTVz6R5Ygyew/AHJ2fTAAk" +
            "n94BtGK/KboX9GPWNMUNq3wHgAN7IZD6GDcqztQG+DoAs4vDX/qxRpBDwm7K3wV0zd+EFhzcyLznAgnVsvUAZtfTMPunoYT1g1hmJeWJxxoo" +
            "pTWdeiA5/YzQJbr+RYwntsICE7M337KFD4do9MYSPFxWSj/WGiarD6aWHclxneArTg2G/sHtMUyOO9L7BqFx/pgZ+uGv3J6rTO87ChSrTECI" +
            "HaWH2tDPPL1kgQK8XPgMvbgJ8LVd1qQfaa6dRZZYHEUiy0q72XNTf+nRD7vTj7vMtSeYuwYukVU/ltycVSgqc7kKdXuC8XNB57RVv9OHa6HO" +
            "WWrcGVxfQ9n6DiP9rp1ALzWiFxr/K4yZYwQDjUr/FPu2XdwIry+WexPdSfgKUC5G/YlrsZvlXlmwbrwFa055CbIY7FKL0u8cWL8rWKuSe+0j" +
            "lOv5A12AVw/izK/fKrn/dWkg1laKADBGtxvsOreQxXFpYL32UDBXODUaYIaU16JOJnZo9J5rD0KudHJJ0nl3IUPMeKT/w5EpTdWL3rziMW8X" +
            "N65fDvn01ZNQl2duP88v6IS+/vMLH3OixAOuiv0AAAAASUVORK5CYII=";
    }
}
