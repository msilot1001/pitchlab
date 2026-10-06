using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;

namespace Pitchlab.Gameplay.Players
{
    /// <summary>
    /// Generic archetypes and the two generic teams (TASK-017). Not real players: each player is a batting, running and
    /// defensive archetype plus an arm. The average defender at a position is rated to reproduce that position's generic
    /// profile (catchers and first basemen ≈ 25 ft/s, middle fielders 28 ft/s), so differences come from the elite and poor
    /// players, not from the scale.
    /// </summary>
    public static class GenericRosters
    {
        public readonly struct Batting
        {
            public Batting(int contact, int power, int vision, int discipline) => (Contact, Power, Vision, Discipline) = (contact, power, vision, discipline);
            public int Contact { get; }
            public int Power { get; }
            public int Vision { get; }
            public int Discipline { get; }
        }

        public readonly struct Running
        {
            public Running(int speed, int acceleration, int baserunning) => (Speed, Acceleration, Baserunning) = (speed, acceleration, baserunning);
            public int Speed { get; }
            public int Acceleration { get; }
            public int Baserunning { get; }
        }

        public readonly struct Defense
        {
            public Defense(int reaction, int fielding, int catching, int armAccuracy, int transfer) =>
                (Reaction, Fielding, Catching, ArmAccuracy, Transfer) = (reaction, fielding, catching, armAccuracy, transfer);
            public int Reaction { get; }
            public int Fielding { get; }
            public int Catching { get; }
            public int ArmAccuracy { get; }
            public int Transfer { get; }
        }

        public readonly struct Pitching
        {
            public Pitching(int velocity, int command, int movement, int stamina) => (Velocity, Command, Movement, Stamina) = (velocity, command, movement, stamina);
            public int Velocity { get; }
            public int Command { get; }
            public int Movement { get; }
            public int Stamina { get; }
        }

        // Batters
        public static readonly Batting ContactHitter = new Batting(78, 35, 65, 60);
        public static readonly Batting PowerHitter = new Batting(42, 85, 45, 40);
        public static readonly Batting BalancedHitter = new Batting(55, 55, 55, 55);
        public static readonly Batting PatientHitter = new Batting(55, 50, 72, 85);
        public static readonly Batting FreeSwinger = new Batting(45, 65, 35, 18);
        public static readonly Batting PitcherBatting = new Batting(15, 10, 15, 20);

        // Runners (speed 21 ≈ 25 ft/s, 36 ≈ 26, 50 = 27, 64 ≈ 28, 85 ≈ 29.5)
        public static readonly Running FastRunner = new Running(85, 75, 70);
        public static readonly Running AboveAverageRunner = new Running(64, 60, 60);
        public static readonly Running AverageRunner = new Running(50, 50, 50);
        public static readonly Running SlowRunner = new Running(21, 35, 40);

        // Defenders
        public static readonly Defense EliteDefender = new Defense(80, 85, 82, 78, 75);
        public static readonly Defense AverageDefender = new Defense(50, 50, 50, 50, 50);
        public static readonly Defense PoorDefender = new Defense(25, 25, 28, 30, 30);

        // Pitchers
        public static readonly Pitching PowerStarter = new Pitching(82, 45, 60, 70);
        public static readonly Pitching CommandStarter = new Pitching(38, 85, 55, 75);
        public static readonly Pitching BreakingBallPitcher = new Pitching(50, 58, 82, 60);

        /// <summary>A power right-hander: a hard four-seamer he commands best, a slider, a changeup.</summary>
        public static Repertoire PowerRepertoire() => new Repertoire(
            new RepertoirePitch(PitchType.FourSeam, 55, 0.0, 10), new RepertoirePitch(PitchType.Slider, 30, 0.0, 0), new RepertoirePitch(PitchType.Changeup, 15, 0.0, -15));

        /// <summary>A command pitcher: sinker first, a four-seamer, a changeup he trusts, a curveball.</summary>
        public static Repertoire CommandRepertoire() => new Repertoire(
            new RepertoirePitch(PitchType.Sinker, 40, 0.0, 10), new RepertoirePitch(PitchType.FourSeam, 20, 0.0, 0),
            new RepertoirePitch(PitchType.Changeup, 25, 0.0, 10), new RepertoirePitch(PitchType.Curveball, 15, 0.0, -10));

        /// <summary>A breaking-ball pitcher: four-seamer, curveball and slider in near-equal parts.</summary>
        public static Repertoire BreakingBallRepertoire() => new Repertoire(
            new RepertoirePitch(PitchType.FourSeam, 40, 0.0, 0), new RepertoirePitch(PitchType.Curveball, 35, 0.0, 10), new RepertoirePitch(PitchType.Slider, 25, 0.0, 5));

        public static PlayerRatings Compose(Batting b, Running r, Defense d, int armStrength, Pitching? p = null)
        {
            Pitching pitch = p ?? new Pitching(50, 50, 50, 50);
            return new PlayerRatings(b.Contact, b.Power, b.Vision, b.Discipline, r.Speed, r.Acceleration, r.Baserunning,
                d.Reaction, d.Fielding, d.Catching, armStrength, d.ArmAccuracy, d.Transfer, pitch.Velocity, pitch.Command, pitch.Movement, pitch.Stamina);
        }

        private static readonly double[] Heights = { 70.0, 73.0, 76.0 };

