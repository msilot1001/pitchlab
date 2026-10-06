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
- [x] Tuned on whole games: ≈ 0.25 fielding and 0.28 throwing misplays per team-game.
- [x] Tests (DefensiveVariabilityTests).
- [ ] check.sh; runtime walkthrough; reviews (physics, Unity, test); Codex; merge.
