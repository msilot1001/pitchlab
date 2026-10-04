# TASK-004 — Batted-ball flight validation (status: flight model consistent with 2024 Statcast given average spin; contact spin conditionally validated in TASK-004.5)

Regenerate: explicit test `BattedBallValidationTests.WriteBattedValidationReport` → `TestResults/batted_validation.md`. Thresholds were pre-registered in the overnight ExecPlan before any replay and are unchanged. The pre-registered **spin input** (backspin only) was replaced after it failed — a post-hoc protocol deviation, documented below.

## Model under test

`AerodynamicModel.BattedBall`, **unchanged**: Nathan, *Analysis of Baseball Trajectories* (2017), Eqs. 10–11 — C_D = 0.297 + 0.0292·(ω/1000 rpm), C_L = 1.12S/(0.583 + 2.333S), jointly fitted to 2016 Statcast (TrackMan) fly balls at Tropicana Field — plus spin decay τ = 30 s. Shared `BallFlightSimulator` (RK4, 5 ms); flight ends at the first ground contact. `BattedBallLaunch` sidespin now uses Nathan's axis (⟂ to the launch velocity, Eq. 4) instead of the vertical axis; identical for a level launch.

## Data

459 balls in play from nine 2024 Tampa Bay home games at Tropicana Field (fixed dome: 72 °F, 15 ft, no wind), 5 development / 4 holdout games; `Assets/Game/Tests/Fixtures/Statcast/batted_*.csv` (`Tools/statcast/make_batted_fixture.py`). 14 balls with repeated (imputed) EV/LA dropped. Spray angle from `hc_x/hc_y` (home plate at (125.42, 198.27)) — the fielding/landing direction, not the launch direction, so sidespin curve feeds slightly back into the spin model; batter hand from `stand`. Statcast `hit_distance_sc` vs our horizontal distance at ground contact.

Public Statcast has no batted-ball spin, spin axis, hang time or apex. Spin therefore comes from **Nathan's average-Statcast spin model** — `TrajectoryCalculator-new-3D.xlsx`, sheet *BattedBallTrajectory-2* ("spin of the batted ball fixed at average Statcast values", baseball.physics.illinois.edu/trajectory-calculator-new3D.html):

