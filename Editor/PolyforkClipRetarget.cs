using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// Rebinds an animation clip onto a Polyfork character's own skeleton.
    ///
    /// The usual answer to "play a Mixamo clip on this rig" is a Humanoid avatar, and it is
    /// not available here: glTFast imports .glb through a ScriptedImporter, and its
    /// maintainers are explicit that the rig and avatar settings of Unity's model importer
    /// "would basically have to be rewritten" to exist there. No Humanoid import means no
    /// avatar, and no avatar means no retargeting.
    ///
    /// What makes this tractable anyway is that the two skeletons are the same skeleton. The
    /// clip packs use Mixamo's names with the `mixamorig:` prefix; a Polyfork character uses
    /// the same names with the prefix stripped. Measured against the live catalogue: every
    /// one of naval-officer's 22 bones is driven by xbot's idle clip, and none of its bones
    /// is missing from it. The 45 curves left over are fingers, eyes and toes that a reduced
    /// rig does not have, and dropping them is exactly right.
    ///
    /// So instead of retargeting through an avatar, the curves are re-pointed at the paths
    /// this particular character actually has. Deterministic, and it needs nothing of the
    /// importer.
    /// </summary>
    static class PolyforkClipRetarget
    {
        /// <summary>
        /// A copy of <paramref name="source"/> whose curves address <paramref name="root"/>'s
        /// hierarchy, or null when nothing matched.
        /// </summary>
        public static AnimationClip Rebind(AnimationClip source, Transform root)
        {
            if (source == null || root == null) return null;

            var paths = BonePaths(root);
            var clip = new AnimationClip { name = source.name, frameRate = source.frameRate };

            var bound = 0;
            var dropped = 0;

            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var bone = LeafName(binding.path);
                if (bone == null || !paths.TryGetValue(bone, out var path))
                {
                    dropped++;
                    continue;
                }

                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) continue;

                AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                {
                    path = path,
                    type = binding.type,
                    propertyName = binding.propertyName
                }, curve);

                bound++;
            }

            if (bound == 0)
            {
                Object.DestroyImmediate(clip);
                return null;
            }

            /* Loop it. These are locomotion and idle cycles, and a clip that plays once and
             * freezes reads as a broken character rather than a clip that ended. */
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (dropped > 0 && bound < 8)
            {
                Debug.LogWarning(
                    $"[Polyfork] '{source.name}' bound only {bound} curve(s) to this rig " +
                    $"({dropped} dropped). The skeleton may not be the Mixamo one these clips expect.");
            }

            return clip;
        }

        /// <summary>Every transform under the root, by name, as a path relative to it.</summary>
        static Dictionary<string, string> BonePaths(Transform root)
        {
            var map = new Dictionary<string, string>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;

                // First wins. A duplicate name in a skeleton is ambiguous whatever we do, and
                // taking the shallower one is the better guess.
                var path = AnimationUtility.CalculateTransformPath(t, root);
                if (!map.ContainsKey(t.name)) map[t.name] = path;
            }

            return map;
        }

        /// <summary>
        /// The bone a binding targets, with any namespace prefix removed.
        ///
        /// Only the last segment matters: the clip's full path describes the pack's hierarchy,
        /// which has bones this character does not, so matching the whole chain would fail on
        /// every curve. The leaf is the bone; where it sits is the character's business.
        /// </summary>
        static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var slash = path.LastIndexOf('/');
            var leaf = slash >= 0 ? path.Substring(slash + 1) : path;

            var colon = leaf.IndexOf(':');
            return colon >= 0 ? leaf.Substring(colon + 1) : leaf;
        }
    }
}
