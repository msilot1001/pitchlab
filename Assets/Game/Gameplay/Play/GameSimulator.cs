using System;
using System.Collections.Generic;
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
    /// goes through the production systems — the CPU pitcher's call (TASK-020; the basic automatic pitcher for an unrated
    /// team), the pitch execution and flight, the swing and contact model, the batted-ball flight, the fielding solver, the
    /// live play and <see cref="GameState"/> — none of them re-implemented here. The batter is the CPU batter (<see cref="CpuBatter"/>, TASK-019): he sees the pitch only as it
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
        /// <summary>Every misplay of the game so far (TASK-021).</summary>
        public IReadOnlyList<Misplay> Misplays => _misplays;
        private readonly List<Misplay> _misplays = new List<Misplay>();
        /// <summary>The last pitch's swing and its contact result (null: a take) — calibration and diagnostics (TASK-023).</summary>
        public ContactResult? LastContact { get; private set; }
        /// <summary>The last pitch as thrown (diagnostics).</summary>
        public HittingPitch LastPitch { get; private set; }
        /// <summary>The last ball in play (null before the first).</summary>
        public LivePlay LastPlay { get; private set; }

        /// <summary>Plays one pitch (and its play, if put in play) into the game.</summary>
        public PitchOutcome PlayPitch()
        {
            if (Game.IsOver) throw new InvalidOperationException("The game is over.");
            PlateAppearance pa = Game.Current;
            PlayerProfile batter = pa.Batter;
            int number = pa.Pitches.Count + 1;
            HittingPitch pitch;
            PitchInfo info;
            if (Game.Pitcher?.Repertoire != null)
            {
                // The CPU pitcher (TASK-020).
                PitchDecision call = CpuPitcher.Choose(Game);
                pitch = GamePitches.Create(Game, call.Type, call.TargetX, call.TargetZ, true, _environment, out info, out _);
            }
            else
            {
                PitchCommand command = AutoPitcher.Choose(_seed, pa.Number, number, Game.Count);
                pitch = GamePitches.Create(Game, (Players.PitchType)command.Preset, command.Target, true, _environment, out info, out _);
            }

            Pitches++;
            LastPitch = pitch;

            // The CPU batter (TASK-019): sees the pitch only as it flies, decides, and swings through the contact model.
            SwingParameters swing = SwingParameters.For(batter);
            SeedStream stream = CpuBatter.StreamFor(Game.Seed, batter.Id, pa.Number, number);
            BatterPlan plan = CpuBatter.Plan(CpuBatter.Observe(pitch), batter, Game.Count, swing, pitch.ContactPlaneY, ref stream);
            SwingInput? input = plan.Input;
            ContactResult? result = input is SwingInput s ? ContactResolver.Resolve(pitch, s, swing) : (ContactResult?)null;
            LastContact = result;

            // Decided at the plate (TASK-024: a caught foul tip, a hit by pitch, a strike, a ball), or a batted ball to play out.
            PitchOutcome? decided = PitchOutcomes.BeforePlay(pitch, input, result, swing.SwingDuration, batter.Bats, batter.HeightInches, batter.ZoneBottom, batter.ZoneTop);
            if (decided is PitchOutcome outcome)
            {
                Game.Pitch(outcome, info);
                return outcome;
            }

            ContactResult r = result.Value;
            Situation situation = Game.Situation;
            BallInPlay ball = BallInPlaySimulation.Run(r.BattedBall, _environment, FieldLayout.Standard);
            PlayPersonnel personnel = Game.Personnel;
            var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, personnel.Fielder, FieldLayout.Standard), situation, personnel: personnel,
                executionSeed: Game.PlaySeed);   // defensive execution (TASK-021)
            play.RunToEnd();
            LastPlay = play;
            _misplays.AddRange(play.Misplays);
            Game.Apply(play, info);
            return play.IsFoul ? PitchOutcome.Foul : PitchOutcome.InPlay;
        }

        /// <summary>Plays until the game is over (or <paramref name="maxPitches"/> pitches, a safety stop).</summary>
        public void PlayToEnd(int maxPitches = 3000)
        {
            while (!Game.IsOver && Pitches < maxPitches) PlayPitch();
            if (!Game.IsOver) throw new InvalidOperationException($"No end after {maxPitches} pitches.");
        }
    }
}
