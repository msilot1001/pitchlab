using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;

namespace Pitchlab.Gameplay.Players
{
    /// <summary>
    /// A team for a game (TASK-017): its batting order, who plays where, and its starting pitcher. No substitutions or bullpen
    /// yet. A team made only of a lineup (TASK-013 style, no positions or pitcher) fields the generic position profiles.
    /// </summary>
    public sealed class Team
    {
        private readonly PlayerProfile[] _fielders = new PlayerProfile[DefensiveAlignment.Count];

        public Team(string name, Lineup lineup, PlayerProfile pitcher = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Lineup = lineup ?? throw new ArgumentNullException(nameof(lineup));
            Pitcher = pitcher;
            for (int slot = 1; slot <= Lineup.Size; slot++)
                if (lineup[slot].FieldingPosition is DefensivePosition p)
                {
                    if (p == DefensivePosition.P || _fielders[(int)p] != null) throw new ArgumentException($"{p} is filled twice (or by a batter).", nameof(lineup));
                    _fielders[(int)p] = lineup[slot];
                }

            if (pitcher != null)
            {
                if (pitcher.FieldingPosition != DefensivePosition.P) throw new ArgumentException("The starting pitcher plays P.", nameof(pitcher));
                _fielders[(int)DefensivePosition.P] = pitcher;
            }
        }

        public string Name { get; }
        public Lineup Lineup { get; }
        /// <summary>The starting pitcher (null for a lineup-only team).</summary>
        public PlayerProfile Pitcher { get; }

        /// <summary>Who plays <paramref name="position"/> (null: unassigned — the generic profile plays it).</summary>
        public PlayerProfile Fielder(DefensivePosition position) => _fielders[(int)position];

        /// <summary>Every position filled by a rated player.</summary>
        public bool FullyStaffed => Array.TrueForAll(_fielders, f => f != null);
    }
}
