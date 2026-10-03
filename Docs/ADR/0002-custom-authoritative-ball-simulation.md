# ADR 0002: Authoritative ball simulation

**Decision:** Pitch flight will use a custom fixed-step simulation, with Unity rendering the resulting state.

**Reasoning:** Explicit state, forces, and integration allow repeatable trajectories and physics validation independent of render timing.

**Consequences:** Rigidbody is not authoritative for pitch flight. Pitch labels do not encode motion curves. Coefficients and integration choices require later research and tests.
