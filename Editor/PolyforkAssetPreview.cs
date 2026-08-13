using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
        /// <summary>0xeceae6, the store viewer's background.</summary>
        public static readonly Color Background = new(0.925f, 0.918f, 0.902f, 1f);

        PreviewRenderUtility _utility;

        /* No ground plane.
         *
         * There was one, lit by the same key and rim as the model, and it could not be made
         * to match the background: what you see from a lit surface is albedo times lighting,
         * never a flat colour, so setting its albedo to the sky colour still left a visible
         * horizon. Unlit and exactly the sky colour, a plane is indistinguishable from no
         * plane - so this is that, with one fewer object, material and pipeline question.
         * The contact shadow is what says the model is standing on something. */
        GameObject _contact;
        GameObject _target;
        Vector2 _orbit = new(25f, -25f);
        float _distance = 2.2f;
        Bounds _bounds;

        void EnsureUtility()
        {
            if (_utility != null) return;

            /* Matched to public/viewer.js on polyfork.dev so an asset looks the same here as
             * on its store page. Same background, same key and rim, same 38 degree lens.
             * A model that changes colour and mood between the store and the editor makes
             * the buyer wonder which one is the asset. */
            _utility = new PreviewRenderUtility();
            _utility.camera.fieldOfView = 38f;
            _utility.camera.nearClipPlane = 0.01f;
            _utility.camera.farClipPlane = 200f;
            _utility.camera.clearFlags = CameraClearFlags.SolidColor;
            _utility.camera.backgroundColor = Background;

            // key: 0xfff2e0 at 2.4, from (4, 7, 5)
            _utility.lights[0].color = new Color(1f, 0.949f, 0.878f);
            _utility.lights[0].intensity = 1.5f;
            _utility.lights[0].transform.rotation = Quaternion.LookRotation(new Vector3(-4f, -7f, -5f));
            _utility.lights[0].shadows = LightShadows.Soft;
            _utility.lights[0].shadowStrength = 0.32f;   // ShadowMaterial opacity 0.18 on the web, plus ambient
            _utility.lights[0].shadowBias = 0.005f;      // the ground is a big flat plane: prime acne territory
            _utility.lights[0].shadowNormalBias = 0.05f;
            _utility.lights[0].shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;

            // rim: 0xdfe8ff at 0.9, from (-5, 4, -6)
            _utility.lights[1].color = new Color(0.874f, 0.910f, 1f);
            _utility.lights[1].intensity = 0.75f;
            _utility.lights[1].transform.rotation = Quaternion.LookRotation(new Vector3(5f, -4f, 6f));
            _utility.lights[1].shadows = LightShadows.None;

            // Stands in for the hemisphere light: warm bounce from below, white from above.
            _utility.ambientColor = new Color(0.72f, 0.70f, 0.66f);
        }

        /// <summary>
        /// Takes ownership of the instance.
        ///
        /// <paramref name="frameCamera"/> should be false when the same asset is simply
        /// being rebuilt by a knob change: re-framing there would throw away the zoom the
        /// user set, and it would also hide the thing they are looking for, since holding
        /// the distance fixed is what makes a geometry change read as the model growing.
        /// </summary>
        public void SetTarget(GameObject go, bool frameCamera = true)
        {
            var hadTarget = _target != null;

            Clear();
            EnsureUtility();

            _target = go;
            if (_target == null) return;

            _target.hideFlags = HideFlags.HideAndDontSave;
            _utility.AddSingleGO(_target);

            _bounds = PolyforkSpawner.CalculateBounds(_target);

            EnsureContactShadow();
            PlaceContactShadow();

            if (frameCamera || !hadTarget) Frame();
        }

        /// <summary>Pulls the camera back to fit the current target.</summary>
        public void Frame()
        {
            var size = Mathf.Max(_bounds.size.x, _bounds.size.y, _bounds.size.z);
            _distance = Mathf.Max(0.4f, size * 2.6f);
        }

        /// <summary>
        /// The blob the model sits in. Drawn rather than cast: see the shader's own note on
        /// why a real-time shadow cannot be relied on inside a preview scene.
        /// </summary>
        void EnsureContactShadow()
        {
            if (_contact != null) return;

            var shader = Shader.Find("Polyfork/Contact Shadow");
            if (shader == null) return;      // cosmetic; never worth failing a preview over

            _contact = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _contact.name = "Polyfork Contact Shadow";
            _contact.hideFlags = HideFlags.HideAndDontSave;

            var collider = _contact.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            var renderer = _contact.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(shader)
            {
                name = "Polyfork Contact Shadow",
                hideFlags = HideFlags.HideAndDontSave
            };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // A quad faces +Z; lay it flat.
            _contact.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            _utility.AddSingleGO(_contact);
        }

        /// <summary>Sits the shadow under the model.</summary>
        void PlaceContactShadow()
        {
            var span = Mathf.Max(_bounds.size.x, _bounds.size.z, 0.2f);
            var floor = _bounds.min.y;

            if (_contact != null)
            {
                // Wider than the model's footprint so the falloff has somewhere to go, and
                // offset along the key light so the model looks lit rather than pinned.
                _contact.transform.localScale = Vector3.one * (span * 2.1f);
                _contact.transform.position = new Vector3(
                    _bounds.center.x - span * 0.06f,
                    floor - span * 0.002f,       // just under the model's lowest point
                    _bounds.center.z - span * 0.05f);
            }
        }

        public void Clear()
        {
            if (_target == null) return;

            ReleaseGeneratedAssets(_target);
            UnityEngine.Object.DestroyImmediate(_target);
            _target = null;
        }

        /// <summary>
        /// Frees the meshes and materials a preview built for itself.
        ///
        /// Destroying a GameObject destroys its components, not the assets they point at, and
        /// every bake creates a fresh Mesh per part plus a Material. So each rebuild leaked
        /// both - which at 30-60 ms a bake, with a slider being dragged and no debounce in
        /// the way, is dozens of leaked objects a second and an editor that gets heavier the
        /// longer you use it.
        ///
        /// IsPersistent is the safety line: anything saved in the project is somebody else's
        /// and is left alone. What a preview generates at runtime is not persistent, which is
        /// exactly the set that should go.
        /// </summary>
        static void ReleaseGeneratedAssets(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh != null && !EditorUtility.IsPersistent(mesh))
                    UnityEngine.Object.DestroyImmediate(mesh);
            }

            ReleaseGeneratedMaterials(root);
        }

        static void ReleaseGeneratedMaterials(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null && !EditorUtility.IsPersistent(material))
                        UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        public void Draw(Rect rect, bool busy)
        {
            // The same fill the camera clears to, so the panel does not flash a different
            // colour before the first frame lands.
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, Background);

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

                // Zoom now persists across rebuilds, so offer the scene-view way back.
                case EventType.KeyDown when e.keyCode == KeyCode.F:
                    Frame();
                    e.Use();
                    GUI.changed = true;
                    break;
            }
        }

        // Dark text now: the panel is a light background whichever editor skin is in use.
        static GUIStyle CenteredLabel() => new(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.42f, 0.41f, 0.39f) }
        };

        public void Dispose()
        {
            Clear();

            // Its material is generated too, and outlives the GameObject the same way.
            if (_contact != null)
            {
                ReleaseGeneratedMaterials(_contact);
                UnityEngine.Object.DestroyImmediate(_contact);
                _contact = null;
            }

            _utility?.Cleanup();
            _utility = null;
        }
    }
}
