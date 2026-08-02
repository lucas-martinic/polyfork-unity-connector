using System;
using System.Collections.Generic;

namespace Polyfork
{
    /// <summary>
    /// A sliding-window cap on remix rebuilds.
    ///
    /// Polyfork may rate-limit unauthenticated remix requests (reported as ~40/hour).
    /// Tripping that mid-session would leave sliders frozen, so the client keeps its own
    /// budget slightly under the server's and degrades predictably instead: a refused
    /// rebuild falls back to the nearest value already on disk.
    ///
    /// Cache hits never consume budget - only requests that actually reach the network do.
    /// </summary>
    public sealed class PolyforkRemixBudget
    {
        readonly Queue<DateTime> _spent = new();
        readonly object _gate = new();

        public PolyforkRemixBudget(int maxRequests = 32, TimeSpan? window = null)
        {
            MaxRequests = maxRequests;
            Window = window ?? TimeSpan.FromHours(1);
        }

        /// <summary>0 or less means unlimited (use when an API key is attached).</summary>
        public int MaxRequests { get; set; }

        public TimeSpan Window { get; set; }

        public bool Unlimited => MaxRequests <= 0;

        public int Remaining
        {
            get
            {
                if (Unlimited) return int.MaxValue;
                lock (_gate)
                {
                    Trim();
                    return Math.Max(0, MaxRequests - _spent.Count);
                }
            }
        }

        /// <summary>When the oldest slot frees up, or null if nothing is queued.</summary>
        public DateTime? NextFreeAt
        {
            get
            {
                lock (_gate)
                {
                    Trim();
                    return _spent.Count == 0 ? null : _spent.Peek() + Window;
                }
            }
        }

        /// <summary>Takes a slot if one is available.</summary>
        public bool TryConsume()
        {
            if (Unlimited) return true;

            lock (_gate)
            {
                Trim();
                if (_spent.Count >= MaxRequests) return false;
                _spent.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        /// <summary>
        /// Marks the budget as exhausted until <paramref name="retryAfter"/>, used when the
        /// server answers 429 and is therefore the authority rather than our estimate.
        /// </summary>
        public void MarkExhausted(TimeSpan retryAfter)
        {
            lock (_gate)
            {
                _spent.Clear();
                var stamp = DateTime.UtcNow - Window + retryAfter;
                var count = Math.Max(MaxRequests, 1);
                for (var i = 0; i < count; i++) _spent.Enqueue(stamp);
            }
        }

        public void Reset()
        {
            lock (_gate) _spent.Clear();
        }

        void Trim()
        {
            var cutoff = DateTime.UtcNow - Window;
            while (_spent.Count > 0 && _spent.Peek() < cutoff) _spent.Dequeue();
        }
    }
}
