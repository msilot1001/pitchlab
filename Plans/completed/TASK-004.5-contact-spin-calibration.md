# TASK-004.5 — ContactResolver batted-ball spin calibration

## Goal
`ContactResolver` outputs a physically plausible 3D batted-ball spin (magnitude, backspin/sidespin split, axis, L/R mirror) so gameplay-generated flights carry like the TASK-004-validated reference. No compensation in `BallFlightSimulator`, no aero retune, no PCI/timing retune.

## Owner
Claude Code (sole writer). Read-only: physics-researcher, physics-reviewer, test-reviewer. Branch `feat/task-0045-contact-spin` from 69778d8.

## Audit of the current resolver (before changes)
Flow: pitch `BallState` at contact time (Hermite) + swing (start time, PCI) → timing error → bat yaw (SprayRate·timing, pull side by hand) → swing direction (attack angle) → **horizontal** barrel axis (no vertical bat angle) → bat-frame offsets (along barrel, vertical) → line of centres n̂ (sin φ = D/(r+R)) → normal: v_n' = q·v_in + (1+q)·V_n (Nathan 2003) → tangential: centre-relative slip (incoming ball spin ignored), J = (2/7)(1+e_x)/(1+r_x)·slip capped by μ·J_n → v' = v_n'·n̂ + V_t + slip − J·t̂, ω = (5/2)·J/r · (n̂ × t̂).

Dimensional check: J is impulse per ball mass (m/s); (5/2)J/r is rad/s for I = 0.4·m·r² ✓; 7/2 = 1 + m r²/I ✓; rpm only at display/tests ✓. Tangential impulse applied once to velocity and once to spin ✓. Spin is already a 3D vector (n̂ × t̂), not a scalar backspin.

Python port (scratch `contact.py`, matches C#: centred 105.3 mph / 12.7° / 758 rpm) findings, four-seam, default swing:
- Friction cap never binds for 10–40° launch (μ 0.2 vs 0.5 identical) → magnitude set by (1+e_x)/(1+r_x).
- Backspin ≈ 195–200 rpm per degree of launch at fixed swing; at fixed LA: 25° → 3257 rpm (Nathan average total 2393, ×1.36), 35° → 5220 (×1.47).
- Sidespin vs spray from bat yaw ≈ 100 rpm/° (Nathan average 94 rpm/°) ✓, but no handedness offset (Nathan: 849 rpm slice at centre field).
- Vertical bat angle (barrel tip below handle) of 30° reproduces Nathan's sidespin offset and slope across spray for both hands from geometry alone; also shifts undercut balls toward the opposite field and topped balls toward the pull side.
- Incoming spin in a rigid point-contact model: gyro spin about n̂ survives untouched (e.g. curveball +2300 rpm) — implausible given torsional contact friction (estimated capacity ≈ 2800 rpm per collision); tangential components keep ≈ 15 % (1 − 2.5·k).

## Hypotheses (magnitude)
- M1 r_x underestimated — supported: Kensrud et al. 2017 Table II wood bat → r_x 0.295 (barrel roll 73 %); was 0.18 "rough"
- M2 e_x — supported: 0.30 ± 0.02 measured at 85–120 mph (Nathan et al. 2012); 0.405 was a stationary-ball value
- M3 population vs fixed-swing comparison — real but secondary; validation compares conditional means per LA bucket
- M4 incoming pitch spin — included: with recoil 2/7 of the transverse part survives (not ≈ 7 % as first written; physics review); per pitch ≈ −600 rpm backspin for fastballs, population means unchanged
- M5 friction cap — ruled out (binds only beyond ≈ ±40° launch)
- M6 implementation bug — none in the collision; found stale serialized swing parameters in HittingLab.unity (old e_x/r_x, no tilt) → fixed, PlayMode guard added
- Sidespin: bat yaw already gave ≈ 100 rpm/° of spray; missing handedness offset comes from the vertical bat angle (MLB mean −32.2°, reported)

## Milestones
1. [x] Audit + Python port + parameter sensitivity
2. [x] Research (agent): e_x, r_x, μ, incoming spin, sidespin origin / vertical bat angle, Statcast spin–LA
3. [x] Model change (sourced parameters only), C# + tests
4. [x] Deterministic grid / population analysis vs Nathan average spin
5. [x] Flight coupling: carry/hang vs reference spin
6. [x] Reviews, check.sh, Unity verification, docs, commit

## Decisions
- e_x 0.30, r_x 0.30, VerticalBatAngle 32° (sourced; no fitted scale). μ 0.2 unchanged.
- PCI offsets stay in the level bat frame; only the collision normal is tilted (keeps PCI difficulty; documented as a gameplay abstraction).
- Incoming spin ⟂ line of centres enters the slip; the component along the line of centres is dropped [Approx].
- Validation thresholds (ratio 0.8–1.25 per bucket, sidespin slope 65–125 rpm/°, zero crossing on the pull side within 20°, carry mean ±10 ft and 10/90 % within ±15 ft) were set AFTER the Python prototype results — regression guards, not pre-registered. Observed (C#): ratio 0.84–0.98; carry mean −1.6…+3.8 ft for 15–40°, −8.3 ft at 10–15°; see TestResults/contact_spin.md.
- The sidespin agreement is not independent evidence for the tilt: ≈ 30° was seen to match before the 32° literature value was adopted.

## Verification
- check.sh: EditMode 133/139 passed (6 ignored/explicit), PlayMode 10/10.
- Unity Play Mode (Editor restarted after a stuck Pipeline bridge; old PID terminated gracefully with the user's OK): PitchLab presets + zero spin unchanged; HittingLab centred 105.2 mph/10°/80 back/+438 side/224 ft, 0.5 in under 22°/1938/+1804/389 ft, overcut −8°/topspin+hook, early pull 25°/−3°, late oppo 24°/+28°/+3395 side, misses (timing, under); BattedBallLab presets unchanged (386/451 ft). Console: only the Pipeline's own "request aborted" entries from Play Mode transitions.
- Reviews: physics-reviewer (incoming-spin fraction wrong → fixed and included; PCI geometry [Fact] → relabelled; r_x radius note), test-reviewer (FromState round trip, tilted hand derivation, contact counts, full-vector mirror, extreme-parameter sweep, off-centre spin, transverse comparison, post-hoc thresholds) — applied.

## Remaining risks
- Individual-ball spin is not validatable with public data; reference is a regression with an inferred back/side split.
- One swing type (attack angle, tilt fixed); r_x for one measured bat; e_x measured below game relative speeds.
- Low line drives (10–15°) carry ≈ 8 ft short of the reference on average.
- Undercut fly balls go ≈ 5–15° toward the opposite field for on-time swings (physical consequence of bat tilt; changes where on-time fly balls land).
