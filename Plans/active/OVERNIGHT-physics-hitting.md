# Overnight: TASK-001 audit → TASK-002 Statcast calibration → physics baseline → TASK-003 Hitting Sandbox (→ TASK-004)

## Goal
Make verified progress through milestones A–D (E optional) on `feat/overnight-physics-hitting`, branched from TASK-001 (`f61b8d8`). Each milestone crosses only when its quality gate passes; completed milestones are committed separately.

## Scope
A audit/hardening of TASK-001 · B Statcast replay validation and evidence-based calibration · C physics baseline decision · D Hitting Sandbox (timing, PCI, deterministic ContactResolver, HittingLab) · E batted-ball flight (only if A–D done).
Out: fielding, running, innings, franchise, online, polished presentation, seam-shifted wake without evidence, per-pitch-type corrections.

## Owner
Claude Code (sole production writer). Read-only subagents: physics-reviewer, test-reviewer, physics-researcher, unity-reviewer. No Codex.

## Safety
No push, no merge to main, no history rewrite, no broad process termination (exact PID only, after inspection).

## Architectural constraints
- `Pitchlab.Simulation` stays engine-free; Statcast-specific logic lives in an adapter, never inside `BallFlightSimulator`.
- Hitting logic separate from BallFlight; contact is deterministic and testable, not Unity-collider based.
- Physics depends only on physical state; no pitch-type corrections.

## Milestones
- A [x] Audit: A1 physics, A2 RK4, A3 spin model, A4 coordinates, A5 tests, A6 fixes, A7 Unity verification + check.sh
- B [x] TASK-002: B1 field research, B2 fixture, B3 adapter, B4 replay, B5 report, B6 systematic errors, B7 evidence-based changes, B8 holdout, B9 tests
- C [x] Baseline decision (physics-reviewer), classification, PHYSICS.md, ADR only if warranted
- D [x] TASK-003 Hitting Sandbox
- E [~] TASK-004 Batted-ball physics — PARTIAL: implemented, visualised, tested; pre-registered Statcast validation FAILED (≈ +30 ft), cause unresolved

