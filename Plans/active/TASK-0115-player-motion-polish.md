# TASK-011.5–011.8: Player motion polish

## Goal
Reduce the disconnect between authoritative gameplay and what the mannequins appear to do: shared locomotion
(cadence, direction, facing, braking, foot planting, ground contact), defensive actions (pickups, catches, transfers,
throws, pivots, receiving, tags, recovery), baserunning (stance, break, rounding, run-through, slides, tag-up, retreat,
home-run trot) and their integration (ball↔glove, runner↔base, tag alignment, off-ball motion, cameras, slow motion,
debug overlay). Gameplay stays authoritative; presentation follows it.

## Owner
Claude Code (sole writer); unity-reviewer, test-reviewer, rules/research reviewer and Codex read-only.

## Current state (inspected)
- `PlayerMannequin`: primitive figure, two-bone IK for hands/feet, poses in the figure frame; no root motion.
- `FieldingPoser.Compose(FieldingPoseInput)`: one run cycle phased by distance / fixed 3.6 m stride (jogs look like slow
  sprints), stance-foot no-skate, generic glove reach (low crouch, side lean, jump > 2.05 m), hold, generic throw.
- `DefenseView`: heading = slerp(face target, run direction, speed) — switches instantly when the face target changes
  (take, release); facing uses the shown ball transform; the throw stride follows a weight that rises and falls (the lead
  foot slides back during the follow-through); ankle height ignores the mound.
- `RunnerView`: reuses the fielding run cycle; heading from the path; no stance, no slides; standing runners rigid.
- Gameplay: `InterceptKind` {FlyCatch, HopCatch, GroundPickup}; runners already decelerate at 10 m/s² for stops on 2B/3B
  (the slide); home-run runners sprint (~15 s).

## Design
### Shared motion (Presentation)
- `MotionTrack` (per figure per play, deterministic): sampled on a fixed 1/120 s grid from contact, lazily up to the
  requested time — gait phase ∫ cadence(v) dt and a turn-rate-limited heading (≤ 720°/s, desired heading from
  authoritative state only). Pure function of play time: identical at any frame rate and in slow motion.
- Gait from speed: cadence f(v) = 1.8 + 0.28 v steps/s (walk ~2.2, jog ~2.6, sprint ~4.1), step = v / f, duty factor
  0.6 → 0.28; stance foot moves backward at exactly v along the *movement direction in the figure frame* (lateral
  shuffle / crossover / backpedal without skating). Lean from longitudinal acceleration (forward when accelerating, back
  and lower when braking) and lateral lean from turn rate × speed.
- Planted feet stay planted: the throw stride is monotone (step, plant, keep), idle feet do not rotate with the body.
- Foot ground height: ankle targets corrected by the mound height at each foot.

### Defensive actions (Gameplay classification + Presentation)
- `FieldingAction` (Gameplay, from authoritative geometry/timing — never from animation): StandingCatch, RunningCatch,
  SlidingCatch, DivingCatch, JumpingCatch, OverShoulderCatch, ForehandPickup, BackhandPickup, CenteredPickup,
  ChargingPickup (slow roller), ShortHopPickup, HopCatch (chest/high hop), WallPlay.
- Diving catch (the one gameplay change): when no fielder has a normal fly catch, a fielder may take the ball in the air by
  diving — extra horizontal reach 1.0 m, ball 0.15–1.2 m high, after a ≥ 6 m run-up — with a recovery cost (+1.0 s
  before the throw can be ready). Before/after catch rates measured on a batted-ball grid; tests.
- Presentation per action: approach → lower → glove presentation → take → gather → transfer (ball glove → hand) → throw
  (planted / on the run / outfield crow hop / quick relay / pivot) → recovery (slide/dive get-up). Base receivers:
  approach, plant on the bag, stretch toward the throw. Tags: glove sweeps to the runner at the authoritative tag time.
  Off-ball roles settle into a ready stance facing the live ball.

### Baserunning (Gameplay trot + Presentation)
- Lead stance between pitches (side-on toward home, weight shift during the delivery — no translation), break (turn and
  push off over the first steps), rounding lean from path curvature, run-through first (continue, brake, turn back),
  retreat braking, tag-up on the bag facing the next base.
- Slides: feet-first for a stop on 2B/3B (the gameplay slide decel phase) and close plays at home; head-first when
  returning to a base with a throw coming. Deterministic selection from play context; the slide covers the authoritative
  decel phase so base arrival is the authoritative time.
