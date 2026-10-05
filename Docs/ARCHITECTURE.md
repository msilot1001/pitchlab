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

## Batting loop, fair/foul and feedback (TASK-004.6B-2)

### Batting state machine
`BattingStateMachine` (Gameplay) defines the loop: Ready → Windup → PitchInFlight → Swinging → BallInPlay (or a miss or a take) → Result → Ready, after a 1.5 s `ResultPause`.
- **Pure function:** the state is computed from the authoritative pitch, swing, contact result and ball in play at a simulation time. It is therefore identical at any frame rate, and nothing needs per-frame bookkeeping.
- **The press** (click, Space or gamepad South) acts by state:

  | State | What a press does |
  |---|---|
  | Ready, Result, BallInPlay | Throws the next pitch. During a play, this skips the rest of it, except within `DoublePressGrace` (0.3 s) after contact: a double click doesn't throw the hit away. |
  | Windup, Swinging | Ignored: no swing before release, and no double swing. |
  | PitchInFlight | Swings at the event timestamp, once per pitch. A second press stamped earlier (another device's event handled later) is ignored. |

- **Clean reset:** the next throw resets the presentation (`ResetForPitch`). When the state reaches Ready, the camera returns to the batting view.

### PCI persistence
The PCI keeps its position between pitches. With a mouse it is where the hand left it, which matches TASK-003 keyboard behaviour. A reset to the zone middle would fight the player's hand.

### Fair/foul
`FairFoul.Call(BallInPlay)` (Gameplay) applies the Official Baseball Rules definitions of a fair and a foul ball, for a field with no fielders:
- **Over the fence on the fly:** judged where the ball leaves the field. Fair means a home run, which includes off the pole.
- **Bounce over the fence:** a fair ball that bounces over the fence is judged by its landing, so it is a ground-rule double, not a home run.
- **Off the wall on the fly:** fair. The wall stands only over fair territory and the line.
- **Beyond first or third base:** judged where it first lands. "Past first or third base" means past the line through first and third base (90 ft·√½ toward centre). That line meets each foul line at its bag and also covers balls hit up the middle.
- **Before the bases:** judged where it passes first or third base, or where it settles.
- **The lines and poles are fair territory.** The ball is judged by its own position: it is fair if any part of it is over the line (its centre within one radius outside the line).

Each call records its basis and the decisive state. `FairFoulTests` covers synthetic plays on and near the lines, plus calls on simulated plays.

### Feedback
- **Contact feedback:** `ContactFeedback` (Gameplay) turns timing error into words ("on time" inside the resolver's own Good window of ±7 ms, otherwise "early/late N ms"). It turns the authoritative barrel offset into a contact quality ("sweet spot" within 1.5 in, "off the end" toward the tip, "jammed" toward the hands).
- **Normal UI:** one compact line, which is a pure function of the simulation time:
  1. timing and quality at contact;
  2. FAIR / FOUL / HOME RUN at the decisive moment;
  3. the carry at the first bounce;
  4. the final distance at rest.
  - Misses show "Swing and miss · early 12 ms"; takes show "Take"; Ready shows "Click to pitch".
  - A fair bounce over the fence adds "ground-rule double".
  - A late miss reads only after the swing ends.
  - A one-line hint (mouse, H, T) sits at the top left.
- **Debug UI:**
  - H opens the details panel (pitch, readout and the fair/foul basis).
  - T turns on the debug view:
    - paths, the contact marker and the PCI range;
    - the PCI conversion chain;
    - the visual target and error;
    - the sweet-spot trail;
    - event markers at every bounce, wall impact, slide → roll and rest.
- **Sound:** none beyond the existing contact crack (optional, not added).

## Fielding (TASK-005)

`Gameplay/Fielding` covers positions, profiles, the running law, the intercept solver and `FieldingPlay` / `FieldingSolver` (see Docs/FIELDING.md).
- **Input:** the authoritative `BallInPlay` only.
- **Output:** a play that is a pure function of time: every defender's motion, the primary's intercept, possession, and the fielder-aware call.
- **Presentation:** the Sandbox `DefenseView` and the Presentation `FieldingPoser` read it and never write it.
- **HittingLab:** the controller solves the defense at contact, then plans the default throw. The ball follows `DefensivePlay.BallPositionAt`, and the batting loop's play ends at `DefensivePlay.EndTime`.

## Throwing (TASK-006A)
`DefensivePlay` (Gameplay) is the fielding play plus an optional `ThrowPlay`:
- **Base positions:** they come from `FieldLayout.BasePosition` (Simulation).
- **Flight:** the throw is a `BallInPlay`, the same simulator as the hit.
- **Authority:** an explicit authority timeline: FreeBall, Possessed or Thrown.
- **Presentation:** it reads the play and never writes it.
