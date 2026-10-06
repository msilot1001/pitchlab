# TASK-018: Pitcher repertoire + pitch execution

## Goal
A pitcher aims at a location but executes an imperfect physical pitch from his abilities: repertoire, speed, spin,
command, handedness, mild fatigue — the existing pitch physics then decides where it goes.

## Owner
Claude Code (sole writer); physics / test reviewers and Codex read-only.

## Current state (inspected, after TASK-017)
- Pitches come from five right-handed presets (`PitchPresets`) aimed exactly at a target by correcting release angles
  (`PitchTargets.Aim`, within 3 cm). Every pitcher can throw everything; no variability; no handedness.
- Ratings exist (Velocity, Command, Movement, Stamina) but are unused.

## Design (Docs/PITCH_EXECUTION.md)
- `PitchType` (= preset order), `RepertoirePitch` (usage, speed offset, familiarity), `Repertoire`; pitchers carry one
  (power / command / breaking-ball repertoires). The home starter is a left-hander.
- `PitchExecution`: the pitcher's pitch (league speed by type + rated offset; spin by Movement; left-handers mirrored —
  release side, azimuth, spin axis θ → −θ, gyro γ → −γ); `Execute`: correlated release-angle errors scaled by command and
  familiarity, a 3 % heavy tail, speed / spin / axis errors, mild fatigue. `SeedStream` (Rules): hash-keyed deviates.
- `GamePitches.Create(game, type, target, variance)`: one path for the GameLab and the simulator — the mound's pitcher,
  his pitch aimed at the target in the batter's zone, executed from `GameState.Seed`, the plate appearance and the pitch
  number; the record keeps the target (`PitchInfo.TargetX/Z`). `GameState`: `Seed`, `Pitcher`, `PitchCount(team)`.
- Lab: `ExecutionVariance` (debug toggle, default on); pitch-type keys cycle the repertoire; the auto pitcher picks from
  the repertoire by usage (fastballs favoured behind in the count); the delivery figure mirrors for left-handers. Labs
  without a game throw the presets exactly.

## Milestones
- [x] Repertoire, execution, mirror, seeds, GamePitches, lab/simulator integration, presentation mirror.
- [x] Tests: PitchExecutionTests (determinism, command ordering, speed/spin spread, familiarity, fatigue, mirror,
  repertoire, game seed), AtBat frame-rate determinism with variance on; scripted GameLab suites throw as intended
  (variance off — they test the at-bat loop); simulator seed pins re-chosen.
- [ ] check.sh, runtime, reviews (physics, test), Codex, merge.
