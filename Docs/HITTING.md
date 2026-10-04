# Hitting model (TASK-003)

Code: `Assets/Game/Gameplay/Hitting` (`Pitchlab.Gameplay`, engine-free, depends only on Simulation). Sandbox: `Assets/Scenes/HittingLab.unity` with `HittingLabController`. Labels as in `PHYSICS.md`: **[Fact]** sourced, **[Approx]** model approximation, **[Tune]** game parameter.

## Flow

pitch input → `HittingPitch` (full flight to 1 m behind the plate, plus the exact time/state at the contact plane) → player swing (start time, PCI) → `ContactResolver` → batted-ball initial `BallState` (contact position, exit velocity, spin). Everything is deterministic; no Unity collider takes part.

## Timing

- The swing reaches the contact plane `SwingDuration` (150 ms **[Tune]**; forward-swing durations of 130–280 ms are reported) after it starts. **Timing error** = bat arrival − ball arrival at the contact plane (Y = plate front + 0.25 m **[Tune]**); negative = early. This number is authoritative; labels (Good within ±7 ms, Early/Late within ±20 ms, Very early/late beyond) are display only. Collegiate hitters' sweet-spot window averages 9.4 ± 6.3 ms (Higuchi et al. 2025) **[Fact]**.
- The contact plane has one source: `HittingPitch.ContactPlaneY` (default `HittingPitch.DefaultContactPlaneY`), set when the pitch is prepared and read by `ContactResolver`.
- Contact uses the ball state at the bat's arrival time (cubic Hermite between 5 ms RK4 samples, sub-µm), so early contact happens out front and late contact deeper.
- The throw, the swing time and the PCI position at the swing all come from input-event timestamps on the same real-time clock as pitch playback. PCI motion is an exact function of timestamped aim events (`PciTrack`), so frame rate cannot change outcomes (regression test: `HittingInputFrameRateTests`, 30/60/144 fps and jittered frames). Frames only render.
- Beyond ±35 ms **[Tune]** the bat is not in the zone: miss.

## PCI (spatial error)

The PCI is a point in the contact plane (world X, Z). At contact the bat's sweet spot is at the PCI's X and, along the swing plane, at height z = PCI_z + (y_contact − Y_plane)·tan(attack angle) — so mistiming also changes the vertical contact point unless the swing plane matches the pitch's descent (a flat four-seamer is more timing-sensitive than a curveball). Offsets are physical distances (m), not screen space:

- **vertical** = ball centre − bat centre, measured in the level bat frame: line-of-centres angle sin φ = D / (r_ball + R_bat) (geometry of Kensrud, Nathan & Smith 2017 for a level bat) **[Approx]**; |D| ≥ r + R → miss over/under. With the tilted barrel (below) this is a gameplay abstraction: the PCI's vertical offset sets the line-of-centres angle, and its horizontal offset only sets q — on a real tilted bat a horizontal miss would also change the undercut.
- **along the barrel** = ball X − PCI X: collision efficiency q falls off quadratically, faster toward the tip than the handle; beyond 5 in **[Tune]** → miss off the barrel. The drawn PCI rectangle is that contact region.

## Contact (`ContactResolver`)

