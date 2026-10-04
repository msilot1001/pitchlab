# Architecture

The intended direction is `Simulation ← Gameplay ← Presentation`. Simulation owns deterministic state transitions and physical calculations; it must not reference gameplay or rendering code. Gameplay turns input and rules into simulation commands and interprets results. Presentation observes state to drive camera, animation, audio, and UI. Presentation may read simulation data where useful.

Pitch flight will use a custom authoritative simulation. Unity transforms display its results. Rigidbody may serve other purposes later, but it does not determine pitch positions or velocities.

Data flow: input/configuration → gameplay command → fixed-step simulation → state/events → presentation. Rendering may interpolate state without feeding its frame timing back into simulation outcomes.

Start with few assembly definitions. Add a Simulation assembly when code exists so the dependency boundary can be enforced; add further assemblies only when they provide a useful compile-time boundary.

Test pure simulation behavior in EditMode with units, coordinate conventions, repeatability, and invariants. Use PlayMode tests for Unity integration and scene behavior. Tests should assert observable behavior rather than internal method shapes.

## Presentation (TASK-004.6)

`Pitchlab.Presentation` (`Assets/Game/Presentation`, UnityEngine only, no Simulation/Gameplay reference) holds reusable visuals:

- `PlayerMannequin` (+ `Prefabs/PlayerMannequin.prefab`): a dark primitive figure built in code — pelvis, spine, chest, head, two-segment arms and legs, hands, feet — with equipment anchors (right/left hand, bat grip, glove, held ball). Transform hierarchy only: no Rigidbody, no colliders. `LeftHanded` mirrors the figure, so every pose is authored once for a right-handed player.
- `MannequinPose` / `PoseSequence` / `MannequinPoses`: key poses (torso Euler angles, limb directions in the figure frame, optional two-handed grip) and smooth interpolation, allocation-free per frame. The library has pitcher, batter and a fielder `Ready` pose; TASK-005 adds running, reaching, catching and throwing poses to the same system.
- `Equipment` (bat with sweet-spot marker, glove, ball), `FieldDressing` (grass, infield, mound, foul lines, boxes, bases, distance arcs), `BaseballCamera` (batting view → hold → ball-follow), `ContactCue` (crack + flash), `LandingMarker`.

Scene glue lives with the sandbox (`HittingLabPresentation`): it reads `HittingLabController` state on the controller's clock and drives mannequins, the held ball, camera, cues and landing marker. It never writes gameplay state; poses are timed to authoritative release and contact times and the figures are slid (clamped) toward the simulated release/contact points — the simulation is never adjusted to the animation. The only gameplay-visible sequencing change: a throw press starts the delivery and the authoritative release follows `DeliveryLead` (1.1 s) later; presses during the delivery are early swings.
