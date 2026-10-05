# Fielding (TASK-005)

The defense responds to the **authoritative** ball in play: `BallInPlay`, from TASK-004.7, holding flight, hops, slides, rolls, wall caroms and rest. No second trajectory exists; the ball the defenders chase is the ball on screen.

Evidence labels: MEASURED (a primary source read), REPORTED (secondary), DERIVED (our arithmetic), ASSUMED (a modelling choice).

## Pipeline
Contact produces a `BallInPlay`, then `FieldingSolver.Solve` builds a `FieldingPlay`:
- each of the nine defenders' earliest intercept (`InterceptSolver`);
- the primary defender;
- his route (`FielderMotion`), with the others holding;
- possession;
- the fielder-aware fair/foul call.

Everything is a pure function of the play's time, so it is identical at any frame rate. Presentation (`DefenseView`, `FieldingPoser`) only reads it.

## Positions (`DefensiveAlignment.Standard`)
Positions are midpoints of the Statcast league-average positioning zones against right-handed batters (REPORTED, MLB glossary "fielding alignments"). Angles are Statcast's: −45° is the third-base line, +45° the first-base line.

| Pos | Depth (ft) | Angle |
|---|---|---|
| 1B | 110 | +32° |
| 2B | 148 | +12° |
| SS | 148 | −15° |
| 3B | 110 | −28° |
| LF | 295 | −27° |
| CF | 322 | 0° (league average 322 ft, REPORTED) |
| RF | 295 | +27° |
| P | 55 (follow-through) | 0° (ASSUMED) |
| C | 2.5 ft behind the plate | (ASSUMED) |

These are generic positions, not a specific park.

## Defender profile (`FielderProfile`)

| Parameter | Value | Basis |
|---|---|---|
| Top speed | 27 ft/s; CF/SS 28; 1B/C 25; P 26 | MLB average sprint speed 27 ft/s, poor 23, elite 30 (Statcast, MEASURED). The per-position split is ASSUMED. |
| Acceleration | v(t) = v_max(1 − e^(−t/τ)), τ = 0.78 s | The exponential sprint model (MEASURED method). τ is fitted to the Statcast 90-ft splits: average 4.02 s, which is about 90/v_max + τ for the 27.8 ft/s runners of those splits (DERIVED). For the 27 ft/s baseline the same law gives 4.11 s. Nobody starts at top speed: initial acceleration is v_max/τ ≈ 10.5 m/s². |
| Braking | 6 m/s² | Team-sport maximal deceleration averages 4.2–5.0 m/s², with peaks above 10 (REPORTED). The cap is ASSUMED. |
| Reaction after contact | OF 0.40 s, IF 0.25 s, P 0.45 s, C 0.50 s | Statcast Jump (feet covered in the first 3 s after pitch release) implies about 0.3–0.55 s for outfielders (DERIVED); Cain's first step was 0.37 s (REPORTED). Infield, P and C values are ASSUMED (P follows through; C rises from the crouch). |
| Catch height | ≤ 2.6 m (ball centre) | Standing reach ≈ 1.33 × height (REPORTED) for 1.87 m (ASSUMED), plus the glove pocket (DERIVED). No jumping in gameplay yet. |
| Reach | 1.0 m from waist height (0.9 m) to 2.0 m; 0.6 m for a ball on the ground (linear between 0.4 and 0.9 m); shrinking to 0.3 m at the 2.6 m catch height | ASSUMED: arm span ≈ height. The 0.6 m comes from arm length from a deep crouch. Fully stretched upward the glove is near the body's axis. |
| Pickup height | ≤ 0.30 m after a bounce | ASSUMED. It is classification only: a ground pickup vs a hop catch. |

**Sanity check** against Statcast catch probability (DERIVED):
- A 3.9 s opportunity, less about 0.43 s of pitch flight and a 0.4 s reaction, leaves about 62 ft of running. That is short of the 71 ft of a 7 % catch, so it is out of reach.
- A 4.3 s opportunity gives about 73 ft, enough for the 65 ft of a 75 % catch.

## Locomotion (`RunningLaw`, `FielderMotion`)
- **Route:** a straight line from the start to the target (a baseball field needs no path finding; no NavMesh, no Rigidbody).
- **Settling:** with time to spare (run, brake and stop before the ball arrives), the defender stops on the spot and waits.
- **Tight plays:**
  - Otherwise he arrives at speed exactly at the intercept and brakes past it after the take.
  - Any spare time shorter than braking would need is absorbed by a later start.
