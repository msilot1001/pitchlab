# Rules foundation (TASK-006B)

`Gameplay/Rules` decides outs and safe calls from authoritative gameplay state:
- the possession timeline (`DefensivePlay.HolderAt`);
- defender positions against a base-touch envelope;
- runner arrival from a replaceable timing model.

Colliders, meshes, animation and the camera never decide anything. Every result is a pure function of the play, so a replay is identical at any frame rate. There is no continuous baserunning yet (TASK-007).

## State
- **`Runner`:** identified by where he started the play, either the batter (`Base.Home`) or the base he occupied. In this foundation a runner advances at most one base.
- **`BaseOccupancy`:** first, second and third. There is one runner per base by construction, so two runners on one base cannot be represented.
- **Batter-runner:** exists when the ball is fielded in play. A caught fly retires him at the catch. A foul ball (no foul catches yet: TASK-005 lets foul flies drop) or a ball out of the park is a dead ball with no events. For a home run the batter and runners are actually awarded home; that award, and scoring, are not modelled yet.
- **`RulesPlay.RunnerStateAt(runner, t)`:** OnBase, Advancing, Safe or Out, plus whether he is still forced.

## Forces (`Forces`)
- **Force play (OBR Definitions):** the batter is forced to first when he becomes a runner. A runner on base is forced to the next base when every base behind him is occupied. Examples:

| Bases | Forced runners | Forced bases |
|---|---|---|
| empty | batter-runner | 1B |
| 1B | batter-runner, runner on 1B | 1B, 2B |
| 1B, 2B | + runner on 2B | 1B, 2B, 3B |
| loaded | + runner on 3B | 1B, 2B, 3B, home |
| 2B only, or 1B and 3B | the runner on 2B (or 3B) is **not** forced | — |

- **Removal (OBR 5.09(b)(6)):** the force is removed when a following runner is put out on a force play (the runner must then be tagged), and as soon as the runner touches the base he is forced to.
  - The code removes it on any out of a following runner. While each runner advances at most one base, every such out is a force out.
  - Not modelled: the force being reinstated when a runner retreats. `Forces.IsForcedAt(runner, before, events, t)` applies this to the chronological events.

## Runner timing: a temporary placeholder (`IRunnerTiming`, `ReferenceRunnerTiming`)
The rules ask only two questions: "when does he touch his next base" and "where is he". TASK-007 replaces the answers with running motion and the rules stay unchanged.
- **Home to first: 4.28 s from contact (MEASURED).** This is the MLB average: RHB 4.30 s and LHB 4.26 s, σ ≈ 0.2 s (Statcast 2016 data, FanGraphs home-to-first analysis).
- **One base with a lead, from contact: 3.75 s (DERIVED, a gameplay assumption).** It comes from the 27 ft/s average sprint speed (Statcast), a lead of about 15 ft at release (Statcast 2024 lead data) and a 0.25 s ground-ball read. The plausible range is 3.6–3.9 s.
- **Position:** a straight line along the base path at the constant average speed. It is a placeholder for tag checks only.
- **Scope:** one generic runner, with no ratings, leads, steals or acceleration.

