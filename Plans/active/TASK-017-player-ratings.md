# TASK-017: Player profiles + ratings foundation

## Goal
Players with coherent, documented abilities that the simulation consumes as inputs — not outcomes: a compact rating set,
explicit rating → physical-parameter mappings, generic differentiated rosters, and clean data flow into running,
fielding and throwing.

## Owner
Claude Code (sole writer); Unity / test reviewers and Codex read-only.

## Current state (inspected, after TASK-016)
- `PlayerProfile` (TASK-013): id, name, bats, height, position label. No ratings.
- Running: one `RunnerProfile` for every runner of a play (`LivePlay(..., RunnerProfile?)`, default `Standard`).
- Fielding/throwing: static per-position `FielderProfile.For` / `ThrowProfile.For` / `ThrowProfile.Full`, used throughout
  `LiveDefense`; `FieldingSolver.Solve` already takes a profile function.
- `GameState` tracks base occupancy, not who is on base.

## Design
- `PlayerRatings` (immutable class, 0–100, default 50): batting (Contact, Power, Vision, Discipline), running (Speed,
  Acceleration, Baserunning), fielding (Reaction, Fielding, Catching), throwing (ArmStrength, ArmAccuracy, Transfer),
  pitching (Velocity, Command, Movement, Stamina). A class, not a struct: a struct's `new()` would silently be all zeros.
- `RatingScale`: linear in r̂ = (r − 50)/50, monotonic, equal to the generic profile at 50 (Docs/PLAYER_RATINGS.md):
  sprint speed 27 ± 3.5 ft/s; runner τ 0.69(1 ∓ 0.15); read delay 0.25 ∓ 0.08 s; fielder reaction ×(1 ∓ 0.25), own sprint
  speed, τ ×(1 ∓ 0.15); arm strength position average ± 8 mph; transfer ×(1 ∓ 0.2). Batting/pitching mappings come with
  TASK-018/019; fielding/catching/accuracy with TASK-021.
- `PlayerProfile` + throwing hand, fielding position, ratings (old constructor = an unrated player). `Team`: lineup,
  positions (eight fielders + DH), starting pitcher. `GenericRosters`: archetypes and two differentiated teams (same
  identities as TASK-013 so existing scenarios keep their batters).
- `PlayPersonnel`: the per-play fielder / routine throw / full throw / runner profiles (`Standard` = the generic set).
  `LivePlay(..., personnel)`; each `LiveRunner` runs with his own profile (trot on awards); `LiveDefense` reads the
  personnel instead of the static tables. `GameState` tracks who is on each base (walks force, plays move them, the
  editor's runners are the batters ahead, cleared at the half) and builds `Personnel` from the fielding team and runners.
  A lineup-only game (tests) fields the generic profiles.
- GameLab / GameSimulator pass the game's personnel into the fielding solve and the live play. The legacy static rules
  planner (`RulesPlay`, labs) keeps the generic profiles.
- Debug: the Situation Editor shows the batter's and pitcher's ratings.

## Milestones
- [x] Ratings, mappings, profiles, teams, rosters, personnel data flow + EditMode tests (PlayerRatingsTests).
- [x] Docs/PLAYER_RATINGS.md (research: Statcast sprint speed, running splits, arm strength, pop time, jump, bat speed,
  pitch arsenals, fielding totals).
- [x] Runtime (GameLab): Away #1 (Speed 85) runs 29.5 ft/s; the home elite CF reacts in 0.34 s vs 0.40 for an average LF;
  3B arm 88.1 mph; the single leaves Away #1 tracked on first; plays use the game's personnel.
- [x] Reviews — Unity/test: editor stand-ins never duplicate a runner already on base; ratings null-guarded; a live play
  refuses a fielding solve made with other fielders; shared transfer pinned in a comment; docs: a fielder's top speed is his
  own (27 ft/s at 50). Tests: defense reads ratings (arm/transfer → earlier, faster throw; reaction/speed → earlier
  intercept), per-runner profiles and trots, who ends up where after a fielder's choice and a single, RESET restores runners,
  every simulated pitch keeps identities = occupancy and distinct, team-specific personnel per half, full-throw
  monotonicity, a lineup-only game plays identically to the generic personnel. Not taken: the runners' estimate of an
  outfield throw stays at the routine speed (switching to full effort changed validated TASK-007/008 scenarios).
- [ ] Codex, merge.

## Limitations
- Runners read an outfielder's throw at his routine speed (0.85 of his arm), while he throws at full effort — conservative,
  as validated; with arm ratings the gap scales with the arm.
- No pitcher batting (DH lineups only).
- Ratings beyond running/fielding movement/arm strength/transfer are stored but not yet consumed (TASK-018/019/021).
- Fielder reach, braking and catch heights stay generic; no per-player range modifier.
- Simulated-game seed pins re-chosen (rosters changed the games).
