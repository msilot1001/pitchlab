# Ball–surface interaction (TASK-004.7)

This is the source of truth for bounces, sliding, rolling, wall impacts and the field-surface layout. Airborne flight is unchanged: see [PHYSICS.md](PHYSICS.md). The Unity Rigidbody is never authoritative.

Evidence labels:
- **MEASURED**: from a primary source that was read.
- **REPORTED**: secondary source.
- **DERIVED**: our arithmetic on sourced data.
- **ASSUMED**: no source; a calibration knob.

## Model

### Frame
Simulation frame (Docs/PHYSICS.md): +X toward first base, +Y toward centre field, +Z up. The ground is Z = 0. The ball is a sphere: radius R = 36.5 mm, mass m = 0.145 kg, I = α m R² with α = 0.40 (Cross 2002 Table I, MEASURED).

### Impact
Cross, AJP 70:1093 (2002) and AJP 73:914 (2005). The impact is instantaneous. n is the unit surface normal pointing out of the surface.
- Contact point r_c = −R n.
- Contact-point velocity v_c = v + ω × r_c.
- Normal speed v_n = v · n. An impact happens only when v_n < 0.
- Tangential slip u = v_c − (v_c · n) n.
- Normal impulse J_n = −m (1 + e_n) v_n, along n.
- Grip impulse J_t = −m_t (1 + e_t) u, with m_t = m α / (1 + α) (DERIVED from Δu = J_t (1/m + R²/I)).
  - If |J_t| > μ J_n, the ball slides throughout and J_t = −μ J_n û (Coulomb).
- Plow impulse J_p = −κ(v) J_n v̂_t at the centre of mass, with no torque, applied **before** friction. κ(v) = max(0, κ + slope·(v − 40.2 m/s)), with v clamped to the measured 31–40.2 m/s, so it only interpolates and never extrapolates. It is capped at the tangential momentum m|v_t|, so the plow never reverses the ball.
- Friction (grip or Coulomb) then acts on the slip that remains. The ball therefore leaves gripping the surface (rolling, for e_t = 0) rather than over-spinning. In an earlier order, plow after friction, the ball left grass bounces with about 6000 rpm of topspin that the next contact returned as speed; the physics review caught this.
- Grip friction can still reverse v_t for a strongly back-spinning ball. That is physical: backspin bounces back.
- Updates:
  - Δv = (J_n n + J_t + J_p) / m;
  - Δω = (r_c × J_t) / I.
- These are vector formulas: incoming topspin and backspin, sidespin, and any surface orientation (ground or wall) need no special cases.

The plow term is not in Cross. It is DERIVED: a rigid Coulomb bounce cannot reproduce the measured speed ratio on natural grass. At 25°, 40 m/s the measured ratio R = 0.43 would need e_t ≈ 0.9, which implies unrealistic spin. A centre-of-mass tangential loss proportional to the normal impulse (turf plowing and blade drag) fits both incidence angles.

Its speed slope comes from the measured speed dependence, which goes in opposite directions on the two surfaces (Pennbounce):
- Grass loses relatively more at higher speed, because the ball penetrates deeper.
- Dirt loses relatively less at higher speed.

### Phases
The trajectory is a chain of event-separated segments: airborne → impact → (bounce → airborne …) → slide → roll → rest. A wall impact can occur in any moving phase.
- **Airborne.** The existing BallFlightSimulator: RK4, 5 ms, drag + Magnus + spin decay, with exact event location by bisection. The first airborne segment is bit-identical to `BattedBallSimulation.Run`, so carry is unchanged.
- **Bounce or ground contact.** After a ground impact, if the rebound normal speed exceeds `v_hop` = 0.31 m/s (a 5 mm hop) the ball flies again. Otherwise it stays on the ground.
- **Slide.** The ball is on the ground, with contact slip u ≠ 0.
  - Kinetic friction F = −μ m g û.
  - Contact slip decelerates at μ g (1 + 1/α) = 3.5 μ g. Only spin changes it, through r_c × F.
  - The speed-proportional turf resistance b·v (canopy drag, see Roll) also acts while sliding, torque-free at the centre of mass. Its constant part a₀ is replaced by the Coulomb friction. Without it, a skidding ball (μg ≈ 3.9 m/s² on grass) would lose less than a rolling one at high speed (a₀ + b·v ≈ 6.6 m/s² at 28 m/s), and a backspinning grounder would outrun a topspinning one.
  - Air drag acts on v, using the spin-free C_D. The spin-dependent fit covers fly-ball spins up to about 3000 rpm, not a rolling ball's ω = v/R. Lift is ignored on the ground.
  - Fixed 1 ms steps.
