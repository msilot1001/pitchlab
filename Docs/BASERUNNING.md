# Baserunning (TASK-007)

Runners are gameplay entities (`Gameplay/Running`, `Gameplay/Play`). They run a deterministic law along authoritative base paths, decide with replaceable heuristics, and touch bases at exact times. Presentation (`RunnerView`) only shows them. Labels: MEASURED (tracking data), REPORTED (secondary source or coaching text), DERIVED (computed from those), ASSUMED (gameplay baseline).

## Base paths (`BaseLeg`)
- **Legs:** home → 1B → 2B → 3B → home, on the ground, always passing through the bag centres (`FieldLayout.BasePosition`).
- **Straight vs banana:**
  - A leg that ends at the runner's destination is straight.
  - A leg ending at a base he will round is the **banana route**: from 55 % of the leg he drifts outward, away from the diamond, up to 1.2 m (≈ 4 ft), then cuts back across the bag toward the next base.
  - Coaching advice is to start the turn ~10 m before the bag (REPORTED). The 3–6 ft drift is ASSUMED.
  - Arc length is tabulated, so runners move by distance travelled.

## Running law (`RunnerProfile`, `PathMotion`)
- **Model:** along the leg, the sprint law v(t) = s·v_max + (v₀ − s·v_max)·e^(−t/τ) with s = ±1, so a runner can reverse, then constant braking to the end speed at the target. Position and velocity are continuous and exact functions of time.
- **Constants:**

| Quantity | Value | Basis |
|---|---|---|
| Top speed | 27 ft/s (8.23 m/s) | MLB average Statcast sprint speed, MEASURED |
| τ | 0.69 s | fits the average 90-ft split of 4.02 s from the first step at 27 ft/s (Statcast running splits), DERIVED |
| Contact → first step (batter) | 0.25 s | 4.27 s home-to-first average − 4.02 s split, DERIVED |
| Home to first (result) | 4.27 s | MLB average 4.26 LHB / 4.30 RHB, MEASURED; test ±0.06 s |
| Read before committing (runner on base) | 0.25 s | ASSUMED |
| Rounding speed | 0.8 · v_max | 15–25 % lost per turn, optimal-path model (Illinois), REPORTED |
| Braking (stops, overrun of first) | 6 m/s² | ASSUMED; first is overrun by ~15–20 ft (15–25 ft REPORTED) |
| Sliding stop on 2B / 3B | 10 m/s² | ASSUMED |
| Leads at contact | 1B 12 ft, 2B 20 ft, 3B 10 ft | 1B: Statcast secondary lead 11.4–13.5 ft, REPORTED; 2B: coaching primary 15–18 / secondary ~29 ft; 3B ASSUMED |
| Home to third (prediction) | ≈ 11.5 s | records scaled to an average runner, DERIVED; test 11.0–12.5 s |

## Prediction equals execution (`RunnerPlanner`)
- **The plan:** "go to base X" from (leg, distance, velocity) runs the leg's remainder, rounds each intermediate base at ≤ the rounding speed, and finishes at X:
  - stopped on it (sliding on 2B/3B);
  - through it for first base (an overrun the rules allow);
  - through it for home.
- **Same law both ways:** the planner's arrival time is what the executed run produces. A test checks this to 1e-6 s.
- **Uses:** the defense's decision (through the TASK-006B `IRunnerTiming` interface) and the runners' own decisions both read these predictions. The 006B placeholder timing is no longer used in normal play.

## Decisions (`RunnerBrain`, deterministic, no randomness)
- **Send margin:** a runner goes to a base when his predicted arrival plus a margin beats the defense's estimated time to have the ball there.
  - The margin is 0.30 s with none out, 0.15 s with one, and −0.10 s with two. These are DERIVED from the run-expectancy break-even for sending a runner (88 % / 71 % / 27 %).
  - The defense's estimate covers who has the ball, or where it will be fielded, plus the transfer, plus a throw at that fielder's routine speed covering the distance at 0.9 of it. Tag plays add 0.15 s for the tag.
