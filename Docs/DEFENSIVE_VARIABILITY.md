# Defensive variability and misplays

Defenders no longer succeed whenever the geometry says a play is possible (TASK-021). A take or a throw is executed from
its difficulty, the defender's skill and a seeded draw. Every failure is physical:
- a missed ball carries on along its own path;
- a knocked-loose ball leaves the glove as a new free ball;
- an off-target throw flies on the throw physics.

The ball stays live, the next defender fields it, and the rules read what happens. There is no coin flip that marks a
runner safe or teleports the ball. Code: `FieldingExecution` (Assets/Game/Gameplay/Fielding), `LiveDefense.Attempt` /
`Misplayed` / `LooseBall` / `Executed`, `LivePlay.OnMisplay`, `ThrowPlanner.PlanLive(…, aimError)`.

Labels: MEASURED / DERIVED / ASSUMED / TUNED (see Docs/PLAYER_RATINGS.md).

## When it applies
A `LivePlay` built with an execution seed (`executionSeed`) uses it:
- `GameSimulator` seeds every play from the game: `GameState.PlaySeed`, from the game seed, plate appearance and pitch.
- The GameLab seeds plays when `ExecutionVariance` is on (the same toggle as pitch execution).

Without a seed the defense is the validated deterministic defense of TASK-005–008, which the scenario tests and labs use.

## Takes (catches, pickups, throws received)
- **What a defender can know:** the chance of failure uses only what the play knows at the take:
  - the kind of take (`FieldingAction`);
  - the time to spare (arrival margin);
  - his speed;
  - the ball's speed relative to him;
  - for a throw, how far he had to move for it.
- **How the chance is built:**
  - base chance by kind;
  - × (1 + (v − 20 m/s)/15) for a ground ball faster than 20 m/s;
  - × (1 + v/20) for a catch on the run;
  - × 1.5 when rushed (< 0.15 s to spare);
  - × (1 + 2·moved m), at most × 5, for a throw received on the move;
  - × e^(−r̂) by Fielding (ground balls) or Catching (everything else);
  - capped at 0.9.

  All factors are ASSUMED and TUNED.
- **Base chances** for an average fielder, routine conditions:

  | Kind | Chance | Kind | Chance |
  |---|---|---|---|
  | standing catch | 0.10 % | centered pickup | 0.20 % |
  | running catch | 0.20 % | forehand pickup | 0.6 % |
  | over-shoulder catch | 1.5 % | backhand pickup | 1.0 % |
  | sliding catch | 2.5 % | charging pickup | 1.25 % |
  | jumping catch | 5 % | short-hop pickup | 2 % |
  | diving catch | 40 % | hop catch | 0.75 % |
  | wall play | 2 % | receiving a throw | 0.15 % |

  These are the listed values × 0.5 (`RoutineScale`), except dives. Routine plays stay nearly certain; dives at the edge of
  the envelope fail often. Statcast's catch-probability stars put a 5★ play at 0–25 % caught (MEASURED definitions).
- **What happens on a failure:**
  - **Miss** (all failed dives, a third of failed ground balls, half of other failed catches): the ball carries on along
    its own path. A missed throw is followed over its whole remaining flight.
    - The defender recovers before he can go after it again: 0.5 s, or a failed dive's 1.0 s, the same as getting up from
      a successful one.
    - A missed fly is still catchable until it touches the ground: a backup who holds it in the air makes the out (OBR).
  - **Bobble / drop:** the ball leaves the glove as a new free ball from where it was taken:
    - a ground ball rebounds at a quarter of its speed, at most 4 m/s, back toward where it came from, turned up to ±60°,
      with 1 m/s lift;
    - a ball in the air drops out at about 1 m/s in a random direction.

    Everyone else reacts 0.2 s later. The bobbler needs 0.4 s to find it and set himself again. A dropped ball is played
    once it has touched the ground, so there are no juggling catches. The re-pickup costs real time.
  - **A failed attempt is shown:** it is recorded as a take that was not held (`BallTake.Held`), so the reach, dive or
    glove is shown while the ball stays free.
