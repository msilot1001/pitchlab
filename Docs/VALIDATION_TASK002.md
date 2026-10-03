# TASK-002 — Statcast validation of the pitch-flight model

Regenerate: run the explicit test `StatcastValidationTests.WriteValidationReport` (e.g. MCP `run_tests` with `filter=WriteValidationReport`, `include_explicit=true`); it writes `TestResults/statcast_validation_{pitches.csv,summary.md}`. Acceptance and regression checks run in `Scripts/check.sh`.

## Data

649 pitches, every tracked pitch from two 2024 games (fixture and provenance: `Assets/Game/Tests/Fixtures/Statcast/`):

| role | game | conditions (MLB Stats API) | air density used | pitches |
|---|---|---|---|---|
| development | Chase Field, 2024-04-01 | roof closed, 72 °F, 1086 ft, no wind | 1.143 kg/m³ | 323 |
| holdout | Yankee Stadium, 2024-04-05 | open, 46 °F, 55 ft, 16 mph out to LF | 1.252 kg/m³ | 326 |

Humidity (assumed 50 %) and station pressure (standard atmosphere for the elevation) are not reported. Families (grouping only): four-seam 233, slider/sweeper 137, changeup/splitter 83, sinker 73, cutter 71, curveball 52. Also committed: Savant 2024 Hawk-Eye *spin-based* active spin for the 17 pitchers involved.

## Field interpretation (Baseball Savant CSV docs; Nathan)

- Frame: catcher's view, origin at the rear point of the plate, +y toward the pitcher, +z up, +x to the catcher's right — identical to the simulation axes; only units change (ft → m). **Verified**: the reconstructed fit reproduces `plate_x/plate_z` within 0.01 ft for all 649 pitches and `release_speed` within 0.2 mph.
- `vx0..az`: 9-parameter constant-acceleration fit with t = 0 at y = 50 ft (ft/s, ft/s², gravity included). The CSV has no x0/z0; they are recovered from `release_pos_*` by solving y(t) = `release_pos_y` (`StatcastNinePointFit`).
- `plate_x/plate_z`: at the **front edge of the plate**, y = 17/12 ft (matches `PitchingGeometry.PlateFrontY`).
- `spin_axis`: degrees in the X–Z plane, 180 = pure backspin. It is the **measured (Hawk-Eye spin-based) axis**, not inferred from movement: a movement-inferred axis would deviate ≈ 0° from the observed Magnus direction, but the median deviation is 13° (development) / 21° (holdout). The signed deviation mirrors between right- and left-handers for four-seam (R +7.9°, L −10.6°), sinker, changeup and cutter, with the signs Nathan reports for seam-shifted wake (right-handed four-seam glove side, sinker/changeup arm side); it does **not** mirror for sliders (development L −26°, R −22°) or curveballs (L +9.4°, R +8.9°), where the cause is unexplained (axis-projection effects of large gyro components are one candidate, see below). The small four-seam deviation also rules out a mirrored axis convention.
- `pfx_x/pfx_z` (feet): best matched by the spin-induced deviation from a spinless trajectory **over release → plate**, drag-corrected: median |Δ| 0.21 in (x), 1.12–1.34 in (z, bias −1.2 in). The PITCHf/x 40 ft window is clearly wrong (2.4–5.3 in). The exact drag treatment in Savant's z value is unresolved (≈ 1 in).
- Pitchlab's movement metric (simulated flight − no-lift flight from the same release state, at the plate front) is the same concept as Savant pfx; the remaining differences are dominated by the spin-axis issue below (median 1.7–2.5 in; 14 pitches that bounce before the plate have no value).

## Results against the pre-registered criteria

Thresholds were fixed in the ExecPlan before any replay. Failing criteria stay in the suite as `[Ignore]`d tests with this document as the reason. Medians drop non-finite values (pitches that bounce before the plate); counts are in the generated report.

| # | criterion | result | |
|---|---|---|---|
| 1 | fit reproduces plate within 0.03 ft (≥ 95 %), release speed 0.2 mph | max 0.01 ft, all pitches | **pass** |
| 2 | time y = 50 ft → plate within 3 ms (median); implied C_D 0.30–0.40 | development 0.74 ms, C_D 0.34 (four-seam 0.35); holdout 2.69 ms, C_D 0.31 | **pass** |
| 3 | four-seam implied efficiency median 0.80–1.00, ≤ 10 % above 1.05 | median 0.86; 17 % above 1.05 | **fail** |
| 4a | replay with implied transverse spin about `spin_axis`: median 2D ≤ 1 in | 2.71 in / 3.14 in | **fail** |
| 4b | holdout replay with development family-median efficiency: median 2D ≤ 3 in | 5.73 in (p90 11.0) | **fail** |
| 5 | a pfx definition within 1 in median on both axes | best: 0.21 / 1.12 in | **fail (narrowly)** |

