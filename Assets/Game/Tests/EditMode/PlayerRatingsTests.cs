using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>TASK-017: ratings, their physical mappings, the generic rosters, and ratings reaching the simulation's inputs.</summary>
    public class PlayerRatingsTests
    {
        private static readonly DefensivePosition[] Positions = (DefensivePosition[])Enum.GetValues(typeof(DefensivePosition));

        [Test]
        public void RatingsRunZeroToHundred()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerRatings(speed: 101));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerRatings(command: -1));
            Assert.AreEqual(50, new PlayerRatings().Transfer);
        }

        [Test]
        public void EveryMappingIsMonotonicAndBounded()
        {
            for (int r = 0; r < 100; r++)
            {
                var lo = new PlayerRatings(speed: r, acceleration: r, baserunning: r, reaction: r, armStrength: r, transfer: r);
                var hi = new PlayerRatings(speed: r + 1, acceleration: r + 1, baserunning: r + 1, reaction: r + 1, armStrength: r + 1, transfer: r + 1);
                RunnerProfile a = RatingScale.Runner(lo), b = RatingScale.Runner(hi);
                Assert.Greater(b.MaxSpeed, a.MaxSpeed, "faster with Speed");
                Assert.Less(b.AccelerationTime, a.AccelerationTime, "quicker with Acceleration");
                Assert.Less(b.ReadDelay, a.ReadDelay, "earlier read with Baserunning");
                foreach (DefensivePosition p in Positions)
                {
                    FielderProfile fa = RatingScale.Fielder(lo, p), fb = RatingScale.Fielder(hi, p);
                    Assert.Less(fb.ReactionTime, fa.ReactionTime, $"{p}: earlier reaction");
                    Assert.Greater(fb.MaxSpeed, fa.MaxSpeed);
                    Assert.Less(fb.AccelerationTime, fa.AccelerationTime);
                    Assert.Greater(RatingScale.Throw(hi, p).Speed, RatingScale.Throw(lo, p).Speed, $"{p}: stronger arm");
                    Assert.Greater(RatingScale.FullThrow(hi, p).Speed, RatingScale.FullThrow(lo, p).Speed, $"{p}: stronger full-effort throw");
                    Assert.Less(RatingScale.Throw(hi, p).TransferTime, RatingScale.Throw(lo, p).TransferTime, $"{p}: quicker transfer");
                }
            }

            // Bounds: the Statcast ranges (Docs/PLAYER_RATINGS.md).
            Assert.AreEqual((23.5, 30.5), (RatingScale.SprintSpeedFtPerS(0), RatingScale.SprintSpeedFtPerS(100)));
            foreach (DefensivePosition p in Positions)
            {
                Assert.AreEqual(ThrowProfile.ArmStrengthMph(p) - 8.0, RatingScale.ArmStrengthMph(0, p), 1e-12);
                Assert.AreEqual(ThrowProfile.ArmStrengthMph(p) + 8.0, RatingScale.ArmStrengthMph(100, p), 1e-12);
                Assert.Greater(RatingScale.Fielder(new PlayerRatings(reaction: 100), p).ReactionTime, 0.1);
                Assert.Less(RatingScale.Fielder(new PlayerRatings(reaction: 0), p).ReactionTime, 0.7);
            }
        }

        [Test]
        public void AnAverageRatedPlayerIsTheGenericProfile()
        {
            var avg = new PlayerRatings();
            RunnerProfile r = RatingScale.Runner(avg), s = RunnerProfile.Standard;
            Assert.AreEqual((s.MaxSpeed, s.AccelerationTime, s.ReadDelay, s.BatterStartDelay, s.RoundingSpeed), (r.MaxSpeed, r.AccelerationTime, r.ReadDelay, r.BatterStartDelay, r.RoundingSpeed));
            foreach (DefensivePosition p in Positions)
            {
                FielderProfile f = RatingScale.Fielder(avg, p), g = FielderProfile.For(p);
                Assert.AreEqual((g.ReactionTime, g.AccelerationTime, g.Reach), (f.ReactionTime, f.AccelerationTime, f.Reach), p.ToString());
                Assert.AreEqual(27.0 * 0.3048, f.MaxSpeed, 1e-12, "a fielder's top speed is his own sprint speed (27 ft/s at 50), not the position's");
                ThrowProfile t = RatingScale.Throw(avg, p), u = ThrowProfile.For(p);
                Assert.AreEqual(u.Speed, t.Speed, 1e-12, p.ToString());
                Assert.AreEqual(u.TransferTime, t.TransferTime, 1e-12, p.ToString());
                Assert.AreEqual(ThrowProfile.Full(p).Speed, RatingScale.FullThrow(avg, p).Speed, 1e-12);
            }
        }

        [Test]
        public void TheGenericTeamsAreFullyStaffedAndDifferent()
        {
            foreach (Team team in new[] { GenericRosters.Away(), GenericRosters.Home() })
            {
                Assert.IsTrue(team.FullyStaffed, team.Name);
                Assert.AreEqual(DefensivePosition.P, team.Pitcher.FieldingPosition);
                Assert.AreEqual(8, Enumerable.Range(1, 9).Count(s => team.Lineup[s].FieldingPosition.HasValue), "eight fielders and a DH bat");
                foreach (DefensivePosition p in Positions) Assert.IsNotNull(team.Fielder(p), $"{team.Name} {p}");
            }

            Team away = GenericRosters.Away();
            // Differentiated players: the centre fielder outruns the catcher; elite and poor defenders react differently.
            Assert.Greater(away.Fielder(DefensivePosition.CenterField).Ratings.Speed, away.Fielder(DefensivePosition.C).Ratings.Speed);
            Assert.Greater(away.Fielder(DefensivePosition.Shortstop).Ratings.Reaction, away.Fielder(DefensivePosition.LeftField).Ratings.Reaction);
            Assert.AreNotEqual(away.Pitcher.Ratings.Velocity, GenericRosters.Home().Pitcher.Ratings.Velocity, "a power and a command starter");
            Assert.Greater(Enumerable.Range(1, 9).Select(s => away.Lineup[s].Ratings.Power).Distinct().Count(), 2);
            Assert.Throws<ArgumentException>(() => new Team("x", away.Lineup, away.Lineup[1]), "the pitcher must play P");
        }

        [Test]
        public void ThePersonnelIsTheFieldingTeamAndTheRunners()
        {
            var game = new GameState();
            Team home = game.TeamOf(TeamSide.Home);
            PlayPersonnel p = game.Personnel;   // top 1: the home team fields
            FielderProfile cf = RatingScale.Fielder(home.Fielder(DefensivePosition.CenterField).Ratings, DefensivePosition.CenterField);
            Assert.AreEqual((cf.MaxSpeed, cf.ReactionTime), (p.Fielder(DefensivePosition.CenterField).MaxSpeed, p.Fielder(DefensivePosition.CenterField).ReactionTime));
            Assert.AreEqual(RatingScale.Throw(home.Fielder(DefensivePosition.ThirdBase).Ratings, DefensivePosition.ThirdBase).Speed, p.Throw(DefensivePosition.ThirdBase).Speed);
            Assert.AreEqual(RatingScale.Runner(game.Batter.Ratings).MaxSpeed, p.Runner(Runner.Batter).MaxSpeed, "the batter runs as himself");

            // A walk: the batter is on first and runs with his own ratings on the next play.
            PlayerProfile walked = game.Batter;
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreSame(walked, game.RunnerOn(Base.First));
            Assert.AreEqual(RatingScale.Runner(walked.Ratings).MaxSpeed, game.Personnel.Runner(new Runner(Base.First)).MaxSpeed);
            // A second walk forces him to second.
            PlayerProfile next = game.Batter;
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            Assert.AreEqual((walked, next), (game.RunnerOn(Base.Second), game.RunnerOn(Base.First)));
            // RESET restores them; the third out clears them.
            game.Pitch(PitchOutcome.Ball);
            game.ResetPlateAppearance();
            Assert.AreSame(walked, game.RunnerOn(Base.Second));
            for (int k = 0; k < 9; k++) game.Pitch(PitchOutcome.SwingingStrike);
            Assert.AreEqual(Half.Bottom, game.Half);
            Assert.IsNull(game.RunnerOn(Base.First));
            // In the bottom half the visitors field (their centre fielder is faster than the home team's).
            Team away = game.TeamOf(TeamSide.Away);
            Assert.AreNotEqual(away.Fielder(DefensivePosition.CenterField).Ratings.Speed, home.Fielder(DefensivePosition.CenterField).Ratings.Speed);
            Assert.AreEqual(RatingScale.Fielder(away.Fielder(DefensivePosition.CenterField).Ratings, DefensivePosition.CenterField).MaxSpeed,
                game.Personnel.Fielder(DefensivePosition.CenterField).MaxSpeed);
            Assert.AreEqual(RatingScale.Throw(away.Fielder(DefensivePosition.ThirdBase).Ratings, DefensivePosition.ThirdBase).Speed, game.Personnel.Throw(DefensivePosition.ThirdBase).Speed);
        }

        [Test]
        public void ResetPutsTheRunnersBack()
        {
            var game = new GameState();
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            PlayerProfile first = game.RunnerOn(Base.First), batter = game.Batter;
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);   // forces him to second
            Assert.AreEqual((batter, first), (game.RunnerOn(Base.First), game.RunnerOn(Base.Second)));
            game.ResetPlateAppearance();
            Assert.AreEqual((first, (PlayerProfile)null), (game.RunnerOn(Base.First), game.RunnerOn(Base.Second)), "the walk undone: back on first alone");
        }

        [Test]
        public void ThePlayDecidesWhoEndsUpWhere()
        {
            // A runner on first (walked), then a fielder's choice: he is forced at second, the batter is on first.
            var game = new GameState();
            for (int i = 0; i < 4; i++) game.Pitch(PitchOutcome.Ball);
            PlayerProfile walked = game.RunnerOn(Base.First), batter = game.Batter;
            LivePlay fc = Play(game, 60.0, -20.0, -40.0, -1000.0);
            Assert.AreEqual(PlayResultKind.FieldersChoice, PlayResults.Classify(fc));
            game.Apply(fc);
            Assert.AreEqual((batter, (PlayerProfile)null, (PlayerProfile)null), (game.RunnerOn(Base.First), game.RunnerOn(Base.Second), game.RunnerOn(Base.Third)));
            Assert.AreNotSame(walked, game.RunnerOn(Base.First));
            // A single with him on first: each runner where the play left him.
            var g2 = new GameState();
            for (int i = 0; i < 4; i++) g2.Pitch(PitchOutcome.Ball);
            PlayerProfile r1 = g2.RunnerOn(Base.First), hitter = g2.Batter;
            LivePlay single = Play(g2, 95.0, 6.0, 0.0, 700.0);
            g2.Apply(single);
            foreach (LiveRunner r in single.Runners.Where(r => !r.IsOut && !r.HasScored))
                Assert.AreSame(r.Id.IsBatter ? hitter : r1, g2.RunnerOn(r.LastTouched), $"{r.Id} on {r.LastTouched}");
        }

        [Test]
        public void APlayBelongsToThePlateAppearanceItWasMadeIn()
        {
            // A play made for one batter is refused once the game has moved on — even if the editor recreates the same outs
            // and bases — so its result can never land on another batter or other runners.
            var game = new GameState();
            LivePlay single = Play(game, 95.0, 6.0, 0.0, 700.0);
            for (int i = 0; i < 3; i++) game.Pitch(PitchOutcome.SwingingStrike);   // the batter strikes out instead
            game.Set(1, Half.Top, 0, BaseOccupancy.Empty, 0, 0);
            Assert.Throws<InvalidOperationException>(() => game.Apply(single));
            // Made now, it applies.
            game.Apply(Play(game, 95.0, 6.0, 0.0, 700.0));
            Assert.AreEqual(2, game.CompletedPlateAppearances);
        }

        [Test]
        public void ThePersonnelMustBeTheFieldersTheBallWasSolvedWith()
        {
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            FieldingPlay solved = FieldingSolver.Solve(ball, DefensiveAlignment.Standard, FielderProfile.For, FieldLayout.Standard);
            // Only the left fielder (not the primary) differs, and only in acceleration.
            var lf = new PlayerRatings(acceleration: 90);
            var other = new PlayPersonnel(p => p == DefensivePosition.LeftField ? RatingScale.Fielder(lf, p) : FielderProfile.For(p), ThrowProfile.For, ThrowProfile.Full, _ => RunnerProfile.Standard);
            Assert.AreNotEqual(DefensivePosition.LeftField, solved.Primary);
            Assert.Throws<ArgumentException>(() => new LivePlay(solved, new Situation(0, BaseOccupancy.Empty), personnel: other));
            Assert.DoesNotThrow(() => new LivePlay(solved, new Situation(0, BaseOccupancy.Empty), personnel: PlayPersonnel.Standard));
        }

        private static LivePlay Play(GameState game, double mph, double launch, double spray, double spin)
        {
            Situation s = game.Situation;
            PlayPersonnel personnel = game.Personnel;
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, personnel.Fielder, FieldLayout.Standard), s, personnel: personnel);
            play.RunToEnd();
            return play;
        }

        [Test]
        public void TheEditorsRunnersAreTheBattersAhead()
        {
            var game = new GameState();
            game.Set(1, Half.Top, 1, BaseOccupancy.Loaded, 0, 0);
            Lineup away = game.LineupOf(TeamSide.Away);
            Assert.AreEqual((away[9], away[8], away[7]), (game.RunnerOn(Base.First), game.RunnerOn(Base.Second), game.RunnerOn(Base.Third)), "standing in: the previous batters");
            game.Set(1, Half.Top, 1, new BaseOccupancy(false, true, false), 0, 0);
            Assert.IsNull(game.RunnerOn(Base.First));
            Assert.AreSame(away[8], game.RunnerOn(Base.Second), "the one still there stays");
            game.Set(1, Half.Top, 1, BaseOccupancy.Loaded, 0, 0);
            Assert.AreEqual(3, new[] { game.RunnerOn(Base.First), game.RunnerOn(Base.Second), game.RunnerOn(Base.Third) }.Distinct().Count(), "three different runners");
            // A walked batter on first, then the editor adds a runner on second: someone else.
            var g = new GameState();
            for (int i = 0; i < 4; i++) g.Pitch(PitchOutcome.Ball);
            g.Set(1, Half.Top, 0, new BaseOccupancy(true, true, false), 0, 0);
            Assert.AreNotSame(g.RunnerOn(Base.First), g.RunnerOn(Base.Second));
        }

        [Test]
        public void ALineupOnlyGamePlaysWithTheGenericProfiles()
        {
            PlayerProfile Make(int i) => new PlayerProfile($"X{i}", $"X {i}", BatterSide.Right, 73.0, "");
            var lineup = new Lineup("X", Enumerable.Range(1, 9).Select(Make).ToArray());
            var game = new GameState(lineup, lineup);
            PlayPersonnel p = game.Personnel;
            foreach (DefensivePosition pos in Positions)
            {
                Assert.AreEqual(FielderProfile.For(pos).MaxSpeed, p.Fielder(pos).MaxSpeed);
                Assert.AreEqual(ThrowProfile.For(pos).Speed, p.Throw(pos).Speed);
            }

            Assert.AreEqual(RunnerProfile.Standard.MaxSpeed, p.Runner(Runner.Batter).MaxSpeed, "an unrated (all-50) batter runs at the standard");
            // The same ball plays out identically with the game's personnel and with the generic one.
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(95.0, 6.0, 0.0, 700.0).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            game.Set(1, Half.Top, 0, new BaseOccupancy(true, false, true), 0, 0);
            Situation s = game.Situation;
            string Log(PlayPersonnel personnel)
            {
                var play = new LivePlay(FieldingSolver.Solve(ball, s.Alignment, personnel.Fielder, FieldLayout.Standard), s, personnel: personnel);
                play.RunToEnd();
                return string.Join("\n", play.Log.Select(e => $"{e.Time:R} {e.Text}"));
            }

            Assert.AreEqual(Log(PlayPersonnel.Standard), Log(game.Personnel));
        }

        [Test]
        public void EachRunnerRunsAsHimselfAndTrotsAsHimself()
        {
            var fast = new PlayerRatings(speed: 100);
            var slow = new PlayerRatings(speed: 0);
            var personnel = new PlayPersonnel(FielderProfile.For, ThrowProfile.For, ThrowProfile.Full, r => RatingScale.Runner(r.IsBatter ? slow : r.From == Base.First ? fast : new PlayerRatings()));
            LivePlay Play(double mph, double launch, double spray, double spin, BaseOccupancy bases)
            {
                var situation = new Situation(0, bases);
                BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
                var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, personnel.Fielder, FieldLayout.Standard), situation, personnel: personnel);
                play.RunToEnd();
                return play;
            }

            LivePlay single = Play(95.0, 6.0, 0.0, 700.0, new BaseOccupancy(true, false, false));
            Assert.AreEqual(RatingScale.Runner(fast).MaxSpeed, single.RunnerOf(new Runner(Base.First)).Profile.MaxSpeed);
            Assert.AreEqual(RatingScale.Runner(slow).MaxSpeed, single.RunnerOf(Runner.Batter).Profile.MaxSpeed);
            Assert.AreEqual(single.RunnerOf(Runner.Batter).Profile.MaxSpeed, single.Profile.MaxSpeed, "the play's profile is the batter's");
            LivePlay hr = Play(106.0, 28.0, -10.0, 2000.0, BaseOccupancy.Loaded);
            Assert.AreEqual(4, hr.AwardedBases);
            foreach (LiveRunner r in hr.Runners) Assert.AreEqual(personnel.Runner(r.Id).Trot().MaxSpeed, r.Profile.MaxSpeed, $"{r.Id} trots at his own pace");
            Assert.AreEqual(3, hr.Runners.Select(r => r.Profile.MaxSpeed).Distinct().Count());
        }

        [Test]
        public void TheDefensePlaysWithItsRatings()
        {
            // The same grounder to short: a strong, quick arm gets the ball to first sooner (through the live defense's throw).
            BallInPlay grounder = BallInPlaySimulation.Run(new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            LiveThrow FirstThrow(int arm)
            {
                var r = new PlayerRatings(armStrength: arm, transfer: arm);
                var personnel = new PlayPersonnel(FielderProfile.For, p => RatingScale.Throw(r, p), p => RatingScale.FullThrow(r, p), _ => RunnerProfile.Standard);
                var s = new Situation(0, BaseOccupancy.Empty);
                var play = new LivePlay(FieldingSolver.Solve(grounder, s.Alignment, personnel.Fielder, FieldLayout.Standard), s, personnel: personnel);
                play.RunToEnd();
                return play.Defense.Throws.First();
            }

            LiveThrow weak = FirstThrow(0), strong = FirstThrow(100);
            Assert.Less(strong.ReleaseTime, weak.ReleaseTime, "a quicker transfer");
            Assert.Less(strong.EndTime - strong.ReleaseTime, weak.EndTime - weak.ReleaseTime, "a faster throw");
            // A fly to the gap: a centre fielder with a quicker first step and more speed takes it sooner (the fielding solve).
            BallInPlay gapper = BallInPlaySimulation.Run(new BattedBallLaunch(90.0, 25.0, 12.0, 1800.0).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            double Arrive(int skill)
            {
                var r = new PlayerRatings(reaction: skill, speed: skill, acceleration: skill);
                FieldingPlay f = FieldingSolver.Solve(gapper, DefensiveAlignment.Standard, p => RatingScale.Fielder(r, p), FieldLayout.Standard);
                return f.Candidate(DefensivePosition.CenterField).Time;
            }

            Assert.Less(Arrive(100), Arrive(0));
            // A play solved with other fielders than its personnel is refused.
            var solvedGeneric = FieldingSolver.Solve(grounder, DefensiveAlignment.Standard, FielderProfile.For, FieldLayout.Standard);
            var rated = new PlayPersonnel(p => RatingScale.Fielder(new PlayerRatings(speed: 90, reaction: 90), p), ThrowProfile.For, ThrowProfile.Full, _ => RunnerProfile.Standard);
            Assert.Throws<ArgumentException>(() => new LivePlay(solvedGeneric, new Situation(0, BaseOccupancy.Empty), personnel: rated));
        }

        [Test]
        public void ASpeedierBatterReachesFirstSoonerOnTheSameGroundBall()
        {
            // Ratings change the simulation's inputs, not its result: the same ball, the same defense, a faster runner.
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(85.0, -8.0, -15.0, -1000.0).ToState(new Vector3d(0.0, 0.7, 0.8)), EnvironmentState.Standard, FieldLayout.Standard);
            var situation = new Situation(0, BaseOccupancy.Empty);
            double Arrival(int speed)
            {
                var personnel = new PlayPersonnel(FielderProfile.For, ThrowProfile.For, ThrowProfile.Full, _ => RatingScale.Runner(new PlayerRatings(speed: speed)));
                var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, personnel.Fielder, FieldLayout.Standard), situation, personnel: personnel);
                play.RunToEnd();
                return play.RunnerEta(play.RunnerOf(Runner.Batter), Base.First, play.ContactTime);
            }

            double slow = Arrival(10), average = Arrival(50), fast = Arrival(95);
            Assert.Less(fast, average);
            Assert.Less(average, slow);
            Assert.That(average - fast, Is.InRange(0.15, 0.6), "a fast runner gains a few tenths to first");
        }
    }
}
