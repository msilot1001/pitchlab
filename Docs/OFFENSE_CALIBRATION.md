# Offense calibration (TASK-023)

Simulated games should score like MLB. The calibration fixes causes; it never scales results. Three causes were found and
corrected:
1. The contact model's bat was incomplete (Docs/HITTING.md, "The whole bat"). This changes the collision calculation
   beyond the old barrel: contact span, taper, the bat's local speed and a negative q floor off the end. The sweet-spot
   parameters (q₀, the fall-off rates, e_x, r_x, μ) and all aerodynamics are unchanged.
2. The CPU hitter's contact quality and timing were too good and too centred (Docs/BATTER_AI.md).
3. The generic park was too easy to hit out: the straight-chord fence and its dimensions (`FieldLayout.Standard`).

Harness: `OffenseCalibrationTests.Report(n)` (EditMode assembly). Guards: `SimulatedOffenseStaysNearMlb`.

Labels: MEASURED / DERIVED / ASSUMED / TUNED (Docs/PLAYER_RATINGS.md).

## Reference (MLB 2024, league — MEASURED unless marked)
- **League rates** (FanGraphs leaders API, Baseball Savant /league):
  - runs 4.39 per team-game (DERIVED);
  - K 22.6 %, BB 8.2 %, HR 2.99 % of PA, 3.89 pitches per PA;
  - contact 76.8 % per swing (zone 85.8 %, chase 62.4 %);
  - fair balls: EV 88.8 mph, LA 13.3°, hard-hit 38.9 %, barrel 7.8 %, HR/FB 11.6 %;
  - fouls ≈ 52 % of contact (DERIVED);
  - pull / centre / opposite ≈ 40 / 34 / 26 % (FanGraphs; recalled, not re-fetched).
- **Home-run probability**, from the Statcast search CSV, regular season, fetched 2026-10-06:

  | Exit velocity (LA 25–35°) | HR | Mean distance |
  |---|---|---|
  | 90–95 mph | 2.5 % | 339 ft |
  | 95–100 mph | 18.3 % | 364 ft |
  | 100–105 mph | 54.6 % | 391 ft |
  | 105–110 mph | 89.1 % | 414 ft |

  | Projected distance (LA 20–40°) | HR |
  |---|---|
  | 360–370 ft | 18.1 % |
  | 370–380 ft | 27.4 % |
  | 380–390 ft | 38.1 % |
  | 390–400 ft | 54.8 % |
  | 400–410 ft | 74.4 % |
  | 410–420 ft | 90.0 % |

## How each cause was found
- **Exit velocity 96 → 87 mph.** Any contact beyond ±5 in of the sweet spot was a whiff, and the whole bat moved at
  sweet-spot speed, so no amount of hitter error produced MLB's weak contact. The whole bat fixed that (physics-reviewed).
  The CPU hitter's aim was then split:
  - along the barrel, σ 11 cm (contact quality);
  - across it, σ 2.5 cm (whiffs and launch spread);
  - a 0.5 cm lift intent;
  - a reach penalty (+10 % aim error per 1 cm he sees the pitch outside the zone);
  - sharper perception (0.35 mrad).
- **Home runs 4.3 → 1.3 per team-game.** Every link was checked:
  - our carry equals the TASK-004-validated flight with Nathan's average Statcast spin (−0.8 ft on in-game fly balls);
  - our EV/LA match MLB;
  - every home run clears the wall's height at the fence.

  The park was the remaining factor. At the same projected distance, our park gave home runs far more often than MLB (the
  second table above):
  - Its fence joined the five quoted distances with straight chords, so between the alleys and centre the wall sat about
    10 ft closer than the dimensions describe. The wall's distance now changes smoothly with angle (37 points).
  - The generic dimensions became an MLB-average park: 332 ft lines, 385 ft alleys, 405 ft centre, 8-ft wall. The lines
    and centre are the parks' average (≈ 333 / 403 ft, 2024–25 Wikipedia infoboxes); the alleys reproduce the measured
    home-run probability by distance (weighted error 24 → 9). Real walls are deep and tall between the quoted points.
    TUNED geometry, physics untouched; TASK-031 will make parks data-driven.
- **Timing.**
  - The CPU hitter's timing spread became 13 ms (10 before): fouls 45 % of contact (MLB ≈ 52 %), K 23 %.
  - He is now 2 ms early on average, so he pulls more: 37 / 43 / 19 % pull / centre / opposite, MLB ≈ 40 / 34 / 26. A
    4 ms bias matches the pull share but raises home runs; 2 ms balances the two (TUNED).

## Result (30 simulated games, standard rosters, seeds 6001–6030)
| | Before (TASK-022) | After | MLB |
|---|---|---|---|
| Runs per team-game | 8.25 | 3.25 (3.2–3.9 across samples) | 4.39 |
| HR per team-game | 4.33 | **1.27** | 1.13 |
| HR/FB | 38 % | **12.1 %** | 11.6 % |
| K % | 16.7 | **23.4** | 22.6 |
| BB % | 10.6 | 9.7 | 8.2 |
| Pitches per PA | 3.71 | **3.74** | 3.89 |
| Contact per swing | 74.7 % | 69.7 % (zone 76, chase 56) | 76.8 % (86 / 62) |
| Exit velocity (fair) | 96.2 mph | **87.2** | 88.8 |
| Launch angle | 7.9° | 10.4° (SD 23.7) | 13.3° |
| Hard-hit | 65 % | **38.6 %** | 38.9 % |
| Barrel | 10.7 % | **6.0 %** | 7.8 % |
| Fouls, share of contact | — | 45 % | ≈ 52 % |

## Remaining discrepancies (handed on, with the measurements)
- **Runs are ≈ 25 % low because balls in play convert wrongly by type.**

  | Type | Sim (30 games) | MLB (approx., FanGraphs BABIP by type) |
  |---|---|---|
  | Ground balls | .353 | ≈ .24 |
  | Line drives | .508 | ≈ .68 |
  | Fly balls (excl. HR) | .052 | ≈ .13 |

  - Doubles are 0.57 per team-game against ≈ 1.6. Outfielders gather hits in a median 3.65 s (P90 5.0 s), so few balls
    get far enough for a double.
  - This is defensive conversion: infield range and outfield reads, positioning and catch success. It is not the hitter
    or the park.
  - Handed to TASK-033 (defensive positioning) and TASK-037 (large-sample calibration).
- **Zone contact is 76 % (MLB 86 %).** Vertical misses come from the hitter's perception; sharper perception would square
  the ball up again.
- **Walks are slightly high, 9.7 %.** The CPU pitcher's zone rate is 47 % (MLB ≈ 49 %).
- **Opposite-field balls are 19 % (MLB 26 %).** Directional tendencies are TASK-030's.
