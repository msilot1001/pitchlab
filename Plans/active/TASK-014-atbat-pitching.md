# TASK-014: Pitcher / pitch selection + at-bat interaction

## Goal
Pitching through whole plate appearances without debug fiddling: an explicit pitch command (type + target), quick
manual controls that never depend on the arrow keys, a basic deterministic automatic pitcher with a steady cadence, and a
long plate appearance that stays visually stable — the call always from the simulated flight.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Current state (inspected, after TASK-013)
- `PitchLocation` (TASK-012): nine offsets from each preset's own mid-zone aim (fixed metres, not the batter's zone),
  chosen with the arrow keys / D-pad (arrows yield to the tactical camera).
- Every pitch needs a press; no automatic pitcher. The catcher figure is hidden unless he plays a batted ball (he stands
  in front of the behind-the-plate cameras); a taken pitch flies on to 1 m behind the plate and stops there.

## Design
- Gameplay/Hitting `PitchTarget` (3×3 grid inside the batter's zone at 0.6 of its half-sizes, catcher's view; four spots
  0.22 m above / below it and 0.25 m beyond the plate's sides) and `PitchCommand` (preset index + target).
  `PitchTargets.Aim` corrects only the preset's release angles from where the simulated flight actually crosses the front
  plane of the plate (three deterministic iterations; speed, spin, release point unchanged). The call reads the crossing.
  Replaces `PitchLocation`.
- `AutoPitcher.Choose(seed, plate appearance, pitch number, count)`: SplitMix64 hash — no shared random state. Zone share
  by count (0.25 at 0–2 … 0.85 at 3–0); fastballs weigh 3 behind in the count, breaking balls 2 ahead, even otherwise.
  A convenience, not MLB pitch calling.
- Lab: `Target` (null = the preset's own aim; HittingLab default unchanged), keys U I O / J K L / N M , (zone grid),
  7 8 9 0 (balls up / down / left / right), Z / X (pitch type), P (auto); gamepad shoulders / Y; arrows / D-pad still
  step targets when the camera does not own them. Auto: after a result the next press happens exactly `AutoPitchDelay`
  (0.8 s) after the loop is ready — scheduled from the pitch's own times, not the frame — so the sequence is identical at
  any frame rate. The first pitch is the player's press.
- Catcher (presentation only): shown when the camera is not behind the plate (offset, tactical); a pitch not put in play
  ends in his glove at his glove plane (−0.35 m), the glove reaching for it over 0.3 s.

## Milestones
- [x] PitchCommand / targets / aim + AutoPitcher + EditMode tests (CountTests, PlateAppearanceTests, AutoPitcherTests).
- [x] Lab controls, auto cadence, catcher receive + PlayMode tests (AtBatPitchingTests, GameLabSceneTests).
- [ ] check.sh, runtime verification, reviews, Codex, merge.

## Limitations
- No pitcher command/accuracy model (the aim converges within 3 cm), no fatigue, no pitch sequencing beyond the count.
- The catcher is not shown in the behind-the-plate views (he would block them); there the ball stops 1 m behind the plate.
