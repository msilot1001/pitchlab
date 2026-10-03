# Pitch-flight direction

## Established design decisions

TASK-001 will eventually compute pitch flight from release position, initial velocity, spin rate, 3D spin axis, and environment. The initial force model is gravity, aerodynamic drag, and Magnus force. Environment properties are configurable. A fixed simulation timestep is independent of rendering frame rate. Pitch type labels do not directly select trajectory curves. Unity Rigidbody is not the authoritative integrator.

## Working assumptions

Use one explicit world coordinate convention and unit system, documented alongside the implementation. Select an integration method after comparing stability and error at plausible pitch conditions. Treat coefficients and environmental inputs as configuration backed by research, not constants guessed during bootstrap.

## Research questions

Which measured data sets are suitable for validation? Which drag and lift formulations fit baseball speeds and spin? What timestep and integrator achieve acceptable error? How should spin efficiency and gyro spin affect effective transverse spin? Later extensions may include seam effects or seam-shifted wake, wind, and varying air density. None is implemented here.