- Home-run trot (Gameplay): awarded runners run at a trot profile (≈ 5.2 m/s → ~21 s around the bases), bases in order.

### Integration
- Objective tests: glove–ball at takes/catches/receptions, ball continuity at transfer and release, runner–base at
  touches, tag glove–runner distance, planted-foot drift, slide arrival, run-through, trot ordering, finite poses,
  presentation never changes gameplay, determinism.
- Debug overlay (FieldingLab, M key): per figure gameplay state, action, presentation state, glove–ball error.
- Playback 1× / 0.5× / 0.25× in both labs.

## Milestones
- [x] 011.5 shared motion (MotionTrack, gait, facing, lean, planted stride, mound feet).
- [x] 011.6 FieldingAction + dive model + defensive presentation; Docs/FIELDING_MOTION_REFERENCE.md.
- [x] 011.7 runner presentation + trot; Docs/BASERUNNING_MOTION_REFERENCE.md.
- [x] 011.8 integration tests, overlay (M), slow motion (S: 1/0.5/0.25× FieldingLab; 1/2/3 HittingLab/GameLab), runtime inspection.
- [x] Reviews (unity, test, rules/research) and fixes; check.sh green.
- [ ] Codex, merge.

## Verification
- check.sh: EditMode 547/553 (6 skipped, pre-existing), PlayMode 66/66.
- Action distribution (final thresholds) on a 420-ball grid (60–110 mph × −10…50° × ±45° spray, 1500 rpm; 344 fielded):
  standing 98, centred 62, backhand 41, forehand 39, running 29, charging 29, over-shoulder 13, hop 12, wall 10, diving 8,
  sliding 3 (short hop and jumping catch occur in presets, not on this grid). Air-catch rate 34.0 % → 36.0 % with dives
  (2.3 % of fielded balls; test bounds dives < 5 % on its own grid).
- Objective alignment (tests): glove–ball ≤ 0.10 m on pickups, ≤ 0.20 m on catches/receptions for every preset incl. dive and
  slide; shown ball never moves more than 0.47 m in a 120 Hz tick; standing feet drift < 2 cm; sprint stance foot < 15 % of
  body speed; slide lead foot – bag < 0.45 m at the authoritative arrival; tag glove – runner < 0.5 m; safe foot – bag < 0.6 m;
  poses identical (< 1 mm) at 30 fps, 144 fps and in one jump; home-run trot 19.5–24.5 s.
- Runtime inspection (FieldingLab, frozen clock, close-up InspectionCamera): SS running pickup (lunge, two hands), backhand,
  charge, slide catch, dive (airborne at the catch, prone, up), jump at the wall, over-the-shoulder, 1B stretch, tag at 3B,
  6-4-3 pivot, crow hop and overhand throw, throw on the run, runner slide into 3B, slide at home, rounding 2B, head-first
  back, home-run trot, tag-up from 3B; GameLab tactical camera during a bases-loaded hit.

## Decisions / discoveries
- Presentation stays a pure function of play time (fixed-grid MotionTrack): slow motion and scrubbing are free.
- Running pickups at full speed (the gameplay run-through take) need a lunge with a waist bend, not a squat over a 2 m stride.
- The action geometry is frozen at the take (the gameplay fielder keeps moving afterwards).
- Fielders and runners standing on the same bag are shown on its opposite edges (0.3 m each, home 0.25 m) — documented
  presentation offsets, tested.
- Arm action blends from the hold over ≥ 0.15 s (the gameplay transfer can leave 0.05 s between secure and arm start).
- Dive: the only gameplay change on the fielding side — 1.0 m extra reach, 0.15–1.2 m ball, ≥ 6 m run-up, only when nobody
  can catch it normally, +1.0 s recovery before the throw. Home-run trot: the other gameplay change (awarded advances).

## Limitations
- Throws on the run stay overhand (the gameplay release point is fixed at 1.8 m; no sidearm without a gameplay change).
- Running pickups at 7 m/s slip the feet briefly in the lunge (the gameplay take does not slow the fielder).
- No dive misses (a deterministic envelope: inside it the catch is made).
- A tag right after a catch is shown with a 0.1 s sweep, so the glove can reach the runner up to 0.1 s after the gameplay tag.
- The catcher's crouch is shown only before the pitch; the return jog after a play runs on the real clock.
- Home slide: the figure slides while the gameplay runner keeps his speed to the plate (no gameplay slide at home).
