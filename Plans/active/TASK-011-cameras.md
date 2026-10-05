# TASK-011: Gameplay cameras

## Goal
Camera modes for the playable slice: F1 umpire, F2 catcher, F3 offset (third-base side), F4 tactical 3D, F5 auto; Tab
cycles; tactical controls only in tactical mode; no effect on gameplay, the PCI or timing; the mode kept across plate
appearances; a subtle mode label; tests.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Design
- `Presentation/Camera/GameplayCameraController` on the camera with `BaseballCamera` (execution order 10000: after
  everything that places the camera). Auto = `BaseballCamera` (batting view, then ball follow; default). Fixed views keep
  their position and turn smoothly to keep the ball (the `BaseballCamera` follow target) in frame while it is in play.
  Tactical: an orbit around the infield/outfield centre (yaw, pitch 8–85°, distance 25–220 m); ←/→ orbit, ↑/↓ pitch,
  PageUp/PageDown or [ / ] zoom, R reset — handled only in tactical mode; the lab's ←/→ pitch selection yields to it.
- The mode is component state: it survives plate appearances (the presentation's `ShowBatting`/`Follow` only drive the
  disabled `BaseballCamera` in other modes).
- `BaseballCamera` shake made deterministic (no random numbers in the game).
- Presentation asmdef references the Input System. Added to the HittingLab and GameLab scenes.

## Milestones
- [x] Controller, scenes, label.
- [x] PlayMode tests (4): views + Tab, tactical-only controls (and arrow ownership), mode kept across PAs with fixed views
  turning to the ball, identical play in two modes.
- [ ] Review; Codex; merge.

## Verification
- check.sh: EditMode 534/540 (6 skipped, pre-existing), PlayMode 52/52.
- Runtime (GameLab): tactical view of the whole field (bases loaded leading off, DP depth), offset view from the third-base
  side; label bottom right.
