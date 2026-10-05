# TASK-010: Half-inning loop

## Goal
A persistent game on the production systems: authoritative `GameState` (inning, half, outs, score, bases, plate
appearance), the plate-appearance lifecycle, results applied when a play is over, the half-inning change, a Situation
Editor with presets, a small state UI, and an integrated GameLab scene.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Design
- `Gameplay/Play/GameState` (engine-free): `Situation` for the next pitch; `Apply(LivePlay)` once the play is over — a dead
  foul changes nothing (same batter); a fair ball ends the plate appearance: runs (already per OBR 5.08(a)) to the batting
  team, outs, bases; the third out switches the half (bases cleared, after the bottom half the next inning); `Set` (editor,
  validated) and `ResetPlateAppearance`; 8 presets.
- No ball/strike count (documented limitation): takes, misses and fouls keep the plate appearance going.
- `HittingLabController` game mode (GameLab only): each pitch is played from `Game.Situation` (its alignment: double-play
  depth); the result is applied when sim time reaches the play's end (or when the next pitch is thrown first). The editor
  is locked unless Ready with the result applied.
- Presentation: between plays the runners on base walk from their bags to their leads (exactly the next play's start: no
  jump at contact); after the result pause the fielders (and a pitcher who left the mound) jog back to the situation's
  alignment and the ball returns to the pitcher. All presentation only, driven by the lab clock.
- `GameLabPanel`: state line (half, inning, outs, bases, PA, score, live/ready) and the editor (inning, half, outs, runners,
  score, presets, START PA, RESET PA, recent log); G toggles; Esc frees the mouse.
- `Assets/Scenes/GameLab.unity`: the HittingLab scene with game mode on and the panel; in the build list.

## Milestones
- [x] GameState + tests (8).
- [x] Lab integration, presentation between plays, panel, scene.
- [x] PlayMode tests (2): result at play end, editor lock, lead-offs = next play's start, defense back in alignment, third
  out → half change, reset.
- [x] Reviews (Unity, Codex B) and fixes: a play cannot be applied twice; Reset PA also rolls back the PA count and log;
  fielders jog back during the result pause (1.4 s < 1.5 s: back before the next pitch can be thrown) keeping the ball in
  the last holder's glove; a new pitch ends any pitcher return (the delivery is his only writer); alignment latched while a
  play is shown; START PA has the wind-up; clicks on the editor panel never capture the mouse or throw.
- [x] check.sh (EditMode 534/540, PlayMode 48/48); merge.

## Verification
- check.sh: EditMode 534/540 (6 skipped, pre-existing), PlayMode 48/48.
- Runtime (GameLab): R1 0 out → hard grounder to 2B, OUT AT 2B; state → 1 out, R1, PA 1, Ready. Scripted half-inning:
  CF hit, RF hit (bases loaded, 1 out), liner to SS caught + runner doubled off → 3 out → Bottom 1, bases empty. Loaded
  preset: runners lead off; panel and state line visible.

## Remaining risks / limitations
- No count, no batting order (one batter model), no game end (innings continue).
- Runners shown between plays are keyed by base (figure identity may change between plays; presentation only).
- A press that skips the rest of a play cuts its presentation (runners/fielders jump to the next state).
