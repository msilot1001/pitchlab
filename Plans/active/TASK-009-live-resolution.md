# TASK-009: Live play resolution

## Goal
Multi-action plays decided by the live state: double plays earned by timing (6-4-3, 4-6-3, 1-6-3, 5-4-3) or failed, live tag
plays, tag-up throws home, home runs (and the ground-rule double award), third-out scoring per OBR, play end and event log.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Design
- Situation → alignment: double-play depth with a runner on first and fewer than two out (`Situation.Alignment`).
- After an out the holder decides again (TASK-008 `OnOut`); that throw is the turn of a double play: full effort.
- Outfielders throw at full effort; a direct throw home is made when it is a close play.
- Awards on a fair ball out of the park: `LivePlay.AwardedBases` (4 / 2), runners advance under the running law; dead ball.
- Play end: every runner settled on a base — the batter-runner back from an overrun too (protection is then decisive).
- Infield fly: documented deferral (no dropped balls → no effect on outcomes).
- Event log: `Log` (narrative + kinds) and `RulesEvents` (outs, runs) as before.

## Milestones
- [x] Double-play depth; full-effort turn; DPs and failed DPs by timing.
- [x] HR / GRD awards; third-out runs (TASK-007 rule, re-verified on tag at home).
- [x] Tag-up throws home (close play safe / out).
- [x] Overrun protection made decisive (play live until the batter-runner is back on first) + mutation check.
- [x] Tests (LivePlayResolutionTests 15); docs.
- [ ] check.sh; runtime examples; Codex; merge.

## Verification
- check.sh: EditMode 520/526 (6 skipped, pre-existing), PlayMode 46/46.
- Runtime (FieldingLab Game View): R1 grounder to SS — OUT AT 2B at ~2.6 s, then OUT AT 1B (banner "OUT AT 2B · OUT AT 1B");
  console clean.
- Examples (EditMode/eval): 6-4-3 85 mph out at 1B by 0.11 s; 5-4-3 by 0.10 s; 4-6-3 88 mph by 0.01 s; 1-6-3 by 0.01 s;
  70 mph roller: force at 2B, batter safe (failed DP); 95 mph to 2B: safe by 0.01 s. Loaded HR: 4 runs. Tag-ups: safe at home
  on a close throw (78 mph / 38°), tagged out at home for the third out, no run (76 mph / 40°).

## Decisions / discoveries
- Pivot catch-to-release stays 0.70 s (college/pro pivots 0.7–0.9 s, Baseball Prospectus / coaching sources); the routine
  0.85 arm factor on the turn was what lost every DP — full effort turns 6-4-3 / 5-4-3 by ~0.1 s.
- Hard 4-6-3 balls can still fail: the shortstop at DP depth needs ~2.8 s to stand on second (covers brake to a stop at the
  bag — no crossing-the-bag pivot). Realistic enough for the slice; a running pivot would turn more.
- Full-effort outfield throws changed TASK-008 outcomes: R2 2-out single to centre is now a close play at the plate (tag out),
  a gap ball with R1 2-out a direct throw to second.
- No relay on a caught fly; the relay man is preferred over the home cut-off as the outfielder's target.
- Ground-rule doubles never occur in the simulated park.

## Remaining risks
- Home-run trot at sprint speed (~15 s).
- No fielding errors, so infield fly, off-bag and missed-throw paths have no effect / are untested.
