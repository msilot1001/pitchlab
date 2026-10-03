# Agent workflow

Claude Code is the usual implementation and integration owner for substantial features and difficult debugging. Codex is often used for bootstrap, independent review, tests, verification, or isolated implementation. Either may own a feature, but exactly one writer owns it at a time. Record the owner in an active ExecPlan for substantial work.

Claude subagents are useful for independent research, numerical review, Unity review, and test review when isolated context or parallel work pays off. Skip them for trivial exploration, simple single-file edits, and tightly sequential work. Research and review agents are read-only by default; do not assign concurrent edits to the same production files.

Use normal file tools for C#, tests, docs, configuration, safe asmdef edits, and Git. Use Unity MCP when Editor state matters: scene and prefab inspection or manipulation, Console, Play Mode, Test Runner, views, import state, and project settings. Only the implementation owner may make mutating Unity MCP calls. Reviewers may inspect state and run tests; coordinate Play Mode changes with the owner. Never mutate one running Editor from multiple agents concurrently.

For future worktrees, keep the main checkout stable, give a feature worktree one writer, and use a review worktree only for independent inspection or reproduction. A mutable Unity worktree needs its own Editor/MCP connection; do not share one Editor across mutable worktrees.

## Unity MCP

Both clients use the official Unity CLI MCP server (`unity mcp`) through the `com.unity.pipeline` package; see [setup](SETUP.md). It needs an open Editor for this project (`unity status`). Read-only checks: `editor_status` (project path and version), `list_open_scenes`, `get_scene_hierarchy`, `console`, `list_tests`. `editor_play`/`editor_stop`, `run_tests`, and `Scripts/check.sh` with the Editor open all enter Play Mode or recompile in that Editor, so coordinate them with the owner. Do not install a second MCP bridge without a demonstrated need.
