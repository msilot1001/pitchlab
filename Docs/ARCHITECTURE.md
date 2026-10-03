# Architecture

The intended direction is `Simulation ← Gameplay ← Presentation`. Simulation owns deterministic state transitions and physical calculations; it must not reference gameplay or rendering code. Gameplay turns input and rules into simulation commands and interprets results. Presentation observes state to drive camera, animation, audio, and UI. Presentation may read simulation data where useful.

Pitch flight will use a custom authoritative simulation. Unity transforms display its results. Rigidbody may serve other purposes later, but it does not determine pitch positions or velocities.

Data flow: input/configuration → gameplay command → fixed-step simulation → state/events → presentation. Rendering may interpolate state without feeding its frame timing back into simulation outcomes.

Start with few assembly definitions. Add a Simulation assembly when code exists so the dependency boundary can be enforced; add further assemblies only when they provide a useful compile-time boundary.

Test pure simulation behavior in EditMode with units, coordinate conventions, repeatability, and invariants. Use PlayMode tests for Unity integration and scene behavior. Tests should assert observable behavior rather than internal method shapes.
