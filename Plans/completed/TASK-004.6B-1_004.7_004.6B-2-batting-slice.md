# Batting vertical slice: TASK-004.6B-1, TASK-004.7, TASK-004.6B-2

There are three milestones. Each has its own section, verification gate and local commit. They run in order on branch `feat/task-0046-visual-playability` from `ad6765e`. Nothing is pushed or merged, and TASK-005 Fielding is out of scope.

- **Owner:** Claude Code is the sole writer.
- **Read-only helpers:** physics-researcher, physics-reviewer, unity-reviewer and test-reviewer.

**Global constraints:**
- Layering is Simulation ← Gameplay ← Presentation, and nothing visual is authoritative.
- These stay untouched:
  - ContactResolver, e_x, r_x and VerticalBatAngle;
  - pitch aero, batted-ball airborne coefficients and the spin model;
  - timing windows and PCI hit thresholds.
- A genuine bug gets isolated, documented and covered by a regression test.

---

## Milestone 1: TASK-004.6B-1, mouse PCI and procedural swing

### Goal
Device-independent aim and swing, with the mouse as the primary input. The PCI is deterministic at the swing event's timestamp, whatever the frame rate. The visual swing is deformed toward the contact point (on a hit) or the aim point (on a miss).

### Relevant files
- **Gameplay:** `Assets/Game/Gameplay/Hitting/PciFrame.cs` (new) and `PciTrack.cs` (adds `Move`).
- **Sandbox:** `Assets/Game/Debug/PitchLab/HittingLabController.cs` (input, PCI drawing, debug overlay) and `HittingLabPresentation.cs` (targeting, sweet-spot trail, overlay).
- **Presentation:** `Assets/Game/Presentation/Animation/SwingTargeting.cs` (new).
- **Tests:**
  - EditMode: `PciTrackTests` (Move, PciFrame) and `SwingTargetingTests`.
  - PlayMode: `HittingInputFrameRateTests` (mouse trace), `HittingLabPresentationTests` (targeting per preset and timing, misses aiming at the PCI) and `HittingLabSceneTests` (wind-up policy).
- **Docs:** `Docs/ARCHITECTURE.md` (PCI chain and input) and `Docs/MOTION_REFERENCE.md` (procedural targeting).

### Status
- [x] PciFrame, a single normalized → metres chain, with axes documented. Key and stick speeds are converted exactly, so TASK-003 behaviour is unchanged.
- [x] Mouse deltas are applied per event at `eventPtr.time` (`InputSystem.onEvent`). Configurable sensitivity. Click-to-capture and Escape to release. A left click throws or swings at `context.time`. In the Editor, `RequireMouseCapture` can be turned off.
- [x] Wind-up policy B: swing presses before release are ignored, and this is tested.
- [x] SwingTargeting: a bounded least-squares deformation with the feet planted and the root fixed; integrated in the presentation.
- [x] PCI display (box plus centre cross) and the debug overlay (T):
  - PCI range;
  - normalized and gameplay PCI;
  - swing PCI;
  - ball position at contact time;
  - contact point;
  - visual target and sweet-spot error;
  - deformation summary;
  - sweet-spot path trail.
- [x] Tests; runtime Game View; docs.
- [x] Reviews and fixes.
  - **unity-reviewer** (no blocking finding). Fixed:
    - the merging flag is now set in OnEnable and restored in OnDisable;
    - a capturing click during a live pitch also swings;
    - history pruning is batched;
    - the solver stops early, with hoisted arrays;
    - the sweet-spot trail is finer.
  - **test-reviewer.** Fixed:
    - deterministic same-update mouse tests (click + report; report + click), verified by mutation: re-enabled merging and frame-time deltas both fail;
    - capture and Escape tests, a lost-capture swing test, and real devices disabled during input tests;
    - the full ±35 ms window, PCI-area corners, timed misses and an off-barrel hit;
    - geometry derived from the source constants;
    - behaviour (hips, barrel) asserted instead of solver fields.
  - The visual target is now gameplay's sweet spot (`ContactResolver.SweetSpotAtContact`, a pure extraction with the same expression). Off-barrel hits and timed misses therefore show where the bat really was.
- [x] `Scripts/check.sh`; commit `feat: add deterministic mouse PCI and procedural swing targeting`.

