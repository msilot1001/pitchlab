# Pitchlab

Unity 6.3 LTS, C#, URP; desktop and gamepad first. Follow `AGENTS.md` and the relevant documents in `Docs/`. See `Docs/AGENT_WORKFLOW.md` for ownership and Unity MCP rules.

One implementation owner per feature; reviewers stay read-only. Use subagents only for independent research or review that benefits from isolated context. Keep Simulation independent of Presentation, physics independent of rendering frame rate, and Rigidbody non-authoritative for baseball flight. Inspect relevant code, stay in scope, and run `Scripts/check.sh` when Unity changes can be verified.
