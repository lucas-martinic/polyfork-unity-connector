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

        /// <summary>
        /// The same colour as the background, so the horizon disappears.
        ///
        /// A ground that contrasts with the sky draws a line across the frame and turns a
        /// product shot into a diorama. Matching them leaves only the shadow to say the model
        /// is standing on something, which is the whole trick the store viewer uses.
        /// </summary>
        static Color GroundColor => Background;

        PreviewRenderUtility _utility;
        GameObject _ground;
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

            EnsureGround();
            EnsureContactShadow();
            PlaceGround();

            if (frameCamera || !hadTarget) Frame();
        }

        /// <summary>Pulls the camera back to fit the current target.</summary>
        public void Frame()
        {
            var size = Mathf.Max(_bounds.size.x, _bounds.size.y, _bounds.size.z);
            _distance = Mathf.Max(0.4f, size * 2.6f);
        }

        /// <summary>
        /// The pale ground the model sits on, and what its shadow lands on.
        ///
        /// Stock shader on purpose. Receiving a shadow is the one thing that differs hard
        /// between the built-in pipeline and URP - the sampling is not portable - so the
        /// plane uses whichever lit shader the project's pipeline already ships, and gets it
        /// right for free. The model needs our own shader because it needs vertex colours;
        /// the ground needs neither.
        /// </summary>
        void EnsureGround()
        {
            if (_ground != null) return;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Polyfork Ground";
            _ground.hideFlags = HideFlags.HideAndDontSave;

            // A collider in a preview scene is dead weight and can be picked up by physics
            // queries running elsewhere in the editor.
            var collider = _ground.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            var urp = GraphicsSettings.currentRenderPipeline != null;
            var shader = (urp ? Shader.Find("Universal Render Pipeline/Lit") : null)
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Diffuse");

            var material = new Material(shader) { name = "Polyfork Ground", hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", GroundColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", GroundColor);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);

            var renderer = _ground.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveShadows = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _utility.AddSingleGO(_ground);
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

        /// <summary>Sits the ground and its shadow under the model.</summary>
        void PlaceGround()
        {
            var span = Mathf.Max(_bounds.size.x, _bounds.size.z, 0.2f);
            var floor = _bounds.min.y;

            if (_ground != null)
            {
                // The primitive plane is 10 units across, hence the tenth.
                _ground.transform.localScale = Vector3.one * (span * 6f / 10f);
                _ground.transform.position = new Vector3(
                    _bounds.center.x,
                    floor - span * 0.004f,       // just below, so it does not z-fight the model
                    _bounds.center.z);
            }

            if (_contact != null)
            {
                // Wider than the model's footprint so the falloff has somewhere to go, and
                // offset along the key light so the model looks lit rather than pinned.
                _contact.transform.localScale = Vector3.one * (span * 2.1f);
                _contact.transform.position = new Vector3(
                    _bounds.center.x - span * 0.06f,
                    floor - span * 0.002f,       // above the ground, below the model
                    _bounds.center.z - span * 0.05f);
            }
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

            if (_ground != null)
            {
                UnityEngine.Object.DestroyImmediate(_ground);
                _ground = null;
            }

            if (_contact != null)
            {
                UnityEngine.Object.DestroyImmediate(_contact);
                _contact = null;
            }

            _utility?.Cleanup();
            _utility = null;
        }
    }
}
