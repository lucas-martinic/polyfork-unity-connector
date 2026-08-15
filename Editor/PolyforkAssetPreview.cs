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

        /* No ground plane and no shadow-catcher object.
         *
         * A lit plane could not be made to match the background - what a lit surface shows is
         * albedo times lighting, never a flat colour - and unlit at exactly the sky colour a
         * plane is indistinguishable from no plane. The shadow is projected geometry instead,
         * so there is nothing left for a plane to catch. */
        static readonly int GroundYId = Shader.PropertyToID("_GroundY");
        static readonly int LightDirId = Shader.PropertyToID("_LightDir");

        /// <summary>Matches the key light set up in EnsureUtility: it sits at (4, 7, 5).</summary>
        static readonly Vector4 KeyLightDirection = new Vector3(4f, 7f, 5f).normalized;
        GameObject _target;
        /* x is yaw, y is PITCH, and pitch is applied as Quaternion.Euler(pitch, yaw, 0) -
         * so a negative pitch puts the camera below the model looking up, which is where it
         * used to start. Product shots look slightly down at the subject. */
        Vector2 _orbit = new(25f, 18f);
        float _distance = 2.2f;

        /* Where the camera is HEADING, with _orbit and _distance easing towards it.
         *
         * The web viewer runs OrbitControls with enableDamping, which is most of why dragging
         * a model on polyfork.dev feels different from dragging one here: input moved the
         * camera instantly and stopped dead. Settle() is called from the window's editor tick,
         * so the glide costs repaints only while it is actually gliding. */
        Vector2 _orbitTarget = new(25f, 18f);
        float _distanceTarget = 2.2f;

        /// <summary>The distance Frame() chose, which the zoom clamps are expressed against.</summary>
        float _frameDistance = 2.2f;

        /* Time based, not per tick.
         *
         * This was a fixed fraction applied per editor tick, on the reasoning that a tick is
         * near enough a frame. It is not: OrbitControls damps per RENDERED frame at 60fps,
         * while EditorApplication.update runs slower and at a rate that changes with what the
         * editor is doing. The same constant therefore became a long floaty glide, which read
         * as the whole preview having got slower.
         *
         * Exponential smoothing against real elapsed time settles in the same wall-clock time
         * whatever the tick rate. Tau is the time to close ~63% of the gap; 0.05 s puts a full
         * settle inside about a sixth of a second, which is where OrbitControls lands. */
        const float DampingTau = 0.05f;
        double _lastSettle;

        // viewer.js: controls.minDistance = d * 0.35, controls.maxDistance = d * 2.5.
        const float MinZoom = 0.35f;
        const float MaxZoom = 2.5f;

        /// <summary>What the camera orbits. Set by Frame, never by a rebuild.</summary>
        Vector3 _focus;
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
            _utility.lights[0].shadowBias = 0.005f;
            _utility.lights[0].shadowNormalBias = 0.05f;

            /* No shadowResolution here. It is a built-in-pipeline property, and setting it
             * under URP logs "compatible only with the Built-In Render Pipeline" on every
             * preview. The shadow is drawn geometry now anyway, so the light's own shadow
             * quality settles nothing. */

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

            ApplyPlanarShadow(_target);

            if (frameCamera || !hadTarget) Frame();
        }

        /// <summary>
        /// Pulls the camera back to fit the current target, and pins what it looks at.
        ///
        /// The focus is captured here rather than read from the bounds every frame. Bounds
        /// move when a knob changes the silhouette, so reading them live meant the camera
        /// drifted after its subject on every rebuild - which reads as the camera moving
        /// rather than the model changing, and that is backwards. Framing is a thing that
        /// happens when an asset is opened, and not again until it is asked for.
        /// </summary>
        public void Frame()
        {
            var size = Mathf.Max(_bounds.size.x, _bounds.size.y, _bounds.size.z);
            _distance = _distanceTarget = _frameDistance = Mathf.Max(0.4f, size * 2.6f);
            _orbitTarget = _orbit;
            _focus = _bounds.center;
        }

        /// <summary>
        /// Flattens the model onto the ground as a second draw of the same mesh.
        ///
        /// Adding the shadow material to each renderer's list is what makes Unity draw the
        /// mesh twice: once in colour, once projected. Cheaper than a duplicate hierarchy,
        /// and it cannot drift out of sync with what it is shadowing.
        /// </summary>
        void ApplyPlanarShadow(GameObject root)
        {
            var shader = Shader.Find("Polyfork/Planar Shadow");
            if (shader == null) return;      // cosmetic; never worth failing a preview over

            // Per target, so the existing cleanup frees it along with everything else the
            // preview generated.
            var shadow = new Material(shader)
            {
                name = "Polyfork Planar Shadow",
                hideFlags = HideFlags.HideAndDontSave
            };

            shadow.SetFloat(GroundYId, _bounds.min.y);
            shadow.SetVector(LightDirId, KeyLightDirection);

            /* Renderer, not MeshRenderer. A rigged asset draws through a
             * SkinnedMeshRenderer, which is a Renderer but not a MeshRenderer, so the narrower
             * type silently skipped every character and fish in the catalogue: the shark had
             * no shadow, the sea anemone did, and the difference was the rig rather than
             * anything about either model. */
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var extended = new Material[materials.Length + 1];
                materials.CopyTo(extended, 0);
                extended[materials.Length] = shadow;
                renderer.sharedMaterials = extended;
            }
        }

        /// <summary>
        /// Eases the camera towards where input pointed it. Returns true while still moving,
        /// so the caller repaints only for as long as there is something to see.
        /// </summary>
        public bool Settle()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = _lastSettle > 0d ? (float)(now - _lastSettle) : 0f;
            _lastSettle = now;

            // A tick after a domain reload or a long stall can report a huge delta; clamping
            // keeps that from teleporting the camera.
            dt = Mathf.Clamp(dt, 0f, 0.1f);

            var dOrbit = _orbitTarget - _orbit;
            var dDist = _distanceTarget - _distance;

            // Below a fifth of a degree and a millimetre nobody can see the difference, and
            // an asymptote that never arrives is a repaint every tick forever.
            if (dOrbit.sqrMagnitude < 0.04f && Mathf.Abs(dDist) < 0.001f)
            {
                _orbit = _orbitTarget;
                _distance = _distanceTarget;
                return false;
            }

            var k = 1f - Mathf.Exp(-dt / DampingTau);
            _orbit += dOrbit * k;
            _distance += dDist * k;
            return true;
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
                Release(filter.sharedMesh);

            // Same blind spot as the shadow had: a rigged asset keeps its mesh on the
            // SkinnedMeshRenderer, so every rebuild of one leaked a mesh.
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Release(skinned.sharedMesh);

            static void Release(Mesh mesh)
            {
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
            _utility.camera.transform.position = _focus + rotation * (Vector3.back * _distance);
            _utility.camera.transform.rotation = rotation;

            _utility.Render(allowScriptableRenderPipeline: true);
            var texture = _utility.EndPreview();

            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);

            /* No "rebuilding" badge.
             *
             * It was there for a rebuild that took long enough to need explaining. They take
             * tens of milliseconds, so what it actually did was flash a dark box over the
             * corner of the model on every knob change - drawing the eye to the one thing on
             * screen that is not the model, exactly while you are looking at the model. The
             * status bar already carries the timing for anyone who wants it. */
        }

        void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                case EventType.MouseDrag when e.button == 0:
                    /* OrbitControls turns a drag of the element's full HEIGHT into a full
                     * revolution, on both axes. A fixed degrees-per-pixel felt different in a
                     * small preview from a large one, and different again from the website. */
                    var perPixel = 360f / Mathf.Max(1f, rect.height);
                    _orbitTarget.x += e.delta.x * perPixel;
                    _orbitTarget.y = Mathf.Clamp(_orbitTarget.y + e.delta.y * perPixel, -89f, 89f);
                    e.Use();
                    GUI.changed = true;
                    break;

                case EventType.ScrollWheel:
                    _distanceTarget = Mathf.Clamp(_distanceTarget * (1f + e.delta.y * 0.05f),
                                                  _frameDistance * MinZoom,
                                                  _frameDistance * MaxZoom);
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


            _utility?.Cleanup();
            _utility = null;
        }
    }
}