- **Who fields the loose ball:** the defender who can reach it first, from where he is and as he is moving. This is the
  same solver as for a missed throw (`LiveDefense.LooseBall`). His take is executed again, so a second misplay is possible.
- **Kind and runners:** a dropped fly stops being a catch at the drop; a missed fly stops when it touches the ground
  (`LivePlay.Kind` becomes a hit ball). Then there is no fly out. Every runner reconsiders at the misplay and again when
  the ball lands; nobody knew before. Runners judge a throw in the air as it was aimed, not by whether it will be held.
- **Out of the field:** a misplayed ball that nobody can reach before it leaves the field ends the play there (no award is
  modelled).

## Throws
- **The decision:** the holder chooses his play with throws as aimed (expected ability, TASK-008 decision).
- **The throw as made:** it gets a direction error, per axis σ = 0.6° · (1 − 0.35 r̂(ArmAccuracy)):
  - × 1.2 at full effort;
  - 3 % of throws have 4 × the spread.

  The error is applied at the receiver's chest over the target. The flight is the existing throw physics (`ThrowSolver`,
  `BallInPlaySimulation`), never snapped to a glove. ASSUMED and TUNED.
- **Arm strength:** ArmStrength sets the throw's speed (TASK-017 mapping) in the same solver.
- **The receiver** reacts to the real throw only after the release (`AdjustReaction` 0.1 s). On a base he may:
  - take it on the bag, stretching 0.6 m beyond his reach with a foot on the bag (`StretchReach`, ASSUMED: a first
    baseman's stretch reaches ≈ 1.0–1.2 m — Docs/RULES.md). This applies only to a throw off its aim, so a throw as
    aimed, and the whole defense without a seed, is planned exactly as before;
  - step off to catch it;
  - catch it on a hop;
  - miss it. A missed throw is a live free ball, retrieved by whoever gets there first while the runners run.

  Force and tag outs follow the actual geometry: a catch off the bag does not complete a force.

## Misplays (terminology)
`LivePlay.Misplays`: each has a time, a fielder, a kind (`FieldingMisplay`, `ThrowingMisplay`) and a description:
- "bobble (backhand pickup)";
- "dive missed (diving catch)";
- "throw pulled 1B off the bag".

A throwing misplay is either of these, when the throw as aimed would have been held on the bag:
- on a force play, a throw that makes the receiver step more than 0.6 m off the bag;
- a throw nobody holds.

These are descriptive MISPLAYs, not official E-numbers: there is no scoring model. A failed dive is a misplay here, not
necessarily an error.

## Population (regression check, not validation)
30 simulated games (seeds 301–330; CPU pitchers and batters):

| | Fielding misplays | Throwing misplays |
|---|---|---|
| Per team-game | 0.27 | 0.12 |

MLB 2025 fielding is ≈ 0.50 errors per team-game, about half fielding and half throwing (DERIVED, TASK-017 research).

Throwing misplays are below MLB's ≈ 0.25: most off-target throws are stretched for or reached. This is not tuned further.

The FieldingLab can replay its presets with execution on (E toggles it, R re-rolls the seed, debug crew skill 50).

Tests (`DefensiveVariabilityTests`) cover:
- routine reliability and the difficulty ordering;
- no change without a seed; same-seed determinism;
- a dive can fail, and the missed ball stays live with no fly out;
- a bobble costs time and is continuous;
- a missed grounder carries on;
- a bad throw flies physically, and the receiver reacts after the release;
- one holder at a time;
- elite < average < poor crews on the same grid;
- ArmAccuracy narrows throws and makes fewer throwing misplays in whole plays;
- Fielding vs Catching apply to the right takes;
- routine whole plays fail < 1 %, dives > 25 %;
- nothing before the first error differs from the error-free play (no planning around future errors);
- a pulled-off-the-bag catch is no force out;
- simulated games label misplays and stay within a misplay-rate guard;
- a backup's air catch of a missed fly is an out. This one is inconclusive when the test grid contains no such play.

## Limitations
- No fumbled transfers (Transfer sets the time only).
- No juggling catches.
- Misplays near the foul lines keep the take's fair/foul judgement.
- No official error scoring.
- No collisions or communication errors.
