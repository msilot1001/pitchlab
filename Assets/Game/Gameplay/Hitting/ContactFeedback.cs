using System;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>Where on the barrel the ball was met, from the authoritative contact offsets.</summary>
    public enum ContactQuality
    {
        /// <summary>Within 1.5 in of the sweet spot along the barrel.</summary>
        Barrel,
        /// <summary>Toward the end of the bat.</summary>
        OffTheEnd,
        /// <summary>Toward the hands.</summary>
        Jammed,
    }

    /// <summary>Compact player feedback (TASK-004.6B-2) derived only from gameplay results: timing words and contact quality.</summary>
    public static class ContactFeedback
    {
        public const double BarrelWindow = 1.5 * 0.0254;

        /// <summary>"on time" (the resolver's Good window), else "early 12 ms" / "late 8 ms"; empty without a timing.</summary>
        public static string Timing(ContactResult r)
        {
            if (double.IsNaN(r.TimingError)) return string.Empty;
            double ms = Math.Round(Math.Abs(r.TimingError) * 1000.0);
            if (r.Timing == TimingLabel.Good) return "on time";   // the resolver's own "Good" window
            return $"{(r.TimingError < 0.0 ? "early" : "late")} {ms:0} ms";
        }

        /// <summary>Contact quality along the barrel; null for a miss.</summary>
        public static ContactQuality? Quality(ContactResult r, SwingParameters p)
        {
            if (!r.IsContact) return null;
            if (Math.Abs(r.OffsetAlongBarrel) <= BarrelWindow) return ContactQuality.Barrel;
            // The barrel's tip points toward first base for a right-handed hitter (ContactResolver), third for a lefty.
            double tipSign = p.Side == BatterSide.Right ? 1.0 : -1.0;
            return r.OffsetAlongBarrel * tipSign > 0.0 ? ContactQuality.OffTheEnd : ContactQuality.Jammed;
        }

        public static string Describe(ContactQuality q) =>
            q == ContactQuality.Barrel ? "sweet spot" : q == ContactQuality.OffTheEnd ? "off the end" : "jammed";
    }
}