- **Reach time:** reaction plus the fastest run (`RunningLaw.RunThroughTime`). The prediction and the motion are the same equations: a test checks predicted arrival equals performed arrival to 1e-12 s.

## Intercept (`InterceptSolver`)
- **Feasibility:** for a candidate ball time t, the authoritative ball state is queried.
  - The ball centre must be at or below the catch height.
  - The defender must be able to stand within his reach (at that height) of the ball's ground point by t.
  - That spot must be inside the park, ≥ 0.3 m from the fence face.
- **Search:** the time line runs on a 5 ms grid; the first feasible transition is refined by 40 bisections.
  - **Known limit:** a feasible window shorter than one step can be missed. That needs a ball passing within about 0.5 cm of the edge of a defender's reach at 35 m/s; it is then taken later, usually by the next defender.
  - **End of search:** the search continues past the end of the play, because a ball at rest in the park is picked up where it lies.
- **Comfort height:** a falling fly ball with time to spare is taken once it has come down to 1.8 m.
- **Kinds:**
  - **FlyCatch:** before the ball touches ground or wall.
  - **HopCatch:** in the air after a bounce or carom.
  - **GroundPickup:** low (≤ 0.3 m) or at rest.
- **Wall caroms** need no special logic: the trajectory contains them.

## Primary defender and the rest
- **Primary:** the earliest intercept wins.
  - Among candidates within 0.10 s of the earliest (two passes, so pairwise ties cannot chain past the window), the larger arrival margin wins.
  - Then positional priority: CF > LF/RF > SS > 2B > 3B > 1B > P > C.
- **Everyone else** holds in a ready stance. There is no backup positioning yet, and no swarm.

## Possession and play semantics
- **Possession:** `FieldingPlay.AuthorityAt(t)` is FreeBall until the intercept and Possessed afterwards.
  - The ball moves with the holder. Over the 0.3 s secure time it goes from where it was taken to his chest (1.2 m), with no jump.
  - The shown ball is exactly the gameplay ball at the take, then settles into the glove over the secure time. The glove follows the same path into the possession hold.
- **Dead and out of play:**
  - A fly ball that comes down foul is dead and nobody fields it. Foul catches are not modelled yet (simplification).
  - Until the call is decided, only takes over fair ground count. A defender never kills a would-be-fair ball by touching it foul, and a ground ball that would end foul becomes fair if taken over fair ground.
  - A play nobody fields ends at its decisive event: the foul call for a dead foul, or the ball leaving the park.
  - Out of the park, nobody chases the ball once it has cleared the fence. A ball taken before it leaves (a catch at the wall) is fair and in play.
- **Fair/foul with a defender:** a ball fielded before the call's decisive moment is judged where it was taken.
- **Batting loop:** the play ends at possession (`FieldingPlay.EndTime`).

## Presentation
- **`FieldingPoser`** poses the shared `PlayerMannequin`:
  - an athletic ready stance;
  - a run cycle phased by the distance actually run (the stance foot moves back as fast as the root moves forward, so no foot skating);
  - the glove (left hand) moving to the authoritative intercept over its last 0.45 s, with a deep crouch for low balls and a step-and-lean or jump for wide or high ones;
  - a two-handed possession hold.
- **Root:** gameplay places the root (no root motion).
- **Glove accuracy:** at the intercept the glove is within 0.10 m of a ground ball and 0.20 m of a caught ball (PlayMode test, every FieldingLab preset).
- **Camera:** after contact `BaseballCamera` frames the ball and the primary defender.
- **Debug view (T):** the route, the intercept marker and its numbers.
- **HittingLab:**
  - The sandbox pitcher becomes the P defender only when he plays the ball. Root, heading and pose blend over 0.25 s from where he stood, and the next pitch blends from the pose shown.
  - The catcher, who would block the batting camera, appears only when he plays the ball.
  - Figures stand on the mound's surface.
- **Known presentation limits:**
  - When starting from rest or braking, the stance foot slides at about (1 − run) of the speed. The run cycle is skate-free only at running speed.
  - In the HittingLab the camera frames the fielder one frame late (its LateUpdate runs first).
- **FieldingLab** (`Assets/Scenes/FieldingLab.unity`): ten presets on the production pipeline, at 1× and 0.5×.
