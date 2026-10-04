# TASK-004.6B-0 — MLB reference motion reconstruction

## Goal
Replace the improvised 6A batting/pitching poses with reference-based motion: a right-handed swing (Mookie Betts 2024 as motion reference) and a right-handed power-pitcher delivery (Gerrit Cole 2024), generic and unnamed in game. Presentation only.

## Owner
Claude Code (sole writer); physics-researcher ×2, unity-reviewer, physics-reviewer read-only. Branch feat/task-0046-visual-playability from 79a65ba.

## Milestones
1. [x] Research (Savant bat tracking / arm angle / release; Fortenbaugh 2011; Fleisig/ASMI; Commons stills) → Docs/MOTION_REFERENCE.md
2. [x] Pose representation: hand/foot IK targets with hints, foot yaw/pitch, Catmull-Rom; MotionClip + MotionTimeline; standard proportions
3. [x] Mound mesh, rubber on the mound
4. [x] Reference swing (bat path constrained by Statcast metrics) and delivery (fit to the authoritative release)
5. [x] Bat-path measurement utility, tolerances set before tuning, tests for invariants
6. [x] Glue integration (release fit, swing time mapping, grip shift), Game View at 1× and 0.5×
7. [x] Reviews (unity-reviewer, physics-reviewer: confirmed findings fixed — bat-head swing length, release fit without moving feet, walk-back/stepping transitions, anchor validation, ankle height, toe clearance, finish overshoot, planted toe yaw), check.sh, commit

## Decisions
- No video analysis was possible (tools could not open MLB video); visual estimates come from CC-licensed Commons stills of other seasons and text. Labelled in the doc.
- Bat path from launch to just after contact is derived from the five Statcast swing metrics (arc), not hand-placed; first hand-authored attempt measured 52 mph / 8.2 ft / +12° / 11° pull / 53° tilt.
- Tolerances (fixed before final tuning): bat speed ±10 %, swing length ±15 %, attack angle ±4°, attack direction ±6°, tilt ±6°.
- Release fit: the release wrist target is corrected (4 passes) so the ball meets the release point; feet and root never move (miss ≤ 0.2 cm on all presets).
- Contact/aim alignment moves the hands (≤ 0.25 m), never the batter's root.
- Windup start simplified to the post-pivot balance position.

## Verification
See final report; ReferenceMotionTests (8), PlayMode release-fit test, Game View captures.

## Remaining risks
- Visual estimates are low-medium confidence (no video).
- Release vertical fit clamp leaves a few cm residual for presets whose release height differs much from the reference.
