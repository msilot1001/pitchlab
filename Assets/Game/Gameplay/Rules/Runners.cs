using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Rules
{
    /// <summary>
    /// A runner, identified by where he started the play: the batter (<see cref="Base.Home"/>, a batter-runner once he hits
    /// a fair ball) or the base he occupied. In this foundation a runner advances at most one base (TASK-007 adds running).
    /// </summary>
    public readonly struct Runner : IEquatable<Runner>
    {
        public Runner(Base from) => From = from;

        public static Runner Batter => new Runner(Base.Home);

        public Base From { get; }
        public bool IsBatter => From == Base.Home;
        /// <summary>The base he advances to: first for the batter, then second, third, home.</summary>
        public Base Next => Bases.Next(From);

        public bool Equals(Runner other) => From == other.From;
        public override bool Equals(object obj) => obj is Runner r && Equals(r);
        public override int GetHashCode() => (int)From;
        public static bool operator ==(Runner a, Runner b) => a.Equals(b);
        public static bool operator !=(Runner a, Runner b) => !a.Equals(b);
        public override string ToString() => IsBatter ? "batter-runner" : $"runner on {Bases.Name(From)}";
    }

    public static class Bases
    {
        /// <summary>The base after <paramref name="b"/> around the diamond (home → first → second → third → home).</summary>
        public static Base Next(Base b) => b switch { Base.Home => Base.First, Base.First => Base.Second, Base.Second => Base.Third, _ => Base.Home };

        public static string Name(Base b) => b switch { Base.First => "1B", Base.Second => "2B", Base.Third => "3B", _ => "home" };
    }

    /// <summary>
    /// Which bases are occupied when the ball is put in play. One runner per base by construction (a runner is the base he
    /// started on), so two runners on one base cannot be represented.
    /// </summary>
    public readonly struct BaseOccupancy : IEquatable<BaseOccupancy>
    {
        public BaseOccupancy(bool first, bool second, bool third)
        {
            First = first;
            Second = second;
            Third = third;
        }

        public bool First { get; }
        public bool Second { get; }
        public bool Third { get; }

        public static BaseOccupancy Empty => default;
        public static BaseOccupancy Loaded => new BaseOccupancy(true, true, true);

        public bool IsOccupied(Base b) => b switch { Base.First => First, Base.Second => Second, Base.Third => Third, _ => false };

        /// <summary>The runners on base, lead runner last (first, second, third).</summary>
        public IEnumerable<Runner> Runners
        {
            get
            {
                if (First) yield return new Runner(Base.First);
                if (Second) yield return new Runner(Base.Second);
                if (Third) yield return new Runner(Base.Third);
            }
        }

        public bool Equals(BaseOccupancy other) => First == other.First && Second == other.Second && Third == other.Third;
        public override bool Equals(object obj) => obj is BaseOccupancy o && Equals(o);
        public override int GetHashCode() => (First ? 1 : 0) | (Second ? 2 : 0) | (Third ? 4 : 0);

        public override string ToString() =>
            !First && !Second && !Third ? "bases empty" : First && Second && Third ? "bases loaded"
            : "on " + string.Join(", ", new[] { First ? "1B" : null, Second ? "2B" : null, Third ? "3B" : null }.Where(x => x != null));
    }

    /// <summary>
    /// Force plays (OBR Definitions, "Force Play": a runner legally loses his right to occupy a base by reason of the batter
    /// becoming a runner). When the batter becomes a runner he is forced to first; a runner is forced to the next base when
    /// every base behind him is occupied. OBR 5.09(b)(6): the force is removed when a following runner is put out, and as
    /// soon as the runner touches the base he is forced to.
    /// </summary>
    public static class Forces
    {
        /// <summary>Is <paramref name="runner"/> forced when the batter becomes a runner with <paramref name="before"/> on base?</summary>
        public static bool IsForced(Runner runner, BaseOccupancy before)
        {
            if (runner.IsBatter) return true;
            if (!before.IsOccupied(runner.From)) return false;   // no such runner
            for (Base b = Base.First; b < runner.From; b++)
                if (!before.IsOccupied(b)) return false;
            return true;
        }

        /// <summary>The runners forced at the start of the play: the batter, then each forced runner on base (trail first).</summary>
        public static IReadOnlyList<Runner> Forced(BaseOccupancy before)
        {
            var forced = new List<Runner> { Runner.Batter };
            foreach (Runner r in before.Runners)
                if (IsForced(r, before)) forced.Add(r);
            return forced;
        }

        /// <summary>Is <paramref name="follower"/> behind <paramref name="runner"/> (the batter, or a runner on an earlier base)?</summary>
        public static bool Follows(Runner follower, Runner runner) => follower.From < runner.From;   // Home (the batter) < First < …

        /// <summary>
        /// Is <paramref name="runner"/> still forced at <paramref name="time"/>, given the play's events so far? Forced at the
        /// start, not yet retired, not yet on the base he is forced to, and no following runner put out (OBR 5.09(b)(6)).
        /// </summary>
        public static bool IsForcedAt(Runner runner, BaseOccupancy before, IEnumerable<PlayEvent> events, double time)
        {
            if (!IsForced(runner, before)) return false;
            foreach (PlayEvent e in events)
            {
                if (e.Time > time) continue;
                if (e.Runner == runner) return false;                         // out, or safe on the forced base
                if (e.IsOut && Follows(e.Runner, runner)) return false;       // a following runner retired: force removed
            }

            return true;
        }
    }

    /// <summary>
    /// When runners get where they are going. TEMPORARY placeholder for TASK-006B's rule checks, to be replaced by TASK-007's
    /// running motion: the force and out rules only ask "when does he touch the base" and "where is he" through this
    /// interface.
    /// </summary>
    public interface IRunnerTiming
    {
        /// <summary>Time from contact until <paramref name="runner"/>, running on contact, touches his next base (s).</summary>
        double TimeToNextBase(Runner runner);

        /// <summary>Where <paramref name="runner"/> is <paramref name="sinceContact"/> s after contact (ground level).</summary>
        Vector3d PositionAt(Runner runner, double sinceContact);
    }

    /// <summary>
    /// One generic runner from reference times (no ratings, leads or steals):
    /// - home to first 4.28 s from contact: the MLB average (RHB 4.30 s, LHB 4.26 s; Statcast 2016, FanGraphs analysis) —
    ///   MEASURED;
    /// - one base with a lead, from contact, 3.75 s: DERIVED from the 27 ft/s average sprint speed (Statcast), a ~15 ft
    ///   lead at release (Statcast 2024) and a 0.25 s ground-ball read (range 3.6–3.9 s) — gameplay ASSUMPTION.
    /// Position: straight along the base line at the constant average speed that matches the time (a placeholder for tag
    /// checks; no acceleration or lead geometry).
    /// </summary>
    public sealed class ReferenceRunnerTiming : IRunnerTiming
    {
        public const double HomeToFirst = 4.28, OneBase = 3.75;

        public static ReferenceRunnerTiming Instance { get; } = new ReferenceRunnerTiming();

        public double TimeToNextBase(Runner runner) => runner.IsBatter ? HomeToFirst : OneBase;

        public Vector3d PositionAt(Runner runner, double sinceContact)
        {
            double u = Math.Max(0.0, Math.Min(1.0, sinceContact / TimeToNextBase(runner)));
            Vector3d a = FieldLayout.BasePosition(runner.From), b = FieldLayout.BasePosition(runner.Next);
            return a + u * (b - a);
        }
    }

    /// <summary>
    /// A defender touches a base (gameplay, not mesh contact) when his body centre is within <see cref="Radius"/> of the bag's
    /// centre: a foot 0.3–0.4 m from the centre of mass plus the 18 in bag's half-width (ASSUMED; no measured envelope).
    /// </summary>
    public static class BaseTouch
    {
        public const double Radius = 0.6;

        public static bool IsTouching(Vector3d defender, Base b)
        {
            Vector3d d = defender - FieldLayout.BasePosition(b);
            return Math.Sqrt(d.X * d.X + d.Y * d.Y) <= Radius + 1e-9;
        }
    }

    /// <summary>
    /// Tag (OBR Definitions, "Tag"): a fielder holding the ball securely touches the runner with it or with the glove
    /// holding it. Gameplay: the defender possesses the ball and the runner is within <see cref="Reach"/> of his body centre
    /// (arm and glove, ≈ the 1 m chest-height glove reach of the fielding model; ASSUMED). No colliders.
    /// </summary>
    public static class TagRules
    {
        public const double Reach = 1.0;

        public static bool CanTag(Vector3d defender, Vector3d runner, bool defenderPossesses, double reach = Reach)
        {
            if (!defenderPossesses) return false;
            Vector3d d = runner - defender;
            return Math.Sqrt(d.X * d.X + d.Y * d.Y) <= reach + 1e-9;
        }
    }

    /// <summary>
    /// Timing calls. OBR 5.09(a)(10) and 5.09(b)(6) put the runner out only when he or the base is tagged *before* he
    /// touches it, so a defensive completion simultaneous with the runner's arrival — within <see cref="Simultaneous"/>, the
    /// simulation's timing resolution — is SAFE (Jim Evans' reading of "before"; there is no tie clause in the rules).
    /// </summary>
    public static class TimingCall
    {
        public const double Simultaneous = 1e-3;

        public static bool DefenseFirst(double defense, double runner) => defense < runner - Simultaneous;
    }
}
