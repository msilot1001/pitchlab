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
- [x] Reviews (Unity, test) and Codex B fixed:
  - the half-inning test asserts that every pitch is a CPU call batted by the CPU, and a ball in play;
  - the HUD notes a pending batter change;
  - the gamepad's Select cycles the mode;
  - no per-frame label allocations;
  - matchup guards with margins, plus a defense guard; caps on the known discrepancies;
  - a full F-cycle and behavioural mode test;
  - doc wording.
- [x] Codex round 2: B — fixed: a pending mode change (pitcher or batter) is noted on the HUD. check.sh green (EditMode
  699 passed, 6 skipped; PlayMode 103/103). Merged.
