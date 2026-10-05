# Defensive responsibilities (TASK-008)

What each defender does on a live ball, and what `DefensiveCoordinator` implements.

**Tags:**
- **[S]** standard convention, consistent across the sources below;
- **[V]** varies by team or level;
- **[I]** an implementation choice or simplification.

There is no single universal coverage chart. This is a defensible generic system.

## Principles
- **[S] Every fielder has a job on every play:** field the ball, cover a base, be a cut-off or relay, back up a base or a fielder, or hold.
- **[S] Only one player fields the ball:** the primary. The others do not chase it (no swarm).
- **[S] The cut-off lines up on the straight line from the thrower to the target base.**
- **[S] Backups go to the far side of a base, in line with the throw,** deep enough to field an overthrow.
- **[S] Throw one base ahead of the lead runner. Cut-off decisions:**
  - let the ball through when it beats the runner;
  - cut and hold when there is no play;
  - cut and redirect when a trailing runner is the better play.

## Infield ground balls, nobody on
- **[S] Covering first:**
  - The fielder throws to first, and the 1B covers it.
  - When the 1B fields the ball away from the bag, the pitcher covers first. On balls to the right side the pitcher breaks toward first anyway (**[I]** not modelled: the pitcher moves only when he covers first or backs up home; otherwise he holds).
- **[S] Backups:** RF backs up first on infield throws. **[V]** The catcher trails the batter-runner to back up first; **[I]** the catcher stays home.
- **[S] Second and third:** of 2B and SS, the one not fielding the ball takes second. 3B covers third unless he fields the ball.
- **[V]/[I] LF and CF:** they drift in to back up second (CF) and third (LF).

## Runner on first, fewer than two out
- **[S] Double-play pivot:**
  - a ball to SS or 3B: 2B covers second (6-4-3, 5-4-3);
  - a ball to 2B or 1B: SS covers second (4-6-3, 3-6-3);
  - a ball to P: SS covers second.
  - When the 1B fields the ball, P covers first.
- **[S] The catcher stays home.**

## Singles to the outfield

| Situation | Cut-off | Covers | Backups |
|---|---|---|---|
| Empty, single to LF / CF | SS, to 2B | 2B second; 1B first; 3B third; C home | P toward second; adjacent OF back up the fielder |
| Empty, single to RF | 2B, to 2B | SS second; 1B first; 3B third; C home | P toward second |
| Runner on 1B (play at third) | SS, to 3B | 3B third; 2B second; 1B first; C home | P backs up third (deep) |
| Runner on 2B / 1B+2B (play at home) | **[V]** LF: 3B or 1B; CF/RF: 1B **[S]** → **[I]** 3B on LF throws, 1B on CF/RF throws | C home; SS third (when 3B is the cut-off) else 3B; with 1B the cut-off, 2B covers first behind the batter-runner and SS second | P backs up home (or third) |

## Extra-base hits (gaps, lines, wall)
- **[S] Relay:** SS on the left side and centre, 2B on the right side, in line between the fielder and the target base.
- **[S] Trail:** the other middle infielder, about 9 m (30 ft) behind the relay, in the same line.
- **Covers:**
  - **[S]** 1B trails the batter-runner to cover second (bases empty / no play at home); with a play at home he is the cut-off for home in line from the relay (**[S]**), and the 2B — if he is not the relay or trail — covers first;
  - **[S]** 3B covers third;
  - **[S]** C covers home;
  - **[S]** P backs up third or home, whichever is the play.
- **[S] Outfielders:** adjacent outfielders back up the fielder.

## Fly balls
- **[S] Priority:** CF > LF = RF > SS > 2B > 3B = 1B > P > C. TASK-005's intercept tie-break already uses this order.
- **[S] Backup:** the adjacent outfielder backs up the catch.
- **Tag-up throws:** align as for a single with a runner on second (cut-off for home ~12–14 m from the plate). Throws after a catch arrive in TASK-009.

## Positions **[I]**
- **Cut-off for home:** on the thrower → plate line, 13 m from the plate.
- **Cut-off for third or second:** on the line, midway, but at least 12 m and at most 30 m from the base.
- **Relay:** on the line, 45 m from the fielder (or the midpoint if the throw is shorter than 90 m).
- **Trail:** 9 m behind the relay on the same line.
- **Base backups:** 12 m beyond the base on the line from the thrower (or from the ball while it is free).
- **Fielder backups:** 10 m behind the primary's intercept point, away from home.
- **Holding:** at his position, or drifting 3 m toward the play.

## Throws, cut-offs and relays **[I]**
- **Covered bases only:** a throw goes only to a base that someone covers, and is timed for the cover (TASK-006A hold-for-cover).
- **Choice of play (every defender with the ball):** an out he can make by stepping on a bag; else a force out, preferring the lead runner; else a tag out; else a close play (the throw at most 0.5 s behind the runner); else, for an outfielder, the ball to the cut-off or relay man; else hold.
- **Cut-off vs direct:** distance does not decide it: an outfielder throws direct only when the play is close (above), otherwise to the cut-off/relay man — aimed at where he will be when the ball arrives (he keeps moving to his spot).
  - The cut-off man, holding the ball, then makes his own decision: relay to the base, or a better base, or hold.
- **Relay:** a deep ball (fielded beyond 92 m ≈ 300 ft from home, past the outfielders' normal depth) is relayed. The relay man goes out on the line, and his throw is a second, real throw.
- **A throw on its way:** its receiver keeps his job (the base he covers, or the cut-off spot) when the roles are recomputed; nobody else is sent to that base.
- **Not modelled [I]:** throwing errors. Every planned throw is catchable, so an off-the-bag catch (the receiver then carries the ball to the bag) and a missed throw (the nearest fielder retrieves it) are handled but do not occur in practice.

## Sources
- Wikipedia, "Covering a base": https://en.wikipedia.org/wiki/Covering_a_base
- WRSSBA, Cutoffs and Relays cheat sheet: https://wrssba.com/wp-content/uploads/2019/04/Cheat-Sheet-Cutoffs-and-Relays-v-1.6.pdf
- Inside Baseball (2014), cut-off and relay manual: https://cdn1.sportngin.com/attachments/document/e934-2443306/Baseball-Cutoff-Relay.pdf
- "Defensive Situations and Strategies": https://cdn1.sportngin.com/attachments/document/0098/4522/Defensive_Situations_and_Strategies.pdf
- ProBaseballInsider, third-base relay positioning: https://probaseballinsider.com/baseball-instruction/third-base/third-base-positioning-for-relays/
- ProBaseballInsider, pop-fly priorities: https://probaseballinsider.com/baseball-instruction/pop-fly-priorities/
- FullWindup, backing up bases: https://www.fullwindup.com/2012/02/backing-up-bases/
- THT, the physics of the cut-off: https://tht.fangraphs.com/the-physics-of-the-cutoff-part-ii/
- Wikipedia, "Double play": https://en.wikipedia.org/wiki/Double_play

**Caveat:** these are youth and amateur coaching sources that agree with one another. No ABCA or MLB source was accessible, and several distances above are implementation choices **[I]**.
