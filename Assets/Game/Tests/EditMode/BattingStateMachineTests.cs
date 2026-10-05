using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Tests
{
    /// <summary>The batting loop's states over time for a hit, a miss and a take (TASK-004.6B-2).</summary>
    public class BattingStateMachineTests
    {
        private static readonly HittingPitch Pitch = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
        private static readonly SwingParameters Swing = SwingParameters.Default;

        private static BattingState At(double t, SwingInput? swing, ContactResult? result, BallInPlay play) =>
            BattingStateMachine.At(t, Pitch, swing, result, play?.EndTime ?? double.NaN, Swing.SwingDuration);

        [Test]
        public void NoPitchIsReady() => Assert.AreEqual(BattingState.Ready, BattingStateMachine.At(5.0, null, null, null, double.NaN, 0.15));

        [Test]
        public void HitGoesThroughEveryStateInOrder()
        {
            var ball = Pitch.IdealContactState.Position;
            var swing = new SwingInput(Pitch.IdealContactTime - Swing.SwingDuration, ball.X, ball.Z - 0.01);
            ContactResult result = ContactResolver.Resolve(Pitch, swing, Swing);
            Assert.IsTrue(result.IsContact);
            BallInPlay play = BallInPlaySimulation.Run(result.BattedBall, EnvironmentState.Standard, FieldLayout.Standard);

            Assert.AreEqual(BattingState.Windup, At(-0.5, null, null, null));
            Assert.AreEqual(BattingState.PitchInFlight, At(0.1, null, null, null), "before the swing is known");
            Assert.AreEqual(BattingState.PitchInFlight, At(swing.StartTime - 0.01, swing, result, play));
            Assert.AreEqual(BattingState.Swinging, At(swing.StartTime + 0.01, swing, result, play));
            Assert.AreEqual(BattingState.BallInPlay, At(result.BattedBall.Time + 0.01, swing, result, play));
            Assert.AreEqual(BattingState.BallInPlay, At(play.EndTime - 0.01, swing, result, play));
            Assert.AreEqual(BattingState.Result, At(play.EndTime + 0.01, swing, result, play));
            Assert.AreEqual(BattingState.Ready, At(play.EndTime + BattingStateMachine.ResultPause + 0.01, swing, result, play));
        }

        [Test]
        public void PlayEndsAtPossessionNotAtRest()
        {
            // TASK-005: a defender taking the ball ends the play even though the free ball would still be rolling.
            var ball = Pitch.IdealContactState.Position;
            var swing = new SwingInput(Pitch.IdealContactTime - Swing.SwingDuration, ball.X, ball.Z - 0.01);
            ContactResult result = ContactResolver.Resolve(Pitch, swing, Swing);
            double possession = result.BattedBall.Time + 1.0;
            Assert.AreEqual(BattingState.BallInPlay, BattingStateMachine.At(possession - 0.01, Pitch, swing, result, possession, Swing.SwingDuration));
            Assert.AreEqual(BattingState.Result, BattingStateMachine.At(possession + 0.01, Pitch, swing, result, possession, Swing.SwingDuration));
            Assert.AreEqual(BattingState.Ready, BattingStateMachine.At(possession + BattingStateMachine.ResultPause + 0.01, Pitch, swing, result, possession, Swing.SwingDuration));
        }

        [Test]
        public void MissShowsTheResultAfterThePitchEnds()
        {
            var ball = Pitch.IdealContactState.Position;
            var swing = new SwingInput(Pitch.IdealContactTime - Swing.SwingDuration, ball.X, ball.Z - 0.3);
            ContactResult result = ContactResolver.Resolve(Pitch, swing, Swing);
            Assert.IsFalse(result.IsContact);
            Assert.AreEqual(BattingState.Swinging, At(Pitch.Flight.Duration - 0.01, swing, result, null));
            Assert.AreEqual(BattingState.Result, At(Pitch.Flight.Duration + 0.01, swing, result, null));
            Assert.AreEqual(BattingState.Ready, At(Pitch.Flight.Duration + BattingStateMachine.ResultPause + 0.01, swing, result, null));
        }

        [Test]
        public void BoundariesAreExact()
        {
            var ball = Pitch.IdealContactState.Position;
            var swing = new SwingInput(Pitch.IdealContactTime - Swing.SwingDuration, ball.X, ball.Z - 0.01);
            ContactResult result = ContactResolver.Resolve(Pitch, swing, Swing);
            BallInPlay play = BallInPlaySimulation.Run(result.BattedBall, EnvironmentState.Standard, FieldLayout.Standard);
            Assert.AreEqual(BattingState.PitchInFlight, At(0.0, null, null, null), "a press exactly at release swings");
            Assert.AreEqual(BattingState.Swinging, At(result.BattedBall.Time - 1e-9, swing, result, play));
            Assert.AreEqual(BattingState.Result, At(play.EndTime + BattingStateMachine.ResultPause - 1e-9, swing, result, play));
        }

        [Test]
        public void LateMissIsStillSwingingAfterThePitchEnds()
        {
            // A swing that starts 20 ms before the pitch ends is still swinging after it (no result shown mid-swing).
            var swing = new SwingInput(Pitch.Flight.Final.Time - 0.02, 0.0, 0.8);
            ContactResult result = ContactResolver.Resolve(Pitch, swing, Swing);
            Assert.IsFalse(result.IsContact);
            Assert.AreEqual(BattingState.Swinging, At(Pitch.Flight.Final.Time + 0.05, swing, result, null));
            Assert.AreEqual(BattingState.Result, At(swing.StartTime + Swing.SwingDuration + 0.001, swing, result, null));
        }

        [Test]
        public void TakeShowsTheResultAfterThePitchEnds()
        {
            Assert.AreEqual(BattingState.PitchInFlight, At(Pitch.Flight.Duration - 0.01, null, null, null));
            Assert.AreEqual(BattingState.Result, At(Pitch.Flight.Duration + 0.01, null, null, null));
            Assert.AreEqual(BattingState.Ready, At(Pitch.Flight.Duration + BattingStateMachine.ResultPause + 0.01, null, null, null));
        }
    }

    /// <summary>Compact feedback words derived from gameplay results (TASK-004.6B-2).</summary>
    public class ContactFeedbackTests
    {
        private static ContactResult Result(double timing, double along, ContactOutcome outcome = ContactOutcome.Contact) =>
            new ContactResult(outcome, timing, along, 0.0, 0.2, default);

        [TestCase(0.0, "on time")]
        [TestCase(0.005, "on time")]
        [TestCase(0.007, "on time")]     // the resolver's Good window is ±7 ms
        [TestCase(-0.012, "early 12 ms")]
        [TestCase(0.008, "late 8 ms")]
        public void TimingWords(double error, string expected) => Assert.AreEqual(expected, ContactFeedback.Timing(Result(error, 0.0)));

        [Test]
        public void QualityFromTheResolversOwnOffsets()
        {
            // Aim 8 cm toward third base of the ball: the ball meets the bat toward its tip for a righty (tip toward first
            // base) — off the end; for a lefty the same offset is toward the hands — jammed.
            HittingPitch pitch = HittingPitch.Create(PitchPresets.FourSeam, EnvironmentState.Standard);
            var ball = pitch.IdealContactState.Position;
            SwingParameters right = SwingParameters.Default, left = SwingParameters.Default;
            left.Side = BatterSide.Left;
            var swing = new SwingInput(pitch.IdealContactTime - right.SwingDuration, ball.X - 0.08, ball.Z - 0.01);
            Assert.AreEqual(ContactQuality.OffTheEnd, ContactFeedback.Quality(ContactResolver.Resolve(pitch, swing, right), right));
            Assert.AreEqual(ContactQuality.Jammed, ContactFeedback.Quality(ContactResolver.Resolve(pitch, swing, left), left));
            var flush = new SwingInput(swing.StartTime, ball.X, ball.Z - 0.01);
            Assert.AreEqual(ContactQuality.Barrel, ContactFeedback.Quality(ContactResolver.Resolve(pitch, flush, right), right));
        }

        [Test]
        public void QualityFollowsTheBarrelSideForEachHand()
        {
            SwingParameters right = SwingParameters.Default;
            Assert.AreEqual(ContactQuality.Barrel, ContactFeedback.Quality(Result(0.0, 0.02), right));
            Assert.AreEqual(ContactQuality.OffTheEnd, ContactFeedback.Quality(Result(0.0, 0.08), right), "tip toward first base for a righty");
            Assert.AreEqual(ContactQuality.Jammed, ContactFeedback.Quality(Result(0.0, -0.08), right));
            Assert.IsNull(ContactFeedback.Quality(Result(0.0, 0.0, ContactOutcome.MissUnder), right));
            SwingParameters left = SwingParameters.Default;
            left.Side = BatterSide.Left;
            Assert.AreEqual(ContactQuality.Jammed, ContactFeedback.Quality(Result(0.0, 0.08), left), "tip toward third base for a lefty");
            Assert.AreEqual(ContactQuality.OffTheEnd, ContactFeedback.Quality(Result(0.0, -0.08), left));
        }
    }
}
