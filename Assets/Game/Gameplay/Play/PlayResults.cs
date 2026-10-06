using System;
using Pitchlab.Gameplay.Fielding;
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
        /// <summary>A bunt with fewer than two out on which the batter is put out and every other runner advances (OBR 9.08(a);
        /// TASK-025).</summary>
        SacrificeBunt,
    }

    /// <summary>
    /// A plain description of a finished fair-ball play from its authoritative record — the award, the rules events (outs,
    /// on whom), the batter-runner's last base, the runs and the ball's launch angle. Descriptive, not official scoring: no
    /// errors or scorer's judgement (OBR 9.x): a hit is credited by where the batter ended up (an advance on a throw counts
    /// as part of it), a double or triple play is labelled as such even when the batter also hit or a run scored on a fly,
    /// and a failed attempt on another runner is not a fielder's choice.
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
            // A fair ball bouncing over the fence. (A misplayed ball out of play is a two-base award for every runner, not a
            // ground-rule double: the batter's result is where he ends, labelled with the misplay — TASK-021.)
            if (play.AwardedBases == 2) return PlayResultKind.GroundRuleDouble;

            int outs = play.OutsMade;
            if (outs >= 3) return PlayResultKind.TriplePlay;
            if (outs == 2) return PlayResultKind.DoublePlay;

            LiveRunner batter = play.RunnerOf(Runner.Batter) ?? throw new InvalidOperationException("A fair ball has a batter-runner.");
            PlayEvent? batterOut = null, otherOut = null;
            foreach (PlayEvent e in play.RulesEvents)
                if (e.IsOut)
                {
                    if (e.Runner.IsBatter) batterOut = e;
                    else otherOut = e;
                }

            if (batterOut is PlayEvent o)
            {
                if (o.Kind == PlayEventKind.FlyOut)
                {
                    // A fly an outfielder caught with fewer than two out, on which a runner scores (OBR 9.08(d)).
                    if (play.Situation.Outs < 2 && play.Runs > 0 && DefensiveDecision.IsOutfielder(play.Fielding.Primary.Value)) return PlayResultKind.SacrificeFly;
                    double angle = LaunchAngle(play);
                    return angle < LineDriveAngle ? PlayResultKind.LineOut : angle > PopUpAngle ? PlayResultKind.PopOut : PlayResultKind.FlyOut;
                }

                // Put out after reaching a base (stretching a hit): credited with the bases he reached (OBR 9.06(d)).
                return batter.LastTouched == Base.Home ? PlayResultKind.Groundout : Hit(batter.LastTouched);
            }

            // The batter reached first while the defense retired another runner on a grounder or by a force: a fielder's
            // choice, not a hit (OBR 9.05(b)(1), Definitions "Fielder's Choice").
            if (otherOut is PlayEvent other && batter.LastTouched == Base.First && (play.Kind == LivePlay.BallKind.Grounder || other.Kind == PlayEventKind.ForceOut))
                return PlayResultKind.FieldersChoice;
            return batter.HasScored ? PlayResultKind.InsideTheParkHomeRun : Hit(batter.LastTouched);
        }

        private static PlayResultKind Hit(Base reached) => reached switch
        {
            Base.Third => PlayResultKind.Triple,
            Base.Second => PlayResultKind.Double,
            _ => PlayResultKind.Single,
        };

        private static readonly string[] Upper = Array.ConvertAll((PlayResultKind[])Enum.GetValues(typeof(PlayResultKind)), k => Describe(k).ToUpperInvariant());

        /// <summary><see cref="Describe"/> in capitals (cached: no allocation).</summary>
        public static string DescribeUpper(PlayResultKind kind) => Upper[(int)kind];

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
            PlayResultKind.SacrificeBunt => "sacrifice bunt",
            _ => "triple play",
        };

        /// <summary>
        /// The result of a bunt (TASK-025): a groundout with fewer than two out on which no other runner is put out and at least
        /// one advances is a sacrifice (OBR 9.08(a); descriptive — the scorer's judgement of a bunt for a hit is not modelled).
        /// </summary>
        public static PlayResultKind Bunted(PlayResultKind kind, LivePlay play)
        {
            if (kind != PlayResultKind.Groundout || play.Situation.Outs >= 2) return kind;
            bool advanced = false;
            foreach (LiveRunner r in play.Runners)
            {
                if (r.Id.IsBatter) continue;
                if (r.IsOut) return kind;
                if (r.HasScored || r.LastTouched > r.Id.From) advanced = true;
            }

            return advanced ? PlayResultKind.SacrificeBunt : kind;
        }

        /// <summary>The batted ball's launch angle (°) off the bat.</summary>
        public static double LaunchAngle(LivePlay play)
        {
            Vector3d v = play.Fielding.Ball.First.Velocity;
            return Units.RadiansToDegrees(Math.Atan2(v.Z, Math.Sqrt(v.X * v.X + v.Y * v.Y)));
        }
    }
}
