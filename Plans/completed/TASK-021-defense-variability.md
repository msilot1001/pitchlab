# TASK-021: Fielding / throwing variability and misplays

## Goal
Defenders can fail, physically: difficulty plus skill plus a seeded draw decide the take or the throw. The ball stays live
and the rules read the consequences. There is no outcome coin flip.

## Owner
Claude Code (sole writer); physics, Unity and test reviewers and Codex read-only.

## Current state (inspected)
- `LiveDefense` schedules perfect possessions. Throws are physical flights (`ThrowPlanner.PlanLive`), receivers can step
  off the bag, and a missed throw becomes a free ball retrieved by the nearest defender (`Loose`). None of this happens in
  practice, because every throw is on target.
- `LivePlay.Kind` is fixed at contact: a fielded fly is a catch.

## Design (Docs/DEFENSIVE_VARIABILITY.md)
- **Execution model:** `FieldingExecution`:
  - `FielderSkill` (Fielding, Catching, ArmAccuracy), carried by `PlayPersonnel` and filled from the rosters by
    `GameState.Personnel`;
  - the chance of a take by kind, margin, speeds and skill; the outcome (clean, bobble or drop, miss);
  - the throw direction error.
- **Defense:** `LiveDefense`:
  - `Attempt` around every take;
  - `Misplayed`: a miss keeps the ball on its path; a bobble or drop is a new ball from the glove;
  - `LooseBall`, generalised from `Loose`;
  - `Executed`: the chosen throw is re-planned with its error. The decision used the aimed throws.
- **Play:** `LivePlay`:
  - `ExecutionSeed` (null means perfect);
  - a mutable `Kind` (a fly not held becomes a hit ball);
  - `OnMisplay` and `Misplays`.
- **Throws:** `ThrowPlanner.PlanLive` takes an aim error (no new hold); receivers on a base stretch 0.6 m.
- **Seeding:** `GameState.PlaySeed`; the simulator and the GameLab (with `ExecutionVariance`) seed plays.

## Milestones
- [x] Execution model, defense integration, throw error, stretch, seeds, simulator misplay record.
- [x] Tuned on whole games: ≈ 0.27 fielding and 0.12 throwing misplays per team-game.
- [x] Tests (DefensiveVariabilityTests).
- [x] Reviews:
  - Physics:
    - dropped throws are followed over their whole flight;
    - a ball out of the field ends the play;
    - a missed fly stays catchable until it lands (OBR);
    - the stretch applies only to errant throws (no change without a seed);
    - dive and bobble recovery; a capped rebound;
    - runners read the aimed throw;
    - "pulled off the bag" counts only on force plays.
  - Unity:
    - the readout reads the held take (no phantom "caught by");
    - focus per segment (`Segment.Retriever`);
    - a misplayed attempt is shown (`BallTake.Held`);
    - the HUD flash is cached.
  - Test: the requested assertions were strengthened and new tests added (see Docs/DEFENSIVE_VARIABILITY.md).
- [x] Runtime (FieldingLab, E/R execution toggle, crew 10):
  - LF's dive misses, the ball rolls on to the wall, LF gets up and retrieves it;
  - 2B bobbles a forehand pickup and retakes it 1.05 s later, so the double play becomes a single force;
  - SS throws 2.2 m off, the throw gets past 1B, and the batter-runner takes second;
  - a receiver drops a throw.
  Before its fix, the overthrow was only noticed when 1B gathered it, 6 s later. It is now a misplay seen at the bag, with a
  test.
- [x] Codex: C — fixed:
  - the free ball is installed before runners react;
  - the infield fly rule (no drop modelled);
  - the grounded check respects an air catch.
- [x] Codex round 2: C — fixed:
  - the out-of-play award (two bases, or a home run over the fence on the fly; no outs on a dead ball; the defense stops);
  - the infield-fly guard is limited to ordinary effort;
  - stepped-time tests replace the inconclusive search.
- [x] Codex round 3: C — fixed:
  - a loose ball is fieldable only until it leaves play;
  - a fly over the fence off the glove is a home run (`AwardedBases`);
  - the grounded event is ignored on a dead ball.
- [x] Codex round 4: C — fixed:
  - an uncaught errant throw is seen as it passes the bag;
  - the fielded/caught log follows the current kind.

  Not changed: a two-base out-of-play award after a misplay is classified by where the batter ends, not as a ground-rule
  double (OBR: that is a fair ball bouncing over the fence). This is documented and tested.
- [x] Codex round 5: C — fixed: an overrunning or retouching runner still receives the out-of-play award (it waits until he
  can go), with a live-state test. Codex accepted the classification of a two-base award by the batter's final base.
- [x] Codex round 6: B — fixed:
  - a throw clearing the fence ends there;
  - the label test proves every misplayed play is labelled.

  check.sh green (EditMode 697 passed, 6 skipped; PlayMode 101/101). Merged.
