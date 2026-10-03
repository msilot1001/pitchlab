# ADR 0001: Unity 6.3 LTS

**Decision:** Target Unity 6.3 LTS with URP, C#, desktop builds, and gamepad-first input.

**Reasoning:** A fixed LTS line supports reproducible project imports and verification while URP provides the intended 3D rendering baseline.

**Consequences:** Commit the exact Editor patch version in `ProjectSettings/ProjectVersion.txt` when the project is created. Do not generate or upgrade project files using another Unity version. Add only required packages.
