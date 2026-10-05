using System.Collections.Generic;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Gameplay.Fielding
{
    /// <summary>A defender taking the ball: a fielded batted ball, a caught throw, a retrieved loose ball.</summary>
    public readonly struct BallTake
    {
        public BallTake(double time, DefensivePosition fielder, Vector3d ballPoint, FieldingAction action = FieldingAction.None)
        {
            Action = action;
            Time = time;
            Fielder = fielder;
            BallPoint = ballPoint;
        }

        public double Time { get; }
        public DefensivePosition Fielder { get; }
        /// <summary>The ball's centre at the take (where the glove meets it).</summary>
        public Vector3d BallPoint { get; }
        /// <summary>How he takes it (TASK-011.6).</summary>
        public FieldingAction Action { get; }
    }

    /// <summary>
    /// What presentation reads about a live defense (TASK-008): every defender's movement, the ball and who has it, and the
    /// list of takes and throws (any number of them) that drive the glove and the throwing arm. Exact functions of time.
    /// </summary>
    public interface IDefenseTimeline
    {
        /// <summary>When the play started (contact).</summary>
        double StartTime { get; }
        Vector3d FielderPositionAt(DefensivePosition p, double t);
        /// <summary>Ground velocity (m/s).</summary>
        Vector3d FielderVelocityAt(DefensivePosition p, double t);
        double FielderSpeedAt(DefensivePosition p, double t);
        double FielderDistanceAt(DefensivePosition p, double t);
        Vector3d FielderDirectionAt(DefensivePosition p, double t);
        Vector3d BallPositionAt(double t);
        BallAuthority AuthorityAt(double t);
        DefensivePosition? HolderAt(double t);
        IReadOnlyList<BallTake> Takes { get; }
        IReadOnlyList<LiveThrow> Throws { get; }
        /// <summary>The defender to keep framed with the ball: the holder, the receiver of a throw in the air, else whoever
        /// plays the ball (null before anyone does).</summary>
        DefensivePosition? FocusAt(double t);
    }
}