### Discoveries
- **Input System event merging.** The Input System merges consecutive mouse events that differ only in delta, and keeps the later timestamp. A click followed within the same update by a mouse report took that report's timestamp (3 ms late in the test). The new mouse frame-rate test caught it. Fix: `InputSystem.settings.disableRedundantEventsMerging = true`.
- **Reach of the planted batter.** Gameplay contact for a timing error is where the ball is at that moment: about 40 m/s × error out front or deep, so ±0.8 m at ±20 ms. A planted batter cannot reach that. Visual error within ±10 ms is under 5 cm, except the low-away corner (≤ 9 cm, ≤ 14 cm early). At ±20 ms the bat closes most of the gap but remains 5–35 cm away. This is documented and tested as a known limit. Timing windows are frozen gameplay; a contact-depth model would be a gameplay decision.
- **Body turn and the hands.** Rotating the grip with the body turn reduced lateral reach, so it was reverted. The timing turn turns hips and chest; the hands are placed by the solve.
- **Solver tuning.** A heuristic plus hand-shift solve missed outside targets by 20–35 cm, because the arms cannot reach. It was replaced by the least-squares fit with a grip-gap penalty. A soft hand limit replaced a hard clamp, which had stalled the fit.

### Decisions
- **Wind-up policy B (ignore).** A swing before release can never connect: contact comes about 0.4 s after release and the hit window is ±35 ms. Ignoring such presses removes accidental throw-click double-click whiffs and loses no timing skill.
- **PCI persistence across pitches.** The PCI keeps its position between pitches. For the mouse this is the natural behaviour, and it matches TASK-003. To be revisited in B-2.

### Discoveries (review round)
- **Hits always looked flush.** Aiming the visual at the ball centre showed every hit as a sweet-spot hit, even one 12 cm off the barrel. Gameplay's own sweet-spot point is the honest target.
- **Feet need a hard objective.** Pushing to PCI corners and ±35 ms made the rear ankle skate 1.4 cm, so planted-ankle residuals (6×) were added. The grip weight was raised to 6×.

### Verification (B-1)
- **`Scripts/check.sh` (in Editor):**
  - whitespace OK;
  - EditMode 313 total, 307 passed, 0 failed (6 explicit/ignored, pre-existing);
  - PlayMode 25/25.
- **Suites:**
  - SwingTargetingTests 159/159;
  - HittingInputFrameRateTests 6/6;
  - HittingLabPresentationTests 9/9;
  - HittingLabSceneTests 7/7.
- **Mutations** (temporarily applied, then reverted):
  - `disableRedundantEventsMerging = false` → 2 tests fail;
  - `Move(Clock(), …)` → 3 tests fail.
- **Game View** (Play Mode, frozen clock):
  - curveball contact: bat on the ball;
  - low-away miss: bat visibly under, at the PCI;
  - high-inside miss: bat over and inside;
  - early swing;
  - debug overlay readable.
- **Solve cost:** about 2 ms once per swing (Editor).

---

## Milestone 2: TASK-004.7, ball–surface interaction

### Goal
Batted balls play out on the field after their first landing: bounce, skip, slide, roll, rest and outfield-wall impacts. The model is event-driven and deterministic, uses sourced or fitted coefficients, and does not touch the airborne model.

### Relevant files
- **Simulation:** `Assets/Game/Simulation/Field/`
  - `SurfaceImpact` (impulse model);
  - `BallSurfaceProperties` (coefficients);
  - `FieldLayout` (surfaces and fence);
  - `BallInPlay` / `BallInPlaySimulation` (phases, events, queries).
- **Sandbox:**
  - `HittingLabController` (`LastPlay` drives the ball after contact; carry and final distance in the readout);
  - `HittingLabPresentation` (landing marker at the first ground contact, trail and shadow through bounces, warning track and wall meshes from the layout);
  - `BattedBallLabController` (ball-in-play path; carry/final/rest readout; fence drawn; chopper, wall-ball, home-run and gap-roller scenarios).
- **Tests:**
  - EditMode `BallSurfaceTests.cs`: SurfaceImpactTests, FieldLayoutTests, BallInPlayTests;
  - PlayMode `BattedBallLabSceneTests.ScenariosPlayOutOnTheField`;
  - PlayMode `HittingLabPresentationTests` (landing, bounce on the trajectory, rest, camera stays on the ball).
- **Docs:** `Docs/SURFACE_PHYSICS.md`.

