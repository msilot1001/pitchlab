# CPU batter

The CPU hitter (TASK-019) produces the same inputs a human does: PCI placements over time and a swing press. The swing then
goes through `ContactResolver` (Docs/HITTING.md); nothing chooses a result. Code: `CpuBatter`, `BatterPlan`
(Assets/Game/Gameplay/Hitting); `SwingParameters.For` (bat speed).

Labels: MEASURED / DERIVED / ASSUMED / TUNED (see Docs/PLAYER_RATINGS.md).

## No future information
He sees the ball through a function of time that he only calls for instants he has already seen
(`CpuBatter.Plan(ballAt, …)`). He never reads the completed flight's crossing, the call, `IdealContactTime` or a contact
result. A test replaces the ball's future after his last look with a different one and requires identical events
(`HeNeverUsesTheFlightBeyondWhatHeHasSeen`).

## Perception
- He looks every 10 ms (a deterministic tick, not a frame). Across the line of sight each look has angular noise
  σθ = 0.0005 rad · (1 − 0.3 r̂(Vision)) per axis, times the distance from his eye (0, plate front, 1.5 m).
- Depth is judged from looming (the ball's angular size is 2r/d), so it is far less precise:
  σ_depth = 0.1 · σθ · d² / (2 r). That is ≈ 22 cm at 18 m and ≈ 3 cm at 7 m; the full looming bound would be ≈ 10 % of
  the distance.
- Both scales are TUNED. An average hitter predicts the crossing within ≈ 6.5 cm RMS and the arrival within ≈ 3 ms RMS at
  his decision.
- He estimates the path p(t) = a + b t + c t² by weighted least squares. A prior on the acceleration holds gravity plus a
  typical drag (0, +10, −9.81) m/s², ± 3 m/s² per axis. That is what he expects before he sees the pitch move. Observed
  movement overrides it as he watches, and a breaking pitch fools him partly. The drag is DERIVED from the flight model at
  90 mph; the prior width is TUNED.
- Delay: he acts on a look 60 ∓ 15 ms later, by Vision (ASSUMED). He commits 10 ms before his press. The press already
  includes his pre-drawn timing error, so an early press is never cut short.
  His last usable look is therefore ≈ 220 ms before contact. MLB hitters commit ≈ 175 ms before contact (≈ 24 ft, DERIVED);
  the extra is processing (ASSUMED).

## Decision (take / swing)
At the commit time he predicts the crossing at the front of the plate and his contact time at the contact plane.
- Strike belief: a logistic of his signed distance from the zone edge, with the zone widened by the ball as in the call.
  Its width is σ = 3 cm · (1 − 0.3 r̂(Vision)) (TUNED).
- Swing chance = belief · Z-Swing(count) + (1 − belief) · O-Swing(count) · (1 − 0.45 r̂(Discipline)) · 3 · e^(−d/12 cm),
  capped at 1. Here d is how far outside he judges the pitch to be. The chase falloff is TUNED: it brings the average chase
  rate near the count's rate. Discipline's ±45 % is ASSUMED (MLB O-Swing ≈ 18–40 %).
- The draw uses a seeded deviate: `SeedStream(game seed, batter, plate appearance, pitch)`.

MLB 2024 by count (Savant zones 1–9 vs 11–14; MEASURED from the Statcast search CSV):

| Count | Z-Swing | Z-Contact | O-Swing | O-Contact |
|---|---|---|---|---|
| 0-0 | 45.9 | 80.4 | 16.0 | 50.4 |
| 1-0 | 60.3 | 80.6 | 22.2 | 50.8 |
| 2-0 | 57.9 | 82.9 | 21.0 | 56.7 |
| 3-0 | 13.5 | 86.9 | 3.5 | 68.2 |
| 0-1 | 74.6 | 81.4 | 28.2 | 51.6 |
| 1-1 | 77.9 | 81.8 | 31.2 | 54.3 |
| 2-1 | 78.2 | 83.5 | 32.1 | 56.4 |
| 3-1 | 72.5 | 84.5 | 27.1 | 61.7 |
| 0-2 | 86.6 | 83.8 | 33.1 | 57.4 |
| 1-2 | 88.6 | 83.9 | 38.5 | 58.7 |
| 2-2 | 89.5 | 85.0 | 42.2 | 61.0 |
| 3-2 | 88.9 | 86.0 | 44.8 | 66.0 |
| All | 67.8 | 82.6 | 28.5 | 56.2 |

The swing rates are used directly. The contact rates are only a comparison: contact comes out of the physics.

## Swing (aim and timing)
- PCI: at every look after his first fit he places the PCI at his predicted contact-plane point plus his aim error, clamped
  to the PCI area. The aim error is fixed per swing: σ = 3.5 cm · (1 − 0.3 r̂(Contact)) per axis (TUNED). The swing reads
  the PCI at the press, like a human's.
- Press: his predicted contact time − swing duration + timing error, where σ = 10 ms · (1 − 0.3 r̂(Contact)) (TUNED; the
  sweet-spot window is ≈ 9 ms, MEASURED, Higuchi et al. 2025). With his arrival misjudgement, an average hitter is
  ≈ 10 ms RMS off the ball.
- Power: bat speed = 72 + 5 r̂(Power) mph, i.e. 67–77 mph. MLB average is 72; 2025 qualified hitters run P10 66.5 /
  P90 75.8 (DERIVED). This is the physical input of the collision (exit ≈ q·v_pitch + (1+q)·v_bat). It applies to human-
  and CPU-batted game pitches alike: the GameLab swings `SwingParameters.For(batter)`, as the simulator does. No exit
  velocity is scaled after contact.
- The swing duration stays 150 ms at every bat speed. That is defensible to first order, since faster swings are also
  longer (Statcast swing length), but it has trade-offs:
  - Power costs nothing in whiffs here; only Contact does.
  - The bat's yaw rate per ms of timing error (`SprayRate`) does not scale with bat speed.
  - The timing window does not change with bat speed.

## Where it runs
- `GameSimulator`: every simulated plate appearance uses it, replacing the TASK-016 stand-in.
- GameLab: B toggles the CPU batter. His events are applied at their own timestamps to the same `PciTrack` and swing path;
  the player's aim and swing are ignored while he bats. Results are frame-rate independent (PlayMode test).

## Population (regression check, not validation)
Fixed set: 600 pitches from the away starter (power repertoire), aimed by the automatic pitcher, executed, all 12 counts
equally. One average-height right-handed batter per archetype:

| Archetype (Con/Pow/Vis/Dis) | Z-Swing | Chase | Z-Contact | O-Contact | EV mph | squared-up EV |
|---|---|---|---|---|---|---|
| contact 78/35/65/60 | 68 % | 28 % | 80 % | 80 % | 90.0 | 102.4 |
| power 42/85/45/40 | 70 % | 27 % | 69 % | 65 % | 94.1 | 108.6 |
| balanced 55/55/55/55 | 66 % | 25 % | 75 % | 69 % | 91.2 | 104.7 |
| patient 55/50/72/85 | 64 % | 19 % | 82 % | 77 % | 91.9 | 104.6 |
| free swinger 45/65/35/18 | 75 % | 29 % | 63 % | 64 % | 91.0 | 106.3 |
| pitcher 15/10/15/20 | 67 % | 34 % | 49 % | 53 % | 86.7 | 100.6 |

Squared up means within 1 cm of the sweet spot's height and 1 in along the barrel (8–13 balls per archetype). The tests
assert the orderings, and a deterministic squared-up swing gives ≥ 4 mph more exit speed per 40 Power points.

## Known discrepancies
- O-Contact is too high: ≈ 55–80 % against MLB 56 %. His chases are mostly near the edge, where contact is as easy as in the
  zone. Real chases are often breaking balls that leave the zone late, and his prior only partly captures that.
- Simulated games (TASK-016 simulator, basic automatic pitcher) give too much offence: ≈ 18 runs per game, K ≈ 16 %,
  BB ≈ 18 % (MLB ≈ 9, 22 %, 8.5 %; 20 games, seeds 101–120). The pitcher throws too few strikes (TASK-020), and contact is still too easy.
- One swing (no check swings, no two-strike shortening, no pitch-type guessing, no scouting).
