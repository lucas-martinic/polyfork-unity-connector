using System;

namespace Polyfork
{
    /// <summary>
    /// Tracks how many remix bakes are left, so the package can be polite about a shared
    /// allowance instead of discovering it as a 429 mid-drag.
    ///
    /// The server is authoritative: values come from GET /api/me and are mirrored locally
    /// between refreshes so a burst of edits cannot outrun the last sync. Only *new*
    /// geometry is metered, so anything already in the cache is spent-free.
    ///
    /// When the allowance is unknown - no sync yet, or /api/me unreachable - the floor is
    /// assumed rather than plenty. Guessing high spends someone else's quota; guessing low
    /// only costs a little latency.
    /// </summary>
    public sealed class PolyforkRemixBudget
    {
        /// <summary>Assumed remaining when the server has not told us. Deliberately small.</summary>
        public const int UnknownFloor = 5;

        /// <summary>Kept back for interactive edits, so speculative prewarm never starves a drag.</summary>
        public const int InteractiveReserve = 8;

        /// <summary>
        /// Warn at or below this many bakes. Deliberately above the reserve: once the
        /// remainder is into reserve territory prewarming has already stopped, so warning
        /// there would be telling the user after the tool had quietly degraded.
        /// </summary>
        public const int LowThreshold = InteractiveReserve * 2;

        int? _remaining;
        DateTime _exhaustedUntilUtc = DateTime.MinValue;

        public PolyforkAccess Access { get; private set; }

        public bool Synced { get; private set; }

        /// <summary>True when the tier publishes no limit at all.</summary>
        public bool Unlimited => Synced && _remaining == null;

        public bool IsExhausted => DateTime.UtcNow < _exhaustedUntilUtc || Effective <= 0;

        public bool IsLow => !Unlimited && Effective <= LowThreshold;

        /// <summary>Best current estimate of bakes available.</summary>
        public int Effective
        {
            get
            {
                if (DateTime.UtcNow < _exhaustedUntilUtc) return 0;
                if (!Synced) return UnknownFloor;
                return _remaining ?? int.MaxValue;
            }
        }

        /// <summary>How many a speculative prewarm may use right now.</summary>
        public int PrewarmAllowance => Unlimited
            ? int.MaxValue
            : Math.Max(0, Effective - InteractiveReserve);

        public void SyncFrom(PolyforkAccess access)
        {
            Access = access;
            _remaining = access?.Remaining;
            Synced = access != null;

            if (Effective > 0) _exhaustedUntilUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Claims one bake. Callers must only call this for a request that will actually
        /// reach the network - a cache hit is free and must not be counted.
        /// </summary>
        public bool TryConsume()
        {
            if (DateTime.UtcNow < _exhaustedUntilUtc) return false;
            if (_remaining == null) return true;            // uncapped, or not yet known to be capped
            if (!Synced) return _remaining > 0;

            if (_remaining <= 0) return false;
            _remaining--;
            return true;
        }

        /// <summary>Applies a server 429; the server outranks our mirror.</summary>
        public void MarkExhausted(TimeSpan retryAfter)
        {
            _exhaustedUntilUtc = DateTime.UtcNow + retryAfter;
            _remaining = 0;
        }

        /// <summary>Human-readable state for a status bar.</summary>
        public string Describe()
        {
            if (!Synced) return "allowance unknown";
            if (Unlimited) return "unlimited bakes";

            var text = $"{Effective} bake{(Effective == 1 ? "" : "s")} left";
            if (Access?.PeriodName != null && Access.BakesLeftThisPeriod != null)
                text += $" ({Access.BakesLeftThisPeriod} this {Access.PeriodName})";
            return text;
        }

        public void Reset()
        {
            _remaining = null;
            Synced = false;
            _exhaustedUntilUtc = DateTime.MinValue;
        }
    }
}
