using System.Collections.Generic;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// The GameLab's game HUD (TASK-015): a generic scorebug (innings, score, outs, count, bases), the batter, a brief
    /// result call, the plate appearance's pitches with a strike-zone plot, and an optional game log (L). Presentation only:
    /// everything is read from <see cref="GameState"/> and the batting loop — nothing here decides a ball, a strike, a run
    /// or an out. The view model is rebuilt only when what it shows changes (no per-frame strings).
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private HittingLabController _lab;

        // ------------------------------------------------------------------ view model (read by tests)

        public string InningText { get; private set; } = string.Empty;
        public string AwayText { get; private set; } = string.Empty;
        public string HomeText { get; private set; } = string.Empty;
        public int Outs { get; private set; }
        public int Balls { get; private set; }
        public int Strikes { get; private set; }
        public BaseOccupancy Bases { get; private set; }
        public string BatterText { get; private set; } = string.Empty;
        /// <summary>The call being shown (BALL, CALLED STRIKE, …, WALK, STRIKEOUT, SINGLE, DOUBLE PLAY, …) or empty.</summary>
        public string Flash { get; private set; } = string.Empty;
        /// <summary>The shown plate appearance's pitches, one line each ("1  Four-Seam-like 95  BALL").</summary>
        public IReadOnlyList<string> History => _history;
        /// <summary>The previous plate appearance in a few words ("#3 Away #3 — WALK").</summary>
        public string LastResult { get; private set; } = string.Empty;
        public IReadOnlyList<string> EventLog => _events;
        public string HistoryTitle { get; private set; } = string.Empty;
        /// <summary>The plate appearance whose pitches are shown: the one the last pitch belonged to until the loop is ready
        /// again, then the current one.</summary>
        public PlateAppearance Shown { get; private set; }
        public bool ShowLog { get; set; }
        public bool ShowPlot { get; set; } = true;

        private readonly List<string> _history = new List<string>();
        private readonly List<string> _events = new List<string>();
        private (GameState Game, int Version, PlateAppearance Shown, string Flash) _built;

        private void Awake() => _lab = FindFirstObjectByType<HittingLabController>();

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame) ShowLog = !ShowLog;
        }

        /// <summary>Brings the view model up to date with the game at real time <paramref name="realtime"/>.</summary>
        public void Refresh(double realtime)
        {
            GameState g = _lab != null ? _lab.Game : null;
            if (g == null) return;
            bool ready = _lab.CurrentPitch == null || _lab.StateAt(realtime) == BattingState.Ready;
            // The call of the last pitch stays up through the result pause: its plate appearance's end, or the pitch itself.
            string flash = string.Empty;
            PlateAppearance shown = g.Current;
            if (!ready && _lab.LastOutcome is PitchOutcome o)
            {
                if (_lab.LastEnd != PlateAppearanceEnd.None && g.CompletedPlateAppearances > 0)
                {
                    shown = g.Completed[g.CompletedPlateAppearances - 1];
                    flash = EndText(shown);
                }
                else flash = PitchOutcomes.Describe(o);
            }

            if (ReferenceEquals(g, _built.Game) && g.Version == _built.Version && ReferenceEquals(shown, _built.Shown) && flash == _built.Flash) return;
            _built = (g, g.Version, shown, flash);
            Rebuild(g, shown, flash);
        }

        private void Rebuild(GameState g, PlateAppearance shown, string flash)
        {
            Shown = shown;
            Flash = flash;
            InningText = $"{(g.Half == Half.Top ? "TOP" : "BOT")} {g.Inning}";
            AwayText = $"{g.LineupOf(TeamSide.Away).Team.ToUpperInvariant()}  {g.AwayScore}";
            HomeText = $"{g.LineupOf(TeamSide.Home).Team.ToUpperInvariant()}  {g.HomeScore}";
            Outs = g.Outs;
            Balls = g.Count.Balls;
            Strikes = g.Count.Strikes;
            Bases = g.Bases;
            PlateAppearance at = g.Current;
            BatterText = $"#{at.Slot}  {at.Batter.Name}  ({(at.Batter.Bats == BatterSide.Left ? "L" : "R")})";
            HistoryTitle = $"PA {shown.Number}  ·  #{shown.Slot} {shown.Batter.Name}";
            _history.Clear();
            foreach (PitchEvent p in shown.Pitches)
                _history.Add($"{p.Number,2}  {Short(p.Info.Label)} {p.Info.SpeedMph:0}  {PitchOutcomes.Describe(p.Outcome)}");
            if (shown.IsComplete) _history.Add($"     {EndText(shown)}");
            PlateAppearance last = g.CompletedPlateAppearances == 0 ? null : g.Completed[g.CompletedPlateAppearances - 1];
            LastResult = last == null ? string.Empty : $"LAST  #{last.Slot} {last.Batter.Name} — {EndText(last)}";
            _events.Clear();
            int first = System.Math.Max(0, g.CompletedPlateAppearances - 10);
            for (int i = first; i < g.CompletedPlateAppearances; i++)
            {
                PlateAppearance pa = g.Completed[i];
                if (i == first || pa.Half != g.Completed[i - 1].Half || pa.Inning != g.Completed[i - 1].Inning)
                    _events.Add($"{(pa.Half == Half.Top ? "Top" : "Bottom")} {pa.Inning}");
                _events.Add($"  #{pa.Slot} {EndText(pa)}{(pa.Runs > 0 ? $" ({pa.Runs} R)" : "")}");
            }
        }

        /// <summary>A finished plate appearance in capitals: WALK, STRIKEOUT, SINGLE, DOUBLE PLAY, …</summary>
        public static string EndText(PlateAppearance pa) => pa.End switch
        {
            PlateAppearanceEnd.Walk => "WALK",
            PlateAppearanceEnd.Strikeout => "STRIKEOUT",
            PlateAppearanceEnd.InPlay => pa.PlayResult is PlayResultKind k ? PlayResults.Describe(k).ToUpperInvariant() : "IN PLAY",
            _ => string.Empty,
        };

        private static string Short(string label) => label.EndsWith("-like") ? label.Substring(0, label.Length - 5) : label;

        // ------------------------------------------------------------------ drawing

        private GUIStyle _small, _big, _score, _flash;
        private static readonly string[] Numbers = { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20" };
        private static readonly Color Panel = new Color(0.05f, 0.06f, 0.08f, 0.78f), On = new Color(1f, 0.82f, 0.25f), Off = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color BallColor = new Color(0.35f, 0.85f, 0.45f), StrikeColor = new Color(0.95f, 0.35f, 0.3f), FoulColor = new Color(0.95f, 0.75f, 0.25f), PlayColor = new Color(0.4f, 0.7f, 1f);

        private void OnGUI()
        {
            if (_lab == null || _lab.Game == null) return;
            Refresh(_lab.RenderedRealtime);
            if (_small == null)
            {
                _small = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.white } };
                _score = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
                _big = new GUIStyle(_score) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
                _flash = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            }

            DrawScorebug(new Rect(12f, Screen.height - 146f, 300f, 134f));
            if (Flash.Length > 0) GUI.Label(new Rect(Screen.width * 0.5f - 250f, 54f, 500f, 44f), Flash, _flash);
            float right = Screen.width - 12f;
            if (History.Count > 0)
            {
                var box = new Rect(right - 240f, Screen.height - 40f - (24f + 17f * History.Count), 240f, 24f + 17f * History.Count);
                Fill(box, Panel);
                GUI.Label(new Rect(box.x + 8f, box.y + 3f, 230f, 18f), HistoryTitle, _small);
                for (int i = 0; i < History.Count; i++) GUI.Label(new Rect(box.x + 8f, box.y + 21f + 17f * i, 230f, 18f), History[i], _small);
                if (ShowPlot) DrawPlot(new Rect(box.x - 132f, box.yMax - 166f, 124f, 166f));
            }

            if (ShowLog && EventLog.Count > 0)
            {
                var log = new Rect(12f, Screen.height - 150f - (10f + 17f * EventLog.Count), 300f, 10f + 17f * EventLog.Count);
                Fill(log, Panel);
                for (int i = 0; i < EventLog.Count; i++) GUI.Label(new Rect(log.x + 8f, log.y + 4f + 17f * i, 290f, 18f), EventLog[i], _small);
            }
        }

        private void DrawScorebug(Rect r)
        {
            Fill(r, Panel);
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, 150f, 22f), AwayText, _score);
            GUI.Label(new Rect(r.x + 10f, r.y + 28f, 150f, 22f), HomeText, _score);
            GUI.Label(new Rect(r.x + 10f, r.y + 52f, 90f, 22f), InningText, _score);
            // Bases: a small diamond (second at the top), occupied bases lit.
            Vector2 c = new Vector2(r.x + 205f, r.y + 34f);
            Diamond(c + new Vector2(18f, 0f), Bases.First);
            Diamond(c + new Vector2(0f, -18f), Bases.Second);
            Diamond(c + new Vector2(-18f, 0f), Bases.Third);
            // Count and outs: dots.
            Dots(new Vector2(r.x + 160f, r.y + 62f), "B", Balls, 3, BallColor);
            Dots(new Vector2(r.x + 160f, r.y + 80f), "S", Strikes, 2, StrikeColor);
            Dots(new Vector2(r.x + 160f, r.y + 98f), "O", Outs, 2, On);
            GUI.Label(new Rect(r.x + 10f, r.y + 78f, 145f, 18f), BatterText, _small);
            GUI.Label(new Rect(r.x + 10f, r.y + 112f, 285f, 18f), LastResult, _small);
        }

        private void Dots(Vector2 at, string label, int count, int max, Color on)
        {
            GUI.Label(new Rect(at.x, at.y - 8f, 16f, 18f), label, _small);
            for (int i = 0; i < max; i++) Fill(new Rect(at.x + 18f + 15f * i, at.y - 4f, 10f, 10f), i < count ? on : Off);
        }

        private static void Diamond(Vector2 centre, bool occupied)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, centre);
            Fill(new Rect(centre.x - 7f, centre.y - 7f, 14f, 14f), occupied ? On : Off);
            GUI.matrix = m;
        }

        /// <summary>Where the shown plate appearance's pitches crossed the plate (catcher's view), the batter's zone drawn.</summary>
        private void DrawPlot(Rect r)
        {
            Fill(r, Panel);
            PlayerProfile batter = Shown.Batter;
            double x0 = -0.55, x1 = 0.55, z0 = batter.ZoneBottom - 0.45, z1 = batter.ZoneTop + 0.4;
            Vector2 P(double x, double z) => new Vector2(r.x + (float)((x - x0) / (x1 - x0)) * r.width, r.yMax - (float)((z - z0) / (z1 - z0)) * r.height);
            Vector2 a = P(-StrikeZone.HalfWidth, batter.ZoneTop), b = P(StrikeZone.HalfWidth, batter.ZoneBottom);
            Outline(Rect.MinMaxRect(a.x, a.y, b.x, b.y), new Color(1f, 1f, 1f, 0.6f));
            for (int i = 0; i < Shown.Pitches.Count; i++)
            {
                PitchEvent p = Shown.Pitches[i];
                if (double.IsNaN(p.Info.PlateX)) continue;
                Vector2 at = P(p.Info.PlateX, p.Info.PlateZ);
                Color c = p.Outcome == PitchOutcome.Ball ? BallColor : p.Outcome == PitchOutcome.Foul ? FoulColor : p.Outcome == PitchOutcome.InPlay ? PlayColor : StrikeColor;
                Fill(new Rect(at.x - 5f, at.y - 5f, 10f, 10f), c);
                if (p.Number < Numbers.Length) GUI.Label(new Rect(at.x + 5f, at.y - 9f, 20f, 16f), Numbers[p.Number], _small);
            }
        }

        private static void Outline(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
