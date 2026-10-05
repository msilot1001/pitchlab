using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>The two sides: the visitors bat in the top half of each inning, the home team in the bottom.</summary>
    public enum TeamSide
    {
        Away,
        Home,
    }

    /// <summary>
    /// A generic player (TASK-013): a stable identity, how he bats and his height (which sets his strike zone). Deliberately
    /// minimal — no ratings, no real players.
    /// </summary>
    public sealed class PlayerProfile
    {
        /// <summary>The height the default strike zone (1.5–3.5 ft) is drawn for (in).</summary>
        public const double ReferenceHeightInches = 73.0;

        public PlayerProfile(string id, string name, BatterSide bats, double heightInches, string position)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A player needs an id.", nameof(id));
            if (!(heightInches > 48.0 && heightInches < 96.0)) throw new ArgumentOutOfRangeException(nameof(heightInches));
            Id = id;
            Name = name ?? id;
            Bats = bats;
            HeightInches = heightInches;
            Position = position ?? string.Empty;
        }

        public string Id { get; }
        public string Name { get; }
        public BatterSide Bats { get; }
        public double HeightInches { get; }
        /// <summary>Placeholder fielding position (display only; no substitutions or defensive assignment yet).</summary>
        public string Position { get; }

        /// <summary>
        /// His strike zone's bottom and top (m): the default zone scaled with height. REFERENCE CONVENTION — the rulebook zone
        /// follows the batter's stance (knee hollow to the midpoint between shoulders and belt), which scales with height.
        /// </summary>
        public double ZoneBottom => PitchingGeometry.DefaultZoneBottom * HeightInches / ReferenceHeightInches;
        public double ZoneTop => PitchingGeometry.DefaultZoneTop * HeightInches / ReferenceHeightInches;

        public override string ToString() => Name;
    }

    /// <summary>A team's batting order: nine players who bat in turn (OBR 5.04(a)).</summary>
    public sealed class Lineup
    {
        public const int Size = 9;
        private readonly PlayerProfile[] _players;

        public Lineup(string team, IReadOnlyList<PlayerProfile> players)
        {
            if (players == null || players.Count != Size) throw new ArgumentException($"A lineup has {Size} players.", nameof(players));
            var ids = new HashSet<string>();
            foreach (PlayerProfile p in players)
                if (p == null || !ids.Add(p.Id)) throw new ArgumentException("Lineup players must be distinct.", nameof(players));
            Team = team;
            _players = new PlayerProfile[Size];
            for (int i = 0; i < Size; i++) _players[i] = players[i];
        }

        public string Team { get; }

        /// <summary>The player batting in <paramref name="slot"/> (1–9).</summary>
        public PlayerProfile this[int slot] => slot >= 1 && slot <= Size ? _players[slot - 1] : throw new ArgumentOutOfRangeException(nameof(slot));

        /// <summary>A generic nine with a mix of right- and left-handed batters and three heights.</summary>
        public static Lineup Generic(string team, string prefix, bool leftyLeadoff)
        {
            var players = new PlayerProfile[Size];
            string[] positions = { "CF", "SS", "RF", "1B", "LF", "3B", "C", "2B", "DH" };
            double[] heights = { 70.0, 73.0, 76.0 };
            for (int i = 0; i < Size; i++)
            {
                // Left-handed batters in slots 1, 3, 6 (or 2, 4, 7 for the other side): consecutive batters change sides.
                bool left = leftyLeadoff ? i == 0 || i == 2 || i == 5 : i == 1 || i == 3 || i == 6;
                players[i] = new PlayerProfile($"{prefix}{i + 1}", $"{team} #{i + 1}", left ? BatterSide.Left : BatterSide.Right, heights[i % heights.Length], positions[i]);
            }

            return new Lineup(team, players);
        }

        public static Lineup GenericAway() => Generic("Away", "A", leftyLeadoff: true);
        public static Lineup GenericHome() => Generic("Home", "H", leftyLeadoff: false);
    }

    /// <summary>The pitch as thrown, for the record: what was called for and what the flight did (TASK-013).</summary>
    public readonly struct PitchInfo
    {
        public PitchInfo(string label, double speedMph, double plateX, double plateZ)
        {
            Label = label ?? string.Empty;
            SpeedMph = speedMph;
            PlateX = plateX;
            PlateZ = plateZ;
        }

        public string Label { get; }
        /// <summary>Release speed (mph).</summary>
        public double SpeedMph { get; }
        /// <summary>Where it crossed the front plane of the plate (m; NaN if it never did).</summary>
        public double PlateX { get; }
        public double PlateZ { get; }

        public static PitchInfo Of(string label, HittingPitch pitch)
        {
            (double x, double z) = StrikeZone.Crossing(pitch);
            return new PitchInfo(label, Units.MetersPerSecondToMph(pitch.Flight.First.Velocity.Length), x, z);
        }
    }

    /// <summary>One pitch of a plate appearance, as recorded by the game (TASK-013).</summary>
    public readonly struct PitchEvent
    {
        public PitchEvent(int number, PitchInfo info, PitchOutcome outcome, Count before, Count after, PlateAppearanceEnd end)
        {
            Number = number;
            Info = info;
            Outcome = outcome;
            Before = before;
            After = after;
            End = end;
        }

        /// <summary>1-based within the plate appearance.</summary>
        public int Number { get; }
        public PitchInfo Info { get; }
        public PitchOutcome Outcome { get; }
        public Count Before { get; }
        /// <summary>The count after the pitch (0–0, the next batter's, when it ended the plate appearance).</summary>
        public Count After { get; }
        public PlateAppearanceEnd End { get; }

        public override string ToString() =>
            $"{Number}. {(Info.Label.Length > 0 ? Info.Label + " " : "")}{(Info.SpeedMph > 0.0 ? $"{Info.SpeedMph:0} mph " : "")}— {PitchOutcomes.Describe(Outcome)}";
    }
}
