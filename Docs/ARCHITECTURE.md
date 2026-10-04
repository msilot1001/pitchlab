# Architecture

The intended direction is `Simulation ← Gameplay ← Presentation`. Simulation owns deterministic state transitions and physical calculations; it must not reference gameplay or rendering code. Gameplay turns input and rules into simulation commands and interprets results. Presentation observes state to drive camera, animation, audio, and UI. Presentation may read simulation data where useful.

Pitch flight will use a custom authoritative simulation. Unity transforms display its results. Rigidbody may serve other purposes later, but it does not determine pitch positions or velocities.

Data flow: input/configuration → gameplay command → fixed-step simulation → state/events → presentation. Rendering may interpolate state without feeding its frame timing back into simulation outcomes.

Start with few assembly definitions. Add a Simulation assembly when code exists so the dependency boundary can be enforced; add further assemblies only when they provide a useful compile-time boundary.

Test pure simulation behavior in EditMode with units, coordinate conventions, repeatability, and invariants. Use PlayMode tests for Unity integration and scene behavior. Tests should assert observable behavior rather than internal method shapes.

## Presentation (TASK-004.6)

`Pitchlab.Presentation` (`Assets/Game/Presentation`, UnityEngine only, no Simulation/Gameplay reference) holds reusable visuals:

- `PlayerMannequin` (+ `Prefabs/PlayerMannequin.prefab`): a dark primitive figure built in code — pelvis, spine, chest, head, two-segment arms and legs, hands, feet — with equipment anchors (right/left hand, bat grip, glove, held ball). Transform hierarchy only: no Rigidbody, no colliders. `LeftHanded` mirrors the figure, so every pose is authored once for a right-handed player.
- `MannequinPose` / `MotionClip` / `MotionTimeline`: key poses (torso Euler angles; limbs by direction or by IK target — hand/ankle position with elbow/knee hint, two-bone reach; feet yaw/pitch; two-handed grip) on normalized time with named markers, Catmull-Rom sampled, allocation-free per frame; a timeline pins markers to authoritative event times. `ReferenceMotions` holds the reference swing and delivery (Docs/MOTION_REFERENCE.md); `MannequinPoses.Ready` is the fielder base for TASK-005 (running, reaching, catching and throwing clips go in the same system). `SwingPathMetrics` measures a presentation bat path against Statcast definitions.
- `Equipment` (bat with sweet-spot marker, glove, ball), `FieldDressing` (grass, infield, a raised 10 in mound with slope, foul lines, boxes, bases, distance arcs), `BaseballCamera` (batting view → hold → ball-follow), `ContactCue` (crack + flash), `LandingMarker`.

Scene glue lives with the sandbox (`HittingLabPresentation`): it reads `HittingLabController` state on the controller's clock and drives mannequins, the held ball, camera, cues and landing marker. It never writes gameplay state; poses are timed to authoritative release and contact times and the figures are slid (clamped) toward the simulated release/contact points — the simulation is never adjusted to the animation. The only gameplay-visible sequencing change: a throw press starts the delivery and the authoritative release follows `DeliveryLead` (1.1 s) later; presses during the delivery are early swings.
