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
Not started. Research is done (physics-researcher). Notes are kept in this session's scratchpad and become `Docs/SURFACE_PHYSICS.md`.

## Milestone 3: TASK-004.6B-2, batting UX, fair/foul, loop polish
Not started.
