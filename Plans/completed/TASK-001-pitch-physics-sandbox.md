# TASK-001 — Pitch Physics Sandbox

## Goal
First playable Pitch Physics Sandbox: an authoritative, deterministic, fixed-step baseball flight simulation (gravity + drag + Magnus) driven only by physical initial conditions, plus a `PitchLab` scene to configure, throw, visualize, and measure pitches.

## Scope
In: pure C# flight simulator, environment (air density inputs, wind), SI internally, unit conversion at boundaries, trajectory recording, plate-crossing detection, movement metrics, PitchLab scene with debug controls/visualization, presets as plain initial-condition data, EditMode + PlayMode tests, docs.
Out: batting, PCI, hitter, fielding, running, rules, ratings, animation, stadium, replay, seam-shifted wake, spin decay (unless research says essential), pitch-type-driven curves.

## Owner
Claude Code (sole implementation owner). Subagents: physics-researcher (research), physics-reviewer and unity-reviewer (read-only review).

## Architectural constraints
- `Pitchlab.Simulation` assembly has `noEngineReferences: true`: no UnityEngine, no MonoBehaviour, no scene. Uses `double` and its own `Vector3d`.
- Simulation ← PitchLab sandbox (presentation/debug). Presentation reads results; it never feeds frame timing into the simulator.
- Rigidbody is not used for the ball.
- Physics depends only on initial state, ball properties, environment. Presets are data only.

## Relevant files
- `Assets/Game/Simulation/**` — `Pitchlab.Simulation` (Core, BallFlight, Pitching)
- `Assets/Game/Debug/PitchLab/**` — `Pitchlab.Sandbox` sandbox MonoBehaviours and materials
- `Assets/Game/Tests/EditMode/**`, `Assets/Game/Tests/PlayMode/**`
- `Assets/Scenes/PitchLab.unity`
- `Docs/PHYSICS.md`

## Milestones
1. [x] Research (physics-researcher) and record sourced assumptions in `Docs/PHYSICS.md`
2. [x] Simulation core: vectors, units, ball/environment, forces, integrator, trajectory, plate crossing, metrics
3. [x] EditMode tests incl. timestep convergence study → choose dt/integrator
4. [x] PitchLab scene (MCP) + sandbox controller, visualization, IMGUI controls, presets
5. [x] PlayMode smoke test
6. [x] physics-reviewer pass; validate and resolve findings
7. [x] unity-reviewer pass; resolve findings
8. [x] Runtime verification via MCP
9. [x] `Scripts/check.sh`, plan → completed, commit on `feat/task-001-pitch-physics`

## Verification
- Integrator study (scratch Python, same equations): vs 10 µs RK4 reference, RK4 5–20 ms error < 1 µm at plate; Euler/semi-implicit 5 ms ≈ 4 mm, 1 ms ≈ 0.8 mm.
- `Scripts/test.sh EditMode`: 24/24 passed. Mutation check (Magnus sign inverted): 9 tests failed as expected; reverted.
- `Scripts/test.sh PlayMode`: 2/2 passed.
- Physics review resolved: `Scripts/test.sh EditMode` 29/29 passed (incl. new regression tests).
- MCP: PitchLab built via `create_scene`/`create_asset`/`eval`, saved, added to build settings; Play Mode capture shows flight path, reference path, zone.

## Discoveries
- Research (physics-researcher): Nathan 2017 C_L fit; C_D ≈ 0.35 at pitch speeds; spin decay negligible; Statcast frame and spin_axis convention; pfx window (40 ft vs release) unverified for Statcast era.
- RK4 makes accuracy a non-issue at any sensible dt; 5 ms chosen for margin and sample density.
- Unity MCP drops its connection on each domain reload (recompile, enter Play Mode); requests in flight log a Pipeline "Thread was being aborted" error. Wait for `editor_status` ready before calls.
- MCP `eval` bodies cannot contain `using` directives; use fully qualified names.
- Project uses Input System only (`activeInputHandler: 1`); IMGUI still works for the debug panel.

- Physics review (all findings validated before acting):
  - Confirmed + fixed: ground contact and plate crossing in the same step reported the plate even when the ground came first → both events located, earlier wins; regression test over three step sizes.
  - Confirmed + fixed: playback test tolerance (1e-4 m) could not distinguish Hermite from linear interpolation → 1e-7 m plus velocity check.
  - Confirmed + fixed: doc/comment "within 3 % of Sawicki for S = 0.1–0.3" was wrong at S = 0.1 (−8.5 %) → per-point comparison.
  - Fixed: FlightTime NaN when the ball grounds; movement NaN if the reference never reaches the plate; NaN-safe validation; MaxDuration check; clock formula wording; RK4 position-independence comment; axial-vector note in SimulationSpace.
  - Added tests: Magnus magnitude/perpendicularity with wind, 210° axis direction, MaxDuration end, gyro sign.
  - Docs claim "gyro sign does not change lift" was only true at release; the new test showed ≈ 1 in difference over flight (spin vector fixed while v turns). Doc corrected; test now asserts the release-time symmetry and a small in-flight effect.
  - Verified Nathan 2008 Table I values directly from the PDF (75/1000 → 16 in, 75/1800 → 21, 90/1000 → 14, 90/1800 → 19).
  - Not acted on: movement-window question (documented as future research, before tuning C_L/presets); spin-axis reference plane (documented, second order).
