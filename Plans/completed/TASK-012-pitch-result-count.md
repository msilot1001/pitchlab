# TASK-012: Pitch result + ball/strike count (at-bat foundation)

## Goal
Every pitch has an authoritative result (ball, called strike, swinging strike, foul, in play) and the plate appearance
keeps a ball/strike count: four balls walk the batter (forced runners advance, a run scores with the bases loaded), a
third strike retires him, a foul is a strike below two strikes, a fair ball ends the plate appearance as before.

## Owner
Claude Code (sole writer); test-reviewer / rules reviewer and Codex read-only.

## Current state (inspected)
- `GameState` (TASK-010): inning, half, outs, score, bases, plate appearances; no count — a take, a miss or a foul leaves
  the plate appearance as it is; `Apply(LivePlay)` ends it on a fair ball. `ResetPlateAppearance` undoes the last play.
- `BattingStateMachine`: Ready → Windup → PitchInFlight → (Swinging → BallInPlay | take / miss) → Result → Ready.
- Strike zone: only `PitchingGeometry.PlateHalfWidth` and `DefaultZoneBottom/Top` (1.5–3.5 ft), drawn in the HittingLab.
- Pitches: five presets aimed at mid-zone (a test enforces it) — every take would be a strike.

## Design
- Gameplay/Rules `Count` (balls, strikes) with `After(PitchResult)` → next count and how the plate appearance ends
  (none, walk, strikeout, in play). OBR: ball four → walk (5.05(b)(1); forced runners 5.06(b)(3)(B)); strike three → out (5.09(a)(2)); a foul is a strike
  unless there are two (Definition of Terms "Strike" (c)); fair ball → in play.
- Gameplay/Hitting `StrikeZone`: a taken pitch is a strike when any part of the ball passes through the zone at the front
  plane of home plate (Statcast plate_x / plate_z plane) — |x| ≤ half plate + ball radius, bottom − r ≤ z ≤ top + r with the
  default zone. REFERENCE CONVENTION (the rulebook zone is a 3-D volume over the plate and depends on the batter's stance).
- `PitchResults.Of(pitch, swing, contact result, live play)`: no swing → zone call; swing without contact → swinging strike;
  contact that ends dead without an award → foul; otherwise in play.
- `BaseOccupancy.Walk()`: forced runners advance one base; a run scores with the bases loaded.
- `GameState.Pitch(PitchResult)` for pitches that are not put in play; `Apply(LivePlay)` counts a foul. Walk, strikeout and
  fair balls end the plate appearance (log, outs, half-inning change shared). The count resets with each plate appearance.
  `ResetPlateAppearance` goes back to the start of the current (or just finished) plate appearance.
- GameLab: the count in the HUD; the pitch result in the result line; a pitch location (Up / Down: middle, up, down, in,
  away, and four out-of-zone spots) as a small release-angle aim offset so balls are possible. Not pitcher AI (TASK-014).

## Milestones
- [x] Rules: Count, PitchOutcome (renamed: Simulation already has a PitchResult), StrikeZone, Walk + EditMode tests.
- [x] GameState count + plate-appearance ends + tests.
- [x] GameLab integration (result → game, HUD, location) + PlayMode tests.
- [x] check.sh (EditMode 567/573, 6 skipped; PlayMode 69/69); test + rules reviews (late swing after a counted take
  ignored, RESET after an edit returns to 0–0, LivePlay.IsFoul shared, citations, added tests); Codex B (its one finding,
  a "Color Color" compile error, does not occur — the project compiles and the walk tests run; call qualified for
  clarity); merge.

## Verification
- Every preset at every location is called as aimed (CountTests); GameLab runtime: Ball low / Middle / Ball away / Ball in /
  Ball high taken → 1–0, 1–1, 2–1, 3–1, walk to first.

## Not in scope / limitations
- A swing-and-miss is counted when both the pitch and the swing are over (an early miss shows its banner slightly before).
- No hit-by-pitch (no batter body), no check swing, no bunts, no catcher's catch (a foul tip is a foul; no dropped third
  strike), no intentional walk, no balk / wild pitch / stolen base.
