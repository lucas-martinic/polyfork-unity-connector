using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Polyfork.EditorTools
{
    /// <summary>
    /// One complete set of knob values — everything a remix edit can change.
    /// </summary>
    public sealed class PolyforkRemixSnapshot
    {
        public Dictionary<string, float> Ranges;

        /// <summary>Structural choice and toggle knobs; geometry, like Ranges.</summary>
        public Dictionary<string, string> Choices;
        public Dictionary<string, bool> Toggles;

        public Dictionary<string, Color> SlotColors;
        public string Colorway;

        public PolyforkRemixSnapshot Clone() => new()
        {
            Ranges = new Dictionary<string, float>(Ranges),
            Choices = new Dictionary<string, string>(Choices),
            Toggles = new Dictionary<string, bool>(Toggles),
            SlotColors = new Dictionary<string, Color>(SlotColors),
            Colorway = Colorway
        };

        /// <summary>True when the geometry differs, i.e. restoring needs a server rebuild.</summary>
        public bool GeometryDiffers(PolyforkRemixSnapshot other)
        {
            if (other == null || Ranges.Count != other.Ranges.Count) return true;
            if (Choices.Count != other.Choices.Count || Toggles.Count != other.Toggles.Count) return true;

            foreach (var kv in Ranges)
            {
                if (!other.Ranges.TryGetValue(kv.Key, out var v)) return true;
                if (!Mathf.Approximately(kv.Value, v)) return true;
            }

            foreach (var kv in Choices)
            {
                if (!other.Choices.TryGetValue(kv.Key, out var v) || v != kv.Value) return true;
            }

            foreach (var kv in Toggles)
            {
                if (!other.Toggles.TryGetValue(kv.Key, out var v) || v != kv.Value) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Undo history scoped to the remix panel.
    ///
    /// Deliberately separate from Unity's global undo: undoing a slider drag should not
    /// reach into the user's scene edits, and vice versa. The window intercepts the editor's
    /// Undo/Redo commands while it has focus so the two stacks never interleave.
    /// </summary>
    public sealed class PolyforkRemixHistory
    {
        const int Capacity = 64;

        /// <summary>Consecutive edits to the same control within this window collapse into one step.</summary>
        const double CoalesceSeconds = 0.9d;

        readonly List<PolyforkRemixSnapshot> _undo = new();
        readonly List<PolyforkRemixSnapshot> _redo = new();

        string _lastOpKey;
        double _lastOpTime;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _lastOpKey = null;
        }

        /// <summary>
        /// Records the state as it was *before* an edit.
        ///
        /// <paramref name="opKey"/> identifies the control being manipulated (e.g. the knob
        /// name). Dragging a slider fires a change per frame, so repeats of the same key in
        /// quick succession are folded into the first snapshot, making one drag one step.
        /// </summary>
        public void Record(PolyforkRemixSnapshot before, string opKey)
        {
            var now = EditorApplication.timeSinceStartup;

            if (_lastOpKey == opKey && now - _lastOpTime < CoalesceSeconds)
            {
                _lastOpTime = now;      // same gesture; the earlier snapshot already covers it
                return;
            }

            _lastOpKey = opKey;
            _lastOpTime = now;

            _undo.Add(before.Clone());
            if (_undo.Count > Capacity) _undo.RemoveAt(0);

            _redo.Clear();              // a fresh edit invalidates the redo branch
        }

        /// <summary>Steps back, handing <paramref name="current"/> to the redo stack.</summary>
        public PolyforkRemixSnapshot Undo(PolyforkRemixSnapshot current)
        {
            if (_undo.Count == 0) return null;

            var index = _undo.Count - 1;
            var restored = _undo[index];
            _undo.RemoveAt(index);

            _redo.Add(current.Clone());
            _lastOpKey = null;          // don't coalesce across an undo
            return restored;
        }

        public PolyforkRemixSnapshot Redo(PolyforkRemixSnapshot current)
        {
            if (_redo.Count == 0) return null;

            var index = _redo.Count - 1;
            var restored = _redo[index];
            _redo.RemoveAt(index);

            _undo.Add(current.Clone());
            _lastOpKey = null;
            return restored;
        }
    }
}
