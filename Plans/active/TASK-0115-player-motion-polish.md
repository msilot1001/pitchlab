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
  diving — extra horizontal reach 1.0 m, ball 0.15–1.2 m high, fielder at ≥ 70 % of top speed at the take — with a
  recovery cost (+0.8 s before the throw can be ready). Before/after catch rates measured on a batted-ball grid; tests.
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
- [ ] 011.5 shared motion (MotionTrack, gait, facing, lean, planting, ground).
- [ ] 011.6 FieldingAction + dive model + defensive presentation; Docs/FIELDING_MOTION_REFERENCE.md.
- [ ] 011.7 runner presentation + trot; Docs/BASERUNNING_MOTION_REFERENCE.md.
- [ ] 011.8 integration tests, overlay, slow motion, runtime scenarios.
- [ ] Reviews (unity, test, rules/research), check.sh, Codex, merge.

## Verification
(filled in as it runs)

## Decisions / discoveries
(filled in as it runs)
