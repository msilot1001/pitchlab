# Pitch-flight physics

The authoritative model lives in `Assets/Game/Simulation` (`Pitchlab.Simulation`, no Unity references). Labels: **[Fact]** sourced, **[Approx]** modeling approximation, **[Tune]** game parameter, **[Future]** research item.

## Frame and units

SI internally (m, s, kg, rad); baseball units only at the `PitchInput` boundary (`Core/Units.cs`, exact definitions: 1 mph = 0.44704 m/s, 1 ft = 0.3048 m).

World frame, right-handed, same axes as Statcast **[Fact]** (Nathan, [Magnus.pdf](https://baseball.physics.illinois.edu/Magnus.pdf)):

- Origin: rear point of home plate, ground level.
- **+Y** from the plate toward the pitcher; a pitch travels along −Y.
- **+Z** up; gravity is −Z.
- **+X** = Y × Z = the catcher's right = first-base side. From the catcher's view, a right-hander releases at negative X and their arm side is −X; glove side is +X. For a left-hander both flip.
- Front of the rubber: Y = 60.5 ft. Front edge of the plate: Y = 17 in (Statcast `plate_x`/`plate_z` plane). Plate half-width 8.5 in.
- Release Y = 60.5 ft − extension.

Unity presentation maps simulation (x, y, z) → Unity (x, z, y). Swapping Y/Z converts the right-handed frame to Unity's left-handed one, so +X stays first base and Unity +Y is up.

### Spin

Spin is the angular-velocity vector ω (rad/s), right-hand rule. For a pitch moving along −Y, pure backspin is ω ∥ −X (the top of the ball moves toward the pitcher), and the Magnus force ∝ ω × v points +Z.

`PitchInput` specifies spin as rate (rpm), **spin axis θ** and **gyro angle γ**:

- θ follows Statcast `spin_axis` **[Fact]** ([Savant CSV docs](https://baseballsavant.mlb.com/csv-docs)): 180° = pure backspin, 0° = pure topspin. In our frame θ = 90° pushes toward +X (first base), 270° toward −X. A right-hander's four-seamer is ≈ 200–215°.
- The spin vector is built in the **fixed** frame: ω̂ = cos γ·(cos θ·X + sin θ·Z) + sin γ·(−Y). Its projection onto the X–Z plane therefore has exactly Statcast's `spin_axis` angle θ (θ = 180° → −X → Magnus +Z for a pitch moving along −Y). **[Derived]**
- γ tilts the spin vector out of the X–Z plane toward the plate (+, along −Y, the direction of motion) or toward the pitcher (−). For a release exactly along −Y the spin efficiency is cos γ; with the usual 1–3° release angles the exact efficiency (reported by `PitchMetrics`) differs slightly, and ±γ are then not exactly symmetric. Presets use negative γ; Nathan notes gyro spin is generally negative for a right-hander, but the sign convention behind that statement is not verified.
- **Fixed spin axis during flight [Approx, justified]:** ω is held constant in world space. Nathan assumes constant spin rate and axis and notes no precession studies exist (TrajectoryAnalysis.pdf §II.D); a decay time of 20–30 s implies ≈ 2 % spin loss and, at most, ≈ 1° of precession per pitch, versus a 5–8° turn of the velocity. Consequently gyro spin partly becomes sidespin as the pitch drops, so the gyro sign changes movement slightly (≈ 1.4 in for a ±60° gyro slider, tested) — an effect described by Nathan ([gyroball note](https://baseball.physics.illinois.edu/gyro.pdf)), Kagan ([THT](https://tht.fangraphs.com/the-physics-of-the-gyro-pitch/), ≈ 0.5 in per 1500 rpm of gyro) and Barton Smith (baseballaero post 39).
- Pitcher-view clock: hour = (θ/30 + 6) mod 12, with 0 read as 12:00 (θ = 180° → 12:00, θ = 210° → 1:00); the catcher-view clock is mirrored.

## Force model

a = g + (F_D + F_M)/m with air-relative velocity v_r = v − wind.

- **Gravity** g = 9.80665 m/s² **[Fact]** (standard gravity, used by Nathan).
- **Drag** F_D = −½ρA·C_D·|v_r|·v_r **[Fact]** form (Nathan, [TrajectoryAnalysis.pdf](https://baseball.physics.illinois.edu/TrajectoryAnalysis.pdf) eq. 1). **C_D = 0.35 constant [Approx/Tune]**: free-flight measurements of MLB balls at pitch speed give 0.33–0.36 for S = 0.1–0.25, and pitch Reynolds numbers (1.5–2.2 × 10⁵) are at or beyond the drag crisis (Lyu et al. 2022, [pdf](https://baseball.physics.illinois.edu/LyuDragLiftSpin.pdf)). Nathan's Statcast fit C_D = 0.297 + 0.0292·(ω/1000 rpm) gives 0.33–0.37 over pitch spins; a drop-in alternative. Uncertainty ±0.03–0.05; ball lots differ.
- **Magnus** F_M = ½ρA·C_L(S)·|v_r|²·(ω × v_r)/|ω × v_r| **[Fact]** form. Only spin perpendicular to the airflow counts: ω⊥ = |ω × v_r|/|v_r|, spin parameter **S = r·ω⊥/|v_r|**, recomputed every step since v̂ rotates as the pitch drops **[Approx]** (Nathan, [THT on spin efficiency](https://tht.fangraphs.com/pitch-movement-spin-efficiency-and-all-that/)).
- **C_L = 1.120·S / (0.583 + 2.333·S) [Fact, fit]**: Nathan, *Analysis of Baseball Trajectories* (2017), Eqs. 10–11 — fitted to 2016 Statcast **fly balls** (exit speed ≥ 90 mph, launch angle 20–35°; S = Rω/v with total ≈ transverse spin), not to pitches. Nathan's pitch-based fit (motion-capture launches at 80–100 mph, S from transverse spin; [spinaxis.pdf](https://baseball.physics.illinois.edu/trackman/spinaxis.pdf) Eq. 8) is C_L = 0.336·[1 − e^(−6.041 S)], ≈ 10 % more lift at pitch S, with data scatter ≈ ±20 %. Versus the Sawicki et al. (2003) parametrization (1.5S below S = 0.1, 0.09 + 0.6S above): −8.5 % at S = 0.10, −4 % at 0.12, +1.6 % at 0.20, −3 % at 0.30. TASK-002 found no evidence favouring a switch (`Docs/VALIDATION_TASK002.md`). Uncertainty ±10–20 %.
- Coefficients are isolated in `BallFlight/AerodynamicModel.cs`.
- **Supported range [Approx]:** C_D = 0.35 is supported above the drag crisis, Re = ρ·v·2r/μ ≥ 1.5 × 10⁵ (≈ 69 mph at standard density; μ = 1.81 × 10⁻⁵ Pa·s), and the C_L fit up to S ≈ 0.4. `PitchMetrics` reports the flight's minimum Re and maximum S and `WithinSupportedAerodynamicRange`; PitchLab shows a warning outside it. Slow (< ~70 mph) or very high-spin pitches still simulate, but are extrapolations.

### Ball **[Fact]**

Official Baseball Rules: 5–5¼ oz, 9–9¼ in circumference. Nominal midpoints (Nathan's calculator defaults): m = 5.125 oz = 0.14529 kg; C = 9.125 in → r = 0.036888 m, A = 4.275 × 10⁻³ m².

### Environment

- Air density from temperature, station pressure and relative humidity (`EnvironmentState.MoistAirDensity`): dry air + water vapour ideal mixture, Buck saturation pressure **[Approx, standard physics]**. Gives 1.2250 kg/m³ for ISA dry air (tested).
- Default **[Tune]**: 21 °C, 101 325 Pa, 50 % RH, still air → ρ ≈ 1.194 kg/m³. Drag and Magnus both scale with ρ, so altitude matters (Coors Field ≈ −17 % ρ).
- Wind is a uniform vector; zero by default.

### Not modeled

- **Spin decay [Approx]:** ignored. Decay time ≳ 20–30 s (Nathan; Trackman) means ≈ 1–2 % spin loss and ≈ 1 % movement change over a pitch.
- **[Future]** Seam-shifted wake and seam-orientation-dependent C_L at low S (four-seam vs two-seam orientation differ up to 3× for S < 0.15, Lyu et al.), C_D in the drag crisis (< 70 mph), spin decay/precession, non-uniform wind.

## Integration and time step

Classic RK4 at a fixed **5 ms** step (`BallFlightSimulator.DefaultTimeStep`), simulated ahead in one call independent of frames. A one-off study (scratch script, not in the repo, same equations) compared against a 10 µs RK4 reference: RK4 at 5–20 ms stayed below 1 µm at the plate, while explicit and semi-implicit Euler at 5 ms were off by ~4 mm and ~0.3 ms of flight time and needed ≈ 1 ms for sub-mm accuracy. `SelectedTimeStepConvergesToFineReference` enforces the chosen step for every preset: plate position and movement within 1 µm, flight time within 1 µs, plate speed within 1e-5 m/s of a 0.1 ms run. RK4 at 5 ms is both more accurate and cheaper (80 steps × 4 evaluations per pitch). The step is kept below accuracy limits to leave margin for longer, faster batted-ball flights. Nathan also uses RK4 (dt 0.01 s by default).

Events (plate plane, ground) are located inside the last step by bisection on a partial RK4 step, so their time and position do not depend on where a step lands. Presentation plays back recorded states by simulation time with cubic Hermite interpolation; frame timing never feeds back into the result.

## Movement metrics

`PitchMetrics` measures at the front edge of the plate (Y = 17 in):

- **Plate X/Z**: where the ball centre crosses that plane.
- **Movement (HB, IVB)**: (actual crossing) − (crossing of a reference flight with the same release state, gravity and drag but C_L = 0), catcher's view: +HB toward first base, +IVB up. It is the Magnus displacement accumulated over the **full flight from release**.
- Statcast `pfx_x/pfx_z` (feet) are best matched by the drag-corrected spin-induced deviation over **release → plate** (TASK-002: median 0.2 in x, 1.1–1.3 in z), not the PITCHf/x 40 ft window — the same concept as this metric. Our earlier "curveball overshoot" was therefore not a definition mismatch but the breaking-ball lift limitation above combined with the preset's active-spin-like efficiency.
- Flight time is release → plate front. If the ball reaches the ground first, plate values, flight time and movement are NaN (and the UI says so). Movement is also NaN if the no-lift reference never reaches the plate.

## Validation in tests

- Vacuum flight equals the analytic parabola (1e-9 m).
- Drag-only 1D flight equals the analytic quadratic-drag solution.
- Magnus deflection after 55 ft vs Nathan (2008) Table I (90/75 mph, 1000/1800 rpm → 14/19, 16/21 in; Adair C_D, Sawicki C_L): within 15 %.
- Uniform wind equals a Galilean frame shift of a still-air flight (model-independent, 1e-9 m).
- C_L pinned to hand-evaluated values of the Nathan fit; ball constants pinned to the documented values.
- Presets: **regression bands** (±1.5 in around current model output, not external validation).
- External validation against 649 tracked Statcast pitches (`Docs/VALIDATION_TASK002.md`): drag confirmed (implied C_D 0.34–0.35 in a closed-roof game), plate location reproduced to ≈ 0.3–0.6 in when the observed Magnus direction is used, lift magnitude consistent with measured active spin for four-seamers/sinkers/changeups; **known limitation**: breaking balls (slider, cutter, curveball) get ≈ 1.6–2.6× too much transverse-spin effect for their measured active spin, and Statcast's measured spin axis differs from the movement direction by family-dependent angles (seam-shifted wake, not modelled).

## Presets **[Tune]**

`PitchPresets` holds approximate MLB-average right-handed values (speed, spin, axis, efficiency; release ≈ 5.7–6.0 ft high, 6.0–6.4 ft extension, −1.8 to −1.9 ft side). Release angles are tuned to cross mid-zone. Model results (IVB / HB in) vs published averages: four-seam 17.0 / 8.6 arm side (≈ 16 / 7–8); sinker 8.7 / 16.1 (7–9 / 15); slider +0.2 / 9.2 glove (+1–2 / 5–6); curveball −14.6 / 10.8 (≈ −10 / 8–10); changeup 6.4 / 15.0 (6 / 14). The curveball overshoot is a known calibration question (C_L at high S, efficiency, measurement window).
