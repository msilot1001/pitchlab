# Pitchlab agent guide

Unity 6.3 LTS, C#, URP; desktop and gamepad first. This repository is being bootstrapped. Do not use another Editor version to generate project files.

Read the relevant source of truth: [design](Docs/GAME_DESIGN.md), [architecture](Docs/ARCHITECTURE.md), [physics](Docs/PHYSICS.md), and [agent workflow](Docs/AGENT_WORKFLOW.md). For substantial work, maintain an ExecPlan under `Plans/active/` using [.agent/PLANS.md](.agent/PLANS.md).

Invariants: Simulation never depends on Presentation. Rendering frame rate never determines simulation results. Unity Rigidbody is not authoritative for baseball flight. Pitch names never define trajectory curves. Inspect relevant code before making claims; change only requested systems; avoid hypothetical infrastructure. Test behavior, not implementation details.

One writer owns a feature. Other agents normally research, review, or verify. Never concurrently edit the same source files or mutate the same running Unity Editor. Run `Scripts/check.sh` before declaring Unity changes complete; report any checks blocked by missing tooling.
