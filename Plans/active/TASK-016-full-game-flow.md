# TASK-016: Complete game flow + game end

## Goal
Turn the half-inning loop into a complete game: nine innings, the home team's half not played when it is already ahead,
walk-offs that end the game the moment the winning run scores, extra innings, a final, a new game — in the production
GameLab — and an accelerated, deterministic full-game simulation through the production systems for state/rules testing.

## Owner
Claude Code (sole writer); rules / Unity / test reviewers and Codex read-only.

## Current state (inspected, after TASK-015)
- `GameState` (TASK-010/012/013): innings advance on the third out forever; no end, no winner. Lineups and cursors persist.
- `HittingLabController.NewGame` exists (TASK-013) but nothing in the GameLab calls it.

## Rules (verified against the 2025 and 2026 OBR PDFs, identical text)
- 7.01(a): a regulation game is nine innings, extended for a tie or shortened when the home team needs none (or part) of the 9th.
- 7.01(e)(1): ends when the visitors complete the top of the 9th with the home team ahead (bottom not played).
- 7.01(e)(2): ends when the 9th is completed with the visitors ahead.
- 7.01(e)(3): the home team scoring the winning run in the 9th or an extra inning ends the game immediately when that run
  scores — runs beyond it do not count; EXCEPTION: a home run out of the playing field — the batter and every runner score.
- 5.08(b): a forced walk-off (walk with the bases full) ends when the runner from third touches home and the batter first
  (modelled as: the walk scores the one run and ends the game).
- 7.01(b)(1): tied after nine, play continues until the visitors lead after a completed inning or the home team scores the
  winning run. 7.01(b)(2) (MLB regular season) starts each extra half with a runner on second; 7.02(g) excludes it from the
  postseason. Chosen: traditional empty-base extra innings (as in the postseason) — documented choice.
- 5.04(a)(3): each team's order continues from inning to inning (TASK-013).

## Design
- `GameState`: `Status` (Pregame until the first pitch, Playing, GameOver), `Result` (`GameResult`: score, winner, last
  inning and half, reason), `RegulationInnings = 9`. Checked when a plate appearance completes: walk-off first (bottom of
  the 9th or later, home team from not ahead to ahead: credited runs capped at the winning run unless the result is a home
  run), then at three outs the 7.01(e)(1)/(2) ends, else the next half (extras: bases empty). After the end `Pitch` /
  `Apply` throw; RESET PA restores the pre-end state; the Situation Editor puts a game back in play (development tool).
- `GameSimulator` (Gameplay/Play): every pitch through `AutoPitcher` → `PitchTargets` (simulated flight) → a stand-in batter
  (SplitMix hash per seed/PA/pitch: judges the crossing with 3 cm error, swing probability by count, timing σ 25 ms, aim
  σ 4.5 cm) → `ContactResolver` → `BallInPlaySimulation` → `FieldingSolver` → `LivePlay` → `GameState`. No rendering, no
  shared random state.
- GameLab: no pitch after the end (auto stops), the HUD's call then FINAL (score, reason, "Space / A: new game"); a press
  on the final (or the editor's NEW GAME) starts a new standard game: new state and cursors, no pitch or play carried over,
  the camera back on the batter; the player's camera mode is kept (a preference, not game state).

## Milestones
- [x] GameState lifecycle + end rules + EditMode tests (GameFlowTests incl. whole simulated games).
- [x] GameSimulator.
- [x] GameLab end / final / new game + PlayMode tests (GameFlowSceneTests).
- [x] Reviews — rules: walk-off cut at the winning run (outs before it; a hit credited with the winning runner's bases,
  9.06(f)), ordinal suffixes; Unity: auto pitching resumes after a new game (armed at NewGame, works with no pitch),
  no next batter after the final, FINAL up FinalDwell (1 s) before a press starts a new game, START PA disabled after the end,
  explicit final flag in the HUD, PlayToEnd throws if a game never ends, Status restored by RESET; tests: literal 8th-inning
  cases (RegulationInnings mutation), walk-off from one down, 9.06(f) label, force-annulled run → extras (5.08(a)), tag
  third out after the winning run → game over, visitors after exactly nine, Apply / simulator after the end, simulated
  games: recorded = simulated pitches, halves in order with exactly three outs, a replay that the game ended at its first
  decisive moment, seeds pinned to each ending (incl. 12 innings), determinism over pitch records; scene: final dwell,
  NEW GAME mid-play, auto into a new game.
- [ ] Codex, merge.

## Measurements
- Simulated games (EditMode, Editor): ≈ 1.2–1.6 s per nine-inning game, 240–315 pitches, 62–77 plate appearances.

## Limitations
- After a 7.01(e)(1) end the scorebug's outs/bases read the cleared half (the FINAL panel is shown).
- No automatic runner in extra innings (documented choice), no called/suspended games, no mercy rule, no substitutions.
- The stand-in batter of the simulator is a test device, not a hitter model.
