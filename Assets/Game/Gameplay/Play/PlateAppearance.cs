using System.Collections.Generic;
using Pitchlab.Gameplay.Rules;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// One plate appearance (TASK-013), owned by <see cref="GameState"/>: who bats, from which slot, the count and every
    /// pitch, and how it ended. It lasts until a terminal pitch — ball four, strike three, a fair ball once its play is over —
    /// and is completed (and its result applied to the game) exactly once.
    /// </summary>
    public sealed class PlateAppearance
    {
        private readonly List<PitchEvent> _pitches = new List<PitchEvent>();

        internal PlateAppearance(int number, TeamSide team, int slot, PlayerProfile batter, int inning, Half half, int outs, BaseOccupancy bases)
        {
            Number = number;
            Team = team;
            Slot = slot;
            Batter = batter;
            Inning = inning;
            Half = half;
            StartOuts = outs;
            StartBases = bases;
        }

        /// <summary>1-based, game-wide.</summary>
        public int Number { get; }
        public TeamSide Team { get; }
        /// <summary>Batting-order slot (1–9).</summary>
        public int Slot { get; }
        public PlayerProfile Batter { get; }
        public int Inning { get; }
        public Half Half { get; }
        public int StartOuts { get; }
        public BaseOccupancy StartBases { get; }

        /// <summary>The count; a completed plate appearance reads 0–0 (its last count is its last pitch's <c>Before</c>).</summary>
        public Count Count { get; private set; }
        public IReadOnlyList<PitchEvent> Pitches => _pitches;
        public PlateAppearanceEnd End { get; private set; }
        public bool IsComplete => End != PlateAppearanceEnd.None;
        /// <summary>The result in words once complete ("walk", "strikeout looking", "in play: 1 out, 0 runs", …).</summary>
        public string Result { get; private set; } = string.Empty;
        public int Runs { get; private set; }
        /// <summary>The fair ball's result when it ended in play (null for a walk or a strikeout).</summary>
        public PlayResultKind? PlayResult { get; private set; }
        public int OutsMade { get; private set; }

        internal PitchEvent Record(PitchInfo info, PitchOutcome outcome)
        {
            Count before = Count;
            (Count after, PlateAppearanceEnd end) = Count.After(outcome);
            Count = after;
            var e = new PitchEvent(_pitches.Count + 1, info, outcome, before, after, end);
            _pitches.Add(e);
            return e;
        }

        internal void Complete(PlateAppearanceEnd end, string result, int runs, int outsMade, PlayResultKind? playResult)
        {
            PlayResult = playResult;
            End = end;
            Result = result;
            Runs = runs;
            OutsMade = outsMade;
        }

        /// <summary>The Situation Editor changed the situation mid plate appearance: the same batter keeps his count.</summary>
        internal void CarryCount(PlateAppearance from)
        {
            Count = from.Count;
            _pitches.AddRange(from._pitches);
        }

        /// <summary>A fresh copy (RESET PA keeps the state the plate appearance started from).</summary>
        internal PlateAppearance Copy()
        {
            var c = new PlateAppearance(Number, Team, Slot, Batter, Inning, Half, StartOuts, StartBases)
            {
                Count = Count, End = End, Result = Result, Runs = Runs, OutsMade = OutsMade, PlayResult = PlayResult,
            };
            c._pitches.AddRange(_pitches);
            return c;
        }

        public override string ToString() => $"PA {Number}: #{Slot} {Batter} ({Team}) — {(IsComplete ? Result : Count.ToString())}";
    }
}
