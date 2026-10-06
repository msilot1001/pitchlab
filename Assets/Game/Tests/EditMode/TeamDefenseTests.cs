using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-008 team defense: every defender's role for the standard plays (Docs/DEFENSIVE_RESPONSIBILITIES.md), one primary
    /// and no swarm, throws only to covered bases, cut-off and relay throws as real sequential throws, reassignment as the
    /// play develops, and determinism.
    /// </summary>
    public class TeamDefenseTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);
        private const DefensivePosition P = DefensivePosition.P, C = DefensivePosition.C, B1 = DefensivePosition.FirstBase, B2 = DefensivePosition.SecondBase,
            B3 = DefensivePosition.ThirdBase, SS = DefensivePosition.Shortstop, LF = DefensivePosition.LeftField, CF = DefensivePosition.CenterField, RF = DefensivePosition.RightField;

        private static LivePlay Play(double mph, double launch, double spray, double spin, BaseOccupancy bases, int outs = 0)
        {
            var situation = new Situation(outs, bases);
            BallInPlay ball = BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard);
            var play = new LivePlay(FieldingSolver.Solve(ball, situation.Alignment, FielderProfile.For, FieldLayout.Standard), situation);
            play.RunToEnd();
            Assert.IsFalse(play.Log.Any(e => e.Text.Contains("time limit")), "the play ended by itself");
            return play;
        }

        private static readonly BaseOccupancy Empty = BaseOccupancy.Empty, OnFirst = new BaseOccupancy(true, false, false), OnSecond = new BaseOccupancy(false, true, false);
        private static LivePlay SsGrounder(BaseOccupancy b, int outs = 0) => Play(85.0, -8.0, -15.0, -1000.0, b, outs);
        private static LivePlay SecondBaseGrounder(BaseOccupancy b) => Play(80.0, -7.0, 18.0, -900.0, b);
        private static LivePlay ThirdBaseGrounder(BaseOccupancy b, int outs = 0) => Play(80.0, -7.0, -30.0, -900.0, b, outs);
        private static LivePlay FirstBaseRanging() => Play(95.0, -10.0, 24.0, -900.0, Empty);
        private static LivePlay LeftFieldSingle(BaseOccupancy b, int outs = 0) => Play(98.0, 9.0, -24.0, 900.0, b, outs);
        private static LivePlay RightFieldSingle(BaseOccupancy b, int outs = 0) => Play(95.0, 8.0, 25.0, 800.0, b, outs);
        private static LivePlay CenterFieldSingle(BaseOccupancy b, int outs = 0) => Play(95.0, 6.0, 0.0, 700.0, b, outs);
        private static LivePlay GapBall(BaseOccupancy b, int outs = 0) => Play(100.0, 20.0, -15.0, 1800.0, b, outs);
        private static LivePlay WallBall(BaseOccupancy b) => Play(105.0, 14.0, -20.0, 1800.0, b);
        private static readonly BaseOccupancy Loaded = BaseOccupancy.Loaded;

        private static double SinceTake(LivePlay play) => play.Fielding.PossessionTime - play.ContactTime + 0.01;

        /// <summary>Every play the team tests look at.</summary>
        private static LivePlay[] AllPlays() => new[]
        {
            SsGrounder(Empty), SsGrounder(OnFirst), SecondBaseGrounder(OnFirst), ThirdBaseGrounder(Empty), ThirdBaseGrounder(Loaded, 1), FirstBaseRanging(),
            LeftFieldSingle(Empty), LeftFieldSingle(OnSecond), RightFieldSingle(Empty), CenterFieldSingle(OnSecond, 2), GapBall(OnFirst, 2), WallBall(OnFirst),
        };

        private static RoleAssignment Role(LivePlay play, DefensivePosition p, double sinceContact) => play.Defense.RolesAt(play.ContactTime + sinceContact)[(int)p];

        private static void AssertRole(LivePlay play, DefensivePosition p, DefensiveRole role, Base? at, double since = 0.01) =>
            Assert.AreEqual((role, at), (Role(play, p, since).Role, Role(play, p, since).At), $"{p} at +{since:0.00}");

        // ---------------------------------------------------------------- infield

        [Test]
        public void ShortstopGrounderRoles()
        {
            LivePlay play = SsGrounder(Empty);
            AssertRole(play, SS, DefensiveRole.Primary, null);
            AssertRole(play, B1, DefensiveRole.CoverBase, Base.First);
            AssertRole(play, B2, DefensiveRole.CoverBase, Base.Second);
            AssertRole(play, B3, DefensiveRole.CoverBase, Base.Third);
            AssertRole(play, C, DefensiveRole.CoverBase, Base.Home);
            AssertRole(play, RF, DefensiveRole.BackupBase, Base.First);
            AssertRole(play, CF, DefensiveRole.BackupBase, Base.Second);
            AssertRole(play, LF, DefensiveRole.BackupBase, Base.Third);
            AssertRole(play, P, DefensiveRole.Hold, null);   // the outfielders back up the bases; he stays off them
            // The right fielder backs up first beyond the bag, and is closer to that spot than at the start when the throw
            // arrives.
            LiveThrow th = play.Defense.Throws[0];
            Assert.AreEqual((SS, B1, (Base?)Base.First), (th.Thrower, th.Receiver, th.Target));
            Vector3d ball = Flat(play.Fielding.Intercept.Ball.Position), bag = Flat(FieldLayout.BasePosition(Base.First)), backup = Flat(Role(play, RF, 0.01).Point);
            Assert.Greater((backup - ball).Length, (bag - ball).Length, "beyond first base");
            Assert.Less((Flat(play.Defense.FielderPositionAt(RF, th.Catch.Time)) - backup).Length, (Flat(play.Defense.FielderPositionAt(RF, play.ContactTime)) - backup).Length - 10.0, "on his way");
        }

        [Test]
        public void ThirdBaseGrounderRoles()
        {
            LivePlay play = ThirdBaseGrounder(Empty);
            AssertRole(play, B3, DefensiveRole.Primary, null);
            AssertRole(play, SS, DefensiveRole.CoverBase, Base.Third);
            AssertRole(play, B2, DefensiveRole.CoverBase, Base.Second);
            AssertRole(play, LF, DefensiveRole.BackupBase, Base.Third);
            AssertRole(play, RF, DefensiveRole.BackupBase, Base.First);
        }

        [Test]
        public void DoublePlayPivotByTheSideOfTheBall()
        {
            // Runner on first: a ball to the shortstop or third baseman — the second baseman covers second; a ball to the
            // second baseman — the shortstop does; the first baseman covers first.
            AssertRole(SsGrounder(OnFirst), B2, DefensiveRole.CoverBase, Base.Second);
            AssertRole(ThirdBaseGrounder(OnFirst), B2, DefensiveRole.CoverBase, Base.Second);
            LivePlay second = SecondBaseGrounder(OnFirst);
            AssertRole(second, SS, DefensiveRole.CoverBase, Base.Second);
            AssertRole(second, B1, DefensiveRole.CoverBase, Base.First);
        }

        [Test]
        public void PitcherCoversFirstWhenTheFirstBasemanPlaysTheBall()
        {
            LivePlay play = FirstBaseRanging();
            AssertRole(play, B1, DefensiveRole.Primary, null);
            AssertRole(play, P, DefensiveRole.CoverBase, Base.First);
            // The pitcher actually goes there.
            Vector3d bag = FieldLayout.BasePosition(Base.First);
            Vector3d start = play.Defense.FielderPositionAt(P, play.ContactTime), later = play.Defense.FielderPositionAt(P, play.ContactTime + 2.0);
            Assert.Less((later - bag).Length, (start - bag).Length - 5.0, "the pitcher runs to cover first");
        }

        [Test]
        public void BasesLoadedGrounderGoesHomeForTheLeadRunner()
        {
            // Bases loaded, one out, grounder to third: the force on the lead runner at home (5-2), the catcher on the plate;
            // then the catcher looks for a second out.
            LivePlay play = ThirdBaseGrounder(Loaded, 1);
            AssertRole(play, C, DefensiveRole.CoverBase, Base.Home);
            LiveThrow home = play.Defense.Throws[0];
            Assert.AreEqual((B3, C, (Base?)Base.Home), (home.Thrower, home.Receiver, home.Target));
            Assert.IsTrue(BaseTouch.IsTouching(play.Defense.FielderPositionAt(C, home.Catch.Time), Base.Home), "the catcher takes it on the plate");
            Assert.IsTrue(play.RulesEvents.Any(e => e.Kind == PlayEventKind.ForceOut && e.At == Base.Home), "force out at home");
            Assert.Greater(play.Defense.Throws.Count, 1, "and goes for two");
            Assert.AreEqual(C, play.Defense.Throws[1].Thrower);
        }

        [Test]
        public void TheThrowWaitsForTheCoverToReachTheBase()
        {
            // Runner on first, two out (normal depth), grounder to short: the second baseman is still running to second when
            // the shortstop could throw. The shortstop holds the ball until the throw arrives as the cover reaches the bag, and
            // the cover takes it on the bag (a force needs the foot on the base).
            LivePlay play = SsGrounder(OnFirst, 2);
            LiveThrow th = play.Defense.Throws.First();
            Assert.AreEqual((SS, B2, (Base?)Base.Second), (th.Thrower, th.Receiver, th.Target));
            Vector3d bag = Flat(FieldLayout.BasePosition(Base.Second));
            double ready = play.Fielding.PossessionTime + ThrowProfile.For(SS).TransferTime;
            Assert.Greater((Flat(play.Defense.FielderPositionAt(B2, ready)) - bag).Length, 1.0, "the base is uncovered when he could throw");
            Assert.Greater(th.ReleaseTime, ready + 0.05, "so he waits");
            Assert.IsTrue(th.Caught);
            Assert.Less((Flat(play.Defense.FielderPositionAt(B2, th.Catch.Time)) - bag).Length, 0.05, "caught on the bag");
            Assert.IsTrue(play.RulesEvents.Any(e => e.Kind == PlayEventKind.ForceOut && e.At == Base.Second), "the force at second");
        }

        // ---------------------------------------------------------------- outfield

        [Test]
        public void OutfieldSinglesHaveACutoffAndBackups()
        {
            LivePlay left = LeftFieldSingle(Empty);
            AssertRole(left, SS, DefensiveRole.Cutoff, Base.Second);
            AssertRole(left, B2, DefensiveRole.CoverBase, Base.Second);
            AssertRole(left, B1, DefensiveRole.CoverBase, Base.First);
            AssertRole(left, CF, DefensiveRole.BackupFielder, null);
            AssertRole(left, P, DefensiveRole.BackupBase, Base.Second);
            LivePlay right = RightFieldSingle(Empty);
            AssertRole(right, B2, DefensiveRole.Cutoff, Base.Second);
            AssertRole(right, SS, DefensiveRole.CoverBase, Base.Second);
            AssertRole(right, CF, DefensiveRole.BackupFielder, null);
            // The ball goes to the cut-off man, who has got to his spot by then.
            LiveThrow th = left.Defense.Throws[0];
            Assert.AreEqual((LF, SS, (Base?)null), (th.Thrower, th.Receiver, th.Target), "to the cut-off man");
            Assert.IsTrue(th.Caught);
            Assert.Less((Flat(left.Defense.FielderPositionAt(SS, th.ReleaseTime)) - Flat(Role(left, SS, 0.01).Point)).Length, 1.0, "on his spot at the release");
        }

        [Test]
        public void DeepBallsAreRelayedWithATrailer()
        {
            foreach (LivePlay play in new[] { GapBall(Empty), WallBall(OnFirst) })
            {
                DefensivePosition of = play.Fielding.Primary.Value;   // CF on the gap ball, LF off the wall
                Assert.IsTrue(DefensiveDecision.IsOutfielder(of));
                AssertRole(play, SS, DefensiveRole.Relay, Base.Second);
                AssertRole(play, B2, DefensiveRole.Trail, Base.Second);
                AssertRole(play, B1, DefensiveRole.CoverBase, Base.Second);   // 1B trails the batter-runner to second
                AssertRole(play, B3, DefensiveRole.CoverBase, Base.Third);
                if (play.Situation.Bases.First) continue;   // (with the runner on first the throw may go elsewhere)
                // The outfielder throws to the relay man, who catches it.
                LiveThrow th = play.Defense.Throws[0];
                Assert.AreEqual((of, SS, (Base?)null), (th.Thrower, th.Receiver, th.Target));
                Assert.IsTrue(th.Caught);
                // With the ball, the relay man is the one playing it; the first baseman stays at second (no flip back to first).
                double after = th.Catch.Time - play.ContactTime + 1e-6;
                AssertRole(play, SS, DefensiveRole.Primary, null, after);
                AssertRole(play, B1, DefensiveRole.CoverBase, Base.Second, after);
            }
        }

        [Test]
        public void ThrowHomeHasTheCatcherACutoffAndBackups()
        {
            // Runner on second, two out (he runs on contact), single to centre: the play is at home — the catcher covers it,
            // the first baseman is the cut-off (from centre), the pitcher backs up home, the second baseman covers first.
            LivePlay play = CenterFieldSingle(OnSecond, 2);
            double since = SinceTake(play);
            AssertRole(play, C, DefensiveRole.CoverBase, Base.Home, since);
            AssertRole(play, B1, DefensiveRole.Cutoff, Base.Home, since);
            AssertRole(play, P, DefensiveRole.BackupBase, Base.Home, since);
            AssertRole(play, B2, DefensiveRole.CoverBase, Base.First, since);
            AssertRole(play, SS, DefensiveRole.CoverBase, Base.Second, since);
            Vector3d plate = FieldLayout.BasePosition(Base.Home), cut = Role(play, B1, since).Point;
            Assert.AreEqual(DefensiveCoordinator.HomeCutoffDistance, (cut - plate).Length, 1e-6, "13 m up the line");
            // A close play: the centre fielder throws home (through the cut-off man's spot, uncut), the catcher tags the
            // runner — the third out, before he touches the plate: no run.
            LiveThrow th = play.Defense.Throws[0];
            Assert.AreEqual((CF, C, (Base?)Base.Home), (th.Thrower, th.Receiver, th.Target));
            Assert.IsFalse(play.Defense.Throws.Any(x => x.Receiver == B1), "not cut");
            Assert.IsTrue(play.RulesEvents.Any(e => e.Kind == PlayEventKind.TagOut && e.At == Base.Home));
            Assert.AreEqual(0, play.Runs);
        }

        [Test]
        public void TheRelayFollowsTheLeadRunner()
        {
            // Runner on first, ball off the wall in left: at contact the relay lines up for second; once the runner rounds
            // second for third it lines up for third; the left fielder throws to the relay man, who then plays the ball.
            LivePlay play = WallBall(OnFirst);
            AssertRole(play, SS, DefensiveRole.Relay, Base.Second);
            AssertRole(play, SS, DefensiveRole.Relay, Base.Third, SinceTake(play));
            AssertRole(play, B2, DefensiveRole.Trail, Base.Third, SinceTake(play));
            LiveThrow th = play.Defense.Throws[0];
            Assert.AreEqual((LF, SS, (Base?)null), (th.Thrower, th.Receiver, th.Target));
            Assert.IsTrue(th.Caught);
            AssertRole(play, SS, DefensiveRole.Primary, null, th.Catch.Time - play.ContactTime + 1e-6);
            // With two out the runner from first goes on contact on the gap ball: the play is at second, direct.
            LiveThrow gap = Play(100.0, 16.0, -12.0, 1800.0, OnFirst, 2).Defense.Throws[0];   // a gap liner that stays short of the wall
            Assert.AreEqual((CF, (Base?)Base.Second), (gap.Thrower, gap.Target), "a close play at second: direct");
        }

        [Test]
        public void PlayAtThirdLinesUpTheRelayForThird()
        {
            // Runner on first, two out, ball into the right-field corner: at contact the relay lines up for second (one base
            // ahead of the lead runner); when the runner rounds second for third, the second baseman (relay, right side)
            // and the shortstop (trail) line up for third and the third baseman covers it.
            LivePlay play = Play(102.0, 18.0, 35.0, 800.0, OnFirst, 2);
            AssertRole(play, B2, DefensiveRole.Relay, Base.Second);
            double since = SinceTake(play);
            AssertRole(play, B2, DefensiveRole.Relay, Base.Third, since);
            AssertRole(play, SS, DefensiveRole.Trail, Base.Third, since);
            AssertRole(play, B3, DefensiveRole.CoverBase, Base.Third, since);
            Assert.AreEqual(B2, play.Defense.Throws[0].Receiver, "the right fielder throws to the relay man");
        }

        // ---------------------------------------------------------------- the whole team

        [Test]
        public void OnePrimaryAndNoSwarm()
        {
            foreach (LivePlay play in AllPlays())
            {
                FieldingPlay f = play.Fielding;
                // At most one primary at any moment of the play.
                for (double t = play.ContactTime + 0.01; t < play.EndTime; t += 0.05)
                    Assert.LessOrEqual(play.Defense.RolesAt(t).Count(r => r.Role == DefensiveRole.Primary), 1, $"+{t - play.ContactTime:0.00}");
                // At the take nobody else has closed in on the ball: each is no nearer to it than his start or his own spot.
                Vector3d take = Flat(f.Intercept.Ball.Position);
                RoleAssignment[] roles = play.Defense.RolesAt(play.ContactTime + 0.01);
                foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                {
                    if (p == f.Primary) continue;
                    double start = (Flat(play.Defense.FielderPositionAt(p, play.ContactTime)) - take).Length;
                    double spot = (Flat(roles[(int)p].Point) - take).Length;
                    // A backup fielder runs behind the ball (past it); he must still stay clear of it.
                    if (roles[(int)p].Role == DefensiveRole.BackupFielder) spot = Math.Min(spot, 5.0);
                    Assert.GreaterOrEqual((Flat(play.Defense.FielderPositionAt(p, f.PossessionTime)) - take).Length, Math.Min(start, spot) - 1.0, $"{f.Primary} play: {p} ({roles[(int)p]})");
                }
            }
        }

        [Test]
        public void EveryBaseHasOneCovererThroughoutThePlay()
        {
            // Home, second and third always have exactly one coverer; first too, except on a relayed deep ball when the first
            // baseman has gone (the batter-runner is past it). A deep ball with a play at home sends the far outfielder to second.
            bool deepHome = false;
            foreach (LivePlay play in AllPlays().Concat(new[] { GapBall(OnSecond, 2), WallBall(new BaseOccupancy(true, true, false)) }))
                for (double t = play.ContactTime + 0.01; t < play.EndTime; t += 0.05)
                {
                    RoleAssignment[] roles = play.Defense.RolesAt(t);
                    bool relay = roles.Any(r => r.Role == DefensiveRole.Relay) || play.Defense.Throws.Any(x => x.Target == null && x.ReleaseTime <= t && roles[(int)x.Receiver].Role == DefensiveRole.Primary);
                    foreach (Base b in new[] { Base.First, Base.Second, Base.Third, Base.Home })
                    {
                        int n = roles.Count(r => r.Role == DefensiveRole.CoverBase && r.At == b);
                        if (b == Base.First && relay) Assert.LessOrEqual(n, 1, $"{b} at +{t - play.ContactTime:0.00}");
                        else Assert.AreEqual(1, n, $"{play.Fielding.Primary} play: {b} at +{t - play.ContactTime:0.00}");
                    }

                    if (roles.Any(r => r.Role == DefensiveRole.Relay && r.At == Base.Home))
                    {
                        deepHome = true;
                        Assert.IsTrue(DefensiveDecision.IsOutfielder(roles.First(r => r.Role == DefensiveRole.CoverBase && r.At == Base.Second).Position), "an outfielder covers second");
                    }
                }

            Assert.IsTrue(deepHome, "a relay home was exercised");
        }

        [Test]
        public void ThrowsAreTakenOnCoveredBases()
        {
            // Every throw to a base is caught by a fielder on the bag, or he carries it onto the bag at once.
            int checkedThrows = 0;
            foreach (LivePlay play in AllPlays())
                foreach (LiveThrow th in play.Defense.Throws)
                {
                    Assert.AreNotEqual(th.Thrower, th.Receiver);
                    Assert.IsTrue(th.Caught, $"{th.Thrower} → {th.Receiver}");
                    if (!(th.Target is Base b)) continue;
                    checkedThrows++;
                    bool onBag = BaseTouch.IsTouching(play.Defense.FielderPositionAt(th.Receiver, th.Catch.Time), b);
                    HolderDecision next = play.Defense.Decisions.FirstOrDefault(d => d.Holder == th.Receiver && d.Time >= th.Catch.Time - 1e-9);
                    bool carries = next != null && next.Chosen.Kind == LiveActionKind.Carry && next.Chosen.Target == b;
                    Assert.IsTrue(onBag || carries, $"{th.Receiver} at {b}");
                }

            Assert.GreaterOrEqual(checkedThrows, 6);
        }

        [Test]
        public void RolesChangeAsThePlayDevelops()
        {
            // Runner on second, single to left: at contact the shortstop is the cut-off for third (one base ahead of the lead
            // runner); once he has the ball from the left fielder he is the one playing it, and the third baseman stays on third.
            LivePlay play = LeftFieldSingle(OnSecond);
            AssertRole(play, SS, DefensiveRole.Cutoff, Base.Third);
            LiveThrow th = play.Defense.Throws[0];
            Assert.AreEqual((LF, SS), (th.Thrower, th.Receiver));
            AssertRole(play, SS, DefensiveRole.Primary, null, th.Catch.Time - play.ContactTime + 1e-6);
            AssertRole(play, B3, DefensiveRole.CoverBase, Base.Third, th.Catch.Time - play.ContactTime + 1e-6);
            // Runner on third, one out, single to centre: the cut-off for home is the first baseman, who heads for his spot
            // (~30 m from where he started) and takes the throw on the way.
            LivePlay home = CenterFieldSingle(new BaseOccupancy(false, false, true), 1);
            double since = SinceTake(home);
            AssertRole(home, B1, DefensiveRole.Cutoff, Base.Home, since);
            Vector3d cut = Flat(Role(home, B1, since).Point);
            LiveThrow toCut = home.Defense.Throws[0];
            Assert.AreEqual(B1, toCut.Receiver);
            double atStart = (Flat(home.Defense.FielderPositionAt(B1, home.ContactTime)) - cut).Length;
            Assert.Less((Flat(home.Defense.FielderPositionAt(B1, toCut.Catch.Time)) - cut).Length, atStart - 15.0);
            AssertRole(home, B1, DefensiveRole.Primary, null, toCut.Catch.Time - home.ContactTime + 1e-6);
        }

        [Test]
        public void SequentialThrowsUseRealFlightsAndPossession()
        {
            // Bases loaded, grounder to third: home for the force, then the catcher on to first (5-2-3): each throw leaves from
            // where the thrower holds the ball when he releases it, and the next one starts from the receiver who caught it.
            LivePlay play = ThirdBaseGrounder(Loaded, 1);
            Assert.GreaterOrEqual(play.Defense.Throws.Count, 2);
            for (int i = 1; i < play.Defense.Throws.Count; i++)
            {
                LiveThrow a = play.Defense.Throws[i - 1], b = play.Defense.Throws[i];
                Assert.AreEqual(a.Receiver, b.Thrower);
                Assert.GreaterOrEqual(b.ReleaseTime, a.Catch.Time + ThrowProfile.For(b.Thrower).TransferTime - 1e-9);
            }

            foreach (LiveThrow th in play.Defense.Throws)
            {
                Assert.AreEqual(BallAuthority.Possessed, play.Defense.AuthorityAt(th.ReleaseTime - 1e-6));
                Assert.AreEqual(BallAuthority.Thrown, play.Defense.AuthorityAt(th.ReleaseTime + 1e-6));
                Assert.AreEqual(th.Receiver, play.Defense.HolderAt(th.Catch.Time + 1e-6));
                Assert.Less((play.Defense.BallPositionAt(th.ReleaseTime + 1e-7) - play.Defense.BallPositionAt(th.ReleaseTime - 1e-7)).Length, 1e-3, "continuous at the release");
                Assert.Less((play.Defense.BallPositionAt(th.Catch.Time + 1e-7) - play.Defense.BallPositionAt(th.Catch.Time - 1e-7)).Length, 1e-3, "continuous at the catch");
            }
        }

        [Test]
        public void DefendersNeverTeleport()
        {
            foreach (LivePlay play in new[] { SsGrounder(OnFirst), LeftFieldSingle(OnSecond), CenterFieldSingle(OnSecond, 2), GapBall(OnFirst, 2), WallBall(OnFirst) })
                foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                    for (double t = play.ContactTime; t < play.EndTime; t += 0.002)
                    {
                        double step = (play.Defense.FielderPositionAt(p, t + 0.002) - play.Defense.FielderPositionAt(p, t)).Length;
                        Assert.Less(step, 0.002 * 12.0, $"{p} at +{t - play.ContactTime:0.000}");   // never faster than 12 m/s
                    }
        }

        [Test]
        public void SameDefenseEveryTime()
        {
            LivePlay a = CenterFieldSingle(OnSecond, 2), b = CenterFieldSingle(OnSecond, 2);
            Assert.AreEqual(a.Defense.Throws.Count, b.Defense.Throws.Count);
            for (double t = a.ContactTime; t < a.EndTime; t += 0.05)
                foreach (DefensivePosition p in Enum.GetValues(typeof(DefensivePosition)))
                    Assert.AreEqual(a.Defense.FielderPositionAt(p, t), b.Defense.FielderPositionAt(p, t));
            Assert.AreEqual(string.Join("|", a.Log.Select(e => e.Text)), string.Join("|", b.Log.Select(e => e.Text)));
        }

        private static Vector3d Flat(Vector3d v) => new Vector3d(v.X, v.Y, 0.0);
    }
}
