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

Scene glue lives with the sandbox (`HittingLabPresentation`): it reads `HittingLabController` state on the controller's clock and drives mannequins, the held ball, camera, cues and landing marker. It never writes gameplay state; poses are timed to authoritative release and contact times and the figures are slid (clamped) toward the simulated release/contact points — the simulation is never adjusted to the animation. The only gameplay-visible sequencing change: a throw press starts the delivery and the authoritative release follows `DeliveryLead` (1.1 s) later. Swing presses during the wind-up are ignored (TASK-004.6B-1 policy B): contact happens about 0.4 s after release, so a swing before release could never connect.

## Hitting input and the PCI (TASK-004.6B-1)

There is one conversion chain: device input → normalized PCI → contact-plane metres → world.

- **Normalized PCI.** `PciTrack` stores (u, v) ∈ [−1, 1]² as an exact function of time, built from timestamped velocities (keys and stick) and timestamped displacements (mouse).
- **Contact-plane metres.** `PciFrame` (Gameplay) maps (u, v) to metres: centre (0, 0.8), half extents 0.6 × 0.6. The axes:
  - +u is +X: toward first base, the catcher's-view right;
  - +v is up.

  `SwingInput(startTime, pciX, pciZ)` carries metres into `ContactResolver`.
- **World.** `SimulationSpace.ToUnity` converts for presentation only.
- **Mouse.** The primary input:
  - Each mouse state event's delta × `MouseSensitivity` (normalized units per count, default 0.004) moves the PCI at that event's own timestamp. The Input System's `onEvent` reads every event, not once per frame.
  - Mouse up is +v.
  - A left click throws or swings at `context.time`.
  - The first click captures and hides the cursor; Escape releases it.
  - `InputSystem.settings.disableRedundantEventsMerging` is set to true. Otherwise the Input System merges mouse reports that differ only in delta, and a click would take the timestamp of the next report.
- **Keyboard and stick** still set the velocity, converted exactly into normalized units.
- **Verification.** The PCI at a swing is reconstructed at the swing event's timestamp. `HittingInputFrameRateTests` replays the same keyboard trace, and the same 125 Hz mouse trace, at 30, 60 and 144 fps and with jittered frames. The swing, PCI, timing and batted ball must be identical across all of them.