- **Slide → roll.** When the slip would reverse within a step, the ball is snapped to rolling, ω = (ẑ × v) / R (pure rolling). This event is recorded.
- **Roll.** Rolling resistance decelerates the ball at a(v) = a₀ + b·v. Air drag adds F_d/((1 + α) m), because under the rolling constraint the centre-of-mass force also has to spin the ball down. ω stays at rolling. Vertical spin ω_z is kept unchanged; there is no pivot friction (a simplification that matters only for wall contacts).
  - The form, deceleration rising linearly with speed, is MEASURED for balls rolling on turf (Kolitzus, ISSS, football on turf at 1.0–2.6 m/s: a = 0.40 + 0.17 v).
  - Cross (Phys. Educ. 50:717, 2015) REPORTS that rolling friction rises with speed.
  - A constant law lets a ball that settles into rolling at 20 m/s run more than 110 m.
- **Rest.** Deterministic stop condition: once the speed is ≤ a(v) · dt, the ball stops at the exact solution of dv/dt = −(a₀ + b v), applied from that speed: s = v/b − (a₀/b²)·ln(1 + b v/a₀), t = ln(1 + b v/a₀)/b. v and ω become 0, and the rest event is recorded.
- **Surface.** Each step takes its surface from the position, so a ball rolling from dirt onto grass changes coefficients there.
- **Wall.**
  - The outfield fence is a vertical polyline with height H.
  - The ball impacts the wall when its centre comes within R of a fence segment while moving toward it and below H + R.
    - The impulse is the same formula with n horizontal, pointing toward home.
    - A ball rolling into the fence bounces back along the ground.
  - The fence stands between the foul poles only, so foul territory has no wall. The fence and poles also cover the line band: a ball with any part over the line (centre within R outside it) meets them, because the line and poles are fair.
  - Above H + R the ball clears the fence: a home run, out of play. The trajectory ends at its first ground contact beyond the fence, with no bounce.
- **Limits.**
  - At most 40 impacts (a guard; real plays use < 15).
  - At most 30 s of play time.

### Distances
- `CarryDistance`: horizontal distance from the plate origin to the first ground contact. For a flight that lands in the park without touching the fence, this is bit-identical to the existing `BattedBallMetrics.Distance`.
  - If the ball reached the fence in the air (`ReachedFenceInTheAir`: a wall impact or a home run), the displayed distance is the projected airborne-only landing, as Statcast reports for such balls.
- `FinalDistance`: the distance to where the ball comes to rest, or to where it leaves play.

## Coefficients

`BallSurfaceProperties`, one set per surface kind.

### Penn State "Pennbounce" data
Brosnan, McNitt & Schlossberg, JTE 35 (2007), Table 5, MEASURED. R = |v_out| / |v_in|, three plots each, air cannon, no reported spin:

| Surface | 25° at 40.2 m/s | 35° at 40.2 m/s | 25° at 31 m/s | 35° at 31 m/s |
|---|---|---|---|---|
| Skinned infield | 0.60 / 0.63 / 0.60 | 0.54 / 0.51 / 0.51 | 0.53 / 0.57 / 0.55 | 0.42 / 0.48 / 0.51 |
| Natural turfgrass | 0.41 / 0.45 / 0.42 | 0.28 / 0.27 / 0.29 | 0.43 / 0.46 / 0.45 | 0.35 / 0.37 / 0.36 |

Further measurements:
- **MLB/MiLB/NCAA basepaths** (25°, 40.2 m/s): R = 0.514–0.584, mean 0.562 (Brosnan & McNitt 2008, MEASURED).
- **Kentucky bluegrass:** R = 0.445 ± 0.061 (Brosnan, McNitt & Serensits 2011, MEASURED).
- **Ball on a rigid wall** (head-on, 26.8 m/s, ash): COR 0.546 ± 0.032 (ASTM F1887 via Kagan & Atkinson 2004, REPORTED).
- **Real balls grip-slip:** baseball e_t ≈ 0.21 (Cross 2002, MEASURED, at low speed).

### Values

