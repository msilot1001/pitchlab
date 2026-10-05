using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;

namespace Pitchlab.Gameplay.Play
{
    public enum Half
    {
        Top,
        Bottom,
    }

    /// <summary>Where the game is (TASK-016).</summary>
    public enum GameStatus
    {
        /// <summary>No pitch thrown yet.</summary>
        Pregame,
        Playing,
        /// <summary>Over: no more pitches (<see cref="GameState.Result"/>).</summary>
        GameOver,
    }

    /// <summary>How a game ended (TASK-016).</summary>
    public sealed class GameResult
    {
        public GameResult(int away, int home, int inning, Half half, string reason)
        {
            Away = away;
            Home = home;
            Inning = inning;
            Half = half;
            Reason = reason;
        }

        public int Away { get; }
        public int Home { get; }
        public TeamSide Winner => Home > Away ? TeamSide.Home : TeamSide.Away;
        /// <summary>The last half-inning played (partly, on a walk-off).</summary>
        public int Inning { get; }
        public Half Half { get; }
        public string Reason { get; }
        public override string ToString() => $"FINAL · Away {Away} – Home {Home} · {Reason}";
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
        /// <summary>A regulation game: nine innings (OBR 7.01(a)).</summary>
        public const int RegulationInnings = 9;

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
        /// <summary>Every play applied (by reference): a play is applied at most once, whatever happens in between.</summary>
        private readonly HashSet<LivePlay> _applied = new HashSet<LivePlay>();

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
        /// <summary>Changes whenever anything in the game changes (a pitch, a plate appearance, the editor, a reset): a cheap
        /// way for views to know when to rebuild what they show.</summary>
        public int Version { get; private set; }

        /// <summary>How the game ended (null while it is on).</summary>
        public GameResult Result { get; private set; }
        public bool IsOver => Result != null;
        public GameStatus Status => IsOver ? GameStatus.GameOver : _completed.Count == 0 && Current.Pitches.Count == 0 && !_started ? GameStatus.Pregame : GameStatus.Playing;
        private bool _started;
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
            Result = null;   // the editor puts a finished game back in play
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
            Version++;
        }

        public void Set(SituationPreset preset) => Set(Inning, Half, preset.Outs, preset.Bases, AwayScore, HomeScore);

        /// <summary>Back to the start of the current plate appearance, or of the one that just ended (undoes its result).</summary>
        public void ResetPlateAppearance()
        {
            Restore(_paStart);
            Version++;
        }

        /// <summary>
        /// A pitch that was not put in play (ball, called or swinging strike): the count, and on ball four a walk (forced
        /// runners advance, a run scores with the bases loaded), on strike three an out. Fouls and fair balls come with their
        /// play (<see cref="Apply"/>).
        /// </summary>
        public PlateAppearanceEnd Pitch(PitchOutcome result, PitchInfo info = default)
        {
            if (result == PitchOutcome.Foul || result == PitchOutcome.InPlay) throw new ArgumentException("A batted ball is applied with its play.", nameof(result));
            if (IsOver) throw new InvalidOperationException("The game is over.");
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
            if (IsOver) throw new InvalidOperationException("The game is over.");
            if (!play.IsOver) throw new InvalidOperationException("Apply a play once it is over.");
            if (_applied.Contains(play)) throw new InvalidOperationException("This play has already been applied.");
            if (play.Situation.Outs != Outs || !play.Situation.Bases.Equals(Bases)) throw new InvalidOperationException("The play did not start from this state.");
            return Record(play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay, info, play);
        }

        private PlateAppearanceEnd Record(PitchOutcome result, PitchInfo info, LivePlay play)
        {
            if (Current.Pitches.Count == 0) _paStart = Capture(clearPitches: false);   // the first pitch: where RESET PA returns
            _started = true;
            if (play != null) _applied.Add(play);
            PitchEvent e = Current.Record(info, result);
            Version++;
            switch (e.End)
            {
                case PlateAppearanceEnd.Walk:
                    BaseOccupancy walked = Rules.Count.Walk(Bases, out int runs);
                    End(e.End, "walk", runs, 0, walked, null, null);
                    break;
                case PlateAppearanceEnd.Strikeout:
                    End(e.End, result == PitchOutcome.CalledStrike ? "strikeout looking" : "strikeout swinging", 0, 1, Bases, null, null);
                    break;
                case PlateAppearanceEnd.InPlay:
                    PlayResultKind kind = PlayResults.Classify(play);
                    End(e.End, PlayResults.Describe(kind), play.Runs, play.OutsMade, play.ResultingBases(), kind, play);
                    break;
            }

            return e.End;
        }

