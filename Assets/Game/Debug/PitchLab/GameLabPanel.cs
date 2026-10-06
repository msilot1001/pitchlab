using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// GameLab (TASK-010): the Situation Editor, a development tool — outs, runners, inning, half, score, presets, Start /
    /// Reset PA. It works only between plays (G toggles it; Esc frees the mouse to click it). The game itself is shown by
    /// the <see cref="GameHud"/> (TASK-015), which this panel adds.
    /// </summary>
    public sealed class GameLabPanel : MonoBehaviour
    {
        [SerializeField] private HittingLabController _lab;
        [SerializeField] private bool _showEditor;

        private void Awake()
        {
            GameHud hud = GetComponent<GameHud>();
            if (hud == null) hud = gameObject.AddComponent<GameHud>();
            if (_lab != null) hud.Lab = _lab;
        }

        private void OnEnable()
        {
            if (_lab != null) _lab.ClickBlocked = p => _lab.Game != null && PanelRect.Contains(new Vector2(p.x, Screen.height - p.y));
        }

        private void OnDisable()
        {
            if (_lab != null) _lab.ClickBlocked = null;
        }

        /// <summary>The editor's screen area when open (GUI coordinates, origin top-left): clicks there are not swings.</summary>
        private Rect PanelRect => _showEditor ? new Rect(Screen.width - 330, 10, 320, 380) : Rect.zero;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) _showEditor = !_showEditor;
        }

        private void OnGUI()
        {
            GameState game = _lab != null ? _lab.Game : null;
            if (game == null) return;
            if (!_showEditor) return;

            GUILayout.BeginArea(PanelRect, GUI.skin.box);
            GUILayout.Label($"Situation editor (G)  ·  {(_lab.EditorLocked ? "locked: live" : "ready")}");
            // Player inspector (TASK-017, development): the batter's and the pitcher's ratings.
            PlayerProfile batter = game.Batter, pitcher = game.TeamOf(game.Fielding).Pitcher;
            GUILayout.Label($"{batter.Name} {batter.Position}: Con {batter.Ratings.Contact} Pow {batter.Ratings.Power} Vis {batter.Ratings.Vision} Dis {batter.Ratings.Discipline} Spd {batter.Ratings.Speed}");
            if (pitcher != null) GUILayout.Label($"{pitcher.Name}: Vel {pitcher.Ratings.Velocity} Cmd {pitcher.Ratings.Command} Mov {pitcher.Ratings.Movement} Sta {pitcher.Ratings.Stamina}");
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
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !game.IsOver;
            if (GUILayout.Button("START PA")) _lab.StartPlateAppearance();
            GUI.enabled = true;   // a new game is always possible, mid-play too (the pitch on screen is dropped unapplied)
            if (GUILayout.Button("NEW GAME")) _lab.NewGame();
            GUI.enabled = enabled;
            if (GUILayout.Button("RESET PA")) game.ResetPlateAppearance();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            for (int i = Mathf.Max(0, game.Log.Count - 3); i < game.Log.Count; i++) GUILayout.Label(game.Log[i]);
            GUILayout.EndArea();
        }
    }
}
