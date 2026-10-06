# CPU pitcher

A first-pass pitch caller (TASK-020). It makes a seeded, weighted, explainable choice of pitch type and target. Execution
(Docs/PITCH_EXECUTION.md) and the pitch physics then decide where the pitch goes. Code: `CpuPitcher`, `PitchDecision`
(Assets/Game/Gameplay/Play); `GamePitches.Create(…, targetX, targetZ, …)`.

Labels: MEASURED / DERIVED / ASSUMED / TUNED (see Docs/PLAYER_RATINGS.md).

## What he knows
- He may know: the count, the outs, the runners, the batter's side and broad ratings (power, discipline), his own
  repertoire, and the plate appearance's previous pitches. For those he knows his own calls (type, target), not the
  batter's thoughts.
- He never knows: the batter's input, his own execution error, or the result. All of those happen after the call.
- No artificial difficulty: he never reads the PCI and never changes a pitch after release. Difficulty comes from his
  ratings (velocity, command, movement) and his choices.

## Pitch type
Each pitch in his repertoire has weight = usage × count × matchup × repeat:
- Count: fastballs (four-seam, sinker) ×1.5 and others ×0.8 when behind; ×3 / ×0.5 at 3-0; ×0.75 / ×1.3 with two strikes
  (ASSUMED, following the MLB pattern: fastball share 94.7 % at 3-0, MEASURED in TASK-017 research).
- Matchup: sliders ×1.25 against same-side hitters and ×0.8 against opposite-side; changeups ×0.6 against same-side and
  ×1.4 against opposite-side. This is the usual platoon usage: a slider breaks away from a same-side hitter, a changeup
  fades away from an opposite-side one (ASSUMED magnitudes).
- Repeat: ×0.6 for each time in a row he has just thrown that type (TUNED: at most 4–5 in a row).

## Location
- Intent, by count situation (TUNED so the executed zone rate follows MLB by count):

  | Count | Heart | Edge | Chase | Waste |
  |---|---|---|---|---|
  | 3-0 | .55 | .38 | .07 | 0 |
  | Behind | .33 | .50 | .15 | .02 |
  | Even | .25 | .50 | .21 | .04 |
  | Ahead | .12 | .45 | .35 | .08 |
  | 0-2 | .04 | .30 | .48 | .18 |

- The batter adjusts the intent:
  - Heart × (1 − 0.3 r̂(Power)): stay out of a power hitter's heart.
  - Chase × (1 − 0.4 r̂(Discipline)): expand against free swingers.
  - With a runner on third and fewer than two outs, waste × 0.3 (no wild pitch).
- Spot by type:
  - Four-seamers mostly up, in or away.
  - Sinkers, sliders, curveballs and changeups mostly down.
  - Sliders to his glove side; changeups and sinkers to his arm side.
  - The same type to the same spot twice in a row is redrawn once.
- Targets in the batter's zone:
  - Heart: 35 % of the zone's half-extents from the centre.
  - Edge: 5 cm inside the edge. His miss (≈ 7 in per axis for average command) spreads it both ways.
  - Chase: 7 cm beyond the edge where the call changes (the zone widened by a ball radius).
  - Waste: 28 cm beyond that edge.
- The call is a `PitchDecision`: type, target point, intent, a location word ("low-away") and a reason
  ("0-2 · two strikes · chase · opposite side").

## Where it runs
- `GameSimulator` uses it for every rated pitcher; the basic `AutoPitcher` remains for unrated teams.
- In the GameLab, auto pitching (P) uses it. The situation editor (G) shows the intent and reason, and the execution miss
  once the pitch is over (it never tells the hitter where the pitch goes):
  "Slider low-away (Chase) · 0-2 · two strikes · chase · missed 4.1 in glove-side, down".

## Population (regression check, not validation)
Executed pitches in 10 simulated games (seeds 201–210; CPU pitchers against CPU batters), zone rate by count:

| Count | 0-0 | 0-1 | 0-2 | 1-0 | 1-1 | 1-2 | 2-0 | 2-1 | 2-2 | 3-0 | 3-1 | 3-2 | All |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Sim | 51 % | 40 % | 29 % | 53 % | 48 % | 40 % | 56 % | 53 % | 51 % | 50 % (n 34) | 57 % | 42 % | 47 % |
| MLB | 54 % | — | 32 % | — | — | — | — | — | — | 64 % | — | — | ≈ 49 % |

MLB values are MEASURED (zone% by count, TASK-017 research).

Tests (`CpuPitcherTests`) cover:
- determinism, and no fixed cycle;
- only his repertoire, roughly by usage;
- count behaviour: strikes and fastballs behind, expanding ahead;
- the batter profile: chase against free swingers, no heart against power;
- the platoon matchup and the slider's side;
- the runner-on-third rule;
- no long same-type runs in whole simulated games;
- executed zone rate: behind > even > ahead.

## Limitations
- No scouting or hot zones, no pitch tunnelling, no setup sequences beyond the repeat rule, no situational intent
  (double-play balls, pitching around a hitter).
- 3-0 executed zone rate is low (50 % on a small sample, MLB 64 %).
- His own ratings do not change his calls; they act through execution (the command starter misses his spots by less —
  tested in simulated games).
