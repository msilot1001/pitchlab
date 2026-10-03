# ExecPlans

Use an ExecPlan for a complex feature or significant refactor. Skip it for trivial work. Keep one active file in `Plans/active/`; move it to `Plans/completed/` when verified. The implementation owner updates it as work proceeds, especially after discoveries or changed decisions.

An ExecPlan should let another agent continue without chat history. Keep these sections brief and concrete: Goal, Scope, Owner, Architectural constraints, Relevant files, Milestones with current status, Verification with actual commands/results, Discoveries, Decisions, and Remaining risks. Write a rollback or recovery note only when the work warrants one.

Do not treat the plan as proof of completion. Run verification and record what actually passed.
