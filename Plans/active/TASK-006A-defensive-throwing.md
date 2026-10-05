# TASK-006A: Defensive throwing foundation

## Goal
A fielder possesses the ball → transfers → throws toward a base → the ball travels on a real trajectory → the receiving fielder catches it → possession transfers. A missed throw stays a free ball. There are no runners and no outs.

## Owner
- **Writer:** Claude Code is the sole writer.
- **Read-only:** reviewers and Codex.
- **Branch:** `feat/task-006a-defensive-throwing`, from `main` 9214323 (TASK-005 merged).

## Architectural constraints
- Simulation ← Gameplay ← Presentation.
- The throw's flight is the validated ball simulator (`BallInPlaySimulation` with the pitch aerodynamic model), not a Rigidbody, so a missed throw bounces and rolls on the same physics.
- Explicit ball authority: exactly one owner at every instant (FreeBall / Possessed / Thrown).
- Gameplay decides release and catch; presentation follows.
- TASK-005 fielding and all earlier physics are unchanged.

## Design
- **Base geometry:** `FieldLayout.BasePosition(Base)` (Simulation), regulation geometry. The presentation's bags are moved onto it.
- **Throwing (`Gameplay/Fielding/Throwing.cs`):**
  - `ThrowProfile`: routine throw speed from Statcast arm strength, transfer time, release height.
  - `ThrowSolver`: release point, target and speed give the launch angle, by bisection on the real flight.
  - `ReceiverAssignment`: simple conventions.
  - Receiver: covers the base, then adjusts to the throw with the TASK-005 intercept solver. A throw counts as caught only on the fly.
- **`DefensivePlay`:** the fielding play, plus an optional throw, plus the authority timeline (AuthorityAt, Holder, BallPositionAt).
- **Presentation:** throw motion in the poser (transfer, cock, release, follow-through), the ball in the throwing hand until the authoritative release, the receiver's glove to the catch, the camera on the receiver.
- **FieldingLab:** throw-target controls and the throwing scenarios. HittingLab: default target (infield → 1B, outfield → 2B).

## Milestones
- [x] Base geometry; throw solver; receiver; DefensivePlay with authority; EditMode tests.
- [x] Presentation (throw motion, receiver, camera); FieldingLab controls and scenarios; HittingLab default.
- [x] Runtime scenarios; reviews; check.sh; commit.
- [ ] Codex review (merge only with no blockers).

## Verification
- **check.sh:** EditMode 440 passed / 446 (6 skipped, unchanged), PlayMode 41/41.
- **Console:** clean in Play Mode.
- **Scenario table** (FieldingLab, Game View plus eval). Every throw is caught on the bag (0.00 m off it). The ball is 2–9 cm from the throwing hand at release and 8 cm from the receiver's glove at the catch. Times are from contact.

| Preset | Thrower → receiver | Release | Catch |
|---|---|---|---|
| SS grounder → 1B | SS → 1B | +2.44 | +3.48 |
| Slow roller → 1B | P → 1B (holds for cover) | +2.18 | +2.72 |
| Hard grounder → 2B | CF → SS | +4.84 | +5.92 |
| Chopper → 1B | P → 1B (holds ~0.5 s) | +2.15 | +2.72 |
| Wall rebound → 2B | LF → SS | +5.80 | +9.38 |
| Unreachable deep ball → 2B | CF → SS | +5.60 | +8.88 |
| 3B grounder → 1B | 3B → 1B | +1.98 | +3.11 |
| 2B grounder → 1B | 2B → 1B | +2.55 | +3.08 |
| LF single → 2B | LF → SS | +3.44 | +4.92 |
| CF single → home | CF → C | +4.10 | +7.21 |

- **Throw shape (EditMode):** infield elevation 1.6–6°; CF → home 20.5°; average horizontal speed 0.72–0.96 of the release speed.

## Decisions / discoveries
- **Covering first:** the receiver breaks for the bag at contact plus reaction. Starting at possession was too late: the throws to first were all missed.
- **Pitcher fielding a chopper:** his throw beat the 1B to the bag. Now the thrower holds the ball for the cover, iterated to 1 ms.
- **Receiver on the bag:** the earliest-intercept solver pulled receivers 1.5–1.7 m off the bag toward the throw. They now take a throw within reach on the bag.
- **Solver range:** the solver's range check used 40° only. Near the maximum range the best arc is about 35°, so it now scans for it (the mutation-checked test fails without the scan).
- **Bags:** 18 in (OBR 2.03). FieldDressing draws them where gameplay has them, and a PlayMode test pins this.
- **Unity review:** fixed a regression where the pitcher takeover root was read after this frame moved him (frame-dependent), and a pose pop at the start of the wind-up.

## Remaining risks
- **Receiver adjust leg:** it starts from rest. This shows only off the bag after a late cover; none of the presets hit it.
- **Not modelled:** cut-offs, relays, left-handed throwers, wind in the aim. Long outfield throws are single throws of about 3.5 s.
- **1B as thrower:** the 1B → 2B default exists only because there are no unassisted put-outs. Real coverage when the 1B fields is the pitcher at first.
