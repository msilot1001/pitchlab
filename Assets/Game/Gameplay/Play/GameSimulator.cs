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
    /// re-implemented here. The batter is the CPU batter (<see cref="CpuBatter"/>, TASK-019): he sees the pitch only as it
    /// flies and swings through the same timestamped PCI and swing input and the same contact model as a human.
    /// </summary>
    public sealed class GameSimulator
    {
        private readonly int _seed;
        private readonly EnvironmentState _environment;

        /// <summary>Plays <paramref name="game"/>; every seeded choice (pitches, execution, the batter) follows its <see cref="GameState.Seed"/>.</summary>
        public GameSimulator(GameState game) : this(game, EnvironmentState.Standard) { }

        public GameSimulator(GameState game, EnvironmentState environment)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            _seed = game.Seed;
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
            PitchCommand command = AutoPitcher.Choose(_seed, pa.Number, number, Game.Count, Game.Pitcher?.Repertoire);
            HittingPitch pitch = GamePitches.Create(Game, (Players.PitchType)command.Preset, command.Target, true, _environment, out PitchInfo info, out _);
            Pitches++;

            // The CPU batter (TASK-019): sees the pitch only as it flies, decides, and swings through the contact model.
            SwingParameters swing = SwingParameters.For(batter);
            SeedStream stream = CpuBatter.StreamFor(Game.Seed, batter.Id, pa.Number, number);
            BatterPlan plan = CpuBatter.Plan(CpuBatter.Observe(pitch), batter, Game.Count, swing, pitch.ContactPlaneY, ref stream);
            SwingInput? input = plan.Input;
            ContactResult? result = input is SwingInput s ? ContactResolver.Resolve(pitch, s, swing) : (ContactResult?)null;

            if (result is ContactResult r && r.IsContact)
            {
                Situation situation = Game.Situation;
                BallInPlay ball = BallInPlaySimulation.Run(r.BattedBall, _environment, FieldLayout.Standard);
                PlayPersonnel personnel = Game.Personnel;
                var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, personnel.Fielder, FieldLayout.Standard), situation, personnel: personnel);
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
    }
}
