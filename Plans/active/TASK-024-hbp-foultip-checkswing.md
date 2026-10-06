# TASK-024: Hit by pitch, foul tip, check swing

## Scope note
The detailed TASK-023–028 specification never reached this session; only the titles did (the long-session request of
2026-10-06). This plan takes its scope from the title, the Official Baseball Rules and the request's architecture rules.

## Goal
Three rules events that the game could not produce:
- **Hit by pitch:** a pitch that touches the batter.
- **Foul tip:** a tipped pitch caught by the catcher.
- **Check swing:** a swing stopped before it is an offer.

Each is decided by gameplay from the authoritative pitch, swing and contact, never by presentation. The CPU batter and the
human batter use the same rules.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Rules (OBR)
- **Hit by pitch.** Touched by a pitch he is not trying to hit:
  - he is awarded first base, and forced runners advance (5.05(b)(2));
  - the ball is dead (5.06(c)(1));
  - a pitch that touches him in the strike zone, or after he swung, is a strike (5.05(b)(2) exceptions, 5.09(a)(2) and
    the Definitions, "Strike" (e) and (f)).

  "Did not try to avoid it" is not modelled: he always tries to avoid it, an ASSUMED simplification.
- **Foul tip.** A batted ball that goes sharp and direct from the bat to the catcher's hands and is legally caught
  (Definitions). It is a strike, including strike three (a strikeout), and the ball stays live. Uncaught, it is a foul.
- **Check swing.** No rule defines the offer. Umpires judge whether the batter "struck at" the pitch; in practice, whether
  the bat head passed the front of the plate. Modelled as an offer point at a fixed lead before the swing's contact time.
  - A check started before the offer point is no swing: the pitch is called as a take.
  - After the offer point the swing continues: it is a swing (ASSUMED).

## Design
- **`BatterBody` (Gameplay/Hitting).** The batter's body as simple volumes in the simulation frame:
  - stance position from Baseball Savant (MEASURED, as the presentation uses it);
  - limb and torso sizes scaled by height (ASSUMED);
  - the first time the pitch (ball radius included) touches it, from the authoritative flight.
- **`FoulTips` (Gameplay/Hitting).**
  - The catcher's glove waits where the pitch would cross his receiving plane.
  - A batted ball whose straight, early path (before the ground) crosses that plane within the glove's reach is caught.
  - The reach is TUNED to MLB's measured foul-tip rate: 1.03 % of pitches, 7,352 of 711,899 in 2024, Baseball Savant
    search.
- **Check swing.**
  - `SwingInput.CheckTime` (NaN: none) and `SwingParameters.OfferLead`.
  - `ContactResolver` returns `ContactOutcome.CheckedSwing` (no swing) for a check before the offer.
  - `PitchOutcomes` calls it as a take.
  - **CPU:** he keeps watching after he starts the swing. If the pitch now looks clearly worse than when he committed (his
    strike belief has halved and is below ½), he checks at once. Checks after the offer point are too late. No new random
    draws.
  - **Human:** C or the gamepad's East button during the swing.
- **Rules.**
  - `PitchOutcome.HitByPitch` and `PitchOutcome.FoulTip`; `PlateAppearanceEnd.HitByPitch`.
  - Bases on a hit by pitch: the walk's forced advance.
  - A foul tip counts like a swinging strike.
- **Integration.**
  - `GameSimulator` and the Hitting/GameLab controllers use the same functions.
  - A foul tip or a hit by pitch creates no live play.
- **Presentation.**
  - A hit by pitch: the ball stops at the body point and drops.
  - A foul tip: the ball goes into the catcher's glove.
  - A check swing: the bat stops.
  - HUD and log texts.

## Validation
- Tests: geometry and rules (each OBR case above), CPU behaviour, determinism, the human input.
- Sample: the HBP rate (MLB ≈ 1.1 % of PA: 2,020 HBP in 2024 at ≈ 38 PA per team-game) and the foul-tip rate.
- check.sh; runtime check in the GameLab; reviews; Codex.

## Milestones
- [x] Rules and outcomes; BatterBody; FoulTips; the check swing in the contact model.
- [x] CPU check swing:
  - The first rule (belief halved and below ½) checked 18 % of swing starts and doubled the walk rate.
  - Now he checks only a swing he took for a strike, once he sees a clear ball (belief < 0.1): 0.8 % of swing starts.
- [x] Human check button; GameSimulator and lab integration.
- [x] Presentation and texts:
  - the bat stops on a check;
  - a foul tip goes into the catcher's mitt;
  - a hit-by-pitch ball stops at the body and drops;
  - banner texts.
- [x] Foul-tip reach: the mitt's size (OBR 3.04, 0.19 m), not fitted to the rate. The remaining gaps are documented
  (Docs/HBP_FOULTIP_CHECKSWING.md): hit by pitch 1.5 % (MLB 1.1 %), foul tips 0.48 % (MLB 1.03 %).
- [x] Tests (PlateEventsTests 11, human check PlayMode test, CPU perception test extended to the check look, seeds
  re-pinned).
- [x] check.sh green (EditMode 716 passed, 6 skipped; PlayMode 104/104). Runtime: GameLab CPU vs CPU for 3 min, console
  clean.
- [x] Codex round 1: grade C. Fixed:
  - a swing after the touch could erase a hit by pitch; now a swing counts only if it reached the offer point by the
    touch, and the lab ignores later presses;
  - the in-zone exception now uses the ball's place at the touch (`StrikeZone.ContainsAt`), not the plate crossing.
- [x] Test review:
  - ordering tests;
  - the human check test now hits the ball before checking it;
  - a CPU check through the lab with coarse frames;
  - the CPU perception test replays from the press;
  - a pitch in the dirt; checks fall mostly on balls.
- [x] Unity review:
  - a foul tip no longer hangs at the catcher's plane when the catcher is hidden (it flies on behind the plate);
  - no follow camera on a tip;
  - the debug pitch path is restored after a check.
- [x] check.sh green (EditMode 718 passed, 6 skipped; PlayMode 105/105).
- [x] Codex round 2: grade C. Fixed:
  - a late contact after the touch now counts for nothing (`PitchOutcomes.ContactStands`): the ball was dead;
  - the lab no longer plays such contact out;
  - the banner shows the rule's call for every touched pitch.
- [x] check.sh green (EditMode 719 passed, 6 skipped; PlayMode 105/105).
- [ ] Codex round 3; merge.
