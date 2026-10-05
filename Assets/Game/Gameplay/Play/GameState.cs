using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Rules;

namespace Pitchlab.Gameplay.Play
{
    public enum Half
    {
        Top,
        Bottom,
    }

    /// <summary>A named starting situation for the Situation Editor.</summary>
    public readonly struct SituationPreset
    {
        public SituationPreset(string name, int outs, BaseOccupancy bases)
        {
            Name = name;
            Outs = outs;
            Bases = bases;
        }

        public string Name { get; }
        public int Outs { get; }
        public BaseOccupancy Bases { get; }
    }

    /// <summary>
    /// The authoritative game state (TASK-010): inning, half, outs, score, bases, the plate appearance and its ball/strike
    /// count (TASK-012). Changed only by the result of a pitch (<see cref="Pitch"/>), of a finished play (<see cref="Apply"/>)
    /// or by the Situation Editor between plays (<see cref="Set"/>). A walk, a strikeout or a fair ball (in play or out of
    /// the park) ends the plate appearance.
    /// </summary>
    public sealed class GameState
    {
        public const int OutsPerHalf = 3;

        public static readonly SituationPreset[] Presets =
        {
            new SituationPreset("Empty, 0 out", 0, BaseOccupancy.Empty),
            new SituationPreset("R1, 0 out", 0, new BaseOccupancy(true, false, false)),
            new SituationPreset("R1, 1 out", 1, new BaseOccupancy(true, false, false)),
            new SituationPreset("R1 R2, 0 out", 0, new BaseOccupancy(true, true, false)),
            new SituationPreset("R1 R3, 1 out", 1, new BaseOccupancy(true, false, true)),
            new SituationPreset("R3, 1 out", 1, new BaseOccupancy(false, false, true)),
            new SituationPreset("Loaded, 1 out", 1, BaseOccupancy.Loaded),
            new SituationPreset("Loaded, 2 out", 2, BaseOccupancy.Loaded),
        };

        private readonly int[] _score = new int[2];
        private readonly List<string> _log = new List<string>();
        private (int Inning, Half Half, int Outs, BaseOccupancy Bases, int Away, int Home, int Pa, int Log, Count Count) _paStart;
        private LivePlay _lastApplied;

        public GameState() => _paStart = Snapshot();

        public int Inning { get; private set; } = 1;
        public Half Half { get; private set; } = Half.Top;
        public int Outs { get; private set; }
        public BaseOccupancy Bases { get; private set; } = BaseOccupancy.Empty;
        /// <summary>The current plate appearance's count (0–0 before its first pitch).</summary>
        public Count Count { get; private set; }
        public int AwayScore => _score[0];
        public int HomeScore => _score[1];
        /// <summary>Plate appearances completed in the game (the current one is number <see cref="PlateAppearance"/> + 1).</summary>
        public int PlateAppearance { get; private set; }
        /// <summary>One line per completed plate appearance and per half-inning change.</summary>
        public IReadOnlyList<string> Log => _log;
        /// <summary>The situation the next pitch is thrown in.</summary>
        public Situation Situation => new Situation(Outs, Bases);

        /// <summary>The Situation Editor (between plays only — the caller locks it during a live play). The count is kept.</summary>
        public void Set(int inning, Half half, int outs, BaseOccupancy bases, int away, int home)
        {
            if (inning < 1) throw new ArgumentOutOfRangeException(nameof(inning));
            if (outs < 0 || outs >= OutsPerHalf) throw new ArgumentOutOfRangeException(nameof(outs));
            if (away < 0 || home < 0) throw new ArgumentOutOfRangeException(nameof(away));
            Inning = inning;
            Half = half;
            Outs = outs;
            Bases = bases;
            _score[0] = away;
            _score[1] = home;
            _paStart = Snapshot();
        }

        public void Set(SituationPreset preset) => Set(Inning, Half, preset.Outs, preset.Bases, AwayScore, HomeScore);

        /// <summary>Back to the start of the current plate appearance, or of the one that just ended (undoes its result).</summary>
        public void ResetPlateAppearance()
        {
            int log;
            Count count;
            (Inning, Half, Outs, Bases, _score[0], _score[1], PlateAppearance, log, count) = _paStart;
            Count = count;
            _log.RemoveRange(log, _log.Count - log);
            _lastApplied = null;
        }

