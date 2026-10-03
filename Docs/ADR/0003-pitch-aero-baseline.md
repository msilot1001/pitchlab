# ADR 0003: Pitch aerodynamics baseline and effective spin inputs

**Status:** Accepted (overnight milestone C, after TASK-002). Extends ADR 0002.

**Decision:**
- The ball-flight baseline for gameplay is the current custom model: gravity + quadratic drag with constant C_D = 0.35 + Magnus lift with Nathan's (2017) C_L(S) fit, S from transverse spin, spin vector fixed in world space, RK4 at 5 ms. Its data-supported range is Re ≥ 1.5 × 10⁵ and S ≤ 0.4; flights outside it are flagged, not blocked.
- No seam-shifted wake or other seam effects are modelled until data quantifies them for the pitch families involved.
- Pitch spin inputs — rate × efficiency and spin axis — mean **effective, movement-equivalent transverse spin**. Hawk-Eye active spin and `spin_axis` are not used directly for breaking balls; presets and imports target movement.
- The `BallState` / `FlightLimits` / `TrajectoryResult` / `BallFlightSimulator` API is frozen in shape for hitting and batted-ball work; extensions (spin-dependent C_D, spin decay, more stop events, a flight-level validity check) are additive.
- Batted-ball flight must re-validate drag (including spin dependence and low-Re apex speeds) and spin decay against Statcast distance/hang time before it is trusted.

**Reasoning:** TASK-002 (`Docs/VALIDATION_TASK002.md`) replayed 649 tracked pitches: the Statcast field mapping reproduces Statcast exactly; drag matches C_D 0.35 in clean conditions; at measured spin, lift for four-seamers, sinkers and changeups is within the C_L data scatter. Breaking balls get ~1.4–2.6× too much lift for their measured active spin and Statcast's measured axis departs from their movement direction; the mechanism (low-spin seam-orientation lift, SSW, gyro geometry) is underdetermined by the available data, and per-pitch-type corrections are not allowed. Defining inputs as effective spin keeps the simulator physics-only while making gameplay pitches move like real ones.

**Consequences:** Pitch definitions are tuned to movement, not to Hawk-Eye spin readouts. Improving breaking-ball physics later changes how effective spin maps to measured spin, not the API. Gameplay work (hitting) can build on the simulator now.