### Per family (development \| holdout)

| family | implied C_D | implied efficiency | spin_axis vs movement (median \|°\|) | replay 2D median, `spin_axis` direction (4a) | replay 2D median, observed Magnus (diagnostic) |
|---|---|---|---|---|---|
| four-seam | 0.35 \| 0.33 | 0.86 \| 0.76 | 7 \| 11 | 2.1 \| 3.0 in | 0.15 \| 0.23–0.28 in |
| sinker | 0.35 \| 0.30 | 1.03 \| 0.55 | 14 \| 33 | 3.2 \| 6.6 in | 0.46 \| 0.43–0.61 in |
| changeup/splitter | 0.33 \| 0.31 | 0.81 \| 0.47 | 23 \| 22 | 4.5 \| 4.1 in | 0.46–0.60 \| 0.57–0.79 in |
| cutter | 0.32 \| 0.29 | 0.39 \| 0.11 | 32 \| 45 | 4.8 \| 3.2 in | 0.30 \| 0.54–0.60 in |
| slider/sweeper | 0.33 \| 0.30 | 0.28 \| 0.14 | 24 \| 28 | 2.8 \| 2.0 in | 0.42–0.54 \| 0.50–0.73 in |
| curveball | 0.34 \| 0.30 | 0.22 \| 0.21 | 10 \| 20 | 1.9 \| 1.7 in | 0.66–0.88 \| 0.87 in |
| **all** | **0.34 \| 0.31** | **0.78 \| 0.40** | **13 \| 21** | **2.7 \| 3.1 in** | **0.32 \| 0.58 in** |

### Lift magnitude vs measured spin (per game; medians over pitcher × pitch-type groups with ≥ 3 pitches)

"Model ÷ flight lift" is our C_L at the Hawk-Eye-measured transverse spin (rate × active spin, mid-flight speed) divided by the lift coefficient implied by the tracked flight. "÷ Savant observed" compares our implied efficiency with Savant's own movement-based efficiency (a pipeline cross-check, not physics).

| pitch type | groups dev \| holdout | implied eff ÷ Hawk-Eye active spin | ÷ Savant observed | measured S⊥ | model ÷ flight lift |
|---|---|---|---|---|---|
| four-seam | 6 \| 7 | 0.92 \| 0.90 | 1.02 \| 0.94 | 0.21 \| 0.19 | 1.05 \| 1.06 |
| sinker | 4 \| 4 | 1.11 \| 1.02 | 1.10 \| 1.00 | 0.19 \| 0.20 | 0.95 \| 0.99 |
| changeup | 5 \| 2 | 0.89 \| 0.73 | 0.97 \| 0.78 | 0.18 \| 0.17 | 1.08 \| 1.23 |
| curveball | 2 \| 2 | 0.57 \| 0.62 | 0.61 \| 0.74 | 0.17 \| 0.13 | 1.74 \| 1.49 |
| cutter | 3 \| 4 | 0.62 \| 0.31 | 0.72 \| 0.40 | 0.14 \| 0.10 | 1.56 \| 2.58 |
| slider | 5 \| 5 | 0.67 \| 0.54 | 0.68 \| 0.59 | 0.10 \| 0.06 | 1.37 \| 1.67 |

## What the residuals are (B6)

