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
    /// TASK-011.6: how a defender takes the ball is classified from the take's geometry and timing (never from animation), and
    /// the one action with a gameplay consequence — the diving catch — is a narrow, explicit envelope with a recovery cost.
    /// </summary>
    public class FieldingActionTests
    {
        private static readonly Vector3d Contact = new Vector3d(0.0, 0.7, 0.8);

        private static FieldingPlay Field(double mph, double launch, double spray, double spin) =>
            FieldingSolver.Solve(BallInPlaySimulation.Run(new BattedBallLaunch(mph, launch, spray, spin).ToState(Contact), EnvironmentState.Standard, FieldLayout.Standard));

        [TestCase(50.0, -25.0, -30.0, -900.0, FieldingAction.ChargingPickup, DefensivePosition.ThirdBase, TestName = "Slow roller: charging")]
        [TestCase(50.0, -8.0, -40.0, -900.0, FieldingAction.BackhandPickup, DefensivePosition.ThirdBase, TestName = "Backhand")]
        [TestCase(80.0, -8.0, 38.0, -900.0, FieldingAction.ForehandPickup, DefensivePosition.FirstBase, TestName = "Forehand")]
        [TestCase(85.0, -8.0, -15.0, -1000.0, FieldingAction.CenteredPickup, DefensivePosition.Shortstop, TestName = "Centred grounder")]
        [TestCase(60.0, 10.0, -20.0, 1500.0, FieldingAction.SlidingCatch, DefensivePosition.ThirdBase, TestName = "Sliding catch")]
        [TestCase(80.0, 22.0, -40.0, 1500.0, FieldingAction.DivingCatch, DefensivePosition.LeftField, TestName = "Diving catch")]
        [TestCase(60.0, 16.0, -30.0, 1500.0, FieldingAction.OverShoulderCatch, DefensivePosition.ThirdBase, TestName = "Over the shoulder")]
        [TestCase(92.0, 32.0, 0.0, 2400.0, FieldingAction.StandingCatch, DefensivePosition.CenterField, TestName = "Routine fly")]
        [TestCase(90.0, 20.0, 15.0, 1800.0, FieldingAction.RunningCatch, DefensivePosition.RightField, TestName = "Running catch")]
        public void ActionFollowsTheTakesGeometry(double mph, double launch, double spray, double spin, FieldingAction action, DefensivePosition who)
        {
            FieldingPlay f = Field(mph, launch, spray, spin);
            Assert.AreEqual(who, f.Primary);
            Assert.AreEqual(action, f.Action);
            // The classification is a pure function of the authoritative take.
            FielderMotion m = f.Motion(who);
            Assert.AreEqual(action, FieldingActions.Classify(f.Ball, f.Intercept, m.PositionAt(f.Intercept.Time), m.VelocityAt(f.Intercept.Time), FieldLayout.Standard));
        }

        [Test]
        public void ActionCriteriaAreObjective()
        {
            // A sliding catch is low and at speed; a jumping catch is high; a dive is the dive envelope; a running catch is moving.
            foreach (var (mph, launch, spray, spin) in new[] { (60.0, 10.0, -20.0, 1500.0), (95.0, 30.0, -10.0, 2200.0), (80.0, 22.0, -40.0, 1500.0), (90.0, 20.0, 15.0, 1800.0) })
            {
                FieldingPlay f = Field(mph, launch, spray, spin);
                FielderMotion m = f.Motion(f.Primary.Value);
                double speed = m.VelocityAt(f.Intercept.Time).Length, z = f.Intercept.Ball.Position.Z;
                switch (f.Action)
                {
                    case FieldingAction.SlidingCatch:
                        Assert.Less(z, FieldingActions.SlideHeight);
                        Assert.GreaterOrEqual(speed, FieldingActions.SlideSpeed);
                        break;
                    case FieldingAction.JumpingCatch:
                        Assert.Greater(z, FieldingActions.JumpHeight);
                        break;
                    case FieldingAction.DivingCatch:
                        Assert.IsTrue(f.Intercept.Dive);
                        break;
                    case FieldingAction.RunningCatch:
                        Assert.GreaterOrEqual(speed, FieldingActions.RunningSpeed);
                        break;
                }
            }
        }

        [Test]
        public void ADiveOnlyWhenNobodyCanCatchItNormally()
        {
            FieldingPlay f = Field(80.0, 22.0, -40.0, 1500.0);
            Assert.IsTrue(f.Intercept.Dive);
            Assert.AreEqual(InterceptKind.FlyCatch, f.Intercept.Kind);
            // Within the dive envelope: low, after a run-up, the ball's ground point at most the running reach + DiveReach away.
            Assert.That(f.Intercept.Ball.Position.Z, Is.InRange(InterceptSolver.DiveLow, InterceptSolver.DiveHigh));
            Assert.GreaterOrEqual(f.Intercept.RouteDistance, InterceptSolver.DiveRunUp);
            FieldingPlay normal = Field(92.0, 32.0, 0.0, 2400.0);
            Assert.IsFalse(normal.Intercept.Dive, "a routine fly is caught standing, never dived for");
            // Without the dive nobody catches it: every defender's own intercept is not a catch.
            foreach (DefensivePosition p in System.Enum.GetValues(typeof(DefensivePosition)))
                if (p != f.Primary)
                    Assert.AreNotEqual(InterceptKind.FlyCatch, f.Candidate(p).Kind, $"{p} could catch it normally");
        }

        [Test]
        public void TheDiveIsConservativeAcrossBattedBalls()
        {
            // Over a grid of batted balls, dives turn only a small share of fielded balls into catches (no defensive buff).
            int fielded = 0, dives = 0;
            foreach (double mph in new[] { 60.0, 70.0, 80.0, 90.0, 100.0 })
                foreach (double launch in new[] { 4.0, 10.0, 16.0, 22.0, 30.0 })
                    foreach (double spray in new[] { -40.0, -20.0, 0.0, 20.0, 40.0 })
                    {
                        FieldingPlay f = Field(mph, launch, spray, 1500.0);
                        if (f.Outcome != FieldingOutcome.Fielded) continue;
                        fielded++;
                        if (f.Intercept.Dive) dives++;
                    }

            Assert.Greater(dives, 0, "the dive exists");
            Assert.Less((double)dives / fielded, 0.05, "and stays rare");
        }

        [Test]
        public void TheDiverGetsUpBeforeHeThrows()
        {
            // The recovery is a real cost: the throw is ready transfer + DiveRecovery after the catch.
            FieldingPlay f = Field(80.0, 22.0, -40.0, 1500.0);
            var play = new LivePlay(f, new Situation(0, new BaseOccupancy(true, false, false)));
            play.RunToEnd();
            LiveThrow th = play.Defense.Throws.FirstOrDefault(x => x.Thrower == f.Primary);
            Assert.IsNotNull(th, "he throws the ball in");
            Assert.GreaterOrEqual(th.ReleaseTime, f.PossessionTime + ThrowProfile.For(f.Primary.Value).TransferTime + FieldingActions.DiveRecovery - 1e-9);
        }
    }
}
