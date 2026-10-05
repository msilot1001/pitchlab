using System.Linq;
using System.Text;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;

namespace Pitchlab.Sandbox
{
    /// <summary>Text for a <see cref="RulesPlay"/> (TASK-006B): the call to show and the decision/debug report. Reads only.</summary>
    public static class RulesText
    {
        /// <summary>
        /// The calls made by <paramref name="time"/>: every out, and SAFE for a runner the defense made a play on. A runner
        /// reaching base with no play on him is not a "call" (a hit), so it is not shown.
        /// </summary>
        public static string Latest(RulesPlay play, double time)
        {
            string[] calls = play.Resolution.EventsUntil(time).Where(e => IsCall(play, e)).Select(e => e.ToString()).ToArray();
            return string.Join(" · ", calls);
        }

        public static bool IsCall(RulesPlay play, PlayEvent e) => e.IsOut || (play.Chosen?.Runner is Runner r && r == e.Runner);

        public static string Report(RulesPlay play, double time)
        {
            double t0 = play.ContactTime;
            var text = new StringBuilder();
            text.AppendLine($"{play.Before} · outs {play.Resolution.OutsBefore} → {play.Resolution.OutsAt(time)} · {play.Resolution.StatusAt(time)}");
            foreach (Runner r in play.Runners)
            {
                RunnerState s = play.RunnerStateAt(r, time);
                string arrival = play.Advancing.Contains(r) ? $" · at {Bases.Name(r.Next)} +{play.ArrivalTime(r) - t0:0.00} s" : "";
                text.AppendLine($"  {r}: {s.Status} {Bases.Name(s.At)}{(s.Forced ? " (forced)" : "")}{arrival}");
            }

            if (play.Fielding.Primary is DefensivePosition holder && play.Chosen != null)
            {
                text.AppendLine($"ball: {HittingLabPresentation.Abbreviation(holder)} +{play.Fielding.PossessionTime - t0:0.00} s · candidates:");
                foreach (DefensiveAction a in play.Candidates)
                {
                    string when = a.Feasible ? $"+{a.CompletionTime - t0:0.00} s" : "—";
                    string verdict = a.Runner == null ? "no runner" : a.Retires ? $"OUT by {a.Margin:0.00} s" : a.Feasible ? $"late by {-a.Margin:0.00} s" : "cannot";
                    text.AppendLine($"  {(a == play.Chosen ? "▶" : " ")} {a}: {when} · {verdict}{(a.DoublePlayPossible ? " · DP possible" : "")}");
                }

                text.AppendLine($"chosen: {play.Chosen} ({play.Reason})");
            }
            else text.AppendLine(play.Reason);

            foreach (PlayEvent e in play.Resolution.EventsUntil(time))
                text.AppendLine($"  +{e.Time - t0:0.00} s {e} — {e.Runner}{(e.Fielder is DefensivePosition f ? $" ({HittingLabPresentation.Abbreviation(f)})" : "")}");
            return text.ToString();
        }
    }
}