1. **No implementation or interpretation bug found.** The fit reconstruction is exact. Replaying each pitch from its y = 50 ft state with the *observed* Magnus acceleration (direction from the fit; magnitude inverted through C_L and re-applied by the same C_L, so lift magnitude is an identity here) lands a median 0.32 in (development) / 0.58 in (holdout) from the tracked plate location, flight time within 1 ms (development). That checks drag, frame, units, the integrator and how Magnus scales along the flight — **not** C_L itself. Replays that end at the ground are exactly the pitches Statcast tracks crossing below ball height (bounced; the fit extrapolates them underground).
2. **Drag is consistent in the one clean game.** Closed roof: implied C_D 0.34 overall, 0.35 for four-seamers, matching the model. Open-air holdout: 0.31 (11 % low), with equally low implied lift; implied C_D scales with 1/ρ_assumed and humidity, pressure and field-level wind are not known. Applying the reported 16 mph stadium wind makes it worse (C_D 0.24), so that reading does not describe the air the pitches flew through.
3. **Most of failure 4a is the spin axis.** Statcast `spin_axis` is the measured spin axis; the movement direction departs from it by family- and hand-dependent angles. For four-seam, sinker and changeup this matches seam-shifted-wake (SSW) forces (Nathan, Hawk-Eye III: ≈ 34° arm-side for sinkers, ≈ 5° glove-side for four-seamers), which the model does not have. Sliders and curveballs deviate with the same sign for both hands, which SSW does not explain. A contributor for gyro-heavy pitches: `StatcastAdapter.TransverseSpin` puts the spin in the X–Z plane with no gyro component, while the true transverse spin of a pitch released 1–3° off −Y includes ≈ ω_gyro·sin(angle) of the gyro spin (≈ 240 rpm for a slider, against ≈ 700 rpm of transverse spin), and the model converts gyro into transverse spin during flight. These change breaking-ball axis and magnitude by up to ~15–20° / ~15–20 %; they do not explain a 1.5× lift gap.
4. **Lift magnitude: consistent for high-efficiency pitches, too strong for breaking balls.** At the Hawk-Eye-measured transverse spin our C_L gives four-seam 1.05 / 1.06, sinker 0.95 / 0.99, changeup 1.08 / 1.23 of the lift the flights show — within the ±20 % scatter Nathan quotes for C_L data (sinker/changeup also carry SSW). For breaking balls it gives 1.4–2.6× too much lift (curveball 1.74 / 1.49, slider 1.37 / 1.67, cutter 1.56 / 2.58). Our implied efficiency also matches Savant's movement-based efficiency for four-seamers (1.02 / 0.94) and sinkers (1.10 / 1.00), confirming the pipeline. Ordered by measured S⊥, the excess grows as S⊥ falls (four-seam ≈ 0.20 → 1.05, curveball ≈ 0.15 → 1.6, cutter ≈ 0.12 → 2, slider ≈ 0.08 → 1.5 with large scatter), so a **lower C_L at low transverse spin** (seam-orientation-dependent lift and reverse-Magnus effects; Lyu et al. 2022) is a leading candidate, alongside SSW and the gyro geometry in point 3. With 3–5 groups per family, season-level active spin and these confounders, the data **underdetermine** the mechanism.
5. **Failure 3** (17 % of four-seamers above 1.05): consistent with the ±20 % scatter and SSW adding vertical movement for some fastballs; the median (0.86) is in range. The alternative pitch-based fit (Nathan 2020, C_L = 0.336[1 − e^(−6.041 S)], ≈ 10 % more lift at pitch S) would move the four-seam lift ratio further from 1.
6. **Failure 4b** (holdout prediction 5.7 in) mostly reflects that spin efficiency is a per-pitcher property (e.g. cutters 0.39 vs 0.11 between the two games' pitchers) plus points 2–4; family medians are a poor stand-in.

## Decisions (B7)

- **No coefficient changes.** C_D 0.35 is consistent with the clean game; C_L is consistent for high-efficiency pitches; a low-S⊥ lift reduction is plausible but underdetermined by this data, and a per-pitch-type correction is not allowed.
- **C_L provenance corrected**: the constants are Nathan (2017) Eqs. 10–11, fitted to 2016 Statcast *fly balls* (exit speed ≥ 90 mph, launch angle 20–35°), not pitches (`Docs/PHYSICS.md`).
- **Game inputs** (ADR 0003): pitch spin inputs — rate × efficiency *and* axis — are *effective, movement-equivalent* transverse spin. Hawk-Eye active spin and `spin_axis` are not used directly for breaking balls until seam effects are modelled.
- **Movement definition**: kept; it is the same concept as Savant pfx (release → plate).

## Limitations

Two games, 17 pitchers, 3–7 groups per pitch type; season-level (not per-pitch) active spin; per-game air density estimated from temperature and elevation; humidity and field-level wind unknown; Statcast's 9P fit is itself a constant-acceleration approximation (its fitted acceleration is a flight average).

## Future research

Larger sample (more games and pitchers, per-pitch spin-based efficiency if available), low-S⊥ lift vs seam orientation, SSW, game-level drag variation.