- Bat velocity: `BatSpeed` (72 mph; MLB average ≈ 71.5–72 mph, Statcast **[Fact]**) along the swing direction, tilted up by the attack angle (10°; MLB average ≈ 10° **[Fact]**) and yawed by `SprayRate`·timing error toward the pull side (1.2°/ms bat yaw **[Tune]** → ≈ 2–2.5° of spray per ms; Nathan's swing model rotates at ≈ 2.6°/ms **[Approx]**). Right-handed hitters pull toward −X (left field).
- **Normal** (line of centres): exit = q·incoming + (1+q)·bat, Nathan (2003, AJP 71:134) **[Fact]**. q = 0.21 at the sweet spot (Nathan's simulated wood bat peaks at ≈ 0.21; Statcast's squared-up ceiling implies ≈ 0.23) with q = 0.21 − k·d², k = 0.012/in² toward the tip, 0.003/in² toward the handle (read from Nathan 2003 Fig. 3) **[Approx/Tune]**.
- **Tangential**: contact-point slip reverses by e_x = 0.30 ± 0.02, measured at 85–120 mph against a clamped wood cylinder (Nathan et al. 2012) **[Fact]** (0.405 with a bat hitting a stationary ball, 77 mph, Kensrud, Nathan & Smith 2017), reduced by the bat's tangential recoil 1/(1 + r_x), r_x = (m·α/(1+α))(1/M + b²/I₀ + R²/I_z) ≈ 0.30 for Kensrud et al.'s wood bat (Table II, R = 1.16 in at their impact point; ≈ 73 % of it from the bat rolling about its long axis) **[Fact, derived]** — with this game's 1.305 in barrel radius it would be ≈ 0.35 (≈ 4 % less spin). One r_x serves every slip direction, although for slip along the barrel the roll term does not apply. (1 + e_x)/(1 + r_x) = 1, so the spin magnitude happens to equal the rolling, rigid-bat case. Impulse J = (2/7)(1 + e_x)/(1 + r_x)·slip for a sphere with I = 0.4·m·r², capped by Coulomb friction μ·(normal impulse), μ = 0.2 (≥ 0.15 measured; gross slip only beyond ≈ ±40° launch) **[Tune]**.
- **Vertical bat angle**: the barrel tip is 32° below the knob at contact (MLB average −32.2°, SwingGraphs via Cressey **[Fact, reported]**). The tilt turns the line-of-centres direction about the swing direction; PCI offsets stay in the level frame, so the hit/miss region and PCI difficulty are unchanged: undercut gives backspin plus slice and pushes fly balls ≈ 5–14° toward the opposite field; overcut gives topspin plus hook toward the pull side.
- **Spin**: ω = ω_in,⊥ + (5/2)·J/r about n̂ × t̂, a full 3D vector: backspin/topspin from undercut/overcut, sidespin from the bat tilt and from bat yaw (timing: pulled balls hook, opposite-field balls slice). The pitch's spin ⟂ the line of centres moves the contact point and enters the slip; with bat recoil 1 − (5/7)(1 + e_x)/(1 + r_x) = 2/7 of it survives (Nathan et al. 2012 Eq. 3 gives (0.4 − e_x)/1.4 for a clamped bat) — a fastball's backspin, now topspin relative to the batted ball, costs ≈ 600 rpm of backspin and ≈ 2–3° of launch for the same swing. Spin about the line of centres is dropped (torsional friction over the contact patch can remove ≈ 2800 rpm per collision) **[Approx]**.

### Resulting behaviour (four-seamer, average right-handed swing; `TestResults/contact_spin.md`)

| contact | EV mph | LA ° | spray ° | backspin | sidespin | carry ft (reference spin) |
|---|---|---|---|---|---|---|
| centred | 105.2 | 10.4 | +2.1 | 80 | +438 | 224 (248) |
| 0.5 in under | 103.7 | 22.2 | +10.5 | 1938 | +1804 | 389 (391) |
| 0.75 in under | 101.8 | 28.1 | +15.4 | 2774 | +2588 | 386 (391) |
| 0.75 in over | 102.1 | −7.9 | −9.7 | −2868 | −1477 | grounder |
| 8 ms early, 27° | 102.2 | 27.2 | −1.7 | 2770 | +1116 | 412 (418) |
| 8 ms late, 27° | 102.4 | 26.8 | +30.8 | 2366 | +3771 | 357 (365) |

Reference = Nathan's average-Statcast spin at the same EV/LA/spray, with which the flight model matches 2024 Statcast (TASK-004). Over a deterministic grid (5 pitch types × both hands × timing × PCI offsets) back+side spin is 0.84–0.98 × Nathan's average per 5° launch bucket from 10° to 40° (before: 1.11–1.30 ×), sidespin follows spray at ≈ 90–100 rpm/° with zero on the pull side for both hands, and carry differs from the reference by −1.6…+3.8 ft on average for 15–40° launches (before: −15…+11 ft, LA-dependent). Low line drives (10–15°) carry 8 ft short on average: a centred hit on a fastball keeps little backspin because the pitch's surviving backspin cancels it.

## Ratings

All hitter- and swing-specific values live in `SwingParameters` (bat speed, attack angle, swing duration, barrel contact width, sweet-spot q, timing window, spray rate, handedness). Ratings map onto these fields; the resolver has no hidden constants.

## Limitations

One swing type with one attack angle and bat tilt (real hitters vary both per swing); bat is straight-line during contact (no bat rotation inside the collision); no bat-speed variation along the barrel; spin about the line of centres dropped; no foul-tip/caught distinction; e_x measured at 85–120 mph, below game relative speeds (≈ 150 mph); r_x for one measured wood bat; q curve from a simulated generic wood bat. Individual-ball spin cannot be validated (public Statcast has no batted-ball spin); only averages against Nathan's Statcast-derived model, whose back/side split Trackman infers from the trajectory.
