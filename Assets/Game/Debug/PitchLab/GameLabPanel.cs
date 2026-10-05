using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// GameLab (TASK-010): the game state line (inning, outs, bases, score, plate appearance) and the Situation Editor —
    /// outs, runners, inning, half, score, presets, Start / Reset PA. The editor works only between plays (G toggles it; Esc
    /// frees the mouse to click it).
    /// </summary>
    public sealed class GameLabPanel : MonoBehaviour
    {
        [SerializeField] private HittingLabController _lab;
        [SerializeField] private bool _showEditor = true;

        private void OnEnable()
        {
            if (_lab != null) _lab.ClickBlocked = p => _lab.Game != null && PanelRect.Contains(new Vector2(p.x, Screen.height - p.y));
        }

        private void OnDisable()
        {
            if (_lab != null) _lab.ClickBlocked = null;
        }

        /// <summary>The panel's screen area (GUI coordinates, origin top-left): everything the panel draws (state box, and the editor below it when open): clicks there are not swings.</summary>
        private Rect PanelRect
        {
            get
            {
                float state = 66f + 18f * (_lab.Game?.Current.Pitches.Count ?? 0);
                return new Rect(Screen.width - 330, 10, 320, state + (_showEditor ? 336f : 0f));
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) _showEditor = !_showEditor;
        }

        private void OnGUI()
        {
            GameState game = _lab != null ? _lab.Game : null;
            if (game == null) return;
            PlateAppearance pa = game.Current;
            var state = new Rect(Screen.width - 330, 10, 320, 66 + 18 * pa.Pitches.Count);
            GUI.Box(state, GUIContent.none);
            GUI.Label(new Rect(state.x + 8, state.y + 4, 310, 20), $"{game.HalfName} {game.Inning}   {game.Outs} out   {Bases(game.Bases)}   PA {game.CompletedPlateAppearances + 1}   {game.Count.Balls}-{game.Count.Strikes}");
            GUI.Label(new Rect(state.x + 8, state.y + 24, 310, 20), $"Away {game.AwayScore} – Home {game.HomeScore}   {(_lab.EditorLocked ? "live" : "ready")}   {End(game)}G editor");
            GUI.Label(new Rect(state.x + 8, state.y + 44, 310, 20), $"{pa.Team} #{pa.Slot} {pa.Batter.Name} ({(pa.Batter.Bats == BatterSide.Left ? "L" : "R")})");
            for (int i = 0; i < pa.Pitches.Count; i++) GUI.Label(new Rect(state.x + 16, state.y + 62 + 18 * i, 300, 20), pa.Pitches[i].ToString());
            if (!_showEditor) return;

            GUILayout.BeginArea(new Rect(Screen.width - 330, state.yMax + 6, 320, 330), GUI.skin.box);
            GUI.enabled = !_lab.EditorLocked;
            int inning = game.Inning, outs = game.Outs, away = game.AwayScore, home = game.HomeScore;
            Half half = game.Half;
            bool first = game.Bases.First, second = game.Bases.Second, third = game.Bases.Third;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inning", GUILayout.Width(60));
            if (GUILayout.Button("−", GUILayout.Width(24)) && inning > 1) inning--;
            GUILayout.Label(inning.ToString(), GUILayout.Width(24));
            if (GUILayout.Button("+", GUILayout.Width(24))) inning++;
            if (GUILayout.Button(half == Half.Top ? "Top" : "Bottom", GUILayout.Width(70))) half = half == Half.Top ? Half.Bottom : Half.Top;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Outs", GUILayout.Width(60));
            for (int o = 0; o < GameState.OutsPerHalf; o++)
                if (GUILayout.Toggle(outs == o, o.ToString(), GUI.skin.button, GUILayout.Width(30))) outs = o;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Runners", GUILayout.Width(60));
            first = GUILayout.Toggle(first, "1B", GUI.skin.button, GUILayout.Width(40));
            second = GUILayout.Toggle(second, "2B", GUI.skin.button, GUILayout.Width(40));
            third = GUILayout.Toggle(third, "3B", GUI.skin.button, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Score", GUILayout.Width(60));
            if (GUILayout.Button("−", GUILayout.Width(24)) && away > 0) away--;
            GUILayout.Label($"A {away}", GUILayout.Width(40));
            if (GUILayout.Button("+", GUILayout.Width(24))) away++;
            if (GUILayout.Button("−", GUILayout.Width(24)) && home > 0) home--;
            GUILayout.Label($"H {home}", GUILayout.Width(40));
            if (GUILayout.Button("+", GUILayout.Width(24))) home++;
            GUILayout.EndHorizontal();
            var bases = new BaseOccupancy(first, second, third);
            if (GUI.enabled && (inning != game.Inning || half != game.Half || outs != game.Outs || !bases.Equals(game.Bases) || away != game.AwayScore || home != game.HomeScore))
                game.Set(inning, half, outs, bases, away, home);

            GUILayout.Label("Presets");
            for (int i = 0; i < GameState.Presets.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < GameState.Presets.Length; j++)
                    if (GUILayout.Button(GameState.Presets[j].Name, GUILayout.Width(150))) game.Set(GameState.Presets[j]);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("START PA")) _lab.StartPlateAppearance();
            if (GUILayout.Button("RESET PA")) game.ResetPlateAppearance();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            for (int i = Mathf.Max(0, game.Log.Count - 3); i < game.Log.Count; i++) GUILayout.Label(game.Log[i]);
            GUILayout.EndArea();
        }

        /// <summary>The last plate appearance's walk or strikeout, until the next batter's first pitch (read from the game, so
        /// a RESET PA or an edit never leaves a stale call up).</summary>
        private static string End(GameState g)
        {
            if (g.CompletedPlateAppearances == 0 || g.Current.Pitches.Count > 0) return string.Empty;
            PlateAppearanceEnd e = g.Completed[g.CompletedPlateAppearances - 1].End;
            return e == PlateAppearanceEnd.Walk ? "WALK   " : e == PlateAppearanceEnd.Strikeout ? "STRIKEOUT   " : string.Empty;
        }

        private static string Bases(BaseOccupancy b) =>
            b.First || b.Second || b.Third ? $"{(b.Third ? "3" : "-")}{(b.Second ? "2" : "-")}{(b.First ? "1" : "-")}" : "empty";
    }
}