| Surface | e_n | μ | e_t | κ (40.2 m/s) | κ slope (per m/s, within 31–40.2) | Rolling a₀ + b·v | Basis |
|---|---|---|---|---|---|---|---|
| InfieldDirt | 0.40 | 0.50 | 0.25 | 0.070 | −0.014 | 0.50 + 0.10 v | e_n ASSUMED (≈ Cross's 0.39 for a baseball on a hard surface). κ and slope are a least-squares fit to the four Pennbounce skinned-infield plot means, RMS 0.017 (DERIVED). The slope follows the MEASURED rise of R with speed on dirt (pooled 0.511 → 0.564). μ and e_t follow Cross. Rolling ASSUMED. |
| NaturalGrass | 0.30 | 0.40 | 0.00 | 0.655 | +0.014 | 1.00 + 0.20 v | e_n ASSUMED (softer than dirt). κ and slope are fit to the four Pennbounce turfgrass means, RMS 0.013 (DERIVED); R falls with speed on grass. Rolling ASSUMED: the top of the range scaled from football-on-turf data (a₀ 0.5–1.0 m/s², b 0.1–0.2 s⁻¹), because a baseball sits deeper in the blades than a football. |
| WarningTrack | 0.40 | 0.55 | 0.25 | 0.10 | −0.014 | 0.80 + 0.12 v | No baseball data. ASSUMED: the skinned-infield bounce, slightly looser (Brosnan 2008: looser and wetter surfaces are slower). |
| Wall (padded) | 0.30 | 0.50 | 0.10 | 0.00 | – | – | No data. ASSUMED: padding absorbs more than the 0.55 rigid-wall ball COR. |

**e_n is not identified by Pennbounce.** Speed ratios trade e_n against κ, and rebound angles were not reported. e_n is therefore an assumption, and it sets the visible hop height and rebound angle. Bounce-angle or bounce-height data would pin it down. The fitted impacts have v_n of 13–23 m/s; low-v_n hops are unvalidated.

Regression (`SurfaceImpactTests`): the simulated R for the four Pennbounce conditions on each ground surface, with no incoming spin, stays within ±0.04 of the plot means. That is the plot-to-plot spread of the measurement.

**Ground-ball sanity check** (DERIVED from the model, not validated). Measured baseball roll-out data does not exist publicly (Statcast tracks the ball after the bounce but publishes no aggregates).
- 90 mph at −8°: reaches 110 ft (infielder depth) in 1.39 s at 17.1 m/s, and rests at about 254 ft.
- 102 mph at −9°: reaches 110 ft in 1.23 s, and rests at about 280 ft.
- A 60 mph chopper reaches 110 ft in 2.6 s, and rests at about 168 ft.
- A 100 mph liner at 2° that skips to the wall rests at the track.
- Tango (Statcast, REPORTED): no infielder fields a ball beyond about 213–222 ft.

The rolling coefficients are the weakest part of the model. Calibrate them against tracking video before fielding depends on exact roll-out.

These are calibration knobs: when a source for rolling or for walls appears, change the numbers here and in `BallSurfaceProperties` together.

Sources:
- Cross, "Grip-slip behavior of a bouncing ball", AJP 70:1093 (2002); "Bounce of a spinning ball near normal incidence", AJP 73:914 (2005).
- Brosnan, McNitt & Schlossberg, "Development of a ball-bounce apparatus…" (Pennbounce), J. Testing & Evaluation 35 (2007).
- Brosnan & McNitt, Applied Turfgrass Science (2008), doi:10.1094/ATS-2008-0520-01-RS.
- Brosnan, McNitt & Serensits, JTE 39(3) (2011).
- Kagan & Atkinson, "The coefficient of restitution of baseballs as a function of relative humidity", Phys. Teach. 42:89 (2004).
- Penner, "The physics of putting" and "The run of a golf ball", Can. J. Phys. 80 (2002).
- Kolitzus, "Ball roll behavior" (ISSS); Gabrielsen/NBI, ball roll on football fields (ISSS 2004); the FIFA Quality Programme for natural playing surfaces (2021), ball-roll test.
- Tango, "When is an infielder an outfielder" (Statcast-based).
- Goodall et al., Intl. Turfgrass Soc. Res. J. 10:1085 (2005): a dynamic friction index of 0.27–0.69. That is an index, not μ, so it is used only as a direction.

## Field layout (`FieldLayout`)

Simulation geometry. The presentation draws the same shapes.
- **Infield dirt:**
  - A square rotated 45° on the diamond, of side 90 ft + 4 m, centred on second base's midpoint;
  - minus an inner grass square of side 90 ft − 3 m;
  - plus a 13 ft circle around home and the 9 ft mound circle.
  These match `FieldDressing` exactly.
- **Natural grass:** everywhere else inside the warning track, including foul territory.
- **Fence:** a polyline through 330 ft (foul lines, ±45°), 375 ft (±22.5°) and 400 ft (centre). It is generic, not a real park. Height 8 ft (2.44 m).
- **Warning track:** the 15 ft band inside the fence, between the foul lines.
- **Known simplifications:**
  - The mound is flat for ground physics. It is 10 in visually, so ground balls roll through the hump.
  - There is no backstop and no foul-territory wall: foul balls roll to rest.
  - The fence top is treated as a sharp edge at H + R.
