using System.Linq;
using System.Text;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Running;
using Pitchlab.Gameplay.Rules;

namespace Pitchlab.Sandbox
{
    /// <summary>Text for a <see cref="LivePlay"/>: the calls so far and a debug report (reads only).</summary>
    public static class LiveText
    {
        /// <summary>The calls by <paramref name="time"/>: outs, safe calls on plays at a base, runs.</summary>
        public static string Calls(LivePlay play, double time)
        {
            if (play == null) return "";
            var text = new StringBuilder();
            foreach (PlayLogEntry e in play.Log)
            {
                if (e.Time > time) break;
                string call = e.Kind switch
                {
                    PlayLogKind.Out => e.Text.StartsWith("FLY") ? "FLY OUT" : e.Text.StartsWith("DOUBLED") ? $"DOUBLED OFF {Name(e)}" : $"OUT AT {Name(e)}{(e.Text.Contains("(tag)") ? " (tag)" : "")}",
                    PlayLogKind.Safe => $"SAFE AT {Name(e)}",
                    PlayLogKind.Run => e.Text.StartsWith("RUN SCORES") ? "RUN SCORES" : "NO RUN",
                    _ => null,
                };
                if (call == null) continue;
                if (text.Length > 0) text.Append(" · ");
                text.Append(call);
            }

            return text.ToString();
        }

        private static string Name(PlayLogEntry e) => e.At is Pitchlab.Simulation.Field.Base b ? Bases.Name(b).ToUpperInvariant() : "";

        public static string Report(LivePlay play, double time)
        {
            double t0 = play.ContactTime;
            var text = new StringBuilder();
            text.AppendLine($"{play.Situation} · {play.Kind} · outs {play.Situation.Outs} → {play.Outs} · runs {play.Runs}{(play.IsOver && time >= play.EndTime ? " · OVER" : "")}");
            foreach (LiveRunner r in play.Runners)
            {
                string state = r.IsOut ? "out" : r.HasScored ? "scored" : $"{r.Phase} → {Bases.Name(r.Target)}";
                text.AppendLine($"  {r.Id}: {state}{(play.ForcedAt(r, time) ? " (forced)" : "")}");
            }

            text.AppendLine(RulesText.Report(play.Rules, time).Split('\n').FirstOrDefault(l => l.StartsWith("chosen")) ?? "");
            foreach (PlayLogEntry e in play.Log.Where(e => e.Time <= time && e.Kind != PlayLogKind.Decision))
                text.AppendLine($"  +{e.Time - t0:0.00} {e.Text}");
            return text.ToString();
        }
    }
}
