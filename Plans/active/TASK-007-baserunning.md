# TASK-007: Continuous baserunning

## Goal
Replace TASK-006B's runner-timing placeholder with real runners. Runners must:
- move continuously on authoritative base paths, under a deterministic running law;
- decide (force, hold, extra bases, tag-ups) with replaceable heuristics;
- touch bases at exact times.

Forces, outs and runs come from gameplay state. Presentation follows.

## Owner
- **Writer:** Claude Code.
- **Read-only:** reviewers and Codex.
- **Branch:** `feat/task-007-baserunning`, from `main` 4597383 (TASK-006B plus the Phase 0 performance merge).

## Phase 0: performance (merged before this branch)
Profiled TASK-006B planning. The throw solver spent 63 of 79 ms per throw plan allocating sample lists.

| Item | Before | After |
|---|---|---|
| Throw plan | 79 ms | 4.8 ms |
| Resolve, bases loaded | 331 ms | 26 ms |
| Resolve, bases empty | 89 ms | 5 ms |
| Contact frame | — | 7–39 ms |

Codex: B (Illinois termination), fixed and merged.

## Design
- **Base paths:** `BaseLeg` (straight or banana) through the bag centres.
- **Runner motion:** `PathMotion`, the 1D sprint law with an initial velocity, braking, and reversal. `RunnerPlanner` gives the same plan for prediction and execution.
- **Runners:** `LiveRunner`, gameplay state with motion history.
- **Decisions:** `RunnerBrain`.
- **Engine:** `LivePlay`, a discrete-event engine on a 1/120 s tick grid with exact event location:
  - base touches;
  - force, tag and retouch outs from possession plus envelopes;
  - runs, third out, play end;
  - a log.
- **Defense:** still TASK-006B's single action, chosen at contact from the runners' predicted arrivals via `IRunnerTiming` (TASK-008/009 extend it).
- **Presentation:** `RunnerView`. FieldingLab and HittingLab use `LivePlay`.

## Milestones
- [x] Phase 0 performance (merged).
- [x] Base paths, running law, planner, runners, engine, decisions; EditMode tests (13).
- [x] RunnerView; FieldingLab and HittingLab on LivePlay; PlayMode tests.
- [x] Reviews (rules, physics, Unity, tests), check.sh, runtime, Codex (B: infeasible stop, banana on hits; fixed), merge.

## Verification
- **check.sh:** EditMode 487 passed / 493 (6 skipped, unchanged), PlayMode 46/46.
- **Console:** clean.
- **Runtime (FieldingLab, Game View):** runners shown in the offense colour on their gameplay paths:
  - wall ball with a runner on first: he rounds second and third and scores;
  - runners on first and third, CF single: the run scores, first to second;
  - tag-up from third on a deep fly;
  - forced runners on a bases-loaded grounder.
- **Runtime (HittingLab):** the batter hands over to the batter-runner at contact + 0.34 s and is back at the plate in Ready after the play.
- **Codex:** B. An infeasible stop was snapped onto the target; it now passes it continuously, rests beyond and returns. A runner rounding mid-leg kept a straight leg; on hits every leg is now the banana route from its start, in both prediction and execution. Regression tests added.

## Decisions / discoveries
- **Arrival times:** slides on 2B/3B (10 m/s²) give force-play arrivals of ~4.2 s from contact for a runner on first. Without the slide model it was ~4.5 s.
- **Out detection within a tick:** out conditions must be sampled within a tick. A force completed just before the runner's foot arrived was missed when only the tick's end was checked, and the force-boundary test found it.
- **Safe calls:** these are logged only for plays on a runner, not for a return throw.
- **Review fixes:**

| Review | Fixes |
|---|---|
| Rules | OBR 5.08(a) runs on a force, fly-out or batter-before-first third out; overrun protection only while returning from an overrun; dead-ball guard on outs |
| Physics | exact stops on the bag; reversal brakes first; foot-on-bag touch time for rules and predictions (−0.245 s on slides); cubic banana without a kink; out conditions probed at their exact boundary instants (a 1.1–3 ms force is always an out) |
| Unity | figures toggled once; the batter returns to the plate after the play; no per-tick closures; the engine stops after the end; calls built from event kinds |
| Tests | the force-boundary test now detects removal of the 1 ms window, plus a 1.1–3 ms sweep; every play must end for a reason; protected overrun with the first baseman holding the ball; double-off at second; foul with runners; send margin flips with outs; third-out run rule; mid-run prediction = execution; exact stops and reversal |

- **Calibration:** contact → first step recalibrated to 0.34 s (range 0.20–0.35 s), so the foot reaches first at 4.28 s over the plate-centre → bag path (27.05 m).
- **Mutations:**
  - Caught: no simultaneous window, no foot-window probes, no 5.08(a).
  - Not caught: "no overrun protection". The play ends once the ball is held and every runner is settled, and the protected batter's walk back happens after that. Protection only matters while a play is still live, which first arises with TASK-009's chained defensive actions; the decisive test belongs there.

## Remaining risks
- **Defense:** TASK-006B's single action, decided at contact; no team coverage (TASK-008), no chained actions (TASK-009).
- **Overrun protection:** after rounding first on a hit (banana), the batter-runner is still treated as returning from an overrun. Also untested until plays continue after the first action (TASK-009).
- **Not modelled:** force reinstatement on retreat; retouching an original base after passing one (only lab scripting could produce it); home runs and the infield fly (TASK-009).
- **Runners read the future:** they know at contact whether a fly will be caught (`Kind` comes from the fielding solution).
