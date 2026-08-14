using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Polyfork.Samples
{
    /// <summary>
    /// Plays one of a set of clips on a Polyfork character, chosen from a dropdown.
    ///
    /// Polyfork characters ship a rig and no animation. That is deliberate rather than
    /// missing: they carry a Mixamo skeleton with the prefix stripped - Hips, Spine, Spine1,
    /// Neck, Head, LeftArm - so any humanoid clip retargets onto them, and shipping one
    /// opinionated walk with every character would be the store deciding how your game moves.
    ///
    /// The clips come from a pack rather than the character. polyfork.dev publishes two:
    ///
    ///   https://polyfork.dev/anim/xbot.glb      idle, walk, run, agree, headShake, poses
    ///   https://polyfork.dev/anim/soldier.glb   Idle, Walk, Run, TPose
    ///
    /// Both use `mixamorig:` bone names while the characters do not, so the paths do not
    /// line up and a clip cannot bind directly. Set BOTH the character and the pack to
    /// Rig > Animation Type > Humanoid on import and Unity retargets through the avatar,
    /// which is what makes any clip work on any of these characters regardless of naming.
    ///
    /// Played through a PlayableGraph rather than an AnimatorController: a sample should not
    /// require you to author a controller asset, wire states and add parameters before it
    /// does anything, and a graph plays an arbitrary clip in three lines.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Polyfork/Samples/Polyfork Character Animation")]
    public sealed class PolyforkCharacterAnimation : MonoBehaviour
    {
        [Tooltip("Clips to choose from. Drag them out of an imported animation pack.")]
        public AnimationClip[] clips = Array.Empty<AnimationClip>();

        /// <summary>
        /// Which clip is playing, or -1 for "whatever the default is".
        ///
        /// -1 rather than 0 so an untouched component starts on idle wherever idle happens
        /// to sit in the list. Defaulting to index 0 would mean the starting animation
        /// depended on drag order, which is not a decision anyone made.
        /// </summary>
        [Tooltip("Which clip is playing. The Inspector shows this as a dropdown of names.")]
        public int current = -1;

        [Tooltip("Seconds to blend when the selection changes. Zero cuts.")]
        [Range(0f, 1f)] public float blend = 0.25f;

        public bool loop = true;

        Animator _animator;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        int _playing = -1;
        float _weight;

        /// <summary>
        /// The clip a character should be doing when nothing has asked otherwise.
        ///
        /// Matched by name rather than by index: the two packs disagree on capitalisation
        /// ("idle" in xbot, "Idle" in soldier) and on ordering, so anything positional picks
        /// a different clip depending on which pack was dragged in.
        /// </summary>
        public int DefaultIndex
        {
            get
            {
                for (var i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && clips[i].name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
                return 0;
            }
        }

        void Awake() => _animator = GetComponent<Animator>();

        /// <summary>The clip that will actually play: the chosen one, or the default.</summary>
        public int EffectiveIndex =>
            clips.Length == 0 ? -1
            : current >= 0 && current < clips.Length ? current
            : DefaultIndex;

        void OnEnable()
        {
            if (clips.Length == 0) return;

            Build();
            Play(EffectiveIndex, immediate: true);
        }

        void OnDisable() => Teardown();

        void Build()
        {
            Teardown();

            _graph = PlayableGraph.Create($"Polyfork {name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            // Two inputs, because a blend is only ever between the outgoing clip and the
            // incoming one. More would be a state machine, which is what this sample exists
            // to avoid needing.
            _mixer = AnimationMixerPlayable.Create(_graph, 2);

            var output = AnimationPlayableOutput.Create(_graph, "Polyfork", _animator);
            output.SetSourcePlayable(_mixer);

            _graph.Play();
        }

        void Teardown()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _playing = -1;
        }

        /// <summary>Switches to a clip, blending from whatever is playing.</summary>
        public void Play(int index, bool immediate = false)
        {
            if (clips.Length == 0) return;

            index = Mathf.Clamp(index, 0, clips.Length - 1);
            var clip = clips[index];
            if (clip == null || index == _playing) return;

            if (!_graph.IsValid()) Build();

            // Slot 0 keeps what is leaving, slot 1 takes what is arriving, and the weight
            // walks from one to the other.
            var outgoing = _mixer.GetInput(1);
            _mixer.DisconnectInput(0);
            if (outgoing.IsValid()) _mixer.ConnectInput(0, outgoing, 0);

            _mixer.DisconnectInput(1);

            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(true);
            if (!loop) playable.SetDuration(clip.length);

            _mixer.ConnectInput(1, playable, 0);

            _weight = immediate || blend <= 0f ? 1f : 0f;
            _mixer.SetInputWeight(0, 1f - _weight);
            _mixer.SetInputWeight(1, _weight);

            _playing = index;
            current = index;
        }

        /// <summary>Switches by name, for calling from your own code.</summary>
        public void Play(string clipName)
        {
            var i = Array.FindIndex(clips, c => c != null &&
                string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase));

            if (i >= 0) Play(i);
        }

        public string[] ClipNames =>
            clips.Select((c, i) => c == null ? $"{i}: (none)" : c.name).ToArray();

        void Update()
        {
            if (!_graph.IsValid() || _weight >= 1f || blend <= 0f) return;

            _weight = Mathf.MoveTowards(_weight, 1f, Time.deltaTime / blend);
            _mixer.SetInputWeight(0, 1f - _weight);
            _mixer.SetInputWeight(1, _weight);
        }
    }
}
