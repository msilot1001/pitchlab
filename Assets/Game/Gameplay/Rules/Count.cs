using System;

namespace Pitchlab.Gameplay.Rules
{
    /// <summary>What one pitch did (TASK-012). Hit-by-pitch, check swings and bunts are not modelled.</summary>
    public enum PitchOutcome
    {
        Ball,
        CalledStrike,
        SwingingStrike,
        /// <summary>A batted ball that ends foul (no foul catches or foul tips into the catcher's glove are modelled).</summary>
        Foul,
        /// <summary>A fair ball (in play or out of the park): the play decides the plate appearance.</summary>
        InPlay,
    }

    /// <summary>How a pitch ended the plate appearance, if it did.</summary>
    public enum PlateAppearanceEnd
    {
        None,
        Walk,
        Strikeout,
        InPlay,
    }

    /// <summary>The ball/strike count of a plate appearance.</summary>
    public readonly struct Count : IEquatable<Count>
    {
        public const int BallsForWalk = 4, StrikesForOut = 3;

        public Count(int balls, int strikes)
        {
            if (balls < 0 || balls >= BallsForWalk) throw new ArgumentOutOfRangeException(nameof(balls));
            if (strikes < 0 || strikes >= StrikesForOut) throw new ArgumentOutOfRangeException(nameof(strikes));
            Balls = balls;
            Strikes = strikes;
        }

        public int Balls { get; }
        public int Strikes { get; }

        /// <summary>
        /// The count after <paramref name="pitch"/> and whether it ended the plate appearance: ball four is a walk (OBR
        /// 5.05(b)(1)), strike three an out (5.09(a)(2)); a foul is a strike only with fewer than two strikes (Definitions,
        /// "Strike" (d)); a fair ball ends it in play. A finished plate appearance's count is 0–0 (the next batter's).
        /// </summary>
        public (Count Next, PlateAppearanceEnd End) After(PitchOutcome pitch)
        {
            switch (pitch)
            {
                case PitchOutcome.Ball:
                    return Balls + 1 == BallsForWalk ? (default, PlateAppearanceEnd.Walk) : (new Count(Balls + 1, Strikes), PlateAppearanceEnd.None);
                case PitchOutcome.CalledStrike:
                case PitchOutcome.SwingingStrike:
                    return Strikes + 1 == StrikesForOut ? (default, PlateAppearanceEnd.Strikeout) : (new Count(Balls, Strikes + 1), PlateAppearanceEnd.None);
                case PitchOutcome.Foul:
                    return (new Count(Balls, Math.Min(Strikes + 1, StrikesForOut - 1)), PlateAppearanceEnd.None);
                case PitchOutcome.InPlay:
                    return (default, PlateAppearanceEnd.InPlay);
                default:
                    throw new ArgumentOutOfRangeException(nameof(pitch));
            }
        }

        /// <summary>
        /// The bases after a walk: the batter to first and every forced runner up one base (OBR 5.05(b)(1)); with the bases
        /// loaded the runner on third scores (<paramref name="runs"/> = 1).
        /// </summary>
        public static BaseOccupancy Walk(BaseOccupancy before, out int runs)
        {
            bool second = before.Second || before.First;
            bool third = before.Third || (before.First && before.Second);
            runs = before.First && before.Second && before.Third ? 1 : 0;
            return new BaseOccupancy(true, second, third);
        }

        public bool Equals(Count other) => Balls == other.Balls && Strikes == other.Strikes;
        public override bool Equals(object obj) => obj is Count c && Equals(c);
        public override int GetHashCode() => Balls * 3 + Strikes;
        public override string ToString() => $"{Balls}–{Strikes}";
    }
}
