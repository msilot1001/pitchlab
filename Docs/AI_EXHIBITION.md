# AI exhibition (TASK-022)

The player attributes and gameplay AI of TASK-017–021 come together in the GameLab:
- generic rated rosters;
- the CPU pitcher (Docs/PITCHER_AI.md) and pitch execution (Docs/PITCH_EXECUTION.md);
- the CPU batter (Docs/BATTER_AI.md);
- defensive execution (Docs/DEFENSIVE_VARIABILITY.md).

## Modes (GameLab; F or the gamepad's Select cycles them, or the situation editor's buttons, G)
| Mode | Human | CPU | Defense / runners |
|---|---|---|---|
| Human batting | PCI and swing | pitcher (calls, executed pitches) | gameplay |
| Human pitching | pitch type (Z/X), target (U I O / J K L / N M ,), throw (Space) | batter (perception, decision, timestamped PCI and swing) | gameplay |
| CPU vs CPU | — | both | gameplay |
| Manual | pitching and batting (the pre-AI sandbox) | — | gameplay |

The mode only sets the two existing switches (`AutoPitch`, `CpuBatting`); the rules are unchanged. The HUD shows the mode.
A switch during a pitch changes the batter from the next pitch, and the HUD says so; the pitcher switch acts on the next
throw.
The situation editor shows the AI debug view:
- the batter's ratings, and the pitcher's;
- the CPU batter's decision, prediction, strike belief and swing chance, once he has made it;
- the CPU pitcher's intent, location and reason, and the execution miss once the pitch is over.

## The production path
CPU vs CPU in the GameLab, and `GameSimulator` (the headless runner used for the samples and tests), both use the same
chain:
1. `CpuPitcher.Choose`;
2. `GamePitches.Create` (aim, execution, flight);
3. `CpuBatter.Plan` (events from what he has seen);
4. `ContactResolver`;
5. `FieldingSolver` and `LivePlay` with defensive execution;
6. `GameState`.

There is no stand-in result generator. A PlayMode test (`CpuVsCpuPlaysAHalfInningOnTheProductionPath`) plays a whole half
inning in the GameLab. The test clock advances in 0.1 s steps, updating the lab and its presentation, and every pitch of
the half must be a CPU call batted by the CPU, with at least one ball in play.

## Statistical sanity (30 games, standard rosters, seeds 1001–1030; regression check, not calibration)
Regenerate with `Pitchlab.Tests.ExhibitionSanityTests.Report(30, 12)` (EditMode assembly, e.g. through an Editor eval).
| | Sim | MLB 2024–25 (approx.) | Verdict |
|---|---|---|---|
| Runs per game (both teams) | 15.9 | ≈ 9 | too high |
| PA per game (both) | 81.7 | ≈ 76 | high (more baserunners) |
| BB % | 9.1 | ≈ 8.5 | plausible |
| K % | 16.3 | ≈ 22 | low |
| Balls in play per game (both) | 61 | ≈ 52 | high |
| HR per game (both) | 8.7 | ≈ 2.3 | **absurd — largest discrepancy** |
| Misplays per team-game | 0.52 | errors ≈ 0.5 | plausible (includes hard chances) |
| Pitches per PA | 3.67 | ≈ 3.9 | slightly low |

**Largest discrepancy: home runs and hard contact.** Fair balls average 95.9 mph off the bat (MLB ≈ 88.5), and HR per fly
ball is 37 % (MLB ≈ 12 %). Launch angles are plausible (mean 7.8°, MLB ≈ 12°). The exit speeds come from the validated
contact physics given the CPU batter's contact quality. His contacts are too centred on the barrel, both along it and
vertically, for an average hitter: the aim and perception errors are TUNED for contact rate, not for contact quality.
- Next step: a contact-quality calibration of the CPU batter (larger along-barrel error, a launch-intent spread) against
  Statcast EV/LA distributions. This was deliberately not tuned in this milestone.
- The low strikeout rate has the same root: Z-Contact is close to MLB, but too few weak swings miss.

## Archetype matchups (12 games each; the line of the side at bat — one team per game; its misplays are the defense's it faced)
| Matchup | R/g | BB % | K % | HR/g | misplays/g |
|---|---|---|---|---|---|
| power pitcher vs contact hitters | 9.3 | 12.9 | 11.3 | 3.50 | 1.08 |
| power pitcher vs free swingers | 6.9 | 12.3 | 24.4 | 3.50 | 0.33 |
| command pitcher vs patient hitters | 8.5 | 6.4 | 17.5 | 5.08 | 0.42 |
| poor-command pitcher vs patient hitters | 17.9 | 30.6 | 8.5 | 5.17 | 0.50 |
| poor-command pitcher vs power hitters | 16.0 | 24.8 | 15.9 | 6.08 | 0.50 |
| poor-command pitcher vs contact hitters | 13.3 | 28.5 | 9.0 | 3.75 | 0.75 |
| balanced hitters vs elite defense | 8.6 | 14.3 | 15.6 | 4.42 | 0.42 |
| balanced hitters vs poor defense | 9.0 | 13.2 | 14.9 | 4.42 | 1.08 |

Directions are sensible:
- free swingers strike out ≈ 1.5–2× as often as contact hitters;
- a poor-command pitcher walks patient hitters almost five times as often as a command pitcher;
- power hitters hit more home runs off a wild pitcher than contact hitters;
- a poor defense misplays 2.6× as often as an elite one, though runs differ little because home runs dominate scoring.

The poor-command pitcher's walk rate (≈ 25–31 %) is extreme: command 15 is beyond any MLB starter.
`ExhibitionSanityTests` guards with margins:
- walks by command (more than 2×);
- strikeouts by hitter type (more than 1.3×; a 10-game sample gives 15.1 % vs 22.5 %, so the report's 2.2× is partly a
  favourable sample);
- misplays by defense.

It also bounds a standard game broadly and caps the known discrepancies (runs, home runs), so they cannot get worse
unnoticed.

## Performance (one-off Editor measurements, Apple silicon, per event)
| | Cost |
|---|---|
| CPU pitcher's call | ≈ 1 µs |
| Pitch aim, execution and flight | ≈ 0.6 ms |
| CPU batter, whole pitch (≈ 40 looks, fit, decision, events) | ≈ 11 µs |
| A live play to its end (with or without defensive execution) | ≈ 9 ms |
| A simulated nine-inning game | ≈ 1 s |

Nothing is recomputed per frame:
- the AI and execution run once per pitch or per play, at event times;
- the GameLab applies the batter's timestamped events as frames reach them;
- presentation reads pure functions of time.

## Runtime walkthrough (GameLab, real time, real key presses)
- **Human batting:** a full plate appearance against the CPU pitcher (curveball, four-seamer, changeup, curveball):
  strikeout looking.
- **Human pitching:** a full plate appearance by the CPU batter. He takes two strikes and two balls, then strikes out
  swinging on a four-seamer.
- **CPU vs CPU:** a whole half inning (fly out, groundout, groundout), then the next half under way: a 7-pitch plate
  appearance, a walk.
- Cameras: tactical (F4), catcher (F2), auto (F5).
- Console: no game errors (one MCP tooling timeout at play-mode start).
