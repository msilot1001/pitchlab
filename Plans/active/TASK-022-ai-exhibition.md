# TASK-022: Integrated AI exhibition

## Goal
Selectable GameLab modes:
- human batting vs the CPU pitcher;
- human pitching vs the CPU batter;
- CPU vs CPU on the production pipeline.

Plus a statistical sanity sample, archetype matchups, a runtime walkthrough, and a slower production-path integration test.

## Owner
Claude Code (sole writer); Unity and test reviewers and Codex read-only.

## Current state (inspected)
- `AutoPitch` (CPU pitcher, TASK-020) and `CpuBatting` (TASK-019) are independent toggles.
- `GameSimulator` already runs the production chain headless.

## Design (Docs/AI_EXHIBITION.md)
- `HittingLabController.LabMode` (`HumanBatting`, `HumanPitching`, `CpuVsCpu`, `Manual`) and `Mode`, which sets both
  toggles. F cycles it; the editor has buttons; the HUD has a label.
- `GenericRosters.Uniform` builds archetype teams. `ExhibitionSanityTests` holds the sample and matchup harness
  (`Report`), with directional guards.
- PlayMode tests: the modes, and a CPU-vs-CPU half inning in the GameLab.

## Milestones
- [x] Modes, uniform teams, sanity harness, tests.
- [x] Sanity sample, matchups, performance, runtime walkthrough (Docs/AI_EXHIBITION.md).
- [ ] check.sh; reviews (Unity, test); Codex; merge; final regression.
