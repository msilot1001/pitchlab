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
- `spin_axis`: degrees in the X–Z plane, 180 = pure backspin. Evidence here shows it is the **measured (Hawk-Eye spin-based) axis**, not inferred from movement: the observed Magnus direction differs from it by a median 13° (development) / 21° (holdout), and the signed difference mirrors between right- and left-handers in every family tested (e.g. four-seam R +7.9°, L −10.6°; changeup R −24.9°, L +8.8°; cutter R +35°, L −12°). A wrong axis convention would shift both hands the same way.
- `pfx_x/pfx_z` (feet): best matched by the spin-induced deviation from a spinless trajectory **over release → plate**, drag-corrected: median |Δ| 0.21 in (x), 1.12–1.34 in (z, bias −1.2 in). The PITCHf/x 40 ft window is clearly wrong (2.4–5.3 in). The exact drag treatment in Savant's z value is unresolved (≈ 1 in).
- Pitchlab's movement metric (simulated flight − no-lift flight from the same release state, at the plate front) is the same concept as Savant pfx; the remaining differences are dominated by the spin-axis issue below (median 1.7–2.5 in).

## Results against the pre-registered criteria

Thresholds were fixed in the ExecPlan before any replay. Failing criteria stay in the suite as `[Ignore]`d tests with this document as the reason.

| # | criterion | result | |
|---|---|---|---|
| 1 | fit reproduces plate within 0.03 ft (≥ 95 %), release speed 0.2 mph | max 0.01 ft, all pitches | **pass** |
| 2 | time y = 50 ft → plate within 3 ms (median); implied C_D 0.30–0.40 | dev 0.73 ms, C_D 0.34 (four-seam 0.35); holdout 2.62 ms, C_D 0.31 | **pass** |
| 3 | four-seam implied efficiency median 0.80–1.00, ≤ 10 % above 1.05 | median 0.86; 17 % above 1.05 | **fail** |
| 4a | replay with implied transverse spin about `spin_axis`: median 2D ≤ 1 in | 2.66 in / 3.02 in | **fail** |
| 4b | holdout replay with development family-median efficiency: median 2D ≤ 3 in | 5.73 in (p90 11.0) | **fail** |
| 5 | a pfx definition within 1 in median on both axes | best: 0.21 / 1.12 in | **fail (narrowly)** |

### Per family (development | holdout)

| family | implied C_D | implied efficiency | spin_axis vs movement (median \|°\|) | replay 2D median, spin_axis direction | replay 2D median, observed direction |
|---|---|---|---|---|---|
| four-seam | 0.35 \| 0.33 | 0.86 \| 0.76 | 7 \| 11 | 2.1 \| 3.0 in | 0.15 \| 0.23–0.28 in |
| sinker | 0.35 \| 0.30 | 1.03 \| 0.55 | 14 \| 33 | 3.2 \| 6.6 in | 0.46 \| 0.43–0.61 in |
| changeup/splitter | 0.33 \| 0.31 | 0.81 \| 0.47 | 23 \| 22 | 4.5 \| 3.9 in | 0.46–0.60 \| 0.78 in |
| cutter | 0.32 \| 0.29 | 0.39 \| 0.11 | 32 \| 45 | 4.7 \| 3.2 in | 0.30 \| 0.54–0.59 in |
| slider/sweeper | 0.33 \| 0.30 | 0.28 \| 0.14 | 24 \| 28 | 2.8 \| 2.0 in | 0.42–0.54 \| 0.50–0.73 in |
| curveball | 0.34 \| 0.30 | 0.22 \| 0.21 | 10 \| 20 | 1.9 \| 1.7 in | 0.66–0.88 \| 0.86 in |
| **all** | **0.34 \| 0.31** | **0.78 \| 0.40** | **13 \| 21** | **2.7 \| 3.0 in** | **0.32 \| 0.57 in** |

## What the residuals are (B6)

