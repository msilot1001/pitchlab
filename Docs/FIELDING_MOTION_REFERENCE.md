# Fielding motion reference (TASK-011.6)

Generic professional technique for the procedural mannequins — no player likenesses. Labels:
**MEASURED** (research / tracking data), **REFERENCE CONVENTION** (coaching standard), **VISUALLY ESTIMATED** (our
approximation; a tuning starting point). Presentation follows gameplay; these shape *how* an authoritative event looks.

## Locomotion (shared)
- Walking ≈ 115–120 steps/min, step ≈ 0.75–0.85 m, duty factor ≈ 0.6 — **MEASURED** ([PMC8008308](https://pmc.ncbi.nlm.nih.gov/articles/PMC8008308/)).
- Jogging 150–165 steps/min; fast running 170–190 — **REFERENCE CONVENTION/MEASURED** ([PMC6915645](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6915645/)).
- Elite top speed 10.6 m/s: ≈ 4.5 steps/s, step ≈ 2.3 m, contact 0.096 s, flight 0.124 s (duty ≈ 0.22) — **MEASURED** ([PMC6628312](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6628312/)).
- Acceleration: trunk lean 30–45° over the first 10 m, upright near top speed; contact ≈ 0.20 s on the first steps — **MEASURED**.
- MLB sprint speed average 27 ft/s (8.2 m/s) — **MEASURED** ([Statcast](https://www.mlb.com/glossary/statcast/sprint-speed)).
- Implemented: cadence f(v) = 1.8 + 0.28 v steps/s (2.2 walking, 2.6 jogging, 4.1 at 8.2 m/s), step = v / f, duty 0.6 → 0.28,
  forward lean from speed and acceleration, back-lean and lower when braking, arms opposite the legs — **VISUALLY ESTIMATED** fit to the above.

## Ready stance and first step
- Creep step and split hop timed to contact; feet a little wider than the shoulders, knees bent, glove out — **REFERENCE CONVENTION** ([prolificbaseball](https://prolificbaseball.com/infield-pre-pitch-split-step/), [ABCA](https://www.abca.org/magazine/magazine/2016-2-Spring/Coaches_Corner_5_Secrets_Top_Infielders_use_to_Increase_their_Range.aspx)).
- Lateral first step is a crossover; a drop step opens the hip for a ball behind — **REFERENCE CONVENTION**.
- Implemented: positional ready variants (corner infielders deeper crouch, middle infielders balanced, outfielders taller,
  catcher crouched); the first move turns at a limited rate (no instant 180°) and the stance feet move along the travel
  direction, so a lateral first step reads as a crossover/shuffle and a ball behind turns the fielder (sprinting) rather than
  backpedalling.

## Ground balls
- Approach right-to-left, last two steps right–left into the fielding triangle; glove out front, heel of the glove down — **REFERENCE CONVENTION** ([TeamSnap](https://www.teamsnap.com/community/skills-drills/baseball/fielding/115-teaching-fielding-fundamentals-ground-balls), [battingleadoff](https://battingleadoff.com/how-to-field-a-ground-ball-in-baseball/)).
- Backhand: glove hand across, right leg forward, plant on the right foot and throw — **REFERENCE CONVENTION**.
- Forehand: glove side extended — **REFERENCE CONVENTION**.
- Slow roller: charge in a line, one-hand pickup beside the glove-side foot, throw on the run sidearm/underhand ≈ one step
  (≈ 0.3 s) after the pickup — **REFERENCE CONVENTION / VISUALLY ESTIMATED**.
- Short hop: pick it within a foot of the bounce; long hop: let it peak and receive it — **REFERENCE CONVENTION** ([Coaches Insider](https://coachesinsider.com/baseball/infield-breaking-down-hop-selection-with-tyler-gillum-south-mountain-cc/)).

## Outfield
- Ball over the head: drop step and sprint, never backpedal; glove down while running, extended only at the catch;
  over-the-shoulder catch keeps running — **REFERENCE CONVENTION** ([drop step](https://www.learn-youth-baseball-coaching.com/BaseballOutfield-DropStep.html)).
- Sliding catch: feet first, glove-side hand out, low — **REFERENCE CONVENTION**.
- Dive: take-off ≈ one stride from the ball, airborne ≈ 0.3–0.4 s, up again ≈ 1.0–1.5 s after landing — **VISUALLY ESTIMATED**.
- Statcast catch probability tiers (5★ 0–25 %, 4★ 30–50 %, …) depend on distance, opportunity time, direction, wall — **MEASURED** ([Catch Probability](https://mlb.com/glossary/statcast/catch-probability), [Jump](https://www.mlb.com/glossary/statcast/jump)). No published dive frequency.

## Throwing and receiving
- Outfield crow hop: step, step, a quick low hop, stride to the target, ≈ 0.3–0.4 s — **REFERENCE CONVENTION / VISUALLY ESTIMATED** ([crow hop](https://www.dugoutcaptain.com/drill/how-to-coach-the-crow-hop/)).
- Infield quick throw: glove to the chest, replant, step; on the run sidearm off the plant foot — **REFERENCE CONVENTION**.
- Relay man: glove-side shoulder to the throw, catch moving toward the target, turn to the glove side — **REFERENCE CONVENTION** ([probaseballinsider](https://probaseballinsider.com/baseball-instruction/relay-and-cut-off-fundamentals/)).
- Catcher exchange ≈ 0.73 s (elite 0.64 s) — **MEASURED** ([Statcast](https://www.mlb.com/news/statcast-adds-pop-time-exchange-arm-strength-c268064274)).
- Double-play pivot 0.7–0.9 s catch-to-release; 2B: left foot on the bag, step to the feed, throw; SS: across the bag
  moving toward first — **REFERENCE CONVENTION** ([Coaches Insider](https://coachesinsider.com/baseball/access-level-baseball-2/executing-the-double-play-in-the-middle/), [baseballscouter](https://baseballscouter.com/4-6-3-double-play-footwork-guide/)).
- First baseman: heels at the bag facing the thrower, throwing-side foot on the bag, glove-side foot stretches late toward
  the ball — **REFERENCE CONVENTION** ([first-base footwork](https://probaseballinsider.com/baseball-instruction/first-base/first-base-footwork/)).
- Tags: swipe tag (quick sweep across the path, glove back), plate tag (glove at the front of the plate) — **REFERENCE CONVENTION** ([Rule 7.13](https://www.baseballmonkey.com/learn/buster-posey-collision-rule-7-13)).