- **At contact (standard coaching conventions, REPORTED/ASSUMED):**
  - **Two out:** everyone runs on contact.
  - **Ground ball to an infielder:**
    - forced runners go;
    - an unforced runner on second goes on a ball to the right side (behind him);
    - other unforced runners hold at their lead and go back when the ball is fielded unless a base is safely there.
  - **Hit to the outfield:** everyone runs, and extra bases are decided on the way.
  - **Caught fly, fewer than two out:**
    - A **line drive** (caught < 2 s after contact): runners freeze.
    - A **fly:** the runner on third tags up when the catch is > 45 m from home (also the runner on second when it is > 75 m); other runners go **halfway** (40 % of the leg).
- **On the way:** at the moment he must start braking to stop at his destination (the same motion until then, whatever he decides), he decides whether to round it for the next base. He never goes to a base a runner ahead of him is going to or holding.
- **At ball events (fielded, throw released, a missed throw):** standing or holding runners reconsider.

## Fly balls, tag-ups, retouching
- **When runners may leave:** at the catch, which is the first touch ("Runners may leave their bases the instant the first fielder touches the ball", OBR 5.09(a)(1) Comment).
- **Retouching:**
  - A runner off his base at the catch must go back and retouch it (OBR 5.09(b)(5), 5.09(c)(1)).
  - If a defender holding the ball touches that base first, it is a `RetouchOut` (doubled off).
  - A tag-up runner waiting on the bag decides at the catch.
- **Example:** runner on third, one out, a deep fly to centre caught 5.12 s after contact. He leaves at the catch and scores 3.98 s later. A standing start over 90 ft is ≈ 3.7–4.1 s (DERIVED).

## Base touches, outs and runs (`LivePlay`)
All of these are decided by gameplay state, never by colliders or animation.
- **Base touch:** the runner's path crosses the bag (logged, exact time). Foot reach is 0.3 m along the path (`LiveRunner.TouchDistance`, ASSUMED).
- **Force out:** while he is forced (TASK-006B force rules, with removal) and before his foot reaches the bag, the defender holding the ball is within the base-touch envelope.
- **Tag out:** off a base and not protected, within 1.0 m of the defender holding the ball.
- **Protection after overrunning first:** the batter-runner overrunning first and returning is protected (OBR 5.09(b)(4) Exception).
- **Simultaneous is safe:** a force or tag within 1 ms of his foot reaching the bag counts as simultaneous (TASK-006B rule).
- **Run:** crossing the plate.
- **Third out:** ends the play immediately.
- **End of play:** the ball is held, the defense's action is finished, and every runner is on a base. The batter-runner walking back after overrunning first is already entitled to it.

## The engine
- **Discrete events:** `LivePlay` is a deterministic discrete-event simulation on a fixed 1/120 s tick grid from contact.
- **Event times are exact:** within a tick, base touches are located exactly from the closed-form motion. Out conditions are sampled at five points across the tick, then bisected.
- **Decisions happen only at events.**
- **Frame-rate independent:** results do not depend on when or how often `AdvanceTo` is called. A test compares 30, 60 and 144 fps and a jittered schedule to 1e-9 s.
- **Cost:** a whole play costs 0.3–3 ms to run and 10–30 ms to set up at contact (the defense's decision) in the Editor. The labs resolve it at contact; every motion keeps its history, so rendering any later time is exact.

## Presentation (`RunnerView`)
- **Figures:** runners are `PlayerMannequin`s in the offense's light uniform, on the authoritative position, facing where they run.
- **Run cycle:** phased by the distance actually run, so there is no foot skating.
- **Batter hand-over (batting lab):** the batter figure hands over to the batter-runner when he starts (contact + 0.25 s), blended from the batter's box onto the base path over 0.35 s. This is presentation only.
- **Leaving the field:** retired runners disappear 1.2 s after the out, and scorers 1.5 s after crossing. Walking off is not modelled.

## Not yet (TASK-008/009/010)
- **TASK-008:** coverage, backups and cut-offs by the eight defenders who are not the primary fielder.
- **TASK-009:**
  - chained defensive actions: double plays, throws after a catch, throws on a tag-up;
  - third-out run rules (OBR 5.08(a));
  - home runs and ground-rule doubles (dead balls for now);
  - the infield fly rule.
- **TASK-010:** the situation carried between plate appearances.
- **No sliding animation.**
