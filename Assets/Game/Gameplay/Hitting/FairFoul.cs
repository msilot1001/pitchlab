using System;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Hitting
{
    public enum BallInPlayCall
    {
        Fair,
        Foul,
        /// <summary>Fair, out of the park over the fence.</summary>
        HomeRun,
    }

    /// <summary>Why the call was made, and where (the decisive moment of the play).</summary>
    public enum CallBasis
    {
        /// <summary>First landing on or beyond first/third base (Official Baseball Rules 2.00 "Fair/Foul Ball").</summary>
        FirstLandingBeyondBase,
        /// <summary>Passing first or third base (bounding past it over fair or foul territory).</summary>
        PassingBase,
        /// <summary>Settling (at rest) before reaching first/third base.</summary>
        Settled,
        /// <summary>Leaving the park over the fence on the fly (fair: home run).</summary>
        OverTheFence,
        /// <summary>Hitting the outfield wall on the fly (the wall stands over fair territory: fair).</summary>
        OffTheWall,
    }

    public readonly struct FairFoulResult
    {
        public readonly BallInPlayCall Call;
        public readonly CallBasis Basis;
        /// <summary>Ball state at the decisive moment.</summary>
        public readonly BallState At;

        public FairFoulResult(BallInPlayCall call, CallBasis basis, BallState at)
        {
            Call = call;
            Basis = basis;
            At = at;
        }

        public bool IsFair => Call != BallInPlayCall.Foul;
    }

    /// <summary>
    /// Fair/foul call for a batted ball without fielders (Official Baseball Rules, definitions of fair and foul ball):
    /// before the bases, the ball is judged where it settles or where it passes first or third base; beyond them, where
    /// it first lands; over the fence on the fly, where it leaves the field (a bounce over it is a fair ground-rule
    /// double); off the wall on the fly, fair. The foul lines and poles are fair territory and the ball
    /// is judged by its own position: fair if any part of it is over the line (centre within one radius outside it).
    /// Touching a base (in fair territory) is covered by passing it. Gameplay rules on top of the authoritative play.
    /// </summary>
    public static class FairFoul
    {
        /// <summary>Distance (m) of a ground point outside the nearer foul line; ≤ 0 inside fair territory.</summary>
        public static double OutsideFoulLine(double x, double y) => FieldLayout.OutsideFoulLine(x, y);

        /// <summary>Ball centre over fair territory or over the line (any part of the ball above it).</summary>
        public static bool OverFairTerritory(Vector3d p) => OutsideFoulLine(p.X, p.Y) <= BallProperties.Baseball.Radius;

        /// <summary>
        /// Depth toward centre field. "Past first or third base" is past the line through the two bases (y ≥ 90 ft·√½):
        /// that line meets each foul line exactly at its bag, and it also covers balls up the middle (a ball that first
        /// lands 100 ft toward centre is past the bases, Codex review).
        /// </summary>
        private static double Depth(Vector3d p) => p.Y;

        public static FairFoulResult Call(BallInPlay play)
        {
            double baseLine = FieldLayout.BaseDistance * Math.Sqrt(0.5);
            // The first decisive event in the air: clearing the fence (home run if over fair territory or the pole), or
            // touching the wall (which stands only over fair territory and the line: fair, whatever happens after). A
            // ball that bounces first is judged by its landing — a fair bounce over the fence is a ground-rule double.
            foreach (BallEvent e in play.Events)
            {
                if (e.Kind == BallEventKind.GroundImpact || e.Kind == BallEventKind.LeftPlay) break;
                if (e.Kind == BallEventKind.ClearedFence)
                    return new FairFoulResult(OverFairTerritory(e.Before.Position) ? BallInPlayCall.HomeRun : BallInPlayCall.Foul, CallBasis.OverTheFence, e.Before);
                if (e.Kind == BallEventKind.WallImpact)
                    return new FairFoulResult(BallInPlayCall.Fair, CallBasis.OffTheWall, e.Before);
            }

            BallEvent? landing = play.FirstGroundContact;
            if (landing is BallEvent l && Depth(l.Before.Position) >= baseLine)
                return new FairFoulResult(Judge(l.Before.Position), CallBasis.FirstLandingBeyondBase, l.Before);

            // Landed (or still in the air) before the bases: the first moment it passes first or third base decides,
            // else where it settles. Scanned on the recorded samples (≤ 5 ms apart in the air, 4 ms on the ground).
            double start = landing?.Time ?? play.First.Time;
            BallState previous = play.StateAt(start);
            foreach (BallSegment segment in play.Segments)
                for (int i = 0; i < segment.Path.Samples.Count; i++)
                {
                    BallState s = segment.Path.Samples[i];
                    if (s.Time <= start) continue;
                    if (Depth(previous.Position) < baseLine && Depth(s.Position) >= baseLine)
                    {
                        BallState at = Crossing(previous, s, baseLine);
                        return new FairFoulResult(Judge(at.Position), CallBasis.PassingBase, at);
                    }

                    previous = s;
                }

            return new FairFoulResult(Judge(play.Final.Position), CallBasis.Settled, play.Final);
        }

        private static BallInPlayCall Judge(Vector3d p) => OverFairTerritory(p) ? BallInPlayCall.Fair : BallInPlayCall.Foul;

        /// <summary>Linear interpolation to where the ball passes the base line (sub-centimetre between samples).</summary>
        private static BallState Crossing(BallState a, BallState b, double line)
        {
            double fa = Depth(a.Position) - line, fb = Depth(b.Position) - line;
            double u = fa / (fa - fb);
            return new BallState(a.Time + u * (b.Time - a.Time), a.Position + u * (b.Position - a.Position), a.Velocity + u * (b.Velocity - a.Velocity), a.Spin);
        }
    }
}