## Base touch and tag
- **Base touch (`BaseTouch`):** a defender touches a base when his body centre is within **0.6 m** of the bag centre. That is a foot 0.3–0.4 m from the centre of mass plus the 18 in bag's half-width (ASSUMED; no measured envelope). A 1B stretch with the foot on the bag would reach 1.0–1.2 m. It is not modelled separately, because covering receivers stand on the bag.
- **Force out:** `DefensiveDecision.HoldsBallOnBase(play, defender, base, t)`. The defender holds the ball (the play's ball authority) and is within the touch envelope. A throw's force completes at the receiver's catch on the bag. A catch off the bag does not complete it: returning to the bag is not modelled yet.
- **Tag (`TagRules.CanTag`):** the defender holds the ball and the runner is within **1.0 m** of him (arm and glove, the fielding model's chest-height glove reach; ASSUMED). A tag play's out is the first moment, after the defender has the ball at the base, that the runner comes within reach before touching the base. This is exercised by scripted runners who run without being forced. TASK-007 drives real moving tag plays.

## Simultaneous arrival
- **The rule:** OBR 5.09(a)(10) and 5.09(b)(6) make the runner out only when he or the base is tagged *before* he touches the base. There is no tie clause.
- **The simulation's interpretation:** **the defense must be first by more than 1 ms** (`TimingCall.Simultaneous`, the simulation's timing resolution). A simultaneous arrival is SAFE.
  - This is our interpretation for a deterministic simulation, following the literal "before" and Jim Evans' reading.
  - MLB has published no official interpretation. Many umpires hold that "there are no ties" (Tim McClelland), which in practice means the runner must beat the throw.
- **The tolerance is deliberately tiny.** It removes floating-point accidents, not a margin.

## Events and outs (`PlayEvent`, `PlayResolution`)
- **Events:**
  - **FlyOut:** at the catch (TASK-005 `FlyCatch`).
  - **ForceOut:** the batter-runner at first is a force out, not a separate system.
  - **TagOut.**
  - **Safe:** the runner touches the base he was going to.
- **Chronological list:** events are in time order, and each runner appears at most once (enforced). Several outs per play can be represented.
- **Third out:** it ends the play, and nothing after it is recorded (OBR 5.09(d)). Whether a run that touched home before the third out counts (OBR 5.08(a) exception) is scoring, which is not modelled yet.
- **Not modelled:** the infield fly rule and runners' retouch after a caught fly.
- **Outs:** they are counted from the events: `Outs`, `OutsAfter` and `OutsAt(t)`. The catch, the possession and later states never count an out again. There are no innings yet.
- **`StatusAt(t)`:**
  - Live, OutRecorded and RunnerSafe, according to the latest event;
  - BallDead for a foul or a ball out of the park;
  - PlayComplete after the last event and the end of the defense's action.

  The batting loop only observes `EndTime`.

## Defensive actions and the decision (`DefensiveDecision`)
- **Candidates:** after possession, the holder's candidates are:
  - **HoldBall:** valid, no out.
  - **TouchBase:** carry the ball to a forced runner's base himself. A `ContinuationMotion` from his position and velocity at the take, so no snap or stop. Completion is the earliest moment he is within the touch envelope. He crosses the bag at speed, as a fielder stepping on it does.
  - **ThrowToBase:** the TASK-006A throw to the covering receiver. Completion is the catch on the bag.
  - **TagRunner:** a runner who is not forced but runs (scripted), either by throw or by carry. A carry for a tag ends at rest on the bag (`ContinuationMotion.ToRest`); running through it, he could not tag.
  - **Returning the ball:** an outfielder's throw to second when there is no play.

  Each candidate carries its target, completion time, the runner it can retire with his arrival time, feasibility, whether it retires him, and the resulting `DefensivePlay`.
- **Double plays:** a force at second with the batter-runner forced too is flagged `DoublePlayPossible` when two things hold:
  - the force at second retires the lead runner;
  - a relay estimate (the receiver's transfer plus his throw) beats the batter-runner to first.

  Double plays are not executed.
- **Margin:** for a force, the runner's arrival minus the defense's completion. For a tag, his arrival minus the tag (−∞ for a tag that never comes).
- **Choice (`Choose`), deterministic and explainable:**
  1. an immediate out (the holder already stands on the forced runner's base);
  2. the earliest force out (a touch before a throw when equally early);
  3. the earliest tag out;
  4. a close play the runner wins by less than 0.5 s (ASSUMED): it is still made, so the umpire sees a SAFE;
  5. an outfielder returns the ball to second; anyone else holds it.

  This picks the unassisted put-out whenever touching the bag beats the throw, so it is a generic rule rather than a preset. Example: a 1B grounder is an out at +2.08 s by touch, against +4.94 s for a flip to the covering 2B.
- **Not modelled:** cut-offs, game situation, run expectancy, double-play execution, tagging up after a caught fly (TASK-007/008), rundowns.

## Motion continuity (`ContinuationMotion`)
- **The law:** the TASK-005 sprint law generalised to an initial velocity v₀: v(t) = u·d̂ + (v₀ − u·d̂)·e^(−t/τ).
  - Momentum alone carries the defender to c(t) = start + v₀·τ(1 − e^(−t/τ)).
  - To be at a target exactly at time T, he runs at u = |target − c(T)| / (T − τ(1 − e^(−T/τ))). That is ≤ v_max when reachable, and a jog when there is time.
  - After T he brakes along his velocity.
- **Intercept solver:** `InterceptSolver` takes the same initial velocity, measuring the route from c(t).
- **Receivers:** they adjust to a throw from their current velocity. Before this change the adjustment leg started from rest, an instant stop of up to ~4 m/s. EditMode tests check position and velocity continuity, and that the change in velocity never exceeds 0.03 m/s per ms.
- **Fielders carrying the ball to a base** use the same law.

## Labs
- **FieldingLab:**
  - keys 1–8 are the rules scenarios, ←/→ all presets;
  - Z/X/C/V throw to 1B/2B/3B/home, N holds, B uses the defense's decision;
  - the overlay shows bases, runners and forces, candidates with timing and feasibility, the choice and reason, the events and the outs;
  - the call (OUT AT 1B, SAFE AT 1B, FLY OUT, OUT AT 3B (tag)) appears at its authoritative moment.
- **HittingLab:** bases are empty; the banner appends the call.
- **The SAFE scenario:** across ~1,700 grid infield balls with the average runner, the idealized TASK-005 fielding never loses a race to first by itself. Fielders never bobble, field on the run, and transfer in a fixed 0.70 s. The one natural close play the runner wins is the 1B ranging far to his right and racing him to the bag, 0.11 s late. Infield hits on slow choppers need fielding imperfection, which is a known limitation.
