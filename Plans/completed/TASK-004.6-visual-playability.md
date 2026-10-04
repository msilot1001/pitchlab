# TASK-004.6 — Statcast-style visual playability

## Goal
Pitch → PCI → swing → contact → batted ball reads as a primitive baseball game in Game View: dark articulated mannequins (pitcher, batter) with bat/glove/ball, field context, a batting camera that hands over to a ball-follow camera, landing feedback, restrained contact feedback. Presentation only; simulation and gameplay stay authoritative.

## Owner
Claude Code (sole writer). Read-only: unity-reviewer, test-reviewer. Branch `feat/task-0046-visual-playability` from main 8e20c04.

## Design
- `Pitchlab.Presentation` assembly (UnityEngine only; no Simulation/Gameplay dependency): `PlayerMannequin` (procedural primitive body, joint enum, equipment anchors, mirror for handedness, pose application, simple two-bone arm aim), `MannequinPose` + `PoseSequence` (key poses, smooth interpolation), `MannequinPoses` (pitcher, batter, fielder-ready library), `Equipment` (bat/glove/ball primitives on anchors), `FieldDressing`, `BaseballCamera`, `ContactCue` (sound/flash/impulse), `LandingMarker`.
- Sandbox glue `HittingLabPresentation` reads `HittingLabController` state each frame (explicit reference, no event bus) and drives mannequins, held ball, camera, cues, landing marker. It never writes gameplay state.
- `HittingLabController` (gameplay) keeps rendering the ball from authoritative samples: pitch `Flight.StateAt(t)`, then the batted-ball `Flight.StateAt(t)` until landing (was: freeze at contact).
- Deliberate gameplay sequencing change: throw press → release at press + `DeliveryLead` (authoritative, from the input timestamp; presentation fits the wind-up into it). Presses during the delivery are early swings (MissTiming via the existing resolver). Frame-rate determinism test shifts its post-throw trace by the lead (same protection).

## Not touched
Pitch physics, BallFlight, ContactResolver, SwingParameters, PCI speed, timing windows.

## Milestones
1. [x] Mannequin + poses + equipment (Presentation asm) + EditMode anchor/mirror tests
2. [x] Field dressing, camera, ball visuals, landing, contact cue
3. [x] HittingLab glue: pitcher release sync, batter swing sync, held ball, repeat loop, debug panel secondary
4. [x] PlayMode wiring tests; regression tests updated only where presentation semantics changed
5. [x] Game View verification (screenshots), reviews, check.sh, commit

## Decisions
- Poses are direction-based (limb directions in the figure frame, applied in parent space) so left-handed mirroring (negative scale on the figure's Visual child) works without separate poses.
- Batter: two-handed grip via a small two-bone arm reach; bat sweet spot slid (clamped ±0.3/±0.4 m) toward the authoritative contact point (hit) or the PCI (miss) around contact; timing turns the body, vertical offset tilts the finish.
- Pitcher: whole figure slid (clamped) so the throwing hand meets the simulated release point at release; delivery keys timed relative to release; `DeliveryLead` is simulation seconds (real = lead ÷ playback speed).
- Readability aids: ball 1.6× real size with a minimum on-screen size by camera distance, trail, ground marker under batted balls; centre always on the authoritative sample.
- No Cinemachine (simple `BaseballCamera`); no new packages.

## Verification
- check.sh: EditMode 145/151 passed (6 ignored/explicit), PlayMode 15/15 (incl. frame-rate determinism with the presentation rendering every frame).
- Game View (frozen-clock captures): idle batting view; leg lift with ball in hand; arm cock; ball in flight on the authoritative sample; centred contact (bat sweet spot 1 cm from the contact point); follow camera to a 224 ft line-drive landing; 391 ft fly ball (ball + ground marker high over right-centre, ring just short of the 400 ft arc); topped −8° grounder (17 ft); pulled and opposite-field flies; miss (bat under, pitch continues, "Swing and miss", no landing); left-handed batter mirrored on the first-base side. Live real-clock loop: throw → delivery → next pitch. Console clean.
- Reviews: unity-reviewer (delivery jump at 0.5×, shake falloff/accumulation, disabled-controller start, hidden home circle — fixed; coupling/dead code trimmed), test-reviewer (frame-rate test now renders presentation, in-flight swing test re-timed, early-swing-during-delivery test, visible-state assertions, landing position, reset, miss long after, colliders, batter side, grip for all bat poses × hands — applied).

## Remaining risks
- Presentation materials use `Shader.Find` (URP Lit/Unlit are in builds only via existing debug materials).
- Grounders stop at first ground contact (no bounce/roll in the flight model yet).
- Early/late extreme contacts can leave the bat up to ≈ 15 cm from the contact point (slide clamps).
- The Editor pauses Play Mode when unfocused (`runInBackground` false); MCP verification set it true at runtime only.
