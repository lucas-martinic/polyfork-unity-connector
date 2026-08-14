using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
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
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);

            if (anim.clips == null || anim.clips.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No clips on this component.\n\n" +
                    "Characters imported through Polyfork get them automatically, bound to " +
                    "their own skeleton. On anything else, drag clips into the list above.",
                    MessageType.Info);
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
