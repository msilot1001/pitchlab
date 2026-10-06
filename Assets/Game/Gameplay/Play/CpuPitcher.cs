using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>Where the pitcher wants the pitch relative to the zone.</summary>
    public enum LocationIntent
    {
        /// <summary>Well inside: a strike he expects to be hit at.</summary>
        Heart,
        /// <summary>On the edge: a strike that is hard to square up.</summary>
        Edge,
        /// <summary>Just off: a ball he hopes is chased.</summary>
        Chase,
        /// <summary>Well off: a deliberate ball (setting up, or not giving in).</summary>
        Waste,
    }

    /// <summary>The CPU pitcher's call for one pitch: the type and the target (intent), and why.</summary>
    public readonly struct PitchDecision
    {
        public PitchDecision(PitchType type, double targetX, double targetZ, LocationIntent intent, string location, string reason)
        {
            Type = type;
            TargetX = targetX;
            TargetZ = targetZ;
            Intent = intent;
            Location = location;
            Reason = reason;
        }

        public PitchType Type { get; }
        /// <summary>Target at the front plane of the plate (m; x catcher's view, + toward first base).</summary>
        public double TargetX { get; }
        public double TargetZ { get; }
        public LocationIntent Intent { get; }
        /// <summary>"low-away", "up-in", "middle" … relative to the batter.</summary>
        public string Location { get; }
        public string Reason { get; }

        public override string ToString() => $"{Type} {Location} ({Intent}) · {Reason}";
    }

    /// <summary>
    /// A first-pass CPU pitcher (TASK-020; Docs/PITCHER_AI.md): seeded, weighted, explainable choices of pitch type and
    /// location. He may know the count, the outs, the runners, the batter's side and broad ratings, his own repertoire and
    /// the plate appearance's previous pitches (his own calls). He never sees the batter's input or his own execution error
    /// before the pitch: those come after the call. Difficulty comes from his ratings and these choices — never from
    /// reading the PCI or changing a pitch after release.
    /// </summary>
    public static class CpuPitcher
    {
        /// <summary>Intent weights (heart, edge, chase, waste) by count situation (TUNED so the zone rate by count follows MLB:
        /// ≈ 64 % at 3-0, 54 % at 0-0, 32 % at 0-2; Docs/PITCHER_AI.md).</summary>
        private static readonly double[] ThreeOh = { 0.55, 0.38, 0.07, 0.00 };
        private static readonly double[] Behind = { 0.33, 0.50, 0.15, 0.02 };
        private static readonly double[] Even = { 0.25, 0.50, 0.21, 0.04 };
        private static readonly double[] Ahead = { 0.12, 0.45, 0.35, 0.08 };
        private static readonly double[] OhTwo = { 0.04, 0.30, 0.48, 0.18 };

        /// <summary>A pitch just off the zone is this far beyond the edge where the call changes (m, ball centre past the zone
        /// widened by the ball's radius); a waste pitch this far. An edge pitch is aimed this far inside the zone's edge (his
        /// miss spreads it both ways).</summary>
        public const double ChaseBeyond = 0.07, WasteBeyond = 0.28, EdgeInside = 0.05;
        /// <summary>Each time in a row he has just thrown a type, its weight is multiplied by this (TUNED: ≤ 4–5 in a row).</summary>
        public const double RepeatFactor = 0.6;
        /// <summary>Heart pitches sit at this fraction of the zone's half-extents from its centre.</summary>
        public const double HeartFraction = 0.35;

        /// <summary>The call for the next pitch of <paramref name="game"/>'s plate appearance.</summary>
        public static PitchDecision Choose(GameState game) =>
            Choose(game.Pitcher, game.Batter, game.Count, game.Outs, game.Bases, game.Current.Pitches, game.Seed, game.Current.Number);

        public static PitchDecision Choose(PlayerProfile pitcher, PlayerProfile batter, Count count, int outs, BaseOccupancy bases,
            IReadOnlyList<PitchEvent> previous, int seed, int plateAppearance)
        {
            if (pitcher?.Repertoire == null) throw new ArgumentException("A pitcher with a repertoire.", nameof(pitcher));
            var stream = new SeedStream(seed, SeedStream.Key(pitcher.Id), plateAppearance, previous.Count + 1, 0xC9A7);
            bool sameSide = (pitcher.Throws == Hand.Right) == (batter.Bats == BatterSide.Right);
            PitchType? last = previous.Count > 0 ? TypeOf(previous[previous.Count - 1].Info) : null;
            int run = 0;   // how many times in a row he has just thrown the last type
            for (int i = previous.Count - 1; i >= 0 && TypeOf(previous[i].Info) == last; i--) run++;

            // The pitch type: his usage, the count, the matchup and his last pitches.
            Repertoire rep = pitcher.Repertoire;
            Span<double> typeWeights = stackalloc double[rep.Count];
            double total = 0.0;
            for (int i = 0; i < rep.Count; i++)
            {
                PitchType t = rep.Pitches[i].Type;
                double w = rep.Pitches[i].Usage * CountFactor(t, count) * MatchupFactor(t, sameSide);
                if (last == t) w *= Math.Pow(RepeatFactor, run);   // not the same pitch over and over
                typeWeights[i] = w;
                total += w;
            }

            double u = stream.Unit() * total;
            PitchType type = rep.Pitches[rep.Count - 1].Type;
            for (int i = 0; i < rep.Count; i++)
            {
                u -= typeWeights[i];
                if (u < 0.0)
                {
                    type = rep.Pitches[i].Type;
                    break;
                }
            }

            // The intent: the count, the batter (expand against a free swinger, stay out of the heart against power), the runners.
            PlayerRatings b = batter.Ratings;
            double[] baseWeights = count.Balls == 3 && count.Strikes == 0 ? ThreeOh
                : count.Balls == 0 && count.Strikes == 2 ? OhTwo
                : count.Balls > count.Strikes ? Behind : count.Strikes > count.Balls ? Ahead : Even;
            Span<double> intent = stackalloc double[4];
            intent[0] = baseWeights[0] * (1.0 - 0.3 * RatingScale.Unit(b.Power));
            intent[1] = baseWeights[1];
            intent[2] = baseWeights[2] * (1.0 - 0.4 * RatingScale.Unit(b.Discipline));
            intent[3] = baseWeights[3] * (bases.Third && outs < 2 ? 0.3 : 1.0);   // no wild pitch with a runner on third
            LocationIntent where = (LocationIntent)Draw(intent, stream.Unit());

            // The location: the pitch type's natural spots, relative to the batter (in / away) and to his own arm side.
            double inSign = batter.Bats == BatterSide.Right ? -1.0 : 1.0;             // a right-handed hitter stands on the −X side
            double gloveSign = pitcher.Throws == Hand.Right ? 1.0 : -1.0;             // a right-hander's glove side is +X
            (int v, double h) = Spot(type, where, inSign, gloveSign, ref stream);
            if (previous.Count > 0 && last == type)
            {
                // The same pitch twice: move it (a modest sequencing rule).
                PitchInfo p = previous[previous.Count - 1].Info;
                (double px, double pz) = Target(where, v, h, batter);
                if (p.HasTarget && Math.Abs(px - p.TargetX) < 0.1 && Math.Abs(pz - p.TargetZ) < 0.1) (v, h) = Spot(type, where, inSign, gloveSign, ref stream);
            }

            (double x, double z) = Target(where, v, h, batter);
            string location = Describe(v, h, inSign);
            string situation = count.Strikes == 2 ? "two strikes" : count.Balls > count.Strikes ? "behind" : count.Strikes > count.Balls ? "ahead" : "even";
            string reason = $"{count.Balls}-{count.Strikes} · {situation} · {where.ToString().ToLowerInvariant()}" + (sameSide ? "" : " · opposite side");
            return new PitchDecision(type, x, z, where, location, reason);
        }

        /// <summary>Fastballs when behind (×1.5; ×3 at 3-0), off-speed when ahead (×1.3 with two strikes).</summary>
        private static double CountFactor(PitchType t, Count c)
        {
            bool fastball = t == PitchType.FourSeam || t == PitchType.Sinker;
            if (c.Balls == 3 && c.Strikes == 0) return fastball ? 3.0 : 0.5;
            if (c.Balls > c.Strikes) return fastball ? 1.5 : 0.8;
            if (c.Strikes == 2) return fastball ? 0.75 : 1.3;
            return 1.0;
        }

        /// <summary>Sliders to same-side hitters, changeups to opposite-side hitters (the usual platoon usage: a slider breaks
        /// away from a same-side hitter, a changeup fades away from an opposite-side one).</summary>
        private static double MatchupFactor(PitchType t, bool sameSide) => t switch
        {
            PitchType.Slider => sameSide ? 1.25 : 0.8,
            PitchType.Changeup => sameSide ? 0.6 : 1.4,
            _ => 1.0,
        };

        /// <summary>A vertical row (−1 down, 0 middle, +1 up) and horizontal side (−1…+1 in catcher-view X sign) for the type.</summary>
        private static (int V, double H) Spot(PitchType type, LocationIntent where, double inSign, double gloveSign, ref SeedStream stream)
        {
            // Vertical: four-seamers up, sinkers / off-speed down.
            (double up, double down) = type switch
            {
                PitchType.FourSeam => (0.5, 0.2),
                PitchType.Sinker => (0.1, 0.55),
                PitchType.Slider => (0.1, 0.6),
                PitchType.Curveball => (0.05, 0.75),
                _ => (0.05, 0.7),
            };
            double r = stream.Unit();
            int v = r < up ? 1 : r < up + down ? -1 : 0;
            // Horizontal: sliders to the glove side, changeups and sinkers to the arm side, four-seamers in or away.
            double side = stream.Unit();
            double h = type switch
            {
                PitchType.Slider => side < 0.65 ? gloveSign : side < 0.85 ? 0.0 : -gloveSign,
                PitchType.Changeup or PitchType.Sinker => side < 0.55 ? -gloveSign : side < 0.8 ? 0.0 : gloveSign,
                PitchType.Curveball => side < 0.4 ? 0.0 : side < 0.75 ? gloveSign : -gloveSign,
                _ => side < 0.4 ? inSign : side < 0.6 ? 0.0 : -inSign,
            };
            // Off the zone needs a direction off it: the type's natural one.
            if (where != LocationIntent.Heart && v == 0 && h == 0.0) v = type == PitchType.FourSeam ? 1 : -1;
            return (v, h);
        }

        /// <summary>The target point (m) for the intent and spot in the batter's zone.</summary>
        private static (double X, double Z) Target(LocationIntent where, int v, double h, PlayerProfile batter)
        {
            double w = StrikeZone.HalfWidth, bottom = batter.ZoneBottom, top = batter.ZoneTop, cz = 0.5 * (bottom + top), hh = 0.5 * (top - bottom);
            if (where == LocationIntent.Heart) return (h * HeartFraction * w, cz + v * HeartFraction * hh);
            // Measured from the zone edge widened by the ball's radius (where the call changes) for chase and waste pitches.
            double r = BallProperties.Baseball.Radius;
            double beyond = where == LocationIntent.Edge ? -EdgeInside : where == LocationIntent.Chase ? ChaseBeyond + r : WasteBeyond + r;
            // Edge / off: at (or beyond) the edge in the chosen direction; on a corner the edge of both (an edge pitch) or
            // beyond both (a chase or waste pitch).
            double x = h == 0.0 ? 0.0 : h * (w + beyond);
            double z = v == 0 ? cz : v > 0 ? top + beyond : bottom - beyond;
            return (x, z);
        }

        private static string Describe(int v, double h, double inSign)
        {
            string vertical = v > 0 ? "up" : v < 0 ? "low" : "";
            string horizontal = h == 0.0 ? "" : Math.Sign(h) == Math.Sign(inSign) ? "in" : "away";
            return vertical.Length == 0 && horizontal.Length == 0 ? "middle" : vertical.Length == 0 ? horizontal : horizontal.Length == 0 ? vertical : $"{vertical}-{horizontal}";
        }

        private static int Draw(Span<double> weights, double u)
        {
            double total = 0.0;
            foreach (double w in weights) total += w;
            double x = u * total;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x < 0.0) return i;
            }

            return weights.Length - 1;
        }

        /// <summary>The pitch type of a recorded pitch (from its label; null if not one of the presets).</summary>
        public static PitchType? TypeOf(PitchInfo info)
        {
            PitchInput[] all = PitchPresets.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Label == info.Label) return (PitchType)i;
            return null;
        }
    }
}
