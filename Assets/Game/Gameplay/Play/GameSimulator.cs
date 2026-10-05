using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// Plays a game without rendering (TASK-016): for state and rules testing and as an accelerated smoke run. Every pitch
    /// goes through the production systems — the automatic pitcher's command, the simulated pitch flight, the swing and
    /// contact model, the batted-ball flight, the fielding solver, the live play and <see cref="GameState"/> — none of them
    /// re-implemented here. Only the batter's choices are a simple stand-in: from a hash of the seed and the pitch (no shared
    /// random state), he judges where the pitch will cross (with a few centimetres of error), swings more at strikes and
    /// with two strikes, and swings with a small timing and aim error. Not a model of real hitters.
    /// </summary>
    public sealed class GameSimulator
    {
        /// <summary>The batter's misjudgement of the crossing (m), and his swing's timing (s) and aim (m) errors (standard deviations).</summary>
        public const double JudgeError = 0.03, TimingError = 0.025, AimError = 0.045;

        private readonly int _seed;
        private readonly EnvironmentState _environment;

        public GameSimulator(GameState game, int seed) : this(game, seed, EnvironmentState.Standard) { }

        public GameSimulator(GameState game, int seed, EnvironmentState environment)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            _seed = seed;
            _environment = environment;
        }

        public GameState Game { get; }
        public int Pitches { get; private set; }

        /// <summary>Plays one pitch (and its play, if put in play) into the game.</summary>
        public PitchOutcome PlayPitch()
        {
            if (Game.IsOver) throw new InvalidOperationException("The game is over.");
            PlateAppearance pa = Game.Current;
            PlayerProfile batter = pa.Batter;
            int number = pa.Pitches.Count + 1;
            PitchCommand command = AutoPitcher.Choose(_seed, pa.Number, number, Game.Count);
            HittingPitch pitch = PitchTargets.Create(command, batter.ZoneBottom, batter.ZoneTop, _environment);
            PitchInfo info = PitchInfo.Of(PitchPresets.All[command.Preset].Label, pitch);
            Pitches++;

            ulong h = Mix(Mix(Mix((ulong)(uint)_seed ^ 0xBA77E5UL) ^ (ulong)(uint)pa.Number) ^ (ulong)(uint)number);
            SwingParameters swing = SwingParameters.Default;
            swing.Side = batter.Bats;
            SwingInput? input = null;
            ContactResult? result = null;
            if (pitch.ReachesContactPlane)
            {
                (double x, double z) = StrikeZone.Crossing(pitch);
                bool looksLikeStrike = !double.IsNaN(x) && StrikeZone.Contains(x + JudgeError * Normal(ref h), z + JudgeError * Normal(ref h), batter.ZoneBottom, batter.ZoneTop);
                Count c = Game.Count;
                double p = looksLikeStrike ? (c.Strikes == 2 ? 0.85 : c.Balls == 3 && c.Strikes == 0 ? 0.3 : 0.7) : (c.Strikes == 2 ? 0.35 : 0.25);
                if (Unit(ref h) < p)
                {
                    var s = new SwingInput(pitch.IdealContactTime - swing.SwingDuration + TimingError * Normal(ref h),
                        pitch.IdealContactState.Position.X + AimError * Normal(ref h), pitch.IdealContactState.Position.Z + AimError * Normal(ref h));
                    input = s;
                    result = ContactResolver.Resolve(pitch, s, swing);
                }
            }

            if (result is ContactResult r && r.IsContact)
            {
                Situation situation = Game.Situation;
                BallInPlay ball = BallInPlaySimulation.Run(r.BattedBall, _environment, FieldLayout.Standard);
                var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, FielderProfile.For, FieldLayout.Standard), situation);
                play.RunToEnd();
                Game.Apply(play, info);
                return play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay;
            }

            PitchOutcome outcome = PitchOutcomes.Of(pitch, input, result, null, batter.ZoneBottom, batter.ZoneTop);
            Game.Pitch(outcome, info);
            return outcome;
        }

        /// <summary>Plays until the game is over (or <paramref name="maxPitches"/> pitches, a safety stop).</summary>
        public void PlayToEnd(int maxPitches = 3000)
        {
            while (!Game.IsOver && Pitches < maxPitches) PlayPitch();
            if (!Game.IsOver) throw new InvalidOperationException($"No end after {maxPitches} pitches.");
        }

        private static double Unit(ref ulong h)
        {
            h = Mix(h + 0x9E3779B97F4A7C15UL);
            return ((h >> 11) + 0.5) * (1.0 / (1UL << 53));
        }

        /// <summary>A standard normal deviate (Box–Muller) from the hash stream.</summary>
        private static double Normal(ref ulong h)
        {
            double u1 = Unit(ref h), u2 = Unit(ref h);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