- Incident: a malformed `pkill` during cleanup terminated the Unity Editor and Unity Hub. Scene/materials had been saved; Editor reopened; no data loss found.

- Unity review (validated before acting): fixed build-settings order (PitchLab index 0), `Pitchlab.Debug` namespace shadowing `UnityEngine.Debug` (renamed `Pitchlab.Sandbox`), flaky PlayMode playback-time assertion, sliders rewriting out-of-range authored values, playback time origin, missing-reference handling (Awake check), missing URP camera data, path-based lookups in tests, light shadows with no casters, cached preset labels. Deferred: field objects placed only at Start (Scene view shows them at origin while editing); IMGUI per-frame strings (debug tool); gamepad cannot edit parameters (throw/presets/camera only).
- Runtime verification via MCP (Play Mode, scripted through the live controller + Game View captures):
  - Console clean before, during, after Play Mode.
  - Four-seam base: 0.400 s, plate (0.00, 2.50) ft, HB −8.1 in, IVB +17.3 in; path point count = trajectory samples.
  - Speed 80/100 mph: flight 0.472/0.378 s, plate z 1.65/2.72 ft.
  - Spin 1200/3000 rpm: IVB +11.5/+19.9 in.
  - Axis 90°/270° (efficiency 1): HB +20.2/−20.0 in (1B/3B), IVB ≈ 0. Axis 0° from four-seam release: reaches ground before plate (metrics NaN, UI message).
  - Zero spin: movement 0.0; flight and reference coincide (0 m apart), visible as overlapping lines.
  - Presets: distinct HB/IVB/flight time (sinker −15.8/+9.3, slider +9.4/+0.9, curveball +10.4/−14.4, changeup −14.8/+7.0 in).
  - Captures: catcher view (four-seam, curveball, zero spin) and side view (four-seam, curveball) show expected relationship of flight vs no-lift reference.
  - Exited Play Mode cleanly.
- After review fixes: EditMode 29/29, PlayMode 2/2.
- Final `Scripts/check.sh` (Editor open, all changes staged): whitespace OK, EditMode 29/29, PlayMode 2/2, exit 0. Console: 0 errors, 0 warnings.
- Harness change (blocker): `git diff --cached --check` failed on Unity-generated YAML trailing spaces in every new `.meta`/`.unity`/`.mat`; `.gitattributes` now marks Unity serialized types `-whitespace`. C#/Markdown remain checked.
- `.gitignore`: ignore Unity's temporary `InitTestScene*.unity` that in-Editor PlayMode runs can leave behind briefly.

## Decisions
- Simulation precomputes the full trajectory at a fixed dt; presentation plays it back by simulation time (interpolated), so render frame rate cannot change results.
- Coordinate frame: see `Docs/PHYSICS.md` (Statcast-aligned, metres).
- RK4, dt = 5 ms; events located by bisection on a partial step.
- C_D 0.35 constant; C_L = 1.12S/(0.583+2.333S) with S from transverse spin; no spin decay.
- Movement = plate crossing minus no-lift (C_L = 0) reference crossing, full flight from release.
- Spin input = rate + Statcast spin axis + gyro angle (efficiency = cos gyro).
- Debug UI = IMGUI panel (debug-only tool, minimal setup); Input System for keyboard/gamepad shortcuts.
- Sandbox lives in `Assets/Game/Debug/PitchLab` (assembly/namespace `Pitchlab.Sandbox`), not Gameplay/Presentation, because it is a debug tool. Namespace is not `Pitchlab.Debug` because that shadows `UnityEngine.Debug`.
- PitchLab is the only scene in build settings (index 0); the template SampleScene was removed from the build list but kept in the project.

## Remaining risks
- Coefficient choices (C_D, C_L(S)) are model approximations; curveball IVB ≈ −14 in vs ≈ −10 in published.
- Movement window (release vs 40/50 ft) unverified for Statcast-era data; resolve before tuning C_L or presets.
- Spin-axis angle measured relative to release velocity (Statcast uses X–Z plane); gyro sign convention unverified.
- No seam effects, spin decay, or speed-dependent C_D.
