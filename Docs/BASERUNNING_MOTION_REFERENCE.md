# Baserunning motion reference (TASK-011.7)

Labels as in Docs/FIELDING_MOTION_REFERENCE.md. Generic technique only.

- Primary lead 9–12 ft off first; square to the plate, slightly wider than the shoulders, knees bent, hands off the knees;
  never cross the feet in the lead — **REFERENCE CONVENTION** ([lead off](https://en.wikipedia.org/wiki/Lead_off)). (Gameplay leads:
  12 / 20 / 10 ft from 1B / 2B / 3B.)
- Secondary lead: two shuffles (≈ 3–4 ft each) as the pitch is delivered, landing at contact — **REFERENCE CONVENTION / VISUALLY
  ESTIMATED** ([secondary lead](https://baseballscouter.com/secondary-lead-baseball-base-running/)). Gameplay does not model it: the
  presentation shows a crouch and weight shift in place (no translation, so the authoritative lead stays exact).
- Break: crossover step, low drive, steps lengthen over ≈ 10 m (first contacts ≈ 0.2 s) — **REFERENCE CONVENTION**.
- Rounding: bow out 10–15 ft before the base (banana), touch the inside corner, lean in, shoulders to the next base —
  **REFERENCE CONVENTION** ([first-base turn](https://baseballscouter.com/run-bases-properly-first-base-turn/)). Gameplay path is the
  banana leg; the presentation leans with the path's turn rate.
- Through first: no slide; hit the front of the bag, brake over 3–5 strides, turn toward foul ground. Over 22.9 m:
  run-through 3.28 s, head-first slide 3.33 s, feet-first 3.40 s, run-and-stop 3.48 s — **MEASURED** ([PMC6188574](https://pmc.ncbi.nlm.nih.gov/articles/PMC6188574/)).
- Feet-first (pop-up / bent-leg) slide: start 6–10 ft from the bag, figure-4 legs, slide on calf/thigh/buttocks, head back,
  arms up; ≈ 0.4–0.6 s — **REFERENCE CONVENTION / VISUALLY ESTIMATED** ([how to slide](https://probaseballinsider.com/baseball-instruction/base-running/base-running-how-to-slide/)).
  Gameplay: a stop on 2B/3B decelerates at 10 m/s² (≈ 3.4 m / 0.82 s from 8.2 m/s — longer than the 0.4–0.6 s reference
  slide); the slide pose covers exactly that phase when a throw there is close (caught within 1 s of his arrival); otherwise
  he pulls up standing (the same authoritative braking). A trot (awarded advance) never slides.
- Head-first slide: start ≈ 2 strides (6–8 ft) out — **REFERENCE CONVENTION**; reaches further at contact (0.96 m vs 0.79 m) —
  **MEASURED** ([PMC6188574](https://pmc.ncbi.nlm.nih.gov/articles/PMC6188574/)). Coaching advises against it at home and to break
  up a double play (**REFERENCE CONVENTION**; MLB players do slide head-first into home). Used here when diving back to a base
  with a throw coming; at home the slide is feet-first on a close play (the gameplay runner keeps his speed through the plate,
  so the slide there travels with him — a known look).
- Tag-up: a foot on the base, body turned to watch the catch, leave on the first touch — **REFERENCE CONVENTION**.
- Stopping / retreating: 3–5 short chopping strides leaning back, turn, crossover back — **VISUALLY ESTIMATED**.
- Home-run trot: average 22.0 s (2010) – 22.7 s (2017), fastest ≈ 16 s — **MEASURED** ([SABR](https://sabr.org/latest/whats-the-speed-of-an-average-home-run-trot/)).
  Implemented as a gameplay trot profile for awarded runners (≈ 5.2 m/s top speed → ≈ 21–22 s for the batter).
