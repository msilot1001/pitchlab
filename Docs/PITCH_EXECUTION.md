# Pitch execution

A pitch has an **intent** (type, target) and an **execution** (the physical pitch that was actually released). Execution
perturbs only the release inputs; the validated pitch physics (Docs/PHYSICS.md) decides where the ball goes and the call
reads the simulated crossing. Code: `PitchExecution`, `GamePitches` (Assets/Game/Gameplay).

Labels: MEASURED / DERIVED / ASSUMED / TUNED (see Docs/PLAYER_RATINGS.md).

## The pitcher's own pitch
- Repertoire (`Repertoire`): the pitch types he throws, with usage weights, a speed offset per pitch and a familiarity
  (−50…+50) that adjusts his command of that pitch. He throws nothing else (manual keys cycle his repertoire; the automatic
  pitcher picks by usage).
- Speed: league average for the type + 3.2 mph · r̂(Velocity) + his offset. League averages are the 2025 per-pitcher means
  (four-seam 93.8, sinker 93.2, slider 85.8, curveball 80.4, changeup 86.8 mph) and ±3.2 mph is the P10–P90 spread of
  four-seam averages (90.5–97.0) — DERIVED from the Baseball Savant pitch-arsenal leaderboard.
- Spin rate: the type preset's (effective) spin × (1 + 0.08 r̂(Movement)) — ASSUMED; the physics turns spin into movement.
- Release point, angles, spin axis and gyro: the type's preset (calibrated, Docs/PHYSICS.md).
- Left-handers: the mirror image across x = 0 — release side and azimuth change sign; spin is a pseudovector, so under the
  reflection ω → (ωx, −ωy, −ωz), i.e. spin axis θ → −θ and gyro γ → −γ. The Magnus force, the crossing and the movement then
  mirror exactly (tested), and the presentation mirrors the delivery figure.

## Aiming
The target (a point in the batter's zone, `PitchTargets`) is reached by correcting only the release angles of his pitch from
where the simulated flight crosses the front of the plate (three iterations; within ≈ 3 cm).

## Execution (per pitch, seeded)
| Quantity | Model | Basis |
|---|---|---|
| Release-angle error | per-axis σ = 0.6° · (1 − 0.35 r̂(Command)) · (1 − 0.15·familiarity/50); ≈ 27 cm per degree at the plate → average ≈ 6.4 in per axis, elite ≈ 4.5 in, poor ≈ 9 in | MLB average miss from the catcher's target ≈ 11–13 in (≈ 6–8 in per axis; COMMANDf/x, Driveline, Inside Edge — MEASURED, secondary); Nasu & Kashino 2021: 1-SD release angle 0.7° → ≈ 19 cm at the plate (MEASURED). Spread ASSUMED. |
| Error shape | horizontal and vertical errors correlated ρ = 0.3 (right-hander: up/arm side ↔ down/glove side; mirrored for a left-hander) | Shinya et al. 2017: arm-slot-oriented miss ellipse (MEASURED shape); ρ ASSUMED |
| Heavy tail | 3 % of pitches with 2.2× the spread | MLB has a few % non-competitive misses (> 18 in; Driveline) — ASSUMED |
| Speed | σ 0.9 mph | within-pitcher ≈ 1.0 mph (Statcast pitch-level data, one pitcher, DERIVED) |
| Spin rate | σ 70 rpm | ≈ 68–90 rpm (Nasu 2021; Statcast pitch-level, DERIVED) |
| Spin axis | σ 4° | ≈ 4° within a start (Statcast pitch-level, DERIVED) |
| Fatigue | past 85 ± 25 r̂(Stamina) pitches: angle spread +0.5 %/pitch (≤ +25 %), speed −0.02 mph/pitch (≤ 1.5 mph) | deliberately mild, ASSUMED |

The deviates come from `SeedStream(game seed, pitcher, plate appearance, pitch number)` — a hash, no shared random state:
the same game seed and inputs give the same pitches at any frame rate. `ExecutionVariance = false` (debug) throws the
intended pitch exactly; the labs without a game always throw the presets exactly.

## Sample distributions (four-seamer aimed at the middle of the zone, 300 pitches, regression check — not validation)
| Command | RMS miss horizontal / vertical | Strikes |
|---|---|---|
| 90 (elite) | 4.9 / 4.7 in | 96 % |
| 50 (average) | 6.8 / 6.7 in | 82 % |
| 15 (poor) | ≈ 9 in (a few pitches in the dirt) | 74 % |
Speed: mean = his rated speed, SD ≈ 0.9 mph.

## Limitations
- No release-point (position) variation, no tunnelling, no pitch-specific execution spread beyond familiarity.
- Command ranges are ASSUMED around measured averages; no per-count execution change (e.g. a 3–0 pitch is executed like any other).
