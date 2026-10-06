using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>
    /// Who is on the field for one play, as the simulation sees them (TASK-017): each defender's movement and throwing
    /// profile and each runner's running profile. <see cref="Standard"/> is the generic, position-based set every play used
    /// before players had ratings; a game builds one from its rosters (<see cref="GameState.Personnel"/>).
    /// </summary>
    public sealed class PlayPersonnel
    {
        private readonly FielderProfile[] _fielders = new FielderProfile[DefensiveAlignment.Count];
        private readonly ThrowProfile[] _throws = new ThrowProfile[DefensiveAlignment.Count];
        private readonly ThrowProfile[] _fullThrows = new ThrowProfile[DefensiveAlignment.Count];
        private readonly RunnerProfile[] _runners = new RunnerProfile[4];   // indexed by the base the runner started on (home: the batter)

        public PlayPersonnel(Func<DefensivePosition, FielderProfile> fielder, Func<DefensivePosition, ThrowProfile> routineThrow,
            Func<DefensivePosition, ThrowProfile> fullThrow, Func<Runner, RunnerProfile> runner)
        {
            if (fielder == null || routineThrow == null || fullThrow == null || runner == null) throw new ArgumentNullException(nameof(fielder));
            for (int i = 0; i < DefensiveAlignment.Count; i++)
            {
                var p = (DefensivePosition)i;
                _fielders[i] = fielder(p);
                _throws[i] = routineThrow(p);
                _fullThrows[i] = fullThrow(p);
            }

            for (int b = 0; b < 4; b++) _runners[b] = runner(new Runner((Simulation.Field.Base)b));
        }

        public static PlayPersonnel Standard { get; } = new PlayPersonnel(FielderProfile.For, ThrowProfile.For, ThrowProfile.Full, _ => RunnerProfile.Standard);

        public FielderProfile Fielder(DefensivePosition p) => _fielders[(int)p];
        /// <summary>A routine throw.</summary>
        public ThrowProfile Throw(DefensivePosition p) => _throws[(int)p];
        /// <summary>A max-effort throw.</summary>
        public ThrowProfile FullThrow(DefensivePosition p) => _fullThrows[(int)p];
        public RunnerProfile Runner(Runner r) => _runners[(int)r.From];
    }
}
