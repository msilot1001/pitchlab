# Setup and verification

The Unity project lives at the repository root (`Assets`, `Packages`, `ProjectSettings`) and requires Editor **6000.3.25f1** (3D URP template). Tooling: the `unity` CLI, `jq`, and Git LFS.

On a new machine:

1. Install Editor 6000.3.25f1 (`unity editors --installed` to confirm) and open the project with `unity open .` or Unity Hub.
2. `com.unity.pipeline` is already in the manifest. Sign in with `unity auth login` if needed; `unity status` should list this project as `ready`.
3. `.mcp.json` (Claude Code) and `.codex/config.toml` (Codex) start `unity mcp --project-path /Users/sojak/pitchlab`. On another path or worktree, update both. Claude Code asks for project MCP approval on first use; Codex must trust the repository.
4. Run `Scripts/check.sh`.

`Scripts/check.sh` runs Git whitespace checks (`Scripts/format.sh`; no C# formatter yet) and `Scripts/test.sh EditMode` / `PlayMode`. When an Editor has this project open, `test.sh` recompiles and runs tests inside it through the Pipeline (PlayMode tests briefly enter Play Mode there); otherwise it runs Unity in batch mode. Either way it fails on compile errors or failed tests and prints failing test names.

Packages are the URP template defaults plus Unity Pipeline. Add Cinemachine and Unity Mathematics only when code needs them. Large binary sources matching `.gitattributes` go through Git LFS.