1. **No implementation or interpretation bug found.** The fit reconstruction is exact; replaying each pitch from its y = 50 ft state with the *observed* Magnus direction and our model's lift magnitude lands a median 0.32 in (development) / 0.57 in (holdout) from the tracked plate location, with flight time within 1 ms (development). Integrator, units, frame, drag and the lift-magnitude path are consistent with the tracking.
2. **Drag is confirmed in clean conditions.** In the closed-roof game the implied C_D is 0.34 overall and 0.35 for four-seamers, exactly the model value. The open-air holdout gives 0.31 (and lower implied lift); applying the reported 16 mph stadium wind makes it worse (C_D 0.24), so that reading does not describe field-level air. Game-level conditions (density, gusts) are the likely cause; Nathan reports game-to-game drag variation of this size.
3. **Most of failure 4a is the spin axis.** Statcast `spin_axis` is the measured spin axis; the movement direction departs from it by family- and hand-dependent angles, the signature of seam-shifted-wake (SSW) forces that act perpendicular to Magnus (Nathan, Hawk-Eye III: ≈ 34° arm-side for sinkers, ≈ 5° glove-side for four-seamers). Our model has no SSW, so a replay that trusts `spin_axis` cannot do better than this deviation allows.
4. **Lift magnitude: good for high-efficiency pitches, too strong for breaking balls.** Implied efficiency ÷ Savant measured active spin, per pitcher and pitch type: four-seam 0.90 (13 groups), sinker 1.09 (8), changeup 0.86 (7) — consistent within the ±20 % scatter Nathan quotes for C_L data. Slider 0.56 (10), curveball 0.62 (4), cutter 0.38 (7). Against Savant's own movement-based ("observed") efficiency our inversion agrees for four-seam 1.02, sinker 1.04, changeup 0.94, which independently confirms the pipeline. For breaking balls, then, **a ball with the measured transverse spin gets ~1.6–2.6× too much transverse-spin effect in our model**. The deficit is not a monotonic function of S⊥ (changeup S⊥ ≈ 0.165 → 0.86, curveball ≈ 0.135 → 0.62, cutter ≈ 0.105 → 0.38, slider ≈ 0.075 → 0.56), so no reshaped C_L(S) explains it; seam-orientation-dependent lift and reverse-Magnus effects at low transverse spin (Lyu et al. 2022) and SSW are the plausible mechanisms, but nothing published quantifies them for MLB breaking balls.
5. **Failure 3 (17 % of four-seamers above 1.05)** is consistent with point 4's ±20 % scatter plus SSW adding to vertical movement for some fastballs; the median (0.86) is in range. Not evidence for changing C_L: the alternative pitch-based fit (Nathan 2020, C_L = 0.336[1 − e^(−6.041 S)], ≈ 10 % more lift at pitch S) would move the four-seam ratio to measured active spin further from 1 (≈ 0.81).
6. **Failure 4b** (holdout prediction 5.7 in) mostly reflects that spin efficiency is a per-pitcher property (e.g. cutters 0.39 vs 0.11 between the two games' pitchers) plus points 2–4; family medians are a poor stand-in.

## Decisions (B7)

- **No coefficient changes.** C_D 0.35 is confirmed; the C_L fit is within the data's scatter for high-efficiency pitches; the breaking-ball deficit is not a function the model can absorb without per-pitch-type correction, which is not allowed.
- **C_L provenance corrected**: the constants are Nathan (2017) Eqs. 10–11, fitted to 2016 Statcast *fly balls* (exit speed ≥ 90 mph, launch angle 20–35°), not pitches (`Docs/PHYSICS.md`).
- **Game inputs**: for breaking balls, pitch definitions should be tuned to *movement* (effective transverse spin), not to Hawk-Eye active spin, until seam effects are modelled.
- **Movement definition**: kept; it is the same concept as Savant pfx (release → plate).

## Limitations

Two games, 17 pitchers; per-game air density estimated from temperature and elevation; humidity unknown; field-level wind unknown; Statcast's 9P fit is itself a constant-acceleration approximation (its fitted acceleration is a flight average).
