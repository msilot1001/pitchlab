# Hit by pitch, foul tip, check swing (TASK-024)

Gameplay decides all three at the plate, from the authoritative pitch, swing and contact. `PitchOutcomes.BeforePlay` is
the one decision used by the headless simulator and the Hitting/GameLab. Presentation only shows the result.

Labels: MEASURED / DERIVED / ASSUMED / TUNED (Docs/PLAYER_RATINGS.md).

## Rules
| Event | Rule (OBR) | Result |
|---|---|---|
| Pitch touches the batter, no swing, out of the zone | 5.05(b)(2) | `HitByPitch`: first base, forced runners advance (the walk's advance), dead ball |
| Pitch touches the batter after a swing (offer point reached by the touch) | Definitions, "Strike" (e) | swinging strike (strike three included) |
| Pitch touches the batter while in the zone (ball inside the zone's volume over the plate at the touch), no swing | 5.05(b)(2)(A); "Strike" (f) | called strike |
| Contact sharp and direct into the catcher's mitt | Definitions, "Foul tip"; 5.09(a)(2) | `FoulTip`: a strike, strike three included (unlike a foul) |
| Swing stopped before the offer point | umpire's judgement ("struck at") | no swing: the zone call |

The ball is dead at the touch:
- a swing that reaches the offer point only after the touch is no swing;
- bat contact after the touch (a late swing meeting the ball behind him) counts for nothing (`PitchOutcomes.ContactStands`);
- the lab ignores swing presses after the touch.

Not modelled:
- the batter making no attempt to avoid the pitch (5.05(b)(2)(B)); he always tries;
- runners advancing on a foul tip (the ball is live, but no play follows);
- foul fly catches.

## Batter body (`BatterBody`)
- **Stance:** where Baseball Savant's batter positioning puts the reference hitter. Hips are 24.7 in behind the front of
  the plate and 27.7 in off its inside edge (MEASURED; the presentation places the figure there too). He stands side-on.
- **Volumes:** front and back leg, torso (elliptic section 0.12 × 0.19 m), head, hands, front arm. Heights scale with the
  batter (ASSUMED round anthropometrics).
- **No motion:** the body does not move during the pitch (ASSUMED: no stride, no flinch).
- **First touch:** found along the authoritative flight in 0.5 ms steps.

## Foul tip (`FoulTips`)
- **Where the mitt waits:** where the pitch would cross the catcher's receiving plane (Y = −0.35 m). He cannot react to a
  tip.
- **Caught:** when the batted ball's direct path crosses that plane above the ground within the mitt's reach. The reach is
  0.19 m centre to centre, DERIVED from OBR 3.04: a mitt is at most 38 in around (≈ 6 in radius), plus the ball's radius.
- **Not tuned to the rate.** The reach is the mitt's size, not fitted to MLB's foul-tip rate.

## Check swing
- **Offer point:** `SwingInput.OfferLead = 60 ms` before the swing's contact time, the last ≈ 40 % of a 150 ms swing
  (ASSUMED). No rule defines the offer; umpires judge whether the bat head passed the front of the plate.
- **Before the offer point:** `ContactOutcome.CheckedSwing`, and the pitch is called as a take.
- **After it:** too late, and the swing is unchanged.
- **CPU batter:**
  - He keeps watching after he starts the swing, until the offer point.
  - He checks a swing he committed to as a strike (strike belief ≥ ½) once his updated belief falls below 0.1, a clear
    ball (TUNED).
  - A chase (he swung though he judged it a ball) is not reconsidered.
  - No random draws are added.
- **Human:** C or the gamepad's East button during the swing.

## Result (30 games, standard rosters, seeds 6001–6030; `OffenseCalibrationTests.Report(30)`)
| | Sim | MLB 2024 (MEASURED, Baseball Savant search) |
|---|---|---|
| Hit by pitch, % of PA | 1.52 | ≈ 1.1 (2,020 HBP) |
| Foul tips, % of pitches | 0.48 | 1.03 (7,352 of 711,899) |
| Check swings, % of swing starts | ≈ 0.8 | (no public measure) |
| K % / BB % | 22.5 / 9.2 | 22.6 / 8.2 |
| Runs per team-game | 3.12 | 4.39 (see Docs/OFFENSE_CALIBRATION.md) |

## Discrepancies
- **Hit by pitch is high (≈ 1.4× MLB).** Likely causes: the static body (real hitters turn away and lift the front leg)
  and how inside the CPU pitcher misses. Neither was tuned to the rate. Handed to TASK-037 (large-sample calibration).
- **Foul tips are low (≈ ½ MLB).** A tip off the edge of the bat is deflected enough that its path at the catcher misses
  the mitt by more than 19 cm. Bat–ball grazing (`MissGlancing`) is rare: 0.09 % of pitches. Real tips are likely more
  gentle, glancing contacts than the contact model's edge collisions. Not fitted by enlarging the mitt; handed to
  TASK-037.
