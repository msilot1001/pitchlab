# Player ratings

Ratings are a 0–100 display scale (50 = an average major leaguer at the skill). Gameplay never reads a rating directly:
`RatingScale` (Assets/Game/Gameplay/Players) converts each into the physical or input parameter the simulation runs on.
Every mapping is linear in r̂ = (rating − 50) / 50 ∈ [−1, 1], monotonic, and returns the pre-ratings generic value at 50,
so an all-50 player plays like the generic profiles — except a fielder's top speed, which is always his own sprint speed (27
ft/s at 50 whatever the position; the generic C/1B profiles run 25, SS/CF 28). Ratings feed inputs (speeds, delays, arm speed, execution
spreads); they never choose a result.

Labels: **MEASURED** (published Statcast / official / peer-reviewed number), **DERIVED** (computed from measured data),
**ASSUMED** (gameplay choice; no public measurement), **TUNED** (set by looking at simulation output).

## Running (TASK-017)

| Rating | Parameter | Mapping | Basis |
|---|---|---|---|
| Speed | sprint speed (ft/s, Statcast's fastest 1-s window) | 27 + 3.5 r̂ → 23.5–30.5 | MEASURED: MLB average 27 ft/s; 2025 distribution P10 25.2, P50 27.2, P90 29.3, min 23.1, max 30.3 (Savant sprint-speed leaderboard CSV, DERIVED percentiles); "bolt" ≥ 30 |
| Acceleration | τ of v(t) = v_max(1 − e^(−t/τ)) | 0.69 (1 − 0.15 r̂) → 0.59–0.79 s | 0.69 DERIVED from the Statcast 90-ft split (Docs/BASERUNNING.md); the ± spread ASSUMED (Savant running splits: fastest 30-ft 1.64 s, slowest ≈ 1.98 s, mean 1.81 s — consistent in size) |
| Baserunning | read delay of a runner on base | 0.25 − 0.08 r̂ → 0.17–0.33 s | ASSUMED (no public baserunner reaction measure) |

Home-to-first follows from the running model (MEASURED 2025 mean ≈ 4.49 s, range ≈ 3.97–5.22 s, DERIVED from the
sprint-speed CSV).

## Fielding (TASK-017; error execution TASK-021)

| Rating | Parameter | Mapping | Basis |
|---|---|---|---|
| Reaction | contact → first movement | position base · (1 − 0.25 r̂): outfield 0.30–0.50 s, infield 0.19–0.31 s | base values Docs/FIELDING.md; the ±25 % spread ASSUMED, sized on the Statcast outfield jump spread (reaction component −1.8…+4.6 ft vs average in 1.5 s, DERIVED from the 2025 jump CSV) |
| Speed | top speed | his sprint speed (as running) | MEASURED range above |
| Acceleration | τ | position base 0.78 s · (1 − 0.15 r̂) | ASSUMED spread |
| Fielding, Catching | handling execution | TASK-021 | — |

## Throwing

| Rating | Parameter | Mapping | Basis |
|---|---|---|---|
| ArmStrength | arm strength (mph, Statcast: average of a fielder's top throws) | position average ± 8 r̂ | MEASURED 2026 position averages 1B 79.3, 2B 79.1, 3B 84.9, SS 86.1, LF 87.4, CF 89.6, RF 90.7; best regulars 2025 ≈ 88–96 mph by position (DERIVED) — ±8 mph spans the regulars |
| — | routine throw speed | 0.85 × arm strength | DERIVED (Docs/FIELDING.md, TASK-006A) |
| Transfer | glove-to-hand time | position base · (1 − 0.2 r̂): infield 0.56–0.84 s | ASSUMED spread; catcher exchange MEASURED 2025 mean ≈ 0.66 s, range 0.55–0.77 (pop-time CSV, DERIVED) |
| ArmAccuracy | throw direction error | TASK-021 | — |

## Batting (TASK-019) and pitching (TASK-018)

Recorded on the profile now; their mappings are added with the systems that use them (Docs/BATTER_AI.md,
Docs/PITCH_EXECUTION.md). Reference ranges gathered for them: bat speed MLB average 72 mph (MEASURED, 2024), 2025 qualified
P10 66.5 / P90 75.8 mph (DERIVED); EV ≈ q·v_pitch + (1 + q)·v_bat with q ≈ 0.2 (MEASURED physics, Nathan); pitch speeds by
type (2025 per-pitcher means, DERIVED): four-seam 93.8 (P10–P90 90.5–97.0), sinker 93.2, cutter 89.9, slider 85.8, changeup
86.8, curveball 80.4 mph.

## Generic rosters

`GenericRosters`: batting archetypes (contact, power, balanced, patient, free swinger), running (fast, above average,
average, slow), defence (elite, average, poor) and pitching (power starter, command starter, breaking-ball pitcher). The
average defender at a position is rated to reproduce that position's generic profile (catchers and first basemen ≈ 25 ft/s,
middle fielders 28 ft/s), so the scale itself adds no bias. Two teams: nine-man lineups with eight fielders and a DH, and a
starting pitcher; no substitutions. Not real players.

## Errors (reference for TASK-021)

MLB 2025: fielding percentage ≈ .986 (teams .980–.991), ≈ 0.50 errors per team-game, about half throwing and half fielding
(DERIVED from FanGraphs team fielding totals). Statcast catch probability stars: 5★ 0–25 %, 4★ 26–50, 3★ 51–75, 2★ 76–90,
1★ 91–95 % (MEASURED definitions); league catch rates per star ≈ 8 / 42 / 68 / 84 / 93 % (FanGraphs 2017, secondary).

## Sources
- https://baseballsavant.mlb.com/leaderboard/sprint_speed (and `?year=2025&min=10&csv=true`)
- https://baseballsavant.mlb.com/leaderboard/running_splits
- https://baseballsavant.mlb.com/leaderboard/arm-strength
- https://baseballsavant.mlb.com/leaderboard/poptime
- https://baseballsavant.mlb.com/leaderboard/outfield_jump ; https://www.mlb.com/glossary/statcast/jump
- https://www.mlb.com/news/mlb-bat-speed-leaders-for-2024 ; https://baseballsavant.mlb.com/leaderboard/bat-tracking
- https://baseballsavant.mlb.com/leaderboard/pitch-arsenals
- https://blogs.fangraphs.com/the-physics-of-the-torpedo-bat/
- https://www.mlb.com/glossary/statcast/catch-probability ; https://blogs.fangraphs.com/lets-play-with-new-defensive-data/
- FanGraphs team fielding API (2025)
