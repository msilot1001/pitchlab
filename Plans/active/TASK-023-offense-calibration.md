# TASK-023: Offense calibration

## Scope note
The detailed TASK-023–028 specification never reached this session; only the titles did (the long-session request of
2026-10-06). This plan takes its scope from the title, the request's architecture rules and the discrepancies TASK-022
measured (Docs/AI_EXHIBITION.md).

## Goal
Simulated games should produce MLB-like offense: runs, home runs, strikeouts, walks, contact, exit velocity and launch
angle. This is reached by fixing the causes, in the CPU hitter's inputs and in an incomplete contact geometry. It is not
reached by scaling results or retuning aerodynamics.

## Owner
Claude Code (sole writer); physics, test and Unity reviewers and Codex read-only.

## Current state (measured, TASK-022 sample)
16 runs per game, 8.7 HR per game, EV 96 mph, hard-hit 65 %, HR/FB 38 %, K 16 %, zone contact 76 %, chase contact 71 %.

## Findings
- **The contact model's bat was only a 10-inch barrel moving at one speed.** Any contact beyond ±5 in was a miss, so the
  weak contact that makes MLB's EV distribution (jammed, off the end) could not happen: more error only made more whiffs.
  The full bat (end to near the hands, tapering, turning about a pivot) is physical and is listed as a known limitation in
  Docs/HITTING.md.
- **The CPU hitter's contact was too centred:** his along-barrel and vertical errors were one value, tuned for contact
  rate only.

## Design
- **Contact model** (`SwingParameters`, `ContactResolver`):
  - contact from 6 in toward the tip to 14 in toward the handle;
  - the bat tapers to a 0.6 in handle radius;
  - bat speed varies along the bat as a rotation about a pivot 0.70 m from the sweet spot;
  - Nathan's q is unchanged.
- **CPU hitter:** separate along-barrel and vertical aim errors, a lift intent (he aims slightly under the ball's centre),
  and a reach penalty (a pitch outside the zone is harder to square up).
- **Harness:** `OffenseCalibrationTests` (Report, plus regression guards); `GameSimulator.LastContact`.
- **Lab scenes:** they lack the new swing fields, so they get the default bat's in code.

## Milestones
- [x] Harness; MLB reference values (FanGraphs and Savant 2024–25).
- [x] Contact-model geometry; CPU hitter levers.
- [x] First calibration pass (30 games, before the park fix): runs 4.45 per team-game, K 21.1 %, BB 9.5 %, EV 87.4 mph, hard-hit 38.8 %, barrel 7.0 %.
- [x] Physics review:
  - the pivot (Cross 2009) and local speed with Nathan's q are correct;
  - added the tip-side negative q floor (−0.10);
  - an exit-speed-along-the-bat test.
- [x] Contact tests updated for the physics (not loosened); golden re-pinned; seed pins; calibration guards.
- [x] Docs: OFFENSE_CALIBRATION.md, HITTING.md ("The whole bat"), BATTER_AI.md.
- [x] Codex round 1 (grade C: home runs ≈ 2× MLB). Every link in the chain was checked against measured 2024 Statcast data:
  - carry matches the validated flight;
  - EV/LA match MLB;
  - the park did not: a straight-chord fence, and dimensions shallower than MLB's average.

  Fixed:
  - a smooth polar fence (37 points);
  - an MLB-average park (332/385/405 ft, 8-ft wall), fitted to the measured P(HR | distance);
  - CPU timing 13 ms with a 2 ms early bias.

  Result: HR 1.27 per team-game (MLB 1.13), HR/FB 12.1 %.
- [x] Park-dependent tests re-pointed: their scenarios keep their intent in the new park.
  - The new dimensions in fence and wall tests.
  - New inputs for "thrown out stretching", the jumping catch and "a close play at second".
  - The missed-fly search now requires a ball untouched by ground or wall.
  - Game-ending seeds 5 and 62.
  - The FieldingLab gap-ball preset now gives a close slide (98 mph, 14°, −15°).
- [x] Two presentation defects exposed by the new plays and fixed:
  - a low wall play is now shown as a pickup;
  - the glove arm's IK now puts the glove anchor, not the wrist, on the ball. The glove went 8 cm past the ball and into
    the ground on low backhands.
- [x] Runs are ≈ 25 % low. Traced to defensive conversion by batted-ball type (GB .353 / LD .508 / FB .052 vs MLB
  ≈ .24 / .68 / .13), documented and handed to TASK-033/037.
- [x] check.sh green (EditMode 705 passed, 6 skipped; PlayMode 103/103).
- [ ] Unity review; Codex round 2; merge.