        private static PlayerProfile Batter(string prefix, string team, int slot, bool left, DefensivePosition? at, Batting b, Running r, Defense d, int arm) =>
            new PlayerProfile($"{prefix}{slot}", $"{team} #{slot}", left ? BatterSide.Left : BatterSide.Right, Hand.Right, Heights[(slot - 1) % 3], at, Compose(b, r, d, arm));

        /// <summary>A generic pitcher of an archetype (tests, matchups).</summary>
        public static PlayerProfile Pitcher(string id, Hand throws, Pitching archetype, Repertoire repertoire) =>
            new PlayerProfile(id, id, throws == Hand.Left ? BatterSide.Left : BatterSide.Right, throws, 75.0, DefensivePosition.P,
                Compose(PitcherBatting, new Running(36, 40, 40), AverageDefender, 50, archetype), repertoire);

        /// <summary>
        /// A uniform team (TASK-022 matchups): nine hitters of one batting archetype and one defence, average runners, batting
        /// sides alternating left and right (all throw right), every position filled (the ninth a designated hitter), and <paramref name="pitcher"/>.
        /// </summary>
        public static Team Uniform(string team, string prefix, Batting batting, Defense defense, PlayerProfile pitcher)
        {
            DefensivePosition?[] at =
            {
                DefensivePosition.CenterField, DefensivePosition.Shortstop, DefensivePosition.RightField, DefensivePosition.FirstBase,
                DefensivePosition.LeftField, DefensivePosition.ThirdBase, DefensivePosition.C, DefensivePosition.SecondBase, null,
            };
            var players = new PlayerProfile[Lineup.Size];
            for (int i = 0; i < players.Length; i++) players[i] = Batter(prefix, team, i + 1, i % 2 == 0, at[i], batting, AverageRunner, defense, 55);
            return new Team(team, new Lineup(team, players), pitcher);
        }

        /// <summary>The visitors: a speed-and-defence top of the order, power in the middle; a power right-hander starting.</summary>
        public static Team Away()
        {
            const string t = "Away";
            var lineup = new Lineup(t, new[]
            {
                Batter("A", t, 1, true, DefensivePosition.CenterField, ContactHitter, FastRunner, EliteDefender, 60),
                Batter("A", t, 2, false, DefensivePosition.Shortstop, BalancedHitter, AboveAverageRunner, EliteDefender, 65),
                Batter("A", t, 3, true, DefensivePosition.RightField, PowerHitter, AverageRunner, AverageDefender, 70),
                Batter("A", t, 4, false, DefensivePosition.FirstBase, PowerHitter, SlowRunner, AverageDefender, 50),
                Batter("A", t, 5, false, DefensivePosition.LeftField, BalancedHitter, AverageRunner, PoorDefender, 40),
                Batter("A", t, 6, true, DefensivePosition.ThirdBase, PatientHitter, AverageRunner, AverageDefender, 60),
                Batter("A", t, 7, false, DefensivePosition.C, FreeSwinger, SlowRunner, AverageDefender, 55),
                Batter("A", t, 8, false, DefensivePosition.SecondBase, ContactHitter, AboveAverageRunner, AverageDefender, 50),
                Batter("A", t, 9, false, null, FreeSwinger, SlowRunner, PoorDefender, 40),
            });
            var pitcher = new PlayerProfile("AP", "Away P", BatterSide.Right, Hand.Right, 76.0, DefensivePosition.P,
                Compose(PitcherBatting, new Running(36, 40, 40), AverageDefender, 50, PowerStarter), PowerRepertoire());
            return new Team(t, lineup, pitcher);
        }

        /// <summary>The home team: contact and patience, an elite left side of the infield, a command left-hander starting.</summary>
        public static Team Home()
        {
            const string t = "Home";
            var lineup = new Lineup(t, new[]
            {
                Batter("H", t, 1, false, DefensivePosition.Shortstop, ContactHitter, new Running(75, 70, 70), EliteDefender, 60),
                Batter("H", t, 2, true, DefensivePosition.CenterField, BalancedHitter, new Running(80, 70, 65), EliteDefender, 55),
                Batter("H", t, 3, false, DefensivePosition.FirstBase, PowerHitter, SlowRunner, AverageDefender, 50),
                Batter("H", t, 4, true, DefensivePosition.LeftField, PowerHitter, AverageRunner, AverageDefender, 45),
                Batter("H", t, 5, false, DefensivePosition.ThirdBase, PatientHitter, AverageRunner, EliteDefender, 70),
                Batter("H", t, 6, false, DefensivePosition.RightField, FreeSwinger, AverageRunner, PoorDefender, 55),
                Batter("H", t, 7, true, DefensivePosition.SecondBase, ContactHitter, AboveAverageRunner, AverageDefender, 45),
                Batter("H", t, 8, false, DefensivePosition.C, BalancedHitter, SlowRunner, AverageDefender, 60),
                Batter("H", t, 9, false, null, PatientHitter, new Running(35, 40, 50), PoorDefender, 40),
            });
            var pitcher = new PlayerProfile("HP", "Home P", BatterSide.Left, Hand.Left, 75.0, DefensivePosition.P,
                Compose(PitcherBatting, new Running(36, 40, 40), AverageDefender, 50, CommandStarter), CommandRepertoire());
            return new Team(t, lineup, pitcher);
        }
    }
}
