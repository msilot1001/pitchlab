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
- [x] Continuation motion, the receiver continuity fix and its regression test; the thrower-settling regression test.
- [x] Rules model (runners, occupancy, forces, timing, events, actions, decision, resolver) with EditMode tests.
- [x] Unassisted touch in DefensivePlay; integration with FieldingLab and HittingLab; presentation; PlayMode tests.
- [x] Research and rules review, Unity and test reviewers, check.sh, runtime checks, Codex review (B: the third out now ends the play exactly; fixed and tested), merge.

## Verification
- **check.sh:** EditMode 469 passed / 475 (6 skipped, unchanged), PlayMode 45/45.
- **Console:** clean.
- **Mutations:** each one is caught by the tests:
  - ties go to the defense;
  - no force removal;
  - the receiver restarts from rest;
  - no touch candidates;
  - tag ranked before force;
  - close window narrowed to 0.35 s;
  - double-play flag without the lead out.
- **Scenario table** (FieldingLab, Game View plus eval; times from contact):

| Scenario | Bases | Chosen | Defense | Runner | Result |
|---|---|---|---|---|---|
| SS routine grounder | empty | Throw 1B | +3.48 | +4.28 | OUT AT 1B |
| 1B ranges right | empty | Touch 1B (close play) | +4.39 | +4.28 | SAFE AT 1B |
| 1B grounder | empty | Touch 1B, no throw | +2.08 | +4.28 | OUT AT 1B |
| Runner on 1B, SS grounder | 1B | Throw 2B | +2.89 | +3.75 | OUT AT 2B |
| Runner on 1B, 2B grounder | 1B | Throw 1B | +3.08 | +4.28 | OUT AT 1B |
| Bases loaded, 3B grounder | loaded | Touch 3B | +2.64 | +3.75 | OUT AT 3B |
| Routine fly | empty | Hold | +5.22 | — | FLY OUT |
| Runner on 2B runs (scripted) | 2B | Tag at 3B (throw) | +2.99 | +3.75 | OUT AT 3B (tag) |

## Decisions / discoveries
- **Runner timing:** 4.28 s home to first (MEASURED); 3.75 s for one base with a lead (DERIVED).
- **Ties:** a simultaneous arrival (within 1 ms) is SAFE. This is our reading of OBR "before"; MLB has no official ruling.
- **Base touch:** the body centre within 0.6 m of the bag centre (ASSUMED).
- **Tag reach:** 1.0 m (ASSUMED).
- **Close-play window:** 0.5 s (ASSUMED).
- **No natural infield hit:** across ~1,700 grid infield balls the idealized fielding never loses a race to first by itself. The SAFE scenario is the 1B ranging right, 0.11 s late.
- **Receiver adjustment:** before, it restarted from rest (up to ~3.9 m/s lost instantly). It is now continuous (`ContinuationMotion`, which the intercept solver also uses).
- **Carried tags:** they stop on the bag (Unity review). Force touches still cross it at speed.
- **Third out:** it ends the play's events (rules review, OBR 5.09(d)) and the play itself, even while the defense is still moving (Codex review).
- **Batting-lab test premise:** the HittingLab test's "topped grounder" was a CF single. It now uses an on-time swing (an SS grounder) and keeps the single as a separate no-play test.

## Remaining risks
- **Solve cost:** in the Editor, `PlayResolver.Resolve` takes ~90 ms with the bases empty (as in TASK-006A) and ~330 ms with them loaded, on the contact frame. One throw is planned per base.
- **Not modelled:**
  - running motion (TASK-007);
  - tag-ups, double-play execution, run scoring, home-run awards, infield fly;
  - a receiver returning to the bag after an off-bag catch;
  - force reinstatement on retreat.
- **Debug route:** `DefenseView` still draws the original fielding route during a carry.