### Status
- [x] Research (two physics-researcher runs): impact model, Pennbounce data, rolling evidence.
- [x] Impulse model, surfaces, layout, event-driven play with queries; carry is bit-identical to the airborne-only flight.
- [x] Integration in HittingLab and BattedBallLab; the camera follows to rest; the wall and warning track are visible.
- [x] Game View: a foul chopper, and a fair grounder through the infield, rolling to rest with the camera following.
- [x] **physics-reviewer.** Fixed:
  - *Plow over-spin (HIGH):* the plow now applies before friction, so it never reverses and never over-spins.
  - *Rolling drag (HIGH):* about 2× too high; now F/((1+α)m) with the spin-free C_D.
  - *κ(v):* clamped to the measured 31–40.2 m/s.
  - *e_n:* labelled ASSUMED, with κ refit.
  - *Wall balls:* show the projected distance (`ReachedFenceInTheAir`).
  - *Docs:* synced with the code.
- [x] **test-reviewer.** Added:
  - grass roll against the closed form;
  - the dirt → grass transition mid-roll;
  - the wall-height ±5 mm boundary;
  - foul ground behind the fence line (which exposed a real phantom-wall bug; now guarded);
  - the time guard;
  - topspin vs backspin (which exposed a real inconsistency: turf b·v resistance now also applies while sliding);
  - queries vs events;
  - roll-out bands;
  - energy never increasing;
  - ground speed never rising after the last bounce;
  - frame-cadence independence of the rendered play;
  - `LastPlay` equal to a fresh simulation;
  - no Rigidbody;
  - outfield meshes against the layout;
  - scenario distances.
- [x] `Scripts/check.sh`; commit `feat: simulate bounces rolling and field-surface interaction`.

### Discoveries
- **Speed dependence in Pennbounce.** A speed-independent rigid impulse model cannot match Pennbounce at both 31 and 40 m/s: grass loses relatively more at speed, dirt less. A plow coefficient linear in impact speed (clamped to the measured 25–45 m/s), fitted per surface, gives RMS 0.007 (dirt) and 0.017 (grass).
- **Rolling.** A constant rolling deceleration let hard grounders roll 330+ ft. Research found the measured form for turf is a(v) = a₀ + b·v. There are no baseball roll-out data; the values are ASSUMED (football-scaled) and documented as the weakest part.
- **The slide → roll instant.** It is located exactly inside the 1 ms step; the slip falls linearly. Before that fix the snap came up to 1 ms late and the rolling distance was 1.3 cm short.
- **Foul territory.** The fence's end segments extended into foul territory; walls now stand only between the poles.

### Decisions
- The ground phase uses explicit 1 ms steps, with exact event location for slide → roll and an analytic stop.
- The airborne stretches reuse `BallFlightSimulator.Simulate` unchanged. Wall crossings are located by bisection on partial RK4 steps, as for other events.
- The fence is generic (330/375/400 ft, 8 ft). The mound is flat for ground physics, and there is no backstop (documented).
- Carry is the first ground contact. Over the fence, the displayed distance is the airborne-only projected landing, as Statcast does.

### Verification (004.7)
- **`Scripts/check.sh`:**
  - whitespace OK;
  - EditMode 369 total, 363 passed, 0 failed (6 explicit/ignored; includes the not-yet-committed B-2 FairFoul and state-machine tests);
  - PlayMode 28/28.
- **Suites:**
  - SurfaceImpactTests 15;
  - FieldLayoutTests 2;
  - BallInPlayTests 23;
  - BattedBallLabSceneTests 2;
  - HittingLabPresentationTests 11.
- **HittingInputFrameRateTests tolerance.** The exit-velocity and spin tolerances are now 1e-7 / 1e-5, up from 1e-9 / 1e-6. Swing times are differences of absolute wall-clock timestamps, which round to about 1e-13 s after hours of Editor uptime; the solve amplifies that about 10³×, and a 144 fps run hit 1.9e-9 m/s. Swing time and PCI stay at 1e-9. A frame dependence would show as at least mm/s.
- **Game View:** a foul chopper; a fair grounder through the infield to rest near the track, with the camera following; wall and track visible.

## Milestone 3: TASK-004.6B-2, batting UX, fair/foul, loop polish

### Goal
A complete, readable at-bat loop on top of B-1 and 004.7:
- fair/foul calls;
- compact result, timing and contact-quality feedback;
- an explicit state machine;
- a clean reset;
- camera through bounce, roll, wall and rest;
- normal vs debug UI;
- debug event markers.

### Relevant files
- **Gameplay:** `FairFoul.cs`, `BattingStateMachine.cs`, `ContactFeedback.cs` (in `Assets/Game/Gameplay/Hitting/`).
- **Sandbox:**
  - `HittingLabController` (state-driven press, `LastCall`, `ShownCarry`, `DebugView`, hint line);
  - `HittingLabPresentation` (`Feedback(t)` banner, camera at Ready, event markers).
