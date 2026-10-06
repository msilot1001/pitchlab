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
            // In the bottom half the visitors field.
            Team away = game.TeamOf(TeamSide.Away);
            Assert.AreEqual(RatingScale.Fielder(away.Fielder(DefensivePosition.Shortstop).Ratings, DefensivePosition.Shortstop).ReactionTime,
                game.Personnel.Fielder(DefensivePosition.Shortstop).ReactionTime);
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
