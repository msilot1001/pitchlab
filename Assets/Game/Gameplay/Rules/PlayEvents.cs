using System;
using System.Collections.Generic;
using System.Linq;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Rules
{
    public enum PlayEventKind
    {
        /// <summary>A fly ball caught before it touched the ground: the batter is out (OBR 5.09(a)(1)).</summary>
        FlyOut,
        /// <summary>A forced runner (the batter-runner at first included, OBR 5.09(a)(10)) beaten to his base by a defender
        /// holding the ball on it (OBR 5.09(b)(6)).</summary>
        ForceOut,
        /// <summary>A runner touched by a defender holding the ball before he reached the base (OBR Definitions, "Tag").</summary>
        TagOut,
        /// <summary>A runner reached the base he was going to.</summary>
        Safe,
    }

    /// <summary>One authoritative rules event of a play (a runner put out or safe), at its play time.</summary>
    public readonly struct PlayEvent
    {
        public PlayEvent(double time, PlayEventKind kind, Runner runner, Base? at, DefensivePosition? fielder)
        {
            Time = time;
            Kind = kind;
            Runner = runner;
            At = at;
            Fielder = fielder;
        }

        public double Time { get; }
        public PlayEventKind Kind { get; }
        public Runner Runner { get; }
        /// <summary>The base where it happened (null for a fly out).</summary>
        public Base? At { get; }
        /// <summary>The defender who made the out (catch, base touch or tag).</summary>
        public DefensivePosition? Fielder { get; }
        public bool IsOut => Kind != PlayEventKind.Safe;

        public override string ToString() => Kind switch
        {
            PlayEventKind.FlyOut => "FLY OUT",
            PlayEventKind.Safe => $"SAFE AT {Bases.Name(At.Value).ToUpperInvariant()}",
            _ => $"OUT AT {Bases.Name(At.Value).ToUpperInvariant()}" + (Kind == PlayEventKind.TagOut ? " (tag)" : ""),
        };
    }

    /// <summary>What the play is doing at a moment.</summary>
    public enum PlayStatus
    {
        /// <summary>In progress; nothing decided yet.</summary>
        Live,
        /// <summary>The latest event so far put a runner out.</summary>
        OutRecorded,
        /// <summary>The latest event so far was a runner reaching his base safely.</summary>
        RunnerSafe,
        /// <summary>No play: a foul ball or a ball out of the park.</summary>
        BallDead,
        /// <summary>Over: every event has happened and the defense is done.</summary>
        PlayComplete,
    }

    /// <summary>
    /// The authoritative result of one play: a chronological list of events (several outs possible later — double plays,
    /// tag plays), each runner at most once, and the out count before and after. Outs are counted once, from the events
    /// themselves, so the catch, the possession and later state changes of the same out can never count it again. The
    /// third out ends the play: nothing after it is recorded (OBR 5.09(d)). Whether a run touched home before it counts
    /// (OBR 5.08(a) exception) is scoring, not modelled yet.
    /// </summary>
    public sealed class PlayResolution
    {
        public PlayResolution(IEnumerable<PlayEvent> events, int outsBefore, double defenseEnd, bool ballDead)
        {
            if (outsBefore < 0 || outsBefore >= OutsPerInning) throw new ArgumentOutOfRangeException(nameof(outsBefore));
            // Stable chronological order (equal times keep their given order), up to and including the third out.
            var ordered = new List<PlayEvent>();
            int outs = outsBefore;
            foreach (PlayEvent e in events.Select((e, i) => (e, i)).OrderBy(x => x.e.Time).ThenBy(x => x.i).Select(x => x.e))
            {
                if (outs == OutsPerInning) break;
                ordered.Add(e);
                if (e.IsOut) outs++;
            }

            Events = ordered;
            var seen = new HashSet<Runner>();
            foreach (PlayEvent e in Events)
                if (!seen.Add(e.Runner)) throw new ArgumentException($"{e.Runner} has two results in one play.", nameof(events));
            if (ballDead && Events.Count > 0) throw new ArgumentException("A dead ball has no plays.", nameof(events));
            OutsBefore = outsBefore;
            Outs = Events.Count(e => e.IsOut);
            BallDead = ballDead;
            EndTime = Math.Max(defenseEnd, Events.Count > 0 ? Events[Events.Count - 1].Time : double.NegativeInfinity);
        }

        public const int OutsPerInning = 3;

        public IReadOnlyList<PlayEvent> Events { get; }
        public int OutsBefore { get; }
        /// <summary>Outs made on this play.</summary>
        public int Outs { get; }
        public int OutsAfter => OutsBefore + Outs;
        public bool BallDead { get; }
        /// <summary>When the play is over: the last event or the end of the defense's action, whichever is later.</summary>
        public double EndTime { get; }

        public IEnumerable<PlayEvent> EventsUntil(double time) => Events.Where(e => e.Time <= time);

        public int OutsAt(double time) => OutsBefore + EventsUntil(time).Count(e => e.IsOut);

        public PlayStatus StatusAt(double time)
        {
            if (BallDead) return PlayStatus.BallDead;
            if (time >= EndTime) return PlayStatus.PlayComplete;
            PlayEvent? last = null;
            foreach (PlayEvent e in Events)
                if (e.Time <= time) last = e;
            return last == null ? PlayStatus.Live : last.Value.IsOut ? PlayStatus.OutRecorded : PlayStatus.RunnerSafe;
        }
    }
}
