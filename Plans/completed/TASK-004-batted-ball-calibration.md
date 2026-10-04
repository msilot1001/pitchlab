# TASK-004 — Batted-ball calibration

## Goal
Resolve or rigorously explain the ≈ +30 ft batted-ball overcarry against 2024 Statcast (dev +28.8 ft, holdout +34.6 ft; pre-registered target |mean| ≤ 10 ft, RMS ≤ 25 ft — unchanged).

## Scope
Audit the batted-ball pipeline; validate the flight model and the contact model separately; evaluate evidence-backed candidate models on development data, then holdout; update `Docs/VALIDATION_TASK004.md`. No fielding, no gameplay fudge factors (no distance/EV/LA/stadium corrections).

## Owner
Claude Code (sole writer). Read-only: physics-researcher, physics-reviewer, test-reviewer.

## Constraints
Pitch physics, TASK-002 validation, TASK-003 contact and input determinism must not regress. Dev/holdout split preserved; decisions use development/reference evidence only.

## Hypotheses (status: supported / weakened / ruled out / unresolved)
- H1 implementation / unit / geometry bug — ruled out (independent Python model; Nathan calculator reproduced)
- H2 exit-velocity measurement convention — ruled out as cause
- H3 tracker-era mismatch — weakened (2024 − 2016 at matched EV/LA ≈ −8 ft, model-free)
- H4 drag model (incl. seasonal ball drag) — weakened (unchanged coefficients pass)
- H5 lift / spin model — supported: the validation spin input (backspin only) was the root cause
- H6 ContactResolver batted-ball spin — supported, separate open issue (backspin only, ≈ 1.6–1.8× Statcast)
- H7 environment — ruled out
- H8 Statcast distance semantics — ruled out as cause
- H9 other — none found
- H10 seam / ball construction — RULED OUT as the cause (≈ 1–14 ft plausible; implicit in the fit; gate fails 5/5 → deferred)

## Milestones
1. [x] Pipeline audit + independent numeric checks
2. [x] Flight-model validation with external initial conditions / benchmarks
3. [x] Contact-model validation against collision literature
4. [x] Research: data availability, EV convention, seasonal drag, C_D(Re), contact spin (agent A); seam aerodynamics, SSW, double counting (agent B)
4b. [x] H10: implicit-vs-explicit seam effects, TASK-002 SSW evidence classification, seam-bounded carry sensitivity; implementation gate (all 5 conditions) before any seam model
5. [x] Candidate models (dev), decision, holdout evaluation
6. [x] Reviews (physics-reviewer, test-reviewer), fixes
7. [x] check.sh, Unity MCP verification, docs, commit

## Verification
- Independent Python flight model (written from Nathan 2017, not ported): 103/27/2000 → 426.5 ft (C# 426.5); 108/28/2100 → 451.6 ft (C# lab 451).
- Kagan & Nathan calculator case (107.3 mph, 26.1°, 1500 rpm, C_D 0.34, no decay, 70 °F sea level assumed): 445–447 ft vs published 447 ft (hang 5.27 vs 5.39 s). Same paper: actual 2016 Statcast distance of that HR 470.5 ft.
- Environment sensitivity at 100/28/2100: ±10 °F → ±2.9 ft; RH 0–100 % → ±0.7 ft; ±300 ft pressure-equivalent → ±1.5 ft; 3 mph head/tail wind → −10.8/+10.2 ft (dome: no wind). −30 ft needs ρ ≈ 1.45 (≈ −40 °F) → H7 ruled out as main cause.
- ContactResolver vs Kensrud/Nathan/Smith game scenario (85 mph pitch, 6° descent, 73 mph bat, 6° attack), undercut D 0 → 2 in: EV 106.2 → 81.2 mph; LA 6 → 78°; spin 0 → ≈ 9000 rpm, ≈ 160 rpm/° of LA (Statcast medians ≈ 2000 rpm at 25°, 2500 at 30° → ours ≈ 1.6–1.8× higher). Separate from the Statcast replay (which uses Statcast EV/LA and an external spin model).

## Discoveries
- The backspin-only replay overcarried 2016 Tropicana balls (the fit's own training season) by +24.8 ft too, so the bias was not era-related.
- Nathan's calculator ships an average-Statcast spin model with large spray-dependent sidespin; with it the unchanged fit gives 2016 −4.0 ft, 2024 +2.6 ft (full seasons, scratch downloads).
- Nathan's analysis step fitted each ball's spin axis; a backspin-only replay is a different input distribution.

## Decisions
- 2026-10-04: adopt Nathan's average-Statcast spin (with sidespin) as the validation spin input; keep the aero model unchanged. Decided on 2016 + development; holdout evaluated afterwards (dev +0.9 / 14.6 ft RMS, holdout +2.1 / 13.5). Scratch full-2024 check contained the holdout games (disclosed in docs).
- `BattedBallLaunch` sidespin axis follows Nathan Eq. 4 (⟂ to launch velocity).
- No explicit seam model (H10 gate fails).

- Reviews (physics-reviewer, test-reviewer, read-only): confirmed and fixed — PHYSICS.md "Nathan uses RK4" (calculator is Euler-type), doc wording (post-hoc deviation, circularity, sidespin mechanism), lab caption/presets without sidespin, sidespin slider range, stale test comment; added spray-sign/hand guard (`BattersPullGroundBalls`) and L/R mirror test. Lift formulation (Nathan C_L(S_total)·(ω×v)/ω vs our C_L(S⊥)) quantified at +1–5 ft, documented, not changed (shared with validated pitch model).
- Verification: check.sh EditMode 111/117 passed (rest ignored/explicit), PlayMode 9/9; Unity Play Mode: PitchLab presets + zero spin, HittingLab centered/early/late/undercut/miss + flight, BattedBallLab presets; live console clean apart from a CLI request aborted by Play Mode exit.

## Remaining risks
- ContactResolver spin (H6) is unvalidated; distance-dependent gameplay depends on it.
- Average spin model only; ≈ 14 ft per-ball RMS.
