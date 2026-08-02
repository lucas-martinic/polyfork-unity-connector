using System;
using Newtonsoft.Json.Linq;

namespace Polyfork
{
    /// <summary>
    /// What this connection is allowed to do, from GET /api/me.
    ///
    /// The endpoint answers without a key, so the package can be honest about the remaining
    /// allowance from the first frame instead of discovering it as a 429.
    ///
    /// The allowance meters *new* geometry only: a variant anyone has already baked is
    /// served from cache and never counted. Converging on the same knob values therefore
    /// costs nothing, which is why the remix sliders snap to a fixed set of stops.
    /// </summary>
    public sealed class PolyforkAccess
    {
        public bool Authenticated;

        /// <summary>"anonymous", "free", "pro", …</summary>
        public string Plan = "anonymous";

        public int? BakesPerHour;
        public int? BakesLeftThisHour;

        /// <summary>The longer allowance window: a month for anonymous, a week for a free key.</summary>
        public int? BakesPerPeriod;
        public int? BakesLeftThisPeriod;

        /// <summary>"month", "week", or null when the tier has no longer window.</summary>
        public string PeriodName;

        public string ResetsOn;
        public string UpgradeNote;

        /// <summary>True when the tier has no longer-window cap (Pro).</summary>
        public bool PeriodUncapped => BakesPerPeriod == null;

        /// <summary>
        /// Bakes actually available right now: the tighter of the two windows.
        /// Null means the server published no limit.
        /// </summary>
        public int? Remaining
        {
            get
            {
                if (BakesLeftThisHour == null) return BakesLeftThisPeriod;
                if (BakesLeftThisPeriod == null) return BakesLeftThisHour;
                return Math.Min(BakesLeftThisHour.Value, BakesLeftThisPeriod.Value);
            }
        }

        public string Describe()
        {
            if (Remaining == null) return $"{Plan}: unlimited bakes";

            var period = PeriodName != null && BakesLeftThisPeriod != null
                ? $", {BakesLeftThisPeriod} this {PeriodName}"
                : "";
            return $"{Plan}: {BakesLeftThisHour ?? 0} bakes left this hour{period}";
        }

        public static PolyforkAccess Parse(string json)
        {
            var root = JObject.Parse(json);
            var a = root["access"] as JObject ?? root;

            var access = new PolyforkAccess
            {
                Authenticated = root["authenticated"]?.Type == JTokenType.Boolean &&
                                root["authenticated"].Value<bool>(),
                Plan = (string)root["plan"] ?? (string)a["as"] ?? "anonymous",
                BakesPerHour = Int(a["remix_bakes_per_hour"]),
                BakesLeftThisHour = Int(a["remix_bakes_left_this_hour"]),
                ResetsOn = (string)a["remix_allowance_resets"],
                UpgradeNote = (string)a["remix_allowance_note"]
            };

            // The longer window is named differently per tier, so accept either.
            foreach (var name in new[] { "week", "month" })
            {
                var per = Int(a[$"remix_bakes_per_{name}"]);
                var left = Int(a[$"remix_bakes_left_this_{name}"]);
                if (per == null && left == null) continue;

                access.PeriodName = name;
                access.BakesPerPeriod = per;
                access.BakesLeftThisPeriod = left;
                break;
            }

            return access;
        }

        /// <summary>Reads an int, tolerating the string values used for "uncapped".</summary>
        static int? Int(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type is JTokenType.Integer or JTokenType.Float) return t.Value<int>();
            return null;
        }
    }
}
