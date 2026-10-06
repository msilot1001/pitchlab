# Bunting (TASK-025)

A bunt goes through the same contact model as a swing (`ContactResolver`); only the bat differs. The rules, the CPU's
sacrifice and the labs build on that. Labels: MEASURED / DERIVED / ASSUMED / TUNED (Docs/PLAYER_RATINGS.md).

## The bunt (`SwingInput.Bunt`, `SwingParameters.AsBunt`)
- **Squaring.** The batter squares at a time. He is set `BuntSetTime = 0.25 s` later (ASSUMED); a bunt squared later than
  that before the ball arrives misses (`MissTiming`).
- **Contact timing.** The bat waits for the ball, so there is no swing timing: contact happens when the ball reaches the
  contact plane. The bat is at the PCI where it is then. In the labs the PCI is read at that moment; the simulator uses the
  CPU's aim at that moment (`BatterPlan.InputFor`).
- **Bat.**
  - Level and with no attack angle.
  - Pushed straight toward the ball at `BuntPushSpeed = 3 m/s`, one speed along the bat (no pivot). This is a TUNED knob
    to the measured exit speed below, not a measured push. It also absorbs the swing's collision efficiency (q = 0.21)
    understating a bunt's: e rises at a bunt's lower impact speed. A bat drawn back ("give") is not modelled.
  - The bat angle must be finite and below 45°; otherwise the input is invalid.
  - Squared to the ball's incoming path, then angled by the batter's aim (`BuntAim`, rad toward first base).
- **Direction is physics, not a script** (physics-reviewed). Along the bat's face the ball keeps 1 − (2/7)(1 + e_x)/(1 + r_x)
  ≈ 0.71 of its speed, while its speed off the face is only q·v·cosθ plus the push. So the deflection is about
  2.3–3.4·tanθ and depends on e_x and r_x; friction does not bind. Against a nearly still bat the ball keeps much of its speed along the bat's face,
  so a few degrees steer it: about 5° of bat angle sends a bunt 25° toward a line. Twenty degrees would send it foul.
- **Pulling back.** Up to `OfferLead` (60 ms) before the ball arrives, a pulled-back bunt is no attempt
  (`ContactOutcome.CheckedSwing`), and the pitch is called as a take.

## Rules
| Event | Rule (OBR) | Result |
|---|---|---|
| Bunt foul | 5.09(a)(4) | `PitchOutcome.FoulBunt`: a strike, strike three included |
| Missed bunt | Definitions, "Strike" (a) | swinging strike |
| Bunt pulled back in time | (no attempt) | the zone call |
| Bunt, under two out, the batter retired, no other runner retired, a runner advanced | 9.08(a) | `PlayResultKind.SacrificeBunt` |
| Bunt for a hit | | e.g. "single (bunt)"; the scorer's judgement of a sacrifice that could have retired a runner is not modelled |

`GameState.Apply(play, info, bunt: true)` records a bunted ball.

## CPU (`BuntStrategy`, `CpuBatter.PlanBunt`)
- **When he bunts (ASSUMED rule of thumb):**
  - nobody out, a runner on first, nobody on third;
  - 7th inning or later, score within one;
  - batter's Power ≤ 40, fewer than two strikes.

  Strategy AI (run expectancy, squeeze, bunt defense) is TASK-033.
- **Aim:**
  - runner on first only: toward first, +5°;
  - runners on first and second: toward third, −5°.
- **The bunt:**
  - He squares as he sees the release.
  - His bat follows his predicted crossing, plus his bunting error: along the bat σ 3 cm (ASSUMED), across it σ 4.5 cm
    (TUNED to the missed-bunt rate).
  - He holds the bat 4 mm over the ball's centre (TUNED to the launch angle).
  - At the swing decision's moment he pulls back a pitch he judges a ball (strike belief < ½).
  - It uses the same perception as his swing, and no future flight.

## Human (GameLab, HittingLab)
- Q (or the gamepad's West button) during the delivery or the pitch squares.
- A second press pulls the bunt back.
- The PCI places the bat. A human bunt's angle is 0 (squared to the pitch).
- The swing button does nothing while squared.
- **Presentation** (not authority): the bat comes level across the plate with its sweet spot on the PCI. Pulled back, or
  after the ball arrives, he returns to his stance. The body does not turn square: a known simplification.

## Result
**Forced sacrifice spots.** 400 plate appearances: 8th inning, nobody out, runner on first, CPU pitcher. Measured with the
production pieces.

| | Sim | MLB 2024 (MEASURED, Baseball Savant search) |
|---|---|---|
| Pitches pulled back | 55 % | (not measured) |
| Missed, of attempts | 9 % | 202 missed of ≈ 2,400 attempts ≈ 8 % |
| Foul, of contact | ≈ 17 % | 1,233 foul bunts ≈ 55 % of contact |
| Fair bunt exit speed / launch angle | 33 mph / −26° | sacrifice bunts 33.8 mph / −35° (452) |

## Discrepancies (documented, handed on)
- **Sacrifices don't succeed (0 of 134 fair bunts in the forced sample): the pitcher forces the lead runner at second.** Two reasons:
  - In a play the runner on first starts from a 12-ft lead with no secondary lead and no "break on the bunt". In MLB he is
    already moving when the ball is bunted.
  - No bunt defense: the corners do not charge, and the first baseman is not holding.

  Leads are TASK-026; positioning and bunt defense are TASK-033. Re-measure after both.
- **Foul bunts are too few** (17 % vs 55 % of contact). The bunting error that sets it also sets the missed-bunt rate, which
  is about right.
