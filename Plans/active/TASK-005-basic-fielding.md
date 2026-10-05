# TASK-005: Basic fielding

## Goal
Pitch → hit → ball in play → defense reacts → primary fielder selected → fielder moves → catch or ground pickup → ball possession. There is no throwing yet; that starts in TASK-006A from possession.

## Owner
- **Writer:** Claude Code is the sole writer.
- **Read-only:** physics-researcher, physics-reviewer, unity-reviewer, test-reviewer; Codex for the pre-merge review.
- **Branch:** `feat/task-005-basic-fielding`, from `main` d1be3af.

## Architectural constraints
- Layering is Simulation ← Gameplay ← Presentation.
- Fielding consumes the **authoritative** `BallInPlay` (TASK-004.7) and nothing else: no second trajectory, no parabola, no landing guess.
- Gameplay owns fielder state and motion. `PlayerMannequin` only shows it.
- No NavMesh and no Rigidbody authority.
- Fielder motion is an analytic function of time, so prediction and actual motion are the same code path, and frame rate never matters.
- Airborne flight, surface physics, contact, timing windows and PCI thresholds are untouched.

## Design
- **`Gameplay/Fielding`:**
  - `DefensivePosition` (P C 1B 2B 3B SS LF CF RF) and `DefensiveAlignment` (generic start positions).
  - `FielderProfile`: reaction, top speed, acceleration time constant, braking, catch and pickup envelope.
  - `FielderMotion`: deterministic straight-line route; exponential sprint law v = v_max(1 − e^(−t/τ)); optional braking to stop.
  - `InterceptSolver`: earliest plausible intercept on the authoritative trajectory, by sample search plus bisection.
  - `FieldingPlay` / `FieldingSolver`: all fielders evaluated, one primary chosen; the others hold, ready.
  - Possession is a function of time.
