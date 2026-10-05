# TASK-015: At-bat UX + pitch history + scoreboard

## Goal
The whole game situation readable at a glance in the GameLab without debug overlays: a generic scorebug, the batter, the
call of each pitch and plate appearance, the plate appearance's pitches with a strike-zone plot, a compact game log — all
read from the authoritative game; debug information stays behind its own keys.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Current state (inspected, after TASK-014)
- `GameLabPanel` drew a debug state box (inning, outs, bases, score, count, batter, pitch list) plus the Situation Editor,
  open by default. The controller's corner help line and H/T debug panels; the presentation's bottom banner (play calls)
  and result line; the camera label bottom-right.
- A finished fair-ball plate appearance was recorded as "in play: N outs" — no description of what happened.

## Design
- Gameplay `PlayResults.Classify(LivePlay)` → single / double / triple / (inside-the-park) home run / ground-rule double /
  line out / fly out / pop out (launch angle < 25° / 25–50° / > 50°, Statcast bands) / groundout / sacrifice fly (OBR
  9.08(d): caught, < 2 out, a run scores) / fielder's choice (grounder, batter safe, another runner out) / double / triple
  play — from the award, the rules events, the batter-runner's last base and the runs. Descriptive, not official scoring
  (no errors, no scorer's judgement). `PlateAppearance.PlayResult` and its result text use it; `GameState.Version` counts
  changes.
- `GameHud` (Debug/PitchLab, added by `GameLabPanel`): scorebug bottom-left (teams and score, TOP/BOT inning, base diamond,
  B/S/O dots, batter #slot name (side), last plate appearance), the call top-centre through the result pause (BALL, CALLED
  STRIKE, SWINGING STRIKE, FOUL, WALK, STRIKEOUT, SINGLE, …; meanwhile the batter line and count are the finished plate appearance's, then the next batter at 0–0), the shown plate
  appearance's pitches bottom-right (type, speed, call; the finished one until the loop is ready) with a zone plot of the
  recorded crossings against the batter's zone, a game log on V (L is a pitcher key). View model rebuilt only when the game version / shown
  plate appearance / call change (no per-frame strings). Reads only.
- `GameLabPanel`: the Situation Editor only, hidden by default (G).
- RunnerBrain (found while building the classifier): on a pop-up an infielder takes, runners go back to the bag instead of
  halfway (they were doubled off: a triple play on a 60° pop-up with runners on first and second).

## Milestones
- [x] PlayResults + GameState version + EditMode tests (PlayResultsTests).
- [x] GameHud + panel → editor only + PlayMode tests (GameHudTests).
- [x] Runtime (GameLab, 3–2 in the bottom 3rd with R1 R3): scorebug, BALL call, five-pitch history and zone plot all
  readable and clear of the pitch view and the camera label.
- [x] Reviews — rules: a batter thrown out stretching keeps his hit, a force on an outfield-fielded ball is a fielder's
  choice, a sacrifice fly needs an outfielder's catch (simplifications documented: hit value by final base, DP label wins);
  Unity: log key V (L aims pitches), refresh once per frame in LateUpdate (no layout pass), cached capital results (no
  allocation through the result pause), batter line and count of the shown plate appearance until ready, bottom band above
  the play banner, off-chart dots skipped, editor hidden by default in code; tests: pre-application, shown index at each
  strikeout, event-log headers/runs/window, press during a play, plot = recorded crossings, allocation check with a working
  probe (Unity's allocation constraint), camera modes through the normal player loop.
- [ ] Codex, merge.

## Limitations
- Results are descriptive (no hit/error judgement, no RBI/earned runs); the infield fly rule is not modelled.
- IMGUI styling only (generic; no broadcast imitation).
