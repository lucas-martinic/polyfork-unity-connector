using UnityEditor;
using UnityEngine;

namespace Polyfork.Demo.EditorTools
{
    /// <summary>
    /// Draws the demo object's steps as instructions rather than as four text fields.
    ///
    /// The fields are there so the scene file carries the words - a reviewer opening the
    /// scene in a text editor can read them - but nobody wants to edit them, and a stack of
    /// editable TextAreas reads like a form to fill in rather than something to follow.
    /// </summary>
    [CustomEditor(typeof(PolyforkDemo))]
    public sealed class PolyforkDemoEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var demo = (PolyforkDemo)target;

            EditorGUILayout.LabelField("Polyfork — getting started", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            var i = 1;
            foreach (var step in new[] { demo.step1, demo.step2, demo.step3, demo.step4 })
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{i++}.", GUILayout.Width(18f));
                    EditorGUILayout.LabelField(step, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.Space(8f);

            if (GUILayout.Button("Open the Polyfork gallery", GUILayout.Height(30f)))
                EditorApplication.ExecuteMenuItem("Tools/Polyfork/Browse Assets");

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(demo.note, MessageType.None);
        }
    }
}
