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
- B [ ] TASK-002: B1 field research, B2 fixture, B3 adapter, B4 replay, B5 report, B6 systematic errors, B7 evidence-based changes, B8 holdout, B9 tests
- C [ ] Baseline decision (physics-reviewer), classification, PHYSICS.md, ADR only if warranted
- D [ ] TASK-003 Hitting Sandbox
- E [ ] TASK-004 Batted-ball physics (optional)

## Verification
### A
- physics-reviewer (A1/A2/A4): no CRITICAL/HIGH. Dimensional analysis, signs, RK4 stages, Statcast axes, spin axis 0/90/180/270/210 all verified.
- test-reviewer (A5): 12 findings + PlayMode notes; all validated (C_L check was self-referential; constants unpinned; mirror event case missing; plate tolerance 200× looser than claim; preset test was tuning presented as validation; wind untested at trajectory level; efficiency test could pass with total-spin S).
- physics-researcher (A3): fixed world-space ω justified (Nathan assumes it; ~1° precession vs 5–8° velocity turn); gyro-sign effect documented (Nathan gyroball note, Kagan, Barton Smith); our 1.4 in matches their ~0.7–1.4 in scaling.
- `Scripts/test.sh EditMode` 44/44; PlayMode 2/2; mutation check (`groundFirst = grounded`) → mirror test fails as intended, reverted.
- MCP PitchLab (Play Mode): zero spin 0 movement; axis 180/0 → IVB +20.1/−20.1; 90/270 → HB +20.1/−20.0; 80/100 mph → 0.472/0.378 s; 1200/3000 rpm → IVB +11.2/+19.5; curveball preset +10.8/−14.6; 45 mph/3000 rpm flagged out of range and warning visible in capture; console 0/0; exited Play Mode.
- `Scripts/check.sh` (staged): exit 0.

## Discoveries
- Savant `plate_x/plate_z` are at the plate front (y = 17/12 ft); 9P constant-acceleration fit with t = 0 at y = 50 ft reproduces plate_x/z exactly (researcher, one row). Savant pfx (feet) on 2 rows matched Nathan's drag-corrected Magnus deviation over release→plate, not the old 40 ft window — to confirm on a larger sample in B.
- Savant `spin_axis` is reportedly movement-inferred (not seam-observed) → direction comparisons against it are partly circular.

## Decisions
- A: spin vector built in the fixed X–Z plane so θ equals Statcast spin_axis exactly (was relative to release velocity; ≈ 4° off for gyro-heavy presets). γ now = tilt out of X–Z toward the plate.
- A: model validity flag (min Re ≥ 1.5e5, max S ≤ 0.4) reported in PitchMetrics and PitchLab, rather than silently extrapolating.
- A: rejected finding — "no test for plane+ground in the same step" (it existed; strengthened with the mirror case and a same-step precondition).
- A: accepted, not changed — ground checked at step ends only (documented; a pitch cannot dip and recover within 5 ms).

## Remaining risks
