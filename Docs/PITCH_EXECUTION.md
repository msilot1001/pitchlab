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
- Spin rate: the type preset's (effective) spin × (1 + 0.20 r̂(Movement)) — TUNED: lift grows sublinearly with spin
  (d ln C_L / d ln S ≈ 0.56), so ±20 % spin ≈ ±11 % movement (≈ ±2 in of four-seam rise), a modest share of the real
  pitcher-to-pitcher spread. Scaling total spin keeps the calibrated efficiency (axis, gyro) — ADR 0003 holds in the mean.
- Fatigue: his speed is lowered before the aim is solved, so a tired pitcher loses velocity but does not miss low on
  average.
- No target: his pitch is aimed at where the type's preset itself crosses (his speed, spin and side would move it).
- Release point, angles, spin axis and gyro: the type's preset (calibrated, Docs/PHYSICS.md).
- Left-handers: the mirror image across x = 0 — release side and azimuth change sign; spin is a pseudovector, so under the
  reflection ω → (ωx, −ωy, −ωz), i.e. spin axis θ → −θ and gyro γ → −γ. In still air (or a head/tail wind) the Magnus force,
  the crossing and the movement then mirror exactly (tested; a crosswind is, correctly, not mirrored), and the presentation
  mirrors the delivery figure.

## Aiming
The target (a point in the batter's zone, `PitchTargets`) is reached by correcting only the release angles of his pitch from
where the simulated flight crosses the front of the plate (three iterations; within ≈ 3 cm).

## Execution (per pitch, seeded)
| Quantity | Model | Basis |
|---|---|---|
| Release-angle error | per-axis σ = 0.7° · (1 − 0.35 r̂(Command)) · (1 − 0.15·familiarity/50); ≈ 27 cm per degree at the plate → average ≈ 7.4 in per axis (mean radial ≈ 9 in), elite ≈ 5 in, poor ≈ 9–10 in | TUNED between sources that disagree: a mean miss of 11–13 in from the catcher's target (COMMANDf/x, Driveline — MEASURED, secondary; glove drift inflates it) implies σ ≈ 9–10 in per axis (mean radial = 1.25 σ); Inside Edge's 58 % within ≈ 6 in implies ≈ 4.6 in. Nasu & Kashino 2021: 1-SD release angle ≈ 0.7° → ≈ 19 cm at the plate (MEASURED). |
| Error shape | horizontal and vertical errors correlated: up-and-arm-side ↔ down-and-glove-side, |ρ| = 0.3 (a right-hander's arm side is −X: ρ = −0.3; a left-hander +0.3) | Shinya et al. 2017: arm-slot-oriented miss ellipse (MEASURED shape); |ρ| ASSUMED |
| Heavy tail | 3 % of pitches with 2.2× the spread | MLB has a few % non-competitive misses (> 18 in; Driveline) — ASSUMED |
| Speed | σ 0.9 mph | within-pitcher ≈ 1.0 mph (Statcast pitch-level data, one pitcher, DERIVED) |
| Spin rate | σ 70 rpm | ≈ 68–90 rpm (Nasu 2021; Statcast pitch-level, DERIVED) |
| Spin axis | σ 4° fastballs, 6° changeup, 8° breaking balls | ≈ 4° within a start for a four-seamer (Statcast pitch-level, DERIVED); wider for breaking balls (ASSUMED) |
| Fatigue | past 85 ± 25 r̂(Stamina) pitches: angle spread +0.5 %/pitch (≤ +25 %); speed −0.02 mph/pitch (≤ 1.5 mph, in the aimed pitch) | deliberately mild, ASSUMED |

The deviates come from `SeedStream(game seed, pitcher, plate appearance, pitch number)` — a hash, no shared random state:
the same game seed and inputs give the same pitches at any frame rate. The GameLab gives each new game its own seed. `ExecutionVariance = false` (debug)
throws the intended pitch exactly; the labs without a game always throw the presets exactly.

## Sample distributions (four-seamer aimed at the middle of the zone, 300 pitches, regression check — not validation)
| Command | RMS miss horizontal / vertical | Strikes |
|---|---|---|
| 90 (elite) | 5.6 / 5.4 in | 92 % |
| 50 (average) | 7.9 / 8.0 in | 72 % |
| 15 (poor) | 9.2 / 9.1 in (2 of 300 in the dirt) | 63 % |

Speed: mean = his rated speed, SD ≈ 0.9 mph.

## Limitations
- No release-point (position) variation, no tunnelling, no pitch-specific execution spread beyond familiarity.
- Command ranges are ASSUMED around measured averages; no per-count execution change (e.g. a 3–0 pitch is executed like any other).