- **Scene:** `HittingLab.unity` (details panel off by default).
- **Presentation:** `FieldDressing` (grass covers foul ground beyond the fence).
- **Tests:**
  - EditMode: `FairFoulTests`, `BattingStateMachineTests`, `ContactFeedbackTests`;
  - PlayMode: `HittingLabPresentationTests` (feedback through the play to Ready, take/miss, debug markers) and `HittingLabSceneTests` (press contract).
- **Docs:** `Docs/ARCHITECTURE.md` (batting loop, fair/foul, feedback).

### Status
- [x] Fair/foul (OBR definitions; lines and poles fair; ball judged by any part over the line).
- [x] State machine and press contract. A double click during the swing no longer re-throws; a press during a play throws the next pitch.
- [x] Feedback line (a pure function of time), "Click to pitch", camera back to batting at Ready, normal vs debug UI, event markers.
- [x] Game View playtest (frozen clock, Play Mode):
  - a fair drive to the track: "FAIR 102 mph · 18° · on time · sweet spot · 372 ft (rests 396 ft)";
  - the debug view with markers and overlays;
  - Ready with the batter back in stance;
  - a pulled foul beyond the pole: "FOUL … early 30 ms …".
- [x] **unity-reviewer.** Fixed:
  - *Double swing (MEDIUM):* an out-of-order earlier-stamped second press could swing twice. Now one swing per pitch; mutation-verified test.
  - Removed the dead `PitchInFlight`/`_swung`.
  - Marker renderers are cached.
  - Outfield meshes are destroyed.
  - `DebugView` guard.
  - No stray separator when timing is missing.
- [x] **physics-reviewer and test-reviewer.** Fixed:
  - *Pole band:* the fence and pole now cover the line band, so a ball grazing the pole by part of itself is a home run or off the wall, not foul.
  - *Fence order:* a fair bounce over the fence is a ground-rule double, not a home run.
  - *Off the wall on the fly:* fair.
  - *Foul-line distance:* correct behind the plate's apex.
  - *On time* now uses the resolver's Good window (±7 ms).
  - The state machine uses absolute times, takes the swing duration (a late miss reads after the swing), and has a 0.3 s double-press grace after contact.
  - Tests added:
    - third-base side;
    - base-distance boundary;
    - passing the base on the line;
    - behind the apex;
    - ground-rule double;
    - wall carom;
    - simulation-driven pole graze (home run, foul, off the wall);
    - state boundaries;
    - late miss;
    - quality through the resolver;
    - banner stages and the shown banner across frame cadences;
    - marker positions;
    - press during the play and the grace period;
    - clean reset;
    - PCI persistence.
- [x] Full regression (`Scripts/check.sh`); commit `feat: finish batting vertical slice UX and play loop`.

### Verification (B-2)
- **`Scripts/check.sh` (in Editor):**
  - whitespace OK;
  - EditMode 389 total, 383 passed, 0 failed (6 explicit/ignored, pre-existing);
  - PlayMode 34/34.
- **Suites:**
  - FairFoulTests 23;
  - BattingStateMachineTests 6;
  - ContactFeedbackTests 7;
  - HittingLabPresentationTests 16;
  - HittingLabSceneTests 8.
- **Game View:** fair drive, debug view, Ready and foul captures (see Status).

### Remaining risks (whole slice)
- **Rolling roll-out** coefficients and **e_n** are ASSUMED; no public baseball roll-out data exists. Calibrate against tracking video before fielding depends on exact roll distances.
- **Contact depth beyond reach.** Contact at ±20–35 ms is 0.8–1.4 m out front or deep, beyond a planted batter. The visual bat approaches but does not meet the ball, which is documented. A contact-depth model would be a gameplay decision; the timing windows are frozen.
- **Simplifications:**
  - the mound is flat for ground physics;
  - there is no backstop or foul-territory wall;
  - bases are not physical, so touching a bag is approximated by passing it;
  - the fence is generic.

### Decisions
- **PCI persistence:** keep the PCI where it is between pitches (the mouse hand's position; TASK-003 behaviour).
- **Press during BallInPlay or Result:** throws the next pitch, a skip, rather than being ignored. A long roll-out (8 s) shouldn't hold the player.
- **Contact quality:** shown only from gameplay data (the barrel offset). No invented "barrel" metric.
- **Sounds:** not added (optional). The existing contact crack remains.