## B pre-registered analysis (written before any replay was run)
Public Statcast has no per-pitch spin efficiency, so spin rate + axis alone cannot predict each pitch's plate location. Analyses and acceptance thresholds, fixed before looking at replay results:
1. Adapter reconstruction (interpretation check): 9P constant-acceleration state reproduces CSV plate_x/plate_z within 0.03 ft for ≥ 95 % of pitches, release_speed within 0.2 mph.
2. Drag (efficiency-independent): from the y = 50 ft state, our simulated time to the plate front within 3 ms of the 9P time (median |Δ|); implied per-pitch C_D median within 0.30–0.40 (documented C_D 0.35 ± 0.05). Air density per game unknown → ρ = 1.194 ± ~3 % noted as a limitation.
3. Lift magnitude: implied spin efficiency ε (our C_L inverted on the fit's Magnus acceleration and the measured spin rate) for four-seamers: median in [0.80, 1.00] and ≤ 10 % of four-seamers above 1.05 (ε > 1 is physically impossible → C_L too low).
4. Plate replay: (a) consistency replay with per-pitch implied ε and Statcast spin_axis: median 2D plate error ≤ 1 in; (b) holdout prediction with ε = median implied ε of the same family from the development game (an input estimate in the validation harness only; never in the simulator): report median 2D error, expectation ≤ 3 in.
5. pfx definition: candidate definitions computed from the 9P fit vs CSV pfx_x/pfx_z; the definition with median |Δ| ≤ 1 in is adopted for comparisons.
Dataset split: one game = development, a different game = holdout.

## E pre-registered analysis (written before any batted-ball replay)
Model: `AerodynamicModel.BattedBall` = Nathan (2017) Eqs. 10–11 jointly fitted pair (C_D = 0.297 + 0.0292·ω/1000 rpm, C_L = 1.12S/(0.583+2.333S)), spin decay τ = 30 s. Fixture: 459 balls in play, 9 Tropicana Field 2024 games (dome, 72 °F, 15 ft, no wind; 5 development / 4 holdout games). Spin is not public → validation spin model ω_back = 100 rpm/° × (LA − 7°), clamped to [0, 4500] rpm, no sidespin (Nathan 2020 Fig. 4 medians). Imputed EV/LA (pairs repeated within the fixture) dropped. Statcast `hit_distance_sc` compared to our horizontal distance at ground contact.
1. Primary set = fly balls/line drives with EV ≥ 90 mph and 20° ≤ LA ≤ 35° (Nathan's fit range). Development and holdout separately: |mean(model − Statcast)| ≤ 10 ft and RMS ≤ 25 ft.
2. Model sensitivities at 103 mph / 27° / 2000 rpm, 70 °F, sea level: distance 400 ± 20 ft (Nathan's Statcast-based table); EV slope 4.9 ft/mph ± 30 % (100 → 105 mph at 27°); temperature +3.3 ft per 10 °F ± 30 %; elevation +5.9 ft per 1000 ft ± 30 %; distance-maximising launch angle between 25° and 32° at 100 mph (with the validation spin model).
3. Report-only: all airborne balls 10–45° LA, per launch-angle bucket.

## Verification
### A
- physics-reviewer (A1/A2/A4): no CRITICAL/HIGH. Dimensional analysis, signs, RK4 stages, Statcast axes, spin axis 0/90/180/270/210 all verified.
- test-reviewer (A5): 12 findings + PlayMode notes; all validated (C_L check was self-referential; constants unpinned; mirror event case missing; plate tolerance 200× looser than claim; preset test was tuning presented as validation; wind untested at trajectory level; efficiency test could pass with total-spin S).
- physics-researcher (A3): fixed world-space ω justified (Nathan assumes it; ~1° precession vs 5–8° velocity turn); gyro-sign effect documented (Nathan gyroball note, Kagan, Barton Smith); our 1.4 in matches their ~0.7–1.4 in scaling.
- `Scripts/test.sh EditMode` 44/44; PlayMode 2/2; mutation check (`groundFirst = grounded`) → mirror test fails as intended, reverted.
- MCP PitchLab (Play Mode): zero spin 0 movement; axis 180/0 → IVB +20.1/−20.1; 90/270 → HB +20.1/−20.0; 80/100 mph → 0.472/0.378 s; 1200/3000 rpm → IVB +11.2/+19.5; curveball preset +10.8/−14.6; 45 mph/3000 rpm flagged out of range and warning visible in capture; console 0/0; exited Play Mode.
- `Scripts/check.sh` (staged): exit 0.

### B
- Fixture: 649 pitches, 2 games (dev Chase Field closed roof; holdout Yankee Stadium open/cold/windy), 17 pitchers + Savant 2024 spin-based active spin. `Tools/statcast/make_fixture.py` regenerates.
- Pre-registered: #1 pass (fit reproduces plate ≤ 0.01 ft), #2 pass (dt 0.73/2.62 ms, C_D 0.34/0.31), #3 FAIL (FF eff median 0.86 but 17 % > 1.05), #4a FAIL (2.66/3.02 in), #4b FAIL (5.73 in), #5 FAIL narrowly (0.21/1.12 in). Kept as [Ignore] tests with reasons.
- Diagnostics: observed-direction replay 0.32/0.57 in; spin_axis deviation mirrors by hand (measured axis, SSW-like); reported holdout wind makes results worse (rejected); implied/active-spin ratio FF 0.90, SI 1.09, CH 0.86 vs SL 0.56, CU 0.62, FC 0.38; vs Savant observed efficiency FF 1.02, SI 1.04, CH 0.94 (pipeline confirmed).
- `Scripts/test.sh EditMode` 68 total, 63 passed, 0 failed (5 ignored pre-registered); report via explicit `WriteValidationReport`.

### C
- physics-reviewer (baseline): no API rewrite needed; one trivial hitting blocker (use own FlightLimits past the plate front). Found reporting flaws in TASK-002, all confirmed and fixed: observed-direction replay is partly circular (validates drag/frame/integrator, not C_L); active-spin ratios pooled games (now per game: FF 0.92/0.90); Median sorted NaN first (fixed, NaN counts reported; the 12 NaN replays are exactly the 5+7 tracked pitches crossing below ball height); mirror claim cherry-picked (sliders/curves do not mirror — doc corrected); "not monotonic in S⊥" overstated (now "underdetermined; low-S lift reduction a leading candidate"); S⊥/observed-efficiency comparisons now produced by committed code (observed leaderboard added to fixture); lift ratio in force terms 1.4–2.6×.
- Presets retuned to movement (slider +5.5/+1.5, curveball +9/−10 HB/IVB) with effective efficiency 0.19 / 0.42.
- ADR 0003 written; PHYSICS.md baseline table.
- `Scripts/test.sh EditMode` 70 total, 65 passed, 0 failed (5 ignored pre-registered).

### D
- physics-researcher: ball-bat parameters (Nathan 2003 q = e_A ≈ 0.21 and its barrel profile; Kensrud/Nathan/Smith 2017 e_x = 0.40, oblique geometry; Nathan 2012 incoming spin negligible; bat speed 72 mph, attack 10°; timing ~2.6°/ms; sweet-spot window 9.4 ms).
- Design changes from exploration: bat height follows the swing plane (timing no longer flips launch angle arbitrarily); rolling exit replaced by e_x tangential restitution + recoil r_x + Coulomb cap (spin 6–9k rpm → 3–4k rpm at 25°).
- test-reviewer: 3 production bugs confirmed + fixed (NaN PCI → Contact with NaN; late swing could hit a landed ball; negative normal impulse not rejected), parameter validation added, bat-frame offsets, speed latch, input-event hook; tests added (hand-derived oblique collision incl. friction-limited branch, boundaries, left-handed tip, spray monotonic/symmetric, golden value, timing units, button routing, event timestamp, speed latch).
- Mutation check (spin cross product reversed) → backspin/topspin test fails; reverted.
- `Scripts/test.sh` EditMode 96 total, 91 passed, 0 failed (5 ignored pre-registered); PlayMode 7/7.
- MCP HittingLab: 15 + 11 scripted pitches through the live controller (perfect ≈ 102–105 mph / 11–13°, under → 25°, over → −9°, early pulls −15..−20°, late +18..+28°, timing/over/off-barrel misses); real keyboard path via simulate_key (Space throws, Space swings with event timestamp 0.0198 s < frame clock 0.032 s); Game View capture shows zone, PCI, contact point, exit ray, pitch path, readout; console 0 errors/0 warnings during play.

### E
- physics-researcher: Nathan 2017 batted-ball pair (C_D 0.297 + 0.0292/krpm, same C_L; Tropicana 2016), spin decay τ ≈ 30 s (Nathan 2008: minor), Statcast-era benchmarks (4.9 ft/mph, 3.3 ft/10 °F, 5.9 ft/1000 ft, peak 25–30°, ≈ 400 ft at 103/27), spin-vs-LA medians (Nathan 2020 Fig. 4), hit_distance_sc definition ambiguity.
- Generalisation (additive, pitch results bit-identical): `AerodynamicModel` spin-dependent drag + spin decay; RK4 evaluates spin at stage times; `Pitchlab.Simulation.Batting` (BattedBallLaunch, BattedBallSimulation, BattedBallMetrics).
- Fixture: 459 balls in play, 9 Tropicana Field games (dome), `Tools/statcast/make_batted_fixture.py`.
- Pre-registered results: primary set mean +28.8 ft (dev) / +34.6 ft (holdout) vs ≤ 10 → FAIL; reference 426.5 ft vs 400 ± 20 → FAIL; EV slope, temperature, elevation, optimal LA → pass. Bias uniform across LA/EV buckets; residual scatter ≈ 15 ft. Diagnostic sweeps: an exit-speed offset of −5…−6 mph flattens residuals best; C_D scale leaves an LA trend; spin scaling rejected. No calibration adopted (underdetermined).
- HittingLab now flies the batted ball (distance/hang/apex + caveat); BattedBallLab scene (MCP-built): presets ground ball 27 ft / line drive 299 ft (2.74 s) / fly 386 ft (5.30 s, apex 88 ft) / deep fly 451 ft (5.71 s); overhead capture confirms landing beyond the 400 ft arc in left field for −20° spray; console 0/0.
- Tests: 8 batted-ball EditMode (launch conversion, vacuum range exact, spin decay exact, sidespin, sensitivities; 1 ignored pre-registered) + validation (2 ignored pre-registered, 2 post-hoc bias guards); PlayMode BattedBallLab preset ordering.

## Discoveries
- Savant `plate_x/plate_z` are at the plate front (y = 17/12 ft); 9P constant-acceleration fit with t = 0 at y = 50 ft reproduces plate_x/z exactly (researcher, one row). Savant pfx (feet) on 2 rows matched Nathan's drag-corrected Magnus deviation over release→plate, not the old 40 ft window — to confirm on a larger sample in B.
- Savant `spin_axis` is the measured (Hawk-Eye) axis, not movement-inferred (contradicts the first research report): deviation from movement mirrors by throwing hand.
- Our C_L constants are Nathan 2017 Eqs. 10–11 fitted to 2016 fly balls, not pitches (verified in the PDF); docs previously said "fit to Statcast" without that qualifier. Nathan 2020 pitch fit: C_L = 0.336[1 − e^(−6.041 S)].
- Breaking balls get ~1.6–2.6× too much transverse-spin effect for their measured active spin; not a monotonic function of S⊥, so not fixable by reshaping C_L(S).

## Decisions
- A: spin vector built in the fixed X–Z plane so θ equals Statcast spin_axis exactly (was relative to release velocity; ≈ 4° off for gyro-heavy presets). γ now = tilt out of X–Z toward the plate.
- A: model validity flag (min Re ≥ 1.5e5, max S ≤ 0.4) reported in PitchMetrics and PitchLab, rather than silently extrapolating.
- A: rejected finding — "no test for plane+ground in the same step" (it existed; strengthened with the mirror case and a same-step precondition).
- A: accepted, not changed — ground checked at step ends only (documented; a pitch cannot dip and recover within 5 ms).
- B: no coefficient changes (C_D confirmed; C_L within scatter for high-efficiency pitches; breaking-ball deficit would need per-pitch-type correction, which is not allowed). Game pitch definitions for breaking balls should target movement (effective transverse spin).
- D: Hitting logic in engine-free `Pitchlab.Gameplay` (Hitting/); HittingPitch simulates past the plate with its own FlightLimits (baseline blocker). Contact is a deterministic impulse model in the bat frame; Unity colliders not used. SwingParameters is the ratings surface (validated).
- D: PCI is a world-space point in the contact plane; its rectangle is the barrel contact region (±5 in × bat–ball centre distance).
- D: swing time = input event timestamp on the pitch's real-time clock; playback speed latched per pitch.
- B: Statcast boundary = `Pitchlab.Simulation.Tracking` (StatcastPitch, StatcastNinePointFit, StatcastAdapter); simulator untouched. CSV parsing and analysis live in the EditMode test assembly.

## Remaining risks
- Batted-ball distances ≈ 30 ft long vs 2024 Statcast; cause unresolved (exit-speed definition, drag/ball lot, spin model; hit_distance_sc catch-height semantics ruled out — home runs show the same bias). Blocks distance-dependent gameplay.
- Breaking-ball lift vs measured active spin (accepted via effective-spin inputs, ADR 0003); mechanism unresolved.
