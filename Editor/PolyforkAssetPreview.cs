using System;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Orbitable 3D preview of a loaded Polyfork asset, drawn into an IMGUI rect.
    ///
    /// Uses PreviewRenderUtility so the model renders in isolation rather than being
    /// dropped into the open scene.
    /// </summary>
    public sealed class PolyforkAssetPreview : IDisposable
    {
        PreviewRenderUtility _utility;
        GameObject _target;
        Vector2 _orbit = new(25f, -25f);
        float _distance = 2.2f;
        Bounds _bounds;

        void EnsureUtility()
        {
            if (_utility != null) return;

            _utility = new PreviewRenderUtility();
            _utility.camera.fieldOfView = 30f;
            _utility.camera.nearClipPlane = 0.01f;
            _utility.camera.farClipPlane = 100f;
            _utility.camera.clearFlags = CameraClearFlags.SolidColor;
            _utility.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);

            _utility.lights[0].intensity = 1.3f;
            _utility.lights[0].transform.rotation = Quaternion.Euler(38f, 140f, 0f);
            _utility.lights[1].intensity = 0.6f;
            _utility.lights[1].transform.rotation = Quaternion.Euler(-20f, -60f, 0f);
            _utility.ambientColor = new Color(0.35f, 0.36f, 0.40f);
        }

        /// <summary>Takes ownership of the instance and frames it.</summary>
        public void SetTarget(GameObject go)
        {
            Clear();
            EnsureUtility();

            _target = go;
            if (_target == null) return;

            _target.hideFlags = HideFlags.HideAndDontSave;
            _utility.AddSingleGO(_target);

            _bounds = PolyforkSpawner.CalculateBounds(_target);
            var size = Mathf.Max(_bounds.size.x, _bounds.size.y, _bounds.size.z);
            _distance = Mathf.Max(0.4f, size * 2.6f);
        }

        public void Clear()
        {
            if (_target != null)
            {
                UnityEngine.Object.DestroyImmediate(_target);
                _target = null;
            }
        }

        public void Draw(Rect rect, bool busy)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0.16f, 0.17f, 0.19f, 1f));

            HandleInput(rect);

            if (_target == null)
            {
                var label = busy ? "Rebuilding…" : "No preview";
                EditorGUI.LabelField(rect, label, CenteredLabel());
                return;
            }

            EnsureUtility();
            _utility.BeginPreview(rect, GUIStyle.none);

            var rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            var focus = _bounds.center;
            _utility.camera.transform.position = focus + rotation * (Vector3.back * _distance);
            _utility.camera.transform.rotation = rotation;

            _utility.Render(allowScriptableRenderPipeline: true);
            var texture = _utility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);

            if (busy)
            {
                var badge = new Rect(rect.x + 6f, rect.y + 6f, 84f, 16f);
                EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(badge, " rebuilding…", EditorStyles.miniLabel);
            }
        }

        void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.MouseDrag when e.button == 0:
                    _orbit.x += e.delta.x;
                    _orbit.y = Mathf.Clamp(_orbit.y + e.delta.y, -89f, 89f);
                    e.Use();
                    GUI.changed = true;
                    break;

                case EventType.ScrollWheel:
                    _distance = Mathf.Clamp(_distance * (1f + e.delta.y * 0.05f), 0.15f, 60f);
                    e.Use();
                    GUI.changed = true;
                    break;
            }
        }

        static GUIStyle CenteredLabel() => new(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.7f, 0.72f, 0.75f) }
        };

        public void Dispose()
        {
            Clear();
            _utility?.Cleanup();
            _utility = null;
        }
    }
}
