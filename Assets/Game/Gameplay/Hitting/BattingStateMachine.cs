using System;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Hitting
{
    /// <summary>One at-bat's progress (TASK-004.6B-2).</summary>
    public enum BattingState
    {
        /// <summary>Waiting for the throw press.</summary>
        Ready,
        /// <summary>Pitcher's delivery: before release. Swing presses are ignored (wind-up policy B).</summary>
        Windup,
        /// <summary>Pitch released, no swing yet: a press swings.</summary>
        PitchInFlight,
        /// <summary>Swing started, before its contact time.</summary>
        Swinging,
        /// <summary>Batted ball in play: bounces, rolls, until rest or out of play.</summary>
        BallInPlay,
        /// <summary>Outcome shown (contact result, miss or take) for the result pause; then Ready.</summary>
        Result,
    }

    /// <summary>
    /// The batting loop as an explicit state machine: Ready → Windup → PitchInFlight → (Swinging → BallInPlay | take/miss)
    /// → Result → (pause) → Ready. The state is a pure function of the authoritative pitch, swing, contact result and ball
    /// in play at a simulation time, so it is identical at any frame rate and needs no per-frame bookkeeping.
    /// </summary>
    public static class BattingStateMachine
    {
        /// <summary>How long the outcome stays up before the loop returns to Ready (s, simulation time). A press during the
        /// result or the play throws the next pitch at once (it skips the rest).</summary>
        public const double ResultPause = 1.5;

        /// <param name="simTime">Seconds after release (negative during the delivery).</param>
        /// <param name="swingDuration">Launch → contact of the swing (SwingParameters.SwingDuration): a miss ends with it.</param>
        public static BattingState At(double simTime, HittingPitch pitch, SwingInput? swing, ContactResult? result, BallInPlay play, double swingDuration)
        {
            if (pitch == null) return BattingState.Ready;
            if (simTime < 0.0) return BattingState.Windup;
            double outcomeTime = OutcomeTime(pitch, swing, result, play, swingDuration);
            if (!swing.HasValue)
                return simTime < pitch.Flight.Final.Time ? BattingState.PitchInFlight : simTime < outcomeTime + ResultPause ? BattingState.Result : BattingState.Ready;
            if (simTime < swing.Value.StartTime) return BattingState.PitchInFlight;
            if (result is ContactResult r && r.IsContact && play != null)
            {
                if (simTime < r.BattedBall.Time) return BattingState.Swinging;
                if (simTime < play.EndTime) return BattingState.BallInPlay;
            }
            else if (simTime < outcomeTime) return BattingState.Swinging;

            return simTime < outcomeTime + ResultPause ? BattingState.Result : BattingState.Ready;
        }

        /// <summary>When the outcome is decided and shown: the ball at rest (or out of play) after contact; the end of the
        /// pitch for a take or a miss.</summary>
        public static double OutcomeTime(HittingPitch pitch, SwingInput? swing, ContactResult? result, BallInPlay play, double swingDuration)
        {
            if (result is ContactResult r && r.IsContact && play != null) return play.EndTime;
            // A miss reads once both the pitch and the swing are over (a late swing can outlast the pitch).
            return swing.HasValue ? Math.Max(pitch.Flight.Final.Time, swing.Value.StartTime + swingDuration) : pitch.Flight.Final.Time;
        }
    }
}
