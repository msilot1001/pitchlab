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

- **vertical** = ball centre − bat centre: line-of-centres angle sin φ = D / (r_ball + R_bat) (Kensrud, Nathan & Smith 2017) **[Fact]**; |D| ≥ r + R → miss over/under.
- **along the barrel** = ball X − PCI X: collision efficiency q falls off quadratically, faster toward the tip than the handle; beyond 5 in **[Tune]** → miss off the barrel. The drawn PCI rectangle is that contact region.

## Contact (`ContactResolver`)

- Bat velocity: `BatSpeed` (72 mph; MLB average ≈ 71.5–72 mph, Statcast **[Fact]**) along the swing direction, tilted up by the attack angle (10°; MLB average ≈ 10° **[Fact]**) and yawed by `SprayRate`·timing error toward the pull side (1.2°/ms bat yaw **[Tune]** → ≈ 2–2.5° of spray per ms; Nathan's swing model rotates at ≈ 2.6°/ms **[Approx]**). Right-handed hitters pull toward −X (left field).
- **Normal** (line of centres): exit = q·incoming + (1+q)·bat, Nathan (2003, AJP 71:134) **[Fact]**. q = 0.21 at the sweet spot (Nathan's simulated wood bat peaks at ≈ 0.21; Statcast's squared-up ceiling implies ≈ 0.23) with q = 0.21 − k·d², k = 0.012/in² toward the tip, 0.003/in² toward the handle (read from Nathan 2003 Fig. 3) **[Approx/Tune]**.
- **Tangential**: contact-point slip reverses by e_x = 0.40 (measured, Kensrud, Nathan & Smith 2017) **[Fact]**, reduced by the bat's tangential recoil 1/(1 + r_x), r_x ≈ 0.18 from rough bat properties **[Approx]**; impulse J = (2/7)(1 + e_x)/(1 + r_x)·slip for a sphere with I = 0.4·m·r², capped by Coulomb friction μ·(normal impulse), μ = 0.2 (≥ 0.15 measured lower bound; chosen so gross slip begins near 40° incidence) **[Tune]**.
- **Spin**: ω = (5/2)·J/r about n̂ × t̂ (undercut → backspin, overcut → topspin, mistiming → sidespin). Incoming pitch spin is ignored: with e_x ≈ 0.4 its coefficient nearly vanishes (Nathan et al. 2012) **[Approx]**.

### Resulting behaviour (four-seamer, average swing)

Centred and on time: ≈ 105 mph, 12–13° launch, ≈ 750 rpm (Statcast squared-up ceiling for this bat and pitch ≈ 108.6 mph). 0.5 in under: ≈ 25°, ≈ 3400 rpm; 1 in over: ≈ −13°, topspin. ±8 ms: pulled grounder / opposite-field fly. Undercut contact gives ≈ 130–160 rpm of backspin per degree of launch, somewhat below the ≈ 180 rpm/° Kensrud et al. measured with a bat hitting a stationary ball.

## Ratings

All hitter- and swing-specific values live in `SwingParameters` (bat speed, attack angle, swing duration, barrel contact width, sweet-spot q, timing window, spray rate, handedness). Ratings map onto these fields; the resolver has no hidden constants.

## Limitations

One swing type; bat is straight-line during contact (no bat rotation inside the collision); no bat-speed variation along the barrel; incoming spin ignored; no foul-tip/caught distinction; e_x measured at lower relative speeds than game contact; q curve from a simulated generic wood bat.
