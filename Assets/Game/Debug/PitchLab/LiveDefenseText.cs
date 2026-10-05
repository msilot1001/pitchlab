using System.Text;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Sandbox
{
    /// <summary>Debug text for a live defense (TASK-008): every defender's role, the throw on, the decisions (reads only).</summary>
    public static class LiveDefenseText
    {
        public static string Roles(LiveDefense defense, double time)
        {
            var text = new StringBuilder("Defense\n");
            RoleAssignment[] roles = defense.RolesAt(time);
            DefensivePosition? holder = defense.HolderAt(time);
            for (int i = 0; i < DefensiveAlignment.Count; i++)
            {
                var p = (DefensivePosition)i;
                string role = holder == p ? "BALL" : roles != null ? roles[i].ToString() : "HOLD";
                text.AppendLine($"{LivePlayNames.Abbrev(p),-3} {role}");
            }

            foreach (LiveThrow th in defense.Throws)
                if (time >= th.ReleaseTime && time <= th.EndTime + 0.5)
                    text.AppendLine($"throw {LivePlayNames.Abbrev(th.Thrower)} → {LivePlayNames.Abbrev(th.Receiver)} ({(th.Target is Base b ? Pitchlab.Gameplay.Rules.Bases.Name(b) : "cut-off")})");
            return text.ToString();
        }

        public static string Decisions(LiveDefense defense, double time, double t0)
        {
            var text = new StringBuilder();
            foreach (HolderDecision dec in defense.Decisions)
            {
                if (dec.Time > time) break;
                text.AppendLine($"+{dec.Time - t0:0.00} {LivePlayNames.Abbrev(dec.Holder)} has it — candidates:");
                foreach (LiveAction a in dec.Candidates)
                {
                    string when = a.Feasible ? $"+{a.Completion - t0:0.00} s" : "—";
                    string verdict = a.Runner == null ? "no runner" : a.Retires ? $"OUT by {a.Margin:0.00} s" : a.Feasible ? $"late by {-a.Margin:0.00} s" : "cannot";
                    text.AppendLine($"  {(a == dec.Chosen ? "▶" : " ")} {a}: {when} · {verdict}");
                }

                text.AppendLine($"  chosen: {dec.Chosen} ({dec.Reason})");
            }

            return text.ToString();
        }
    }

    internal static class LivePlayNames
    {
        public static string Abbrev(DefensivePosition p) => HittingLabPresentation.Abbreviation(p);
    }
}
