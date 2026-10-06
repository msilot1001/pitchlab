# TASK-025: Bunting

## Scope note
The detailed TASK-023–028 specification never reached this session; only the titles did. Scope comes from the title,
the Official Baseball Rules and the request's architecture rules.

## Goal
Bunts as a real batting option, decided by gameplay:
- the bunt's contact (the same collision physics), its rules (foul bunt, sacrifice) and the CPU's sacrifice;
- the human's bunt in the labs; presentation that only shows it.

## Owner
Claude Code (sole writer); reviewers and Codex read-only.

## Design
See Docs/BUNTING.md.
- `SwingInput.Bunt`: the square time, the PCI when the ball arrives, the aim and the pull-back.
- `ContactResolver`: contact at the ball's arrival; the bat squared to the incoming path plus the aim.
- `SwingParameters.AsBunt`.
- Rules: `PitchOutcome.FoulBunt`, `PlayResultKind.SacrificeBunt`, `GameState.Apply(…, bunt)`.
- CPU: `BuntStrategy` (sacrifice spot, aim) and `CpuBatter.PlanBunt`. `BatterPlan.InputFor(pitch)` is the bunt's input at
  the ball's arrival, the same as the labs read it.
- Labs: Q / West to square or pull back, the CPU's bunt events, `ResolveBunt` at arrival.
- Presentation: the squared bat on the PCI; banner texts.

## Milestones
- [x] Contact, rules, CPU and labs.
- [x] Physics checks:
  - a centred still bat gives ≈ 23 mph;
  - bat angle steers ≈ 4°/° (a 20° angle sent bunts foul), so the aim is ±5°;
  - the bat is squared to the ball's incoming path (spray had depended on the pitch's run).
- [x] Calibration over 400 forced spots: fair 33 mph at −26° (MLB 34 / −35); missed 10 % (MLB 8 %); foul 17 % of contact
  (MLB 55 %).
- [x] Found and fixed: the simulator used the CPU's last aim event, the labs the PCI at the arrival; both now use the aim at
  the arrival.
- [x] Tests: BuntTests (11) and the lab bunt PlayMode test. Runtime: the squared pose in the HittingLab.
- [x] Physics review: the reuse of the collision is sound. Fixed:
  - a NaN or ≥ 45° bunt angle is now invalid input;
  - a bunt's bat has one speed (pushed, no pivot);
  - the push is documented as a TUNED knob;
  - the spray amplification is explained by e_x and r_x.
- [x] Codex round 1: grade C. Fixed:
  - a pending bunt is resolved before the pitch is applied or replaced (frame-independent);
  - a bunt can be pulled back on a pitch that falls short (`ContactResolver.BuntArrival`);
  - the CPU's bunt aim is clamped to the PCI area like the labs'.
- [x] Tests for each. check.sh green (EditMode 733 passed, 6 skipped; PlayMode 107/107).
- [x] Codex round 2: grade C. Fixed:
  - aim input after the ball's arrival now resolves the bunt first, so the PCI track's pruning cannot change it (test: 300
    aim updates before a frame);
  - test.sh PlayMode polling extended to the 1500 s timeout.
- [x] check.sh green (EditMode 733 passed, 6 skipped; PlayMode 107/107).
- [x] Codex round 3: grade C.
  - The CPU's sacrifice cannot succeed without secondary leads; running on contact barely helps (106 of 134 still forced),
    so the gap is the lead.
  - Now `BuntStrategy.Sacrifice` is off (`SacrificesWork = false`) until TASK-026; the spot is `BuntStrategy.Spot` (tested).
  - Fixed: test.sh's final status look after polling; the debug readout's bunt contact time.
- [x] check.sh green (EditMode 733 passed, 6 skipped; PlayMode 107/107).
- [ ] Codex round 4; merge.

## Known gaps (handed on)
- Sacrifice success is very low: the runner's lead and break (TASK-026); bunt defense and positioning (TASK-033).
- Foul bunts are too few.
