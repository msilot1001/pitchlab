# TASK-020: CPU pitcher strategy

## Goal
Replace the basic automatic pitcher with a first-pass CPU pitcher. It makes seeded, weighted, explainable choices of pitch
type and target from the count, the matchup, the batter's broad profile and the previous pitches. It never sees the
batter's input or its own execution error.

## Owner
Claude Code (sole writer); Unity and test reviewers and Codex read-only.

## Current state (inspected)
- `AutoPitcher` (TASK-014/018): a hash-seeded zone share by count over 13 fixed targets, plus the repertoire by usage with
  a fastball bias. It has no matchup, no batter awareness and no sequencing.

## Design (Docs/PITCHER_AI.md)
- `CpuPitcher.Choose(game)` → `PitchDecision`:
  - type: usage × count × matchup × repeat;
  - intent: heart / edge / chase / waste by count, adjusted by the batter's power and discipline and by a runner on third;
  - spot by type relative to the batter and the pitcher's arm side;
  - a reason string.
- `GamePitches.Create(…, targetX, targetZ, …)` aims at a point. The simulator and the lab's auto pitching use the CPU
  pitcher for rated pitchers; `AutoPitcher` stays for unrated teams. The GameLab panel shows the intent, reason and
  execution miss.

## Milestones
- [x] CpuPitcher, point targets, simulator and lab integration, debug line.
- [x] Tests: determinism, repertoire and usage, count, batter profile, matchup, runner on third, no repetition, executed
  zone rate by count. Seeds re-pinned.
- [ ] check.sh; runtime walkthrough; reviews (Unity, test); Codex; merge.
