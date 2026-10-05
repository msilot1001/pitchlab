using System;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Running;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Play
{
    /// <summary>What a fair ball's play came to for the batter (TASK-015).</summary>
    public enum PlayResultKind
    {
        Single,
        Double,
        Triple,
        HomeRun,
        InsideTheParkHomeRun,
        GroundRuleDouble,
        LineOut,
        FlyOut,
        PopOut,
        Groundout,
        SacrificeFly,
        FieldersChoice,
        DoublePlay,
        TriplePlay,
    }

    /// <summary>
    /// A plain description of a finished fair-ball play from its authoritative record — the award, the rules events (outs,
    /// on whom), the batter-runner's last base, the runs and the ball's launch angle. Descriptive, not official scoring: no
    /// errors or scorer's judgement (OBR 9.x), and a hit is credited by where the batter ended up.
    /// </summary>
    public static class PlayResults
    {
        /// <summary>Caught balls below this launch angle (°) are line outs, above <see cref="PopUpAngle"/> pop outs (the
        /// Statcast batted-ball type boundaries: line drive 10–25°, fly ball 25–50°, pop-up above 50°).</summary>
        public const double LineDriveAngle = 25.0, PopUpAngle = 50.0;

        public static PlayResultKind Classify(LivePlay play)
        {
            if (play == null) throw new ArgumentNullException(nameof(play));
            if (!play.IsOver) throw new InvalidOperationException("Classify a play once it is over.");
            if (play.IsFoul) throw new ArgumentException("A foul is not a play result.", nameof(play));
            if (play.AwardedBases == 4) return PlayResultKind.HomeRun;
            if (play.AwardedBases == 2) return PlayResultKind.GroundRuleDouble;

            int outs = play.OutsMade;
            if (outs >= 3) return PlayResultKind.TriplePlay;
            if (outs == 2) return PlayResultKind.DoublePlay;

            PlayEvent? batterOut = null;
            foreach (PlayEvent e in play.RulesEvents)
                if (e.IsOut && e.Runner.IsBatter) batterOut = e;

            if (batterOut is PlayEvent o)
            {
                if (o.Kind != PlayEventKind.FlyOut) return PlayResultKind.Groundout;
                // A fly caught with fewer than two out on which a runner scores (OBR 9.08(d)).
                if (play.Situation.Outs < 2 && play.Runs > 0) return PlayResultKind.SacrificeFly;
                double angle = LaunchAngle(play);
                return angle < LineDriveAngle ? PlayResultKind.LineOut : angle > PopUpAngle ? PlayResultKind.PopOut : PlayResultKind.FlyOut;
            }

            // The batter reached: a fielder's choice if the defense retired another runner on a ground ball instead.
            if (outs == 1 && play.Kind == LivePlay.BallKind.Grounder) return PlayResultKind.FieldersChoice;
            LiveRunner batter = play.RunnerOf(Runner.Batter);
            if (batter == null) return PlayResultKind.Single;
            if (batter.HasScored) return PlayResultKind.InsideTheParkHomeRun;
            return batter.LastTouched switch
            {
                Base.Third => PlayResultKind.Triple,
                Base.Second => PlayResultKind.Double,
                _ => PlayResultKind.Single,
            };
        }

        public static string Describe(PlayResultKind kind) => kind switch
        {
            PlayResultKind.Single => "single",
            PlayResultKind.Double => "double",
            PlayResultKind.Triple => "triple",
            PlayResultKind.HomeRun => "home run",
            PlayResultKind.InsideTheParkHomeRun => "inside-the-park home run",
            PlayResultKind.GroundRuleDouble => "ground-rule double",
            PlayResultKind.LineOut => "line out",
            PlayResultKind.FlyOut => "fly out",
            PlayResultKind.PopOut => "pop out",
            PlayResultKind.Groundout => "groundout",
            PlayResultKind.SacrificeFly => "sacrifice fly",
            PlayResultKind.FieldersChoice => "fielder's choice",
            PlayResultKind.DoublePlay => "double play",
            _ => "triple play",
        };

        /// <summary>The batted ball's launch angle (°) off the bat.</summary>
        public static double LaunchAngle(LivePlay play)
        {
            Vector3d v = play.Fielding.Ball.First.Velocity;
            return Units.RadiansToDegrees(Math.Atan2(v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y)));
        }
    }
}