        /// <summary>
        /// A pitch that was not put in play (ball, called or swinging strike): the count, and on ball four a walk (forced
        /// runners advance, a run scores with the bases loaded), on strike three an out. Fouls and fair balls come with their
        /// play (<see cref="Apply"/>).
        /// </summary>
        public PlateAppearanceEnd Pitch(PitchOutcome result)
        {
            if (result == PitchOutcome.Foul || result == PitchOutcome.InPlay) throw new ArgumentException("A batted ball is applied with its play.", nameof(result));
            return Record(result, null);
        }

        /// <summary>
        /// The result of a finished play. A dead foul is a strike below two strikes (same batter, runners back). A fair ball
        /// ends the plate appearance: runs to the batting team (the play has already applied OBR 5.08(a)), the outs, the bases
        /// — or, on the third out, the half-inning changes (bases cleared, no outs; after the bottom half the next inning).
        /// </summary>
        public PlateAppearanceEnd Apply(LivePlay play)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (!play.IsOver) throw new InvalidOperationException("Apply a play once it is over.");
            if (ReferenceEquals(play, _lastApplied)) throw new InvalidOperationException("This play has already been applied.");
            if (play.Situation.Outs != Outs || !play.Situation.Bases.Equals(Bases)) throw new InvalidOperationException("The play did not start from this state.");
            return Record(play.Kind == LivePlay.BallKind.Dead && play.AwardedBases == 0 ? PitchOutcome.Foul : PitchOutcome.InPlay, play);
        }

        private PlateAppearanceEnd Record(PitchOutcome result, LivePlay play)
        {
            if (Count.Equals(default(Count))) _paStart = Snapshot();   // the plate appearance's first pitch: where RESET PA returns
            if (play != null) _lastApplied = play;
            (Count next, PlateAppearanceEnd end) = Count.After(result);
            Count = next;
            switch (end)
            {
                case PlateAppearanceEnd.Walk:
                    BaseOccupancy walked = Count.Walk(Bases, out int runs);
                    End("walk", runs, Outs, walked);
                    break;
                case PlateAppearanceEnd.Strikeout:
                    End(result == PitchOutcome.CalledStrike ? "strikeout looking" : "strikeout swinging", 0, Outs + 1, Bases);
                    break;
                case PlateAppearanceEnd.InPlay:
                    End($"{play.OutsMade} out{(play.OutsMade == 1 ? "" : "s")}", play.Runs, play.Outs, play.ResultingBases());
                    break;
            }

            return end;
        }

        /// <summary>The plate appearance is over: runs, then the outs and bases — or, on the third out, the next half.</summary>
        private void End(string what, int runs, int outsAfter, BaseOccupancy bases)
        {
            _score[Half == Half.Top ? 0 : 1] += runs;
            PlateAppearance++;
            int outs = Math.Min(outsAfter, OutsPerHalf);
            _log.Add($"{HalfName} {Inning}, PA {PlateAppearance}: {what}, {runs} run{(runs == 1 ? "" : "s")} → {(outs >= OutsPerHalf ? "3 out" : $"{outs} out, {bases}")}");
            if (outs < OutsPerHalf)
            {
                Outs = outs;
                Bases = bases;
                return;
            }

            // Three out: the other team bats.
            Outs = 0;
            Bases = BaseOccupancy.Empty;
            if (Half == Half.Top) Half = Half.Bottom;
            else
            {
                Half = Half.Top;
                Inning++;
            }

            _log.Add($"— {HalfName} {Inning} —");
        }

        public string HalfName => Half == Half.Top ? "Top" : "Bottom";

        public override string ToString() => $"{HalfName} {Inning} · {Outs} out · {Count} · {Bases} · Away {AwayScore} – Home {HomeScore}";

        private (int, Half, int, BaseOccupancy, int, int, int, int, Count) Snapshot() => (Inning, Half, Outs, Bases, _score[0], _score[1], PlateAppearance, _log.Count, Count);
    }
}