- **Sandbox presentation `DefenseView`:** nine mannequins (the pitcher's mannequin is adopted in the HittingLab); fielding poses, a run cycle phased by distance (no foot sliding), and glove targeting of the authoritative intercept.
- **Camera:** frames both the ball and the primary fielder.
- **Debug overlay:** route, intercept marker and numbers.
- **FieldingLab:** a new scene with ten presets, driven by the production `BallInPlay` and the solver.

## Milestones
- [x] Gameplay core with EditMode tests: profiles, motion, reach time, intercept, selection, possession.
- [x] Presentation: fielding poses, `DefenseView`, glove, camera, debug overlay.
- [x] FieldingLab scene and presets; HittingLab integration (state machine ends at possession).
- [x] Runtime scenarios in the Game View: SS grounder, chopper, CF/RF fly, wall rebound, unreachable deep ball; the glove meets the ball in every preset.
- [x] **physics-reviewer.** Fixed:
  - a ball at rest is now picked up (it never was);
  - a ground foul is fair if taken over fair ground;
  - selection is non-transitive no more;
  - reach shrinks above 2 m;
  - 5 ms grid;
  - the comfort shift applies only to a falling ball;
  - the held ball is secured to the chest;
  - documentation fixes.
- [x] **unity-reviewer.** Fixed:
  - pitcher takeover: root, heading and pose blend on a deterministic schedule, and the shown pose is written back for the next pitch;
  - no speed-toggle jump;
  - smooth heading;
  - figures on the mound's surface;
  - glove carries the ball into the hold;
  - no per-frame `Enum.GetValues`.
- [x] **test-reviewer.** Added:
  - a ball resting before anyone arrives;
  - a fair roller vs a dead foul;
  - selection: earliest, margin, chain, priority;
  - a nearer defender who reacts more slowly;
  - reaction respected by the solved motion;
  - inclusive height boundaries;
  - possession ending the batting play;
  - every preset must be fielded;
  - the ball stays free before possession.
- [x] check.sh; commit `feat: add deterministic trajectory-based fielding`.
- [ ] Codex review; merge.

## Verification
- **`Scripts/check.sh`:**
  - whitespace OK;
  - EditMode 426 total, 420 passed, 0 failed (6 explicit, pre-existing);
  - PlayMode 39/39.
- **New suites:**
  - FielderMotionTests 8;
  - FieldingTests 27;
  - FieldingLabSceneTests 4;
  - HittingLab fielding integration 1;
  - BattingStateMachineTests 7.
- **Game View** (HittingLab and FieldingLab):
  - RF catch at the wall, with the ball in the glove and a "caught by RF" banner;
  - 2B pickup;
  - SS charge;
  - chopper fielded by P;
  - wall carom taken by LF;
  - drop in the gap, taken on the hop by CF;
  - all nine defenders shown, no swarm;
  - Console clean (one tooling-bridge error only).

**Scenario table** (FieldingLab presets, production pipeline; FieldingLabController.Report):

| Play | Fielder | Start→route | Intercept | Ball state | Margin | Result |
|---|---|---|---|---|---|---|
| Routine SS grounder (85 mph, -8°, -15°) | SS (reacts 0.25 s) | 7.1 m | +1.74 s at (-9.5, 36.2, 0.04) m | Rolling, 14.8 m/s | 0.00 s | GroundPickup, Fair |
| Slow roller (45 mph, -12°, +8°) | P (reacts 0.45 s) | 1.9 m | +1.15 s at (2.1, 15.4, 0.04) m | Rolling, 9.7 m/s | 0.00 s | GroundPickup, Fair |
| Hard grounder (105 mph, -6°, -5°) | CF (reacts 0.40 s) | 22.8 m | +3.84 s at (-6.6, 75.7, 0.04) m | Rolling, 9.3 m/s | 0.00 s | GroundPickup, Fair |
| Chopper (60 mph, -20°, +5°) | P (reacts 0.45 s) | 1.1 m | +0.98 s at (1.3, 15.6, 0.04) m | Airborne, 13.5 m/s | 0.00 s | GroundPickup, Fair |
| Shallow fly (75 mph, +40°, +10°) | CF (reacts 0.40 s) | 21.6 m | +4.83 s at (14.1, 80.4, 1.72) m | Airborne, 22.6 m/s | 1.13 s | FlyCatch, Fair |
| Routine CF fly (92 mph, +32°, +0°) | CF (reacts 0.40 s) | 12.0 m | +5.22 s at (0.0, 111.1, 1.80) m | Airborne, 24.2 m/s | 2.69 s | FlyCatch, Fair |
| Gap fly (90 mph, +20°, +15°) | RF (reacts 0.40 s) | 20.6 m | +3.69 s at (25.3, 95.1, 1.76) m | Airborne, 23.8 m/s | 0.02 s | FlyCatch, Fair |
| Line drive (98 mph, +14°, -8°) | CF (reacts 0.40 s) | 13.1 m | +2.97 s at (-12.9, 92.6, 1.78) m | Airborne, 26.1 m/s | 0.30 s | FlyCatch, Fair |
| Wall rebound (105 mph, +14°, -20°) | LF (reacts 0.40 s) | 24.3 m | +4.80 s at (-37.7, 104.5, 2.60) m | Airborne, 4.2 m/s | 0.68 s | HopCatch, Fair |
| Unreachable deep ball (100 mph, +20°, -15°) | CF (reacts 0.40 s) | 29.2 m | +4.60 s at (-28.3, 108.5, 1.07) m | Airborne, 5.8 m/s | 0.00 s | HopCatch, Fair |



## Discoveries / decisions
- Reference values: physics-researcher, 2026-10-05; see `Docs/FIELDING.md`.

## Remaining risks
- **Coefficients:** the profiles (reaction, reach, catch height) are ASSUMED or derived from Statcast aggregates. Calibrate them against Statcast catch probability.
- **Fielding simplifications:**
  - straight routes only;
  - no dives, jumps or backups;
  - no foul catches;
  - the infield-pickup error zone (in-between hop) is not modelled.
- **Search grid:** a feasibility window shorter than 5 ms can be missed (a ball passing within about 0.5 cm of the edge of reach).
- **Presentation:**
  - the stance foot slides while accelerating from rest;
  - the HittingLab camera frames the fielder one frame late;
  - the catcher's ready pose is standing (not crouched).
