using System;

namespace Pitchlab.Gameplay.Players
{
    /// <summary>Which hand a player throws with.</summary>
    public enum Hand
    {
        Right,
        Left,
    }

    /// <summary>
    /// A player's abilities on a 0–100 display scale (TASK-017), 50 = an average major leaguer at the skill. Gameplay never
    /// reads these numbers directly: <see cref="RatingScale"/> converts each into the physical or input parameter the
    /// simulation uses (Docs/PLAYER_RATINGS.md). Immutable.
    /// </summary>
    public sealed class PlayerRatings
    {
        public const int Min = 0, Max = 100, Average = 50;

        public PlayerRatings(
            int contact = Average, int power = Average, int vision = Average, int discipline = Average,
            int speed = Average, int acceleration = Average, int baserunning = Average,
            int reaction = Average, int fielding = Average, int catching = Average,
            int armStrength = Average, int armAccuracy = Average, int transfer = Average,
            int velocity = Average, int command = Average, int movement = Average, int stamina = Average)
        {
            Contact = Check(contact);
            Power = Check(power);
            Vision = Check(vision);
            Discipline = Check(discipline);
            Speed = Check(speed);
            Acceleration = Check(acceleration);
            Baserunning = Check(baserunning);
            Reaction = Check(reaction);
            Fielding = Check(fielding);
            Catching = Check(catching);
            ArmStrength = Check(armStrength);
            ArmAccuracy = Check(armAccuracy);
            Transfer = Check(transfer);
            Velocity = Check(velocity);
            Command = Check(command);
            Movement = Check(movement);
            Stamina = Check(stamina);
        }

        // Batting
        /// <summary>Squaring the ball up: the swing's aim and timing precision (TASK-019).</summary>
        public int Contact { get; }
        /// <summary>Bat speed (TASK-019).</summary>
        public int Power { get; }
        /// <summary>Reading the pitch: the error of his estimate of where and when it will arrive (TASK-019).</summary>
        public int Vision { get; }
        /// <summary>Swinging at strikes and taking balls (TASK-019).</summary>
        public int Discipline { get; }

        // Running
        public int Speed { get; }
        public int Acceleration { get; }
        /// <summary>Reading the ball off the bat (read delay).</summary>
        public int Baserunning { get; }

        // Fielding
        /// <summary>First move after contact.</summary>
        public int Reaction { get; }
        /// <summary>Handling ground balls (TASK-021).</summary>
        public int Fielding { get; }
        /// <summary>Securing balls in the air and throws (TASK-021).</summary>
        public int Catching { get; }

        // Throwing
        public int ArmStrength { get; }
        /// <summary>Throw direction error (TASK-021).</summary>
        public int ArmAccuracy { get; }
        /// <summary>Glove-to-hand transfer time.</summary>
        public int Transfer { get; }

        // Pitching
        /// <summary>Pitch speed relative to each pitch's average (TASK-018).</summary>
        public int Velocity { get; }
        /// <summary>Release precision: the spread of where the pitch goes around its target (TASK-018).</summary>
        public int Command { get; }
        /// <summary>Spin (TASK-018).</summary>
        public int Movement { get; }
        /// <summary>Pitch count before execution degrades (TASK-018).</summary>
        public int Stamina { get; }

        public static PlayerRatings Average50 => new PlayerRatings();

        private static int Check(int value) =>
            value >= Min && value <= Max ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Ratings run {Min}–{Max}.");
    }
}
