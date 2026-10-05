# TASK-013: Plate appearance lifecycle + batting order

## Goal
Pitches belong to an authoritative plate appearance that lasts until a terminal result (walk, strikeout, a fair ball
once its play is over); each team has a nine-player batting order whose place carries over between innings; the batter's
handedness and zone come from the lineup.

## Owner
Claude Code (sole writer); rules / Unity / test reviewers and Codex read-only.

## Current state (inspected, after TASK-012)
- `GameState` holds the count and applies `Pitch(PitchOutcome)` / `Apply(LivePlay)`; no batter identity, no lineup, no
  pitch record. `ResetPlateAppearance` restores a value-tuple snapshot.
- `HittingLabController` applies each pitch's result once at `BattingStateMachine.OutcomeTime`; the swing side comes from
  its serialized `SwingParameters` (right-handed); `HittingLabPresentation.PlaceBatter` already mirrors for lefties but
  only at start-up. The zone outline is the default zone.

## Design
- Gameplay/Play `PlayerProfile` (id, name, bats, height → zone scaled from the default 73-in zone, placeholder position)
  and `Lineup` (nine distinct players, slots 1–9). Generic lineups alternate sides between some consecutive batters and use
  three heights (70 / 73 / 76 in).
- `PlateAppearance` (owned by GameState): number, team, slot, batter, start inning/half/outs/bases, count, chronological
  `PitchEvent`s (number, `PitchInfo` label / release speed / plate crossing, outcome, count before/after, end), terminal
  result text, runs, outs made. Completed exactly once; `GameState.Current` is never complete.
- `GameState`: lineups, a per-team next-slot cursor (advanced only when a plate appearance completes; the other team's
  cursor waits), `Current`, `Completed`, `Batting`; `Pitch(outcome, info)` / `Apply(play, info)`; a full snapshot for
  RESET PA (state, cursors, current plate appearance); the Situation Editor keeps the batter and count unless the half
  changes (then the other team's next batter at 0–0).
- `PitchOutcomes.Of(..., zoneBottom, zoneTop)` / `StrikeZone.Contains(x, z, bottom, top)`: the call uses the batter's
  zone (the default zone otherwise — HittingLab).
- GameLab: at each pitch the controller fixes the batter (`PitchBatter`): his side for the swing, his zone for the call
  and the outline; the result is recorded with the pitch's `PitchInfo`. The presentation re-places the batter figure when
  the side changes. `NewGame(GameState)` starts a game (drops the pitch on screen unapplied). PCI policy: the aim point
  persists across pitches and batters (it is the player's input, not batter state).
- Panel: the current batter (team, slot, name, side) and the plate appearance's pitches.

## Milestones
- [x] Gameplay types + GameState rewrite + EditMode tests (PlateAppearanceTests, updated GameStateTests).
- [x] GameLab integration (side, zone, pitch info, NewGame, panel) + PlayMode tests.
- [x] check.sh (EditMode 578/584, 6 skipped; PlayMode 76/76); runtime (GameLab: lefty A1 walks from the first-base
  side, righty A2 called out on strikes, lefty A3 singles, A4 up; next batter steps in at Ready).
- [x] Reviews — rules: citations 5.04(a)(1)/(3), zone summary; Unity: pitch info latched at throw, batter swap from the
  rendered time as a new person in stance, take counted after InputGrace (0.1 s) so a late-delivered in-time swing still
  counts, zone outline follows the batter, panel result read from the game; test: zone-dependent call, reset/NewGame,
  applied-once over all plays, both cursors restored, determinism with contact and a mid-play press. Codex B: same-side
  batter change (identity tracked), null label of default PitchInfo — fixed and tested (mutations caught).

## Limitations / deferred
- A runner-only third out (caught stealing) would not complete the batter's PA (5.04(a)(3)); not reachable yet.
- Dropped third strike (strike three always held), HBP, check swings, bunts, substitutions, pinch hitters.
- Zone by height is a convention (no stance model). Switch hitters not modelled.
