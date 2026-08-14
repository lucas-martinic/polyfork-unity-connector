using UnityEditor;
using UnityEngine;

namespace Polyfork.Samples.EditorTools
{
    /// <summary>
    /// Draws the clip list as a dropdown of names, and switches clip as you pick.
    ///
    /// An int field indexing into an array is the same data and a worse question: it asks
    /// which slot rather than which animation, and gets it wrong silently when the array is
    /// reordered. Naming the options also makes the default legible - "idle" rather than 2.
    /// </summary>
    [CustomEditor(typeof(PolyforkCharacterAnimation))]
    public sealed class PolyforkCharacterAnimationEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var anim = (PolyforkCharacterAnimation)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clips"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("blend"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loop"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);

            if (anim.clips == null || anim.clips.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No clips yet.\n\n" +
                    "Polyfork characters ship a rig and no animation, so the clips come from a " +
                    "pack: download polyfork.dev/anim/xbot.glb, drop it in Assets, and set its " +
                    "Rig to Humanoid — and the character's Rig to Humanoid too, or the clips " +
                    "cannot retarget onto it. Then drag the clips into the list above.",
                    MessageType.Info);

                if (GUILayout.Button("Download animation pack (xbot.glb)"))
                    Application.OpenURL("https://polyfork.dev/anim/xbot.glb");

                return;
            }

            var names = anim.ClipNames;
            var shown = Mathf.Clamp(anim.EffectiveIndex, 0, names.Length - 1);
            var next = EditorGUILayout.Popup("Animation", shown, names);

            if (next != shown)
            {
                Undo.RecordObject(anim, "Change animation");
                anim.current = next;
                EditorUtility.SetDirty(anim);

                // Only meaningful in play mode: outside it there is no graph running to switch.
                if (Application.isPlaying) anim.Play(next);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField(
                    anim.current < 0
                        ? $"Starts on \"{names[Mathf.Clamp(anim.DefaultIndex, 0, names.Length - 1)]}\", picked by name."
                        : $"Starts on \"{names[shown]}\".",
                    EditorStyles.miniLabel);
            }
        }
    }
}
