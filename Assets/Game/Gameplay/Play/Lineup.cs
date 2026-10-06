using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Players;
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
    /// A generic player (TASK-013, TASK-017): a stable identity, how he bats and throws, his height (which sets his strike
    /// zone), his fielding position and his ratings. Immutable; no real players.
    /// </summary>
    public sealed class PlayerProfile
    {
        /// <summary>The height the default strike zone (1.5–3.5 ft) is drawn for (in).</summary>
        public const double ReferenceHeightInches = 73.0;

        /// <summary>An unrated player (all 50) with a display-only position label (TASK-013 lineups and tests).</summary>
        public PlayerProfile(string id, string name, BatterSide bats, double heightInches, string position)
            : this(id, name, bats, Hand.Right, heightInches, null, PlayerRatings.Average50)
        {
            Position = position ?? string.Empty;
        }

        /// <param name="fieldingPosition">Where he plays (null: designated hitter, or no assignment).</param>
        /// <param name="repertoire">The pitches he throws (pitchers; null otherwise).</param>
        public PlayerProfile(string id, string name, BatterSide bats, Hand throws, double heightInches, DefensivePosition? fieldingPosition, PlayerRatings ratings,
            Repertoire repertoire = null)
        {
            Repertoire = repertoire;
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A player needs an id.", nameof(id));
            if (!(heightInches > 48.0 && heightInches < 96.0)) throw new ArgumentOutOfRangeException(nameof(heightInches));
            Id = id;
            Name = name ?? id;
            Bats = bats;
            Throws = throws;
            HeightInches = heightInches;
            FieldingPosition = fieldingPosition;
            Ratings = ratings ?? throw new ArgumentNullException(nameof(ratings));
            Position = fieldingPosition is DefensivePosition p ? PositionName(p) : "DH";
        }

        public string Id { get; }
        public string Name { get; }
        public BatterSide Bats { get; }
        public Hand Throws { get; }
        public double HeightInches { get; }
        /// <summary>Position label for display ("SS", "DH", …).</summary>
        public string Position { get; }
        /// <summary>The position he fields (null: designated hitter or unassigned).</summary>
        public DefensivePosition? FieldingPosition { get; }
        public PlayerRatings Ratings { get; }
        /// <summary>The pitches he throws (TASK-018; null for a position player).</summary>
        public Repertoire Repertoire { get; }

        public static string PositionName(DefensivePosition p) => p switch
        {
            DefensivePosition.P => "P", DefensivePosition.C => "C", DefensivePosition.FirstBase => "1B", DefensivePosition.SecondBase => "2B",
            DefensivePosition.ThirdBase => "3B", DefensivePosition.Shortstop => "SS", DefensivePosition.LeftField => "LF",
            DefensivePosition.CenterField => "CF", _ => "RF",
        };

        /// <summary>
        /// His strike zone's bottom and top (m): the default zone scaled with height. REFERENCE CONVENTION — the rulebook zone
        /// follows the batter's stance (knee hollow to the midpoint between shoulders and belt), which scales with height.
        /// </summary>
        public double ZoneBottom => PitchingGeometry.DefaultZoneBottom * HeightInches / ReferenceHeightInches;
        public double ZoneTop => PitchingGeometry.DefaultZoneTop * HeightInches / ReferenceHeightInches;

        public override string ToString() => Name;
    }

    /// <summary>A team's batting order: nine players who bat in turn (OBR 5.04(a)(1)).</summary>
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

        /// <summary>The generic teams' batting orders (TASK-017 rosters: rated players, a mix of sides and three heights).</summary>
        public static Lineup GenericAway() => GenericRosters.Away().Lineup;
        public static Lineup GenericHome() => GenericRosters.Home().Lineup;
    }

    /// <summary>The pitch as thrown, for the record: what was called for and what the flight did (TASK-013).</summary>
    public readonly struct PitchInfo
    {
        public PitchInfo(string label, double speedMph, double plateX, double plateZ, double targetX = double.NaN, double targetZ = double.NaN)
        {
            TargetX = targetX;
            TargetZ = targetZ;
            _label = label;
            SpeedMph = speedMph;
            PlateX = plateX;
            PlateZ = plateZ;
        }

        private readonly string _label;
        /// <summary>The pitch type as called for (empty when not given — also for <c>default</c>).</summary>
        public string Label => _label ?? string.Empty;
        /// <summary>Release speed (mph).</summary>
        public double SpeedMph { get; }
        /// <summary>Where it crossed the front plane of the plate (m; NaN if it never did).</summary>
        public double PlateX { get; }
        public double PlateZ { get; }
        /// <summary>Where the pitcher aimed (front plane of the plate, m; NaN: no target — the preset's own aim).</summary>
        public double TargetX { get; }
        public double TargetZ { get; }
        public bool HasTarget => !double.IsNaN(TargetX) && !double.IsNaN(TargetZ);
        /// <summary>Was a flight recorded that crossed the plate (not a pitch recorded without its info)?</summary>
        public bool HasCrossing => SpeedMph > 0.0 && !double.IsNaN(PlateX) && !double.IsNaN(PlateZ);

        public static PitchInfo Of(string label, HittingPitch pitch, double targetX = double.NaN, double targetZ = double.NaN)
        {
            (double x, double z) = StrikeZone.Crossing(pitch);
            return new PitchInfo(label, Units.MetersPerSecondToMph(pitch.Flight.First.Velocity.Length), x, z, targetX, targetZ);
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