ω_b = −763 + 120·LA + 21·φ·s, ω_s = −849·s − 94·φ (rpm; φ spray °, + toward RF; s = +1 RHB, −1 LHB; Nathan's ω_s > 0 breaks toward LF).

## Root cause of the former ≈ +30 ft bias

The pre-registered replay assumed **backspin only** (100 rpm/° × (LA − 7°), no sidespin). Real fly balls carry substantial sidespin: about 850 rpm straightaway, growing ≈ 94 rpm per degree of spray, so pulled and opposite-field balls have 2000–4000 rpm. Sidespin tilts the Magnus force sideways and, mostly, adds drag through total spin (C_D grows 0.0292 per 1000 rpm). At 100 mph / 27.5° (RHB), with backspin held at the model's value, adding the model's sidespin costs 6 ft straightaway and 44–55 ft at ±35° spray. At large spray this relies on the linear C_D(ω) and C_L fits beyond their fitted spin range (S up to ≈ 0.45). With a mean |spray| of 22°, the average loss is ≈ 30 ft. Backspin magnitude alone hardly matters (Nathan backspin without sidespin: still +28 ft).

Model-free check (full-season Savant downloads, scratch only, not committed), primary cut (fly balls/line drives, EV ≥ 90 mph, LA 20–35°), model − Statcast:

| | Tropicana 2016 (TrackMan; the fit's own training season) n 416 | Tropicana 2024 (Hawk-Eye) n 456 |
|---|---|---|
| backspin-only rule | +24.8 ft | +33.0 ft |
| Nathan average-Statcast spin | −4.0 ft | +2.6 ft |
| Statcast 2024 − 2016 at matched EV/LA (no model) | | −7.6 ft (−4 to −13 by EV bin) |

The old rule overcarried even the 2016 balls the coefficients were fitted on, so the bias was in the replay inputs, not in the era, the exit-speed convention or the coefficients. Nathan's analysis step fitted each ball's spin axis (backspin/sidespin split) to its trajectory; a backspin-only replay is not the model he validated.

**What this does and does not show.**
- Nathan's spin axis (and so the average sidespin) was inferred from trajectories with these same C_D/C_L fits, so agreement on 2016 is close to built in.
- The result shows that the aero model and the average spin model are mutually consistent and transfer to 2024 Hawk-Eye data. It does not independently validate the spin values.
- The calculator takes C_L from total spin with direction (ω × v)/ω; our simulator uses transverse spin. The two agree at launch, and we carry 1–5 ft further as sidespin turns into gyrospin during flight (largest at high spray).
- Feeding an average spin into a non-linear model is not the same as averaging per-ball outcomes, which leaves a small, unquantified bias.

## Results (fixture, primary set)

| model | set | n | mean | MAE | median | RMSE | mean by EV 90–100 / 100–110 | by LA 20–27.5 / 27.5–35 | by predicted-distance tercile |
|---|---|---|---|---|---|---|---|---|---|
| pre-registered: backspin-only rule | development | 27 | +28.6 | 28.6 | +24.9 | 32.1 | 28.2 / 29.1 | 28.2 / 29.1 | 28.1 / 31.9 / 25.7 |
| pre-registered: backspin-only rule | holdout | 28 | +34.4 | 34.5 | +34.9 | 37.4 | 37.4 / 32.1 | 35.6 / 33.5 | 38.2 / 29.2 / 36.3 |
| **adopted: average Statcast spin** | development | 27 | **+0.9** | 12.8 | +3.9 | **14.6** | 5.8 / −6.3 | −3.2 / 7.8 | −3.3 / 1.8 / 4.1 |
| **adopted: average Statcast spin** | holdout | 28 | **+2.1** | 10.3 | +2.8 | **13.5** | 2.3 / 1.9 | −4.5 / 7.1 | −5.5 / 1.0 / 10.9 |
| cross-check: Nathan calculator (C_D,0 0.3008, no decay) + same spin | development | 27 | −1.1 | 12.7 | +2.0 | 14.7 | 4.0 / −8.5 | −4.9 / 5.4 | −9.2 / 3.7 / 2.3 |
| cross-check: same | holdout | 28 | −0.1 | 10.2 | +1.4 | 13.3 | 0.4 / −0.5 | −6.3 / 4.5 | −7.4 / −1.4 / 8.5 |

(The pre-registered row moved from +28.8/+34.6 to +28.6/+34.4 because spray angle is now applied to every replay.) Pre-registered #1 (|mean| ≤ 10 ft, RMS ≤ 25 ft) **passes on development and holdout**; `PrimaryFlyBallsMatchStatcastDistance` is enabled. The ≈ 14 ft RMS is the expected scatter from per-ball spin that the average model cannot know (Nathan 2020: distance SD ≈ 17 ft at fixed EV/LA).

| other pre-registered check | result |
|---|---|
| 103 mph / 27° / 2000 rpm backspin only, 70 °F, sea level: 400 ± 20 ft | 426.5 ft, **fail — mis-specified**: the 400 ft figure averages real balls with sidespin; Nathan's own calculator gives 421.5 ft for a backspin-only 103 mph / 27.5° / 2500 rpm ball. Kept ignored, not redefined. |
| EV slope 4.9 ft/mph ± 30 %; +3.3 ft/10 °F; +5.9 ft/1000 ft; optimal LA 25–32° | pass |

Independent checks: `MatchesNathanTrajectoryCalculator` reproduces both spreadsheet sheets as shipped (421.5 ft; 400.5 ft with average spin) within 1.2 ft; an independent Python implementation matches the C# model to 0.1 ft.

**Protocol note.** This is a post-hoc deviation: the pre-registered backspin-only input fails (first two rows), and the holdout had already been seen failing under it (commit 7a45186). The replacement was chosen on the 2016 season (outside the fixture) and the development set. The scratch full-2024 check also contained the four holdout games (≈ 28 of 456 balls); the holdout fixture metrics above were computed only after the choice. No parameter was fitted to any of this data.

## Hypotheses

| | hypothesis | status | evidence |
|---|---|---|---|
| H1 | implementation / unit / geometry bug | ruled out | independent Python model; Nathan calculator reproduced within ~1 ft (test) |
| H2 | exit-speed convention | ruled out as cause | bias explained without any EV change; 2024 vs 2016 at matched EV/LA only −8 ft (bounds EV + ball + tracker together) |
| H3 | tracker era | weakened | same −8 ft bound; adopted model +2.6 ft on 2024, −4.0 ft on 2016 |
| H4 | drag model / seasonal drag | weakened | coefficients unchanged and pass; season C_D changes 0.01–0.03 ≈ 4–14 ft at most |
| H5 | lift / spin model — **validation spin input** | **supported (root cause)** | backspin-only input; Statcast-average spin with sidespin removes the bias on 2016 and 2024 |
| H6 | ContactResolver batted-ball spin | supported (separate issue; addressed in TASK-004.5) | at the time, the contact model gave backspin only, ≈ 160 rpm/° of LA (≈ 1.6–1.8× Statcast), changing distance by −26 … +17 ft vs average spin at 100 mph |
| H7 | environment | ruled out | ±10 °F → ±2.9 ft; −30 ft would need ρ ≈ 1.45 |
| H8 | `hit_distance_sc` semantics | ruled out as cause | home runs, caught outs and landed hits had the same bias |
| H9 | other | none found | |
| H10 | seam / ball construction | **RULED OUT** as the cause | see below |

## Seam / ball-construction investigation (H10)

- **Implicit vs explicit.** Nathan's coefficients were fitted to game-hit, seamed MLB balls in random orientation with nominal mass/area, so the average seam and construction effects are already inside C_D and C_L. An explicit seam model added on top would double-count them; only a refit could separate them. Model A (empirical average) is used; model B (explicit seam-aware) is not implemented.
- **Batted balls.**
  - Random seam orientation is a variance effect. Two-seam and four-seam lift converge at S ≥ 0.15 (Lyu et al. 2022, free flight, 35 m/s), and fly balls are at S ≈ 0.2. C_D did not depend on orientation in that study.
  - Construction effects are systematic:
    - Ball-to-ball σC_D ≈ 0.011 (Nathan & Young 2020).
    - MLB monthly C_D extremes 2018–2022 differ by 0.028 (Albert & Nathan 2022).
    - 2019 vs 2018 seam height differed by < 0.001 in (HR Committee 2019). Through Kensrud et al. 2015's C_D = 0.20·h[mm] + 0.15, that is ≈ 0.005.
    - At 4–5 ft per 0.01 C_D, plausible MLB construction variation moves carry by **≈ 1–14 ft**. The full MLB-vs-high-seam-NCAA range is ≈ 30 ft (Kensrud 2015), which is not a range MLB balls span.
  - No source quantifies SSW on fly-ball carry.
  - Tracking vs aerodynamics: the 2016→2024 model-free difference (−8 ft) bounds ball and tracker changes together.
- **Pitches (TASK-002).** Four-seam, sinker, changeup and cutter axis-vs-movement deviations mirror by hand with Nathan's SSW signs and sizes. Sliders and curveballs do not mirror. The breaking-ball 1.4–2.6× lift excess is far beyond published SSW lift changes. Classification: **possibly consistent with SSW** (seam orientation is not public; two games; confounded by gyro geometry). Pitch physics is not retuned.
- **Implementation gate:**
  1. Not materially responsible: the 30 ft is explained by sidespin.
  2. No peer-reviewed, validated SSW force model exists (UMBA is blog-level).
  3. Seam orientation is not public.
  4. Double-counting with the empirical fit.
  5. No independent validation data.

  All five conditions fail, so it is **deferred**.

## Remaining limitations

- In-game batted balls get their spin from `ContactResolver`. Since TASK-004.5 (`Docs/HITTING.md`) it produces a 3D spin vector — backspin or topspin from undercut/overcut, sidespin from the bat's vertical angle and timing yaw, plus the surviving part of the incoming pitch spin ⟂ the line of centres — from sourced collision parameters. Validation of that spin is conditional: public Statcast has no per-ball spin, so it compares averages over a deterministic contact grid with Nathan's average-Statcast spin model (itself a regression with an inferred back/side split) and checks that fly-ball carry stays within a few feet of the reference spin. That shows physical plausibility and protects against regressions; it does not prove individual-ball spin accuracy.
- The average spin model is an average; per-ball spin scatter leaves ≈ 14 ft RMS.
- Only Tropicana dome data; wind and outdoor air are covered only by the environment sensitivity tests.