        /// <summary>
        /// The plate appearance is over: recorded, the batting order moves on (OBR 5.04(a)(1); the next inning starts after
        /// the last batter who completed his time at bat, 5.04(a)(3)); runs, then the outs and bases — or,
        /// on the third out, the next half — and the next batter comes up.
        /// </summary>
        private void End(PlateAppearanceEnd end, string what, int runs, int outsMade, BaseOccupancy bases, PlayResultKind? kind, LivePlay play)
        {
            // A walk-off: the home team takes the lead in the 9th or later — the game ends the moment the winning run scores,
            // so only that run counts, unless the batter hit it out of the park: then he and every runner score (OBR 7.01(e)(3)
            // and its EXCEPTION; a forced walk-off scores the one run, 5.08(b)). Outs made after the winning run do not count,
            // and a hit is credited only with the bases the winning runner advanced (9.06(f)).
            bool walkOff = Half == Half.Bottom && Inning >= RegulationInnings && _score[1] <= _score[0] && _score[1] + runs > _score[0];
            if (walkOff && kind != PlayResultKind.HomeRun)
            {
                runs = _score[0] - _score[1] + 1;
                if (play != null) (outsMade, kind, what) = WalkOff(play, runs, outsMade, kind.Value, what);
            }
            Current.Complete(end, what, runs, outsMade, kind);
            _completed.Add(Current);
            int team = (int)Batting;
            _upNext[team] = _upNext[team] % Lineup.Size + 1;
            _score[team] += runs;
            int outs = Math.Min(Outs + outsMade, OutsPerHalf);
            _log.Add($"{HalfName} {Inning}, PA {_completed.Count} ({Current.Batter}): {what}, {runs} run{(runs == 1 ? "" : "s")} → {(walkOff ? "walk-off" : outs >= OutsPerHalf ? "3 out" : $"{outs} out, {bases}")}");
            if (walkOff)
            {
                Outs = Math.Min(outs, OutsPerHalf - 1);
                Bases = bases;
                Finish("walk-off");
            }
            else if (outs < OutsPerHalf)
            {
                Outs = outs;
                Bases = bases;
            }
            else
            {
                Outs = 0;
                Bases = BaseOccupancy.Empty;
                if (Half == Half.Top && Inning >= RegulationInnings && _score[1] > _score[0])
                    Finish($"the home team leads after the top of the {Ordinal(Inning)}");   // 7.01(e)(1)
                else if (Half == Half.Bottom && Inning >= RegulationInnings && _score[0] != _score[1])
                    Finish(Inning == RegulationInnings ? "nine innings" : $"{Inning} innings");   // 7.01(e)(2), 7.01(b)(1)
                else
                {
                    // The other team bats (tied after nine: extra innings, traditional — bases empty, OBR 7.01(b)(1)).
                    if (Half == Half.Top) Half = Half.Bottom;
                    else
                    {
                        Half = Half.Top;
                        Inning++;
                    }

                    _log.Add($"— {HalfName} {Inning} —");
                }
            }

            Current = NewPlateAppearance();
        }

        /// <summary>The walk-off play cut at the winning run: the outs made before it scored, and a hit's value (9.06(f)).</summary>
        private static (int Outs, PlayResultKind Kind, string What) WalkOff(LivePlay play, int winningRun, int outsMade, PlayResultKind kind, string what)
        {
            LiveRunner winner = null;
            int scored = 0;
            foreach (LiveRunner r in System.Linq.Enumerable.OrderBy(play.Runners, r => r.ScoreTime))
                if (r.HasScored && r.RunCounts && ++scored == winningRun)
                {
                    winner = r;
                    break;
                }

            if (winner == null) return (outsMade, kind, what);
            int outs = 0;
            foreach (PlayEvent e in play.RulesEvents)
                if (e.IsOut && e.Time <= winner.ScoreTime) outs++;
            int hitBases = kind switch
            {
                PlayResultKind.Single => 1,
                PlayResultKind.Double or PlayResultKind.GroundRuleDouble => 2,
                PlayResultKind.Triple => 3,
                PlayResultKind.InsideTheParkHomeRun => 4,
                _ => 0,
            };
            if (hitBases == 0) return (outs, kind, what);
            int credited = Math.Min(hitBases, 4 - (int)winner.Id.From);   // the winning runner's bases (from third: one)
            PlayResultKind hit = credited switch { 1 => PlayResultKind.Single, 2 => PlayResultKind.Double, 3 => PlayResultKind.Triple, _ => kind };
            return (outs, hit, PlayResults.Describe(hit));
        }

        private static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        private void Finish(string reason)
        {
            Result = new GameResult(_score[0], _score[1], Inning, Half, reason);
            _log.Add(Result.ToString());
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
            public GameResult Result;
            public bool Started;
            public Half Half;
            public BaseOccupancy Bases;
            public int[] UpNext;
            public PlateAppearance Current;
        }

        private Snapshot Capture(bool clearPitches) => new Snapshot
        {
            Inning = Inning, Half = Half, Outs = Outs, Bases = Bases, Away = _score[0], Home = _score[1],
            Completed = _completed.Count, Log = _log.Count, UpNext = (int[])_upNext.Clone(), Result = Result, Started = _started,
            Current = clearPitches ? NewPlateAppearance(Current.Number) : Current.Copy(),
        };

        private void Restore(Snapshot s)
        {
            (Inning, Half, Outs, Bases, _score[0], _score[1]) = (s.Inning, s.Half, s.Outs, s.Bases, s.Away, s.Home);
            s.UpNext.CopyTo(_upNext, 0);
            Result = s.Result;
            _started = s.Started;
            _completed.RemoveRange(s.Completed, _completed.Count - s.Completed);
            _log.RemoveRange(s.Log, _log.Count - s.Log);
            Current = s.Current.Copy();
        }
    }
}
