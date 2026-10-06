# Offense calibration (TASK-023)

Simulated games should score like MLB. The calibration fixes causes; it never scales results:
- the contact model's bat was incomplete (Docs/HITTING.md, "The whole bat");
- the CPU hitter's contact quality was too good (Docs/BATTER_AI.md).

The aerodynamics, the collision parameters and the pitch physics are unchanged. Harness: `OffenseCalibrationTests.Report(n)`
(EditMode assembly). Guards: `SimulatedOffenseStaysNearMlb`.

Labels: MEASURED / DERIVED / ASSUMED / TUNED (Docs/PLAYER_RATINGS.md).

## Reference (MLB 2024, league; FanGraphs leaders API and Baseball Savant /league — MEASURED unless marked)
- Per plate appearance:
  - runs: 4.39 per team-game (DERIVED);
  - K 22.6 %, BB 8.2 %;
  - HR 2.99 % of PA (DERIVED);
  - 3.89 pitches per PA (DERIVED).
- Swings:
  - swing rate 47.8 %;
  - contact 76.8 % per swing (zone 85.8 %, chase 62.4 %).
- Batted balls (fair):
  - exit velocity 88.8 mph, launch angle 13.3°;
  - hard-hit 38.9 %, barrel 7.8 %, sweet spot ≈ 34 %;
  - HR/FB 11.6 % (FanGraphs batted-ball types);
  - HR per batted ball ≈ 4.4 % (DERIVED).

## Before → after (30 simulated games, standard rosters, seeds 5001–5030)
| | Before (TASK-022) | After | MLB |
|---|---|---|---|
| Runs per team-game | 8.25 | **4.45** | 4.39 |
| HR per team-game | 4.33 | 2.32 | ≈ 1.13 |
| K % | 16.7 | **21.1** | 22.6 |
| BB % | 10.6 | 9.5 | 8.2 |
| Pitches per PA | 3.71 | 3.70 | 3.89 |
| Contact per swing | 74.7 % (Z 76, O 71) | 70.0 % (Z 77, O 55) | 76.8 % (Z 86, O 62) |
| Exit velocity (fair) | 96.2 mph | **87.4** | 88.8 |
| Launch angle | 7.9° (SD 24.6) | 11.0° (SD 23.3) | 13.3° |
| Hard-hit | 65.4 % | **38.8 %** | 38.9 % |
| Barrel | 10.7 % | **7.0 %** | 7.8 % |
| HR per batted ball | ≈ 14 % | 8.8 % | ≈ 4.4 % |

## What changed
1. **The whole bat** (contact model):
   - contact from the end of the bat (6 in tipward) to near the hands (14 in toward the handle);
   - the handle tapers;
   - the bat turns about a pivot, so it is slower toward the hands and faster toward the end;
   - off the end, q goes slightly negative.

   Weak contact, jammed or off the end, now exists. Before, any contact beyond ±5 in was a whiff, so a wider error could
   only make whiffs, never weak balls. The physics review confirmed the pivot model (Cross 2009) and that Nathan's q
   combined with the local bat speed is the correct use of his formula.
2. **The CPU hitter's contact quality:**
   - aim errors split into along the barrel (11 cm) and across it (2.5 cm);
   - a lift intent (0.5 cm under the ball's centre);
   - a reach penalty: a pitch he sees outside the zone is harder to square up, +10 % aim error per 1 cm outside;
   - sharper perception (0.35 mrad).

   All are TUNED to the reference above.

## Known discrepancy: home runs (≈ 2× MLB)
Exit velocity, hard-hit and barrel rates match, but about 8.8 % of fair balls leave the park (MLB ≈ 4.4 %). In the
simulation's fly balls at 20–38°, the HR share is ≈ 7 % at 90–95 mph, ≈ 47 % at 95–100 and ≈ 90 % at 100–105.
- **Reference:** recalled Statcast bands (not re-measured this session) are ≈ 1–5 %, 10–25 % and 40–60 %. That is the
  equivalent of ≈ 15–25 ft of extra carry at those speeds.
- **Ruled out:**
  - the wall test: every home run clears the 8-ft wall's height at the fence;
  - the fence distances (330 / 375 / 400 ft, typical);
  - the hitters' EV/LA.
- **Not resolved:**
  - the carry of contact-generated spin (TASK-004.5 checked it against Nathan's average spin within a few feet, on a grid,
    not on in-game balls);
  - the park (one generic park, standard air).
- **Hand-off:** TASK-031 (park geometry) and TASK-037 (large-sample calibration and park validation). Neither the
  aerodynamics nor the hitter was retuned to hide it. `SimulatedOffenseStaysNearMlb` caps it so it cannot grow.

## Other remaining gaps
- Zone contact is 77 % (MLB 86 %). Vertical misses come from the hitter's perception; tighter perception would make
  contact too square again.
- Walks are slightly high (9.5 %; the CPU pitcher's zone rate is 47 %, MLB ≈ 49 %).
