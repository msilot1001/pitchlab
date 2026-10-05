# TASK-008: Team defensive coordination

## Goal
All nine defenders act during a live ball. One primary fielder plays the ball, and the other eight take baseball roles: covering bases, backing up bases and fielders, cut-off, relay, trail, or holding. The roles are reassigned as the play develops. Throws go to a base someone actually covers, through a cut-off or relay when that is better, as real sequential throws on TASK-006A physics. There is no swarm.

## Owner
- **Writer:** Claude Code.
- **Read-only:** reviewers and Codex.
- **Branch:** `feat/task-008-team-defense`, from `main` decd640 (TASK-007 merged).

## Design
- **Defense moves into the live engine.** Instead of TASK-006B's single action decided at contact (`DefensivePlay`), `LivePlay` owns:
  - **per-fielder motion tracks** (`FielderTrack`): segments of `IFieldMotion`, either the TASK-005 route or a `ContinuationMotion`, so motion is continuous from any state;
  - **a ball track:** batted ball free → held → thrown → held… or free again after a missed throw, until a retriever fields it;
  - **holder decisions** at every possession (fielded, catch, retrieval): throw to a covered base, throw through a cut-off or relay, carry or touch, tag, hold. They are evaluated with the runners' real predicted arrivals (TASK-007);
  - **a generalised throw planner** from any holder to any receiver who is moving to cover a base or a cut-off point. It uses the hold-for-cover and on-bag rules (TASK-006A) and receiver continuity.
- **`DefensiveCoordinator`** (Docs/DEFENSIVE_RESPONSIBILITIES.md). It is a pure function of the play's state and assigns every defender a role and a target point:
  - Primary;
  - CoverBase(b);
  - BackupBase(b);
  - BackupFielder;
  - Cutoff(target);
  - Relay / Trail;
  - Hold.

  It runs at contact, possession, release, catch, a missed throw and an out. Defenders whose target moves get a new stop-on-target route (`ContinuationMotion.ToRest`) from their current position and velocity.
- **Presentation:** `DefenseView` reads a generic defense timeline: positions, takes and throws per fielder, the ball, and authority. It handles any number of throws. A role overlay is added.
- **Scope boundary with TASK-009:** that task adds the play-rule completeness (double-play pivot timing and failed double plays, live tag chases, tag-up throws, home runs and ground-rule doubles, the infield fly, event-log calls). The chain mechanism lives here.

## Milestones
- [x] Research doc `Docs/DEFENSIVE_RESPONSIBILITIES.md`.
- [x] Field-motion interface, fielder tracks, generalised throw planner.
- [x] Coordinator and dynamic defense in LivePlay (ball track, holder decisions, cut-off and relay, retrieval of missed throws, off-bag receiver return).
- [x] DefenseView on the timeline; role overlay; labs.
- [x] Tests (roles for the listed scenarios, no swarm, coverage before a throw, reassignment, cut-off, relay).
- [x] Reviews (rules, Unity, tests) and fixes.
- [ ] check.sh; Codex; merge.

## Verification
- EditMode 504/510 (6 skipped, pre-existing); TeamDefenseTests 16/16; BaserunningTests 18/18 (frame-schedule test now also
  compares the whole defense); PlayMode 46/46.
- Runtime (FieldingLab, Game View): wall ball with R1 — LF retrieves at the wall, SS relay / 2B trail line up, the role overlay
  lists every defender's role; console clean.
- Performance (Editor, 60 fps stepping, worst single frame / whole play): SS grounder 3 / 9 ms; R1 SS grounder 7 / 15 ms;
  bases loaded 3B grounder 15 / 35 ms (was 60 / 161 ms); CF single R2 2 out 16 / 31 ms (was 38 / 114 ms); gap / wall ball
  14 / 31 ms (was 28 / 78 ms). Achieved by: throws simulated only as long as they can be played (new optional horizon on
  BallInPlaySimulation.Run — same ball up to it), plans reused from the take to the commit, the waiting thrower's launch reused,
  and hopeless throws (a straight-line lower bound beyond the close-play window) not planned.

## Reviews (fixed)
- Rules: a throw's receiver keeps his base (no second man sent there); backups no longer stacked (P backs up home only on
  infield balls; one far outfielder); play at home on a deep ball has the 1B as home cut-off; force outs prefer the lead
  runner; with the 1B the cut-off, 2B covers first and SS second (and keeps it when the 1B has the ball); ball to P → SS
  covers second; coverage stays an outfield play while the relay man has the ball; doc corrected (92 m relay depth, choice
  of play, no 55 m rule; dead `LongThrow` removed).
- Unity: catcher shown only when he plays the ball or leaves the plate area (TASK-005 behaviour restored); per-frame LINQ
  closure removed; alignment cached; double planning removed via the plan cache. Deferred (low): pitcher-takeover root
  sampled from the last frame (presentation only), per-frame banner string building.
- Tests: tautological checks replaced (physical coverage at the catch, sequential throws with ≥ 2 throws, primary sampled
  over the whole play, a stronger no-swarm metric, real role changes); added 3B grounder, bases loaded (5-2, then the
  catcher goes for two), throw home with the cut-off cutting it to get the batter-runner at second, wall ball relay.
## Decisions / discoveries
- Decisions are committed at take + transfer − arm action (not at contact): an outfielder decides with the runners as they are.
- After an out the holder decides again (`OnOut`) — this is what makes a second out possible; double-play depth and a
  quick pivot transfer are TASK-009.
- Throws to the cut-off/relay man aim at where he is at release (no hold); throws to a base wait for the cover (hold-for-cover).
- A late throw (> 0.5 s behind the runner) goes to the cut-off instead; e.g. R2, 2 out, CF single: home is 0.66 s late, the
  run scores.
- Relay replaces the cut-off when the ball is fielded deeper than 92 m; first-to-third happens on deep/corner balls only
  (shallow singles are fielded in ~2.5 s and the runner holds at second, which is realistic).
- Presentation bug found by the PlayMode glove test: the fielder faced his later throw target before the catch; fixed
  (face the target only while holding or following through).

## Remaining risks
- LivePlay construction ~30–110 ms (bases loaded) at contact; acceptable for the slice, watch in TASK-009.
- No double plays yet (pivot transfer 0.70 s, standard depth) — TASK-009.
- No throwing errors are modelled: an off-bag catch (re-decision → carry) and a missed throw (retrieval) are handled but
  never happen (0 of 880 scanned plays), so they are untested.
- Cut-off men mostly hold: 1 in 288 outfield plays had a relay throw on (realistic for no-play singles, but few chains).
