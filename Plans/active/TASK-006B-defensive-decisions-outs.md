# TASK-006B: Defensive decisions and base/out foundation

## Goal
Give fielding and throwing enough baseball rules to choose a sensible defensive action and to resolve basic outs and safe calls:
- fly outs, force outs (including the batter-runner out at first) and unassisted put-outs;
- tag-out groundwork;
- an authoritative, chronological play-event list with an out count.

There is no continuous baserunning: TASK-007 replaces the runner timing placeholder with locomotion.

## Owner
- **Writer:** Claude Code is the sole writer.
- **Read-only:** reviewers, the research agent and Codex.
- **Branch:** `feat/task-006b-defensive-decisions-outs`, from `main` 36e0326 (TASK-006A merged).

## Architectural constraints
- Simulation ← Gameplay ← Presentation. The rules live in `Gameplay/Rules`. Presentation only reads them.
- Gameplay decides outs from authoritative state:
  - possession timeline;
  - defender positions against a base-touch envelope;
  - runner arrival from an explicit, replaceable timing model.
  Colliders, meshes and animation never decide them.
- Every result is a pure function of the play (and of time), so it is deterministic at any frame rate.

## Design
- **Motion continuity (`ContinuationMotion`):** the TASK-005 sprint law generalised to an initial velocity. Running to a point by a given time is closed-form (a jog when there is time to spare). The intercept solver accepts an initial velocity. Two uses:
  - the receiver's adjustment to a throw, with no instant stop;
  - a fielder carrying the ball to a base after possession.
- **Rules (`Gameplay/Rules`):**
  - `Runner` (the batter, or the base a runner started on) and `BaseOccupancy` (one runner per base by construction).
  - `Forces`: the initial force chain and force removal.
  - `IRunnerTiming` with `ReferenceRunnerTiming`: arrival times from sourced references. This is a placeholder that TASK-007 replaces.
  - `PlayEvent` (FlyOut, ForceOut, TagOut, Safe) and `PlayResolution` (chronological events, outs before and after, status over time).
  - `DefensiveAction` candidates: Hold, TouchBase, ThrowToBase, TagRunner. Each has a target, a completion time, the runner it can retire with his arrival time, feasibility, and the resulting `DefensivePlay`.
  - `DefensiveDecision`: picks among the candidates with a priority list.
  - `PlayResolver`: the fielding play plus the occupancy give the candidates, the chosen action and the resolution.
- **Tag groundwork:** `TagRules.CanTag` takes defender position, runner position, possession and reach. It is exercised by a scripted runner that advances without being forced.
- **Labs:** FieldingLab gets rules scenarios, an occupancy, force and decision overlay, and OUT/SAFE text. HittingLab gets the result banner.

## Milestones
- [ ] Continuation motion, the receiver continuity fix and its regression test; the thrower-settling regression test.
- [ ] Rules model (runners, occupancy, forces, timing, events, actions, decision, resolver) with EditMode tests.
- [ ] Unassisted touch in DefensivePlay; integration with FieldingLab and HittingLab; presentation; PlayMode tests.
- [ ] Research and rules review, Unity and test reviewers, check.sh, runtime checks, Codex review, merge.

## Verification
(filled in as it runs)

## Decisions / discoveries
(filled in as it runs)

## Remaining risks
(filled in at the end)
