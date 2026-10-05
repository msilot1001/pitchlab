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
    /// The authoritative game state (TASK-010): inning, half, outs, score, bases; the two batting orders and where each
    /// stands (TASK-013); the current plate appearance — batter, count, pitches — and every completed one. Changed only by
    /// the result of a pitch (<see cref="Pitch"/>), of a finished play (<see cref="Apply"/>) or by the Situation Editor
    /// between plays (<see cref="Set"/>). A walk, a strikeout or a fair ball (in play or out of the park) completes the plate
    /// appearance; the batting team's next batter in order comes up, and each team's order carries over between innings.
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

        private readonly Lineup[] _lineups;
        private readonly int[] _score = new int[2];
        /// <summary>Each team's next batter (slot 1–9), kept while the other team bats.</summary>
        private readonly int[] _upNext = { 1, 1 };
        private readonly List<string> _log = new List<string>();
        private readonly List<PlateAppearance> _completed = new List<PlateAppearance>();
        private Snapshot _paStart;
        private LivePlay _lastApplied;

        public GameState() : this(Lineup.GenericAway(), Lineup.GenericHome()) { }

        public GameState(Lineup away, Lineup home)
        {
            _lineups = new[] { away ?? throw new ArgumentNullException(nameof(away)), home ?? throw new ArgumentNullException(nameof(home)) };
            Current = NewPlateAppearance();
            _paStart = Capture(clearPitches: true);
        }

        public int Inning { get; private set; } = 1;
        public Half Half { get; private set; } = Half.Top;
        public int Outs { get; private set; }
        public BaseOccupancy Bases { get; private set; } = BaseOccupancy.Empty;
        public int AwayScore => _score[0];
        public int HomeScore => _score[1];
        /// <summary>The team at bat: the visitors in the top half, the home team in the bottom.</summary>
        public TeamSide Batting => Half == Half.Top ? TeamSide.Away : TeamSide.Home;
        public Lineup LineupOf(TeamSide team) => _lineups[(int)team];
        /// <summary>The slot (1–9) due up next for <paramref name="team"/> — for the team at bat, the current batter's.</summary>
        public int UpNext(TeamSide team) => _upNext[(int)team];

        /// <summary>The plate appearance in progress (never complete: a finished one is replaced by the next batter's).</summary>
        public PlateAppearance Current { get; private set; }
        public PlayerProfile Batter => Current.Batter;
        /// <summary>The current plate appearance's count (0–0 before its first pitch).</summary>
        public Count Count => Current.Count;
        /// <summary>Completed plate appearances, in order.</summary>
        public IReadOnlyList<PlateAppearance> Completed => _completed;
        public int CompletedPlateAppearances => _completed.Count;
        /// <summary>One line per completed plate appearance and per half-inning change.</summary>
        public IReadOnlyList<string> Log => _log;
        /// <summary>The situation the next pitch is thrown in.</summary>
        public Situation Situation => new Situation(Outs, Bases);

        /// <summary>
        /// The Situation Editor (between plays only — the caller locks it during a live play). The batter and count are kept
        /// (a change of half brings up the other team's next batter at 0–0); RESET PA then returns to the edited situation at
        /// 0–0.
        /// </summary>
        public void Set(int inning, Half half, int outs, BaseOccupancy bases, int away, int home)
        {
            if (inning < 1) throw new ArgumentOutOfRangeException(nameof(inning));
            if (outs < 0 || outs >= OutsPerHalf) throw new ArgumentOutOfRangeException(nameof(outs));
            if (away < 0 || home < 0) throw new ArgumentOutOfRangeException(nameof(away));
            bool sameTeam = half == Half;
            Inning = inning;
            Half = half;
            Outs = outs;
            Bases = bases;
            _score[0] = away;
            _score[1] = home;
            PlateAppearance old = Current;
            Current = NewPlateAppearance(old.Number);
            if (sameTeam) Current.CarryCount(old);
            _paStart = Capture(clearPitches: true);
        }

        public void Set(SituationPreset preset) => Set(Inning, Half, preset.Outs, preset.Bases, AwayScore, HomeScore);

        /// <summary>Back to the start of the current plate appearance, or of the one that just ended (undoes its result).</summary>
        public void ResetPlateAppearance()
        {
            Restore(_paStart);
            _lastApplied = null;
        }

        /// <summary>
        /// A pitch that was not put in play (ball, called or swinging strike): the count, and on ball four a walk (forced
        /// runners advance, a run scores with the bases loaded), on strike three an out. Fouls and fair balls come with their
        /// play (<see cref="Apply"/>).
        /// </summary>
        public PlateAppearanceEnd Pitch(PitchOutcome result, PitchInfo info = default)
        {
            if (result == PitchOutcome.Foul || result == PitchOutcome.InPlay) throw new ArgumentException("A batted ball is applied with its play.", nameof(result));
            return Record(result, info, null);
        }

        /// <summary>
        /// The result of a finished play. A dead foul is a strike below two strikes (same batter, runners back). A fair ball
        /// completes the plate appearance: runs to the batting team (the play has already applied OBR 5.08(a)), the outs, the
        /// bases — or, on the third out, the half-inning changes (bases cleared, no outs; after the bottom half the next inning).
        /// </summary>
        public PlateAppearanceEnd Apply(LivePlay play, PitchInfo info = default)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (!play.IsOver) throw new InvalidOperationException("Apply a play once it is over.");
            if (ReferenceEquals(play, _lastApplied)) throw new InvalidOperationException("This play has already been applied.");
            if (play.Situation.Outs != Outs || !play.Situation.Bases.Equals(Bases)) throw new InvalidOperationException("The play did not start from this state.");
            return Record(play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay, info, play);
        }

        private PlateAppearanceEnd Record(PitchOutcome result, PitchInfo info, LivePlay play)
        {
            if (Current.Pitches.Count == 0) _paStart = Capture(clearPitches: false);   // the first pitch: where RESET PA returns
            if (play != null) _lastApplied = play;
            PitchEvent e = Current.Record(info, result);
            switch (e.End)
            {
                case PlateAppearanceEnd.Walk:
                    BaseOccupancy walked = Rules.Count.Walk(Bases, out int runs);
                    End(e.End, "walk", runs, 0, walked);
                    break;
                case PlateAppearanceEnd.Strikeout:
                    End(e.End, result == PitchOutcome.CalledStrike ? "strikeout looking" : "strikeout swinging", 0, 1, Bases);
                    break;
                case PlateAppearanceEnd.InPlay:
                    End(e.End, $"in play: {play.OutsMade} out{(play.OutsMade == 1 ? "" : "s")}", play.Runs, play.OutsMade, play.ResultingBases());
                    break;
            }

            return e.End;
        }

        /// <summary>
        /// The plate appearance is over: recorded, the batting order moves on (OBR 5.04(a)); runs, then the outs and bases — or,
        /// on the third out, the next half — and the next batter comes up.
        /// </summary>
        private void End(PlateAppearanceEnd end, string what, int runs, int outsMade, BaseOccupancy bases)
        {
            Current.Complete(end, what, runs, outsMade);
            _completed.Add(Current);
            int team = (int)Batting;
            _upNext[team] = _upNext[team] % Lineup.Size + 1;
            _score[team] += runs;
            int outs = Math.Min(Outs + outsMade, OutsPerHalf);
            _log.Add($"{HalfName} {Inning}, PA {_completed.Count} ({Current.Batter}): {what}, {runs} run{(runs == 1 ? "" : "s")} → {(outs >= OutsPerHalf ? "3 out" : $"{outs} out, {bases}")}");
            if (outs < OutsPerHalf)
            {
                Outs = outs;
                Bases = bases;
            }
            else
            {
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

            Current = NewPlateAppearance();
        }

        private PlateAppearance NewPlateAppearance() => NewPlateAppearance(_completed.Count + 1);

        private PlateAppearance NewPlateAppearance(int number)
        {
            int slot = _upNext[(int)Batting];
            return new PlateAppearance(number, Batting, slot, LineupOf(Batting)[slot], Inning, Half, Outs, Bases);
        }

        public string HalfName => Half == Half.Top ? "Top" : "Bottom";

        public override string ToString() => $"{HalfName} {Inning} · {Outs} out · {Count} · {Bases} · Away {AwayScore} – Home {HomeScore}";

        /// <summary>Everything a RESET PA restores.</summary>
        private sealed class Snapshot
        {
            public int Inning, Outs, Away, Home, Completed, Log;
            public Half Half;
            public BaseOccupancy Bases;
            public int[] UpNext;
            public PlateAppearance Current;
        }

        private Snapshot Capture(bool clearPitches) => new Snapshot
        {
            Inning = Inning, Half = Half, Outs = Outs, Bases = Bases, Away = _score[0], Home = _score[1],
            Completed = _completed.Count, Log = _log.Count, UpNext = (int[])_upNext.Clone(),
            Current = clearPitches ? NewPlateAppearance(Current.Number) : Current.Copy(),
        };

        private void Restore(Snapshot s)
        {
            (Inning, Half, Outs, Bases, _score[0], _score[1]) = (s.Inning, s.Half, s.Outs, s.Bases, s.Away, s.Home);
            s.UpNext.CopyTo(_upNext, 0);
            _completed.RemoveRange(s.Completed, _completed.Count - s.Completed);
            _log.RemoveRange(s.Log, _log.Count - s.Log);
            Current = s.Current.Copy();
        }
    }
}
