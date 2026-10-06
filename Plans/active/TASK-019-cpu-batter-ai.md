# TASK-019: CPU batter AI

## Goal
A production CPU hitter that perceives the pitch only up to the present (with noise and delay), decides take or swing from
the count and his discipline, and swings through the same timestamped PCI and swing input and the same `ContactResolver`
as a human. Power is bat speed.

## Owner
Claude Code (sole writer); physics, Unity and test reviewers and Codex read-only.

## Current state (inspected)
- `GameSimulator`'s stand-in batter read the completed flight: `StrikeZone.Crossing`, `IdealContactTime` and
  `IdealContactState` (future information), and swung from them with small errors.
- Human input: `PciTrack` (timestamped PCI) plus a swing press; the swing reads the PCI at the press
  (`SwingAtSimTime`). `SwingParameters` already carries the hitter-specific fields ("ratings map onto these").

## Design (Docs/BATTER_AI.md)
- `CpuBatter.Plan(ballAt, batter, count, swing, contactPlaneY, ref stream)` → `BatterPlan`:
  - 10-ms looks with angular noise (Vision);
  - a weighted constant-acceleration fit with a gravity+drag prior;
  - a delay (Vision);
  - the decision at commit (logistic strike belief, MLB 2024 by-count swing rates, Discipline-scaled chase with a distance
    falloff);
  - aim events (prediction + per-swing aim error, clamped to the PCI area);
  - the press (predicted time + timing error).
- `SwingParameters.For(batter)`: side and bat speed 72 + 5 r̂(Power) mph.
- `GameSimulator` uses it, replacing the stand-in. GameLab: `CpuBatting` (B) applies the events at their timestamps to
  `PciTrack` and the swing path, and ignores the player's aim and swing. Game pitches use the batter's bat speed for humans
  too.

## Milestones
- [x] CpuBatter, bat speed, simulator, lab integration.
- [x] EditMode tests: determinism, future-blind, same-input, timing/aim error vs Contact, discipline/chase, count, contact
  and power ordering, archetype regression guard. PlayMode: frame-rate independence (CPU vs CPU in the lab), the player
  cannot swing for him.
- [ ] Re-pin simulator seeds; check.sh; runtime walkthrough; reviews (physics, Unity, test); Codex; merge.

## Known discrepancies
O-Contact is too high (≈ 70–80 % vs 56 %). Simulated offence is too high (also the basic pitcher, TASK-020). See
Docs/BATTER_AI.md.
