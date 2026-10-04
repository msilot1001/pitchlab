# Reference motions (TASK-004.6B-0)

Presentation only. The batter and pitcher motions are generic (`ReferenceRightHandedSwing`, `ReferenceRightHandedPitchDelivery`, in `Assets/Game/Presentation/Animation/ReferenceMotions.cs`); two MLB players served as motion references, with no likeness, names in the game, uniforms or appearance.

Labels:
- **MEASURED**: an official tracked number from a public Baseball Savant view or CSV export.
- **LITERATURE**: a published biomechanics value for elite players, not specific to the reference players.
- **DERIVED**: our arithmetic.
- **VISUALLY ESTIMATED**: read off still photos.

**Limitation:** no video was analysed. The research tools could not open MLB video, and neither could I, so visual estimates come only from a handful of freely licensed still photos from other seasons (listed below) and from written descriptions. Their confidence is low to medium.

## Reference hitter: Mookie Betts (right-handed), 2024 season

2024 was chosen because his 2025 swing changed mid-season (attack angle ≈ 13° instead of ≈ 9°, attack direction about 12° pull). REPORTED: MLB.com, "How Mookie Betts' swing has changed" (2025 World Series); seen only as a search snippet.

| Quantity | Value | Label | Source |
|---|---|---|---|
| Bat speed (mean of top 90 % of swings) | 68.93 mph | MEASURED | Savant bat-tracking CSV, 2024, 641 swings |
| Swing length (bat **head** path, start of tracking → impact) | 6.91 ft (league ≈ 7.3) | MEASURED | Savant; Yahoo 2024 |
| Swing path tilt (swing-plane angle over the last 40 ms) | 34.0° (league ≈ 32°) | MEASURED. Savant doesn't say which bat point defines the plane; we use the sweet spot's path, which is our interpretation | Savant swing-path CSV, 2024 |
| Attack angle | +8.86° | MEASURED | same |
| Attack direction | −4.18°. We use +4.18° toward first base (opposite field), taking FanGraphs' convention that negative = opposite field; Savant's CSV docs don't state the sign | MEASURED; sign interpreted | same |
| Contact point | 8.35 in in front of the plate; 33.1 in in front of the hips' midpoint | MEASURED | same |
| Stance position | hips 24.7 in behind the front of the plate, 27.7 in off its inside edge | MEASURED | same |
| Stance width | 29.1 in (0.74 m) | MEASURED, season uncertain (the CSV ignored the season filter) | Savant batting-stance visual |
| Stance angle | −15.9°. Not used: the sign convention is unresolved | MEASURED, sign unresolved | same |
| Leg lift; "early, slow" load; sinks into the back hip; hands stay under the ball; high, golfer-like finish | as described | VISUALLY ESTIMATED from text (low) | Slate excerpt of *The MVP Machine* (2018 swing rebuild) |
| Contact frame: wide base, firm lead leg, rear heel up and pivoted, trunk bent toward the plate, arms extended, barrel angled down and forward | as described | VISUALLY ESTIMATED (medium) | Wikimedia Commons "Mookie Betts hitting the ball", 2017-09-18, CC BY-SA 2.0, catcher-side angle |
| Extension: hands at shoulder height, chest open to the pitcher | as described | VISUALLY ESTIMATED (medium) | Commons "Mookie Betts batting in game against Yankees 09-27-16", CC BY 2.0 |

Elite-hitter timing and angles (LITERATURE: Fortenbaugh 2011, 43 AA hitters at 300 Hz, live batting practice, middle-pitch fastball), used for the shape of the body motion:
- **Events:** lead foot down −340 ms before contact. Maximum pelvis counter-rotation −21° at −250 ms. Bat starts forward at −170 ms. Maximum hip–shoulder separation −18° at −103 ms.
- **At contact:** pelvis 77° open, trunk ≈ 75°, lead knee 18°, lead elbow 56°, rear elbow 81°, bat −30° (barrel below the hands).
- **Finish:** trunk ≈ 135°.

These were compressed to game speed, non-uniformly (DERIVED, an assumption): plant moves from −0.34 s to −0.20 s (×0.59) and launch from −0.17 s to −0.15 s (×0.88), so launch matches the gameplay swing duration of 0.15 s.

### Key poses
stance → quiet → loadStart → rearHipLoad → legLift → stride → **plant** → hipsFire → **launch** → handsDrive → approach → zone → barrelIn → preContact → **contact** → postContact → extension → followThrough → **finish** → hold.

Times are in seconds from contact and normalized to u. Feet use IK targets. The front foot's target is constant from plant on and the rear foot's until launch, so they stay planted; the rear heel then lifts as the foot pivots.

**Bat path from launch to just after contact:** derived from the measured metrics rather than placed by hand. The sweet spot follows an arc that:
- passes the contact point at the measured attack angle and attack direction;
- lies in a plane tilted 34°;
- has speed growing from 0 at launch to 68.9 mph at contact, with the exponent chosen so the launch-to-contact path is 6.91 ft.

The hands are placed 0.62 m back along the bat, with a lag or lead angle, and the arms solve to the hands by IK.

How the arc's parameters are derived:
- Statcast swing length is the **bat head's** path. The sweet spot travels less, because the bat also turns about the hands. So the arc's own sweet-spot length is a DERIVED parameter, 5.8 ft, chosen so the animated head path matches 6.91 ft.
- Zero speed at launch is a modelling assumption (DERIVED), with the exponent k chosen so the arc length comes out right. Statcast tracking actually starts with the bat already moving.
- The contact point is at z = 0.88 m rather than the 0.84 m (33.1 in) from Savant's hip intercept. 0.88 is where the game's contact plane falls for the measured stance depth (DERIVED); the 4 cm difference is accepted.

## Reference pitcher: Gerrit Cole (right-handed), 2024 season

| Quantity | Value | Label | Source |
|---|---|---|---|
| Four-seam velocity / spin | 95.9 mph / 2362 rpm | MEASURED | Savant 2024 |
| Arm angle (Savant: shoulder → ball at release; 0° = sidearm) | 42.4° (league ≈ 39°) | MEASURED | Savant arm-angle leaderboard 2024 |
| Release height / side | 5.99 ft / −1.80 ft (third-base side) | MEASURED | same |
| Shoulder at release (height / side) | 4.48 ft / −0.15 ft | MEASURED | same |
| Extension | No 2024 value found. Earlier seasons were 6.3–6.5 ft | MEASURED (other seasons) | SI 2023 |
| Windup with the bases empty, hands set at the chest, "front foot flat under the knee" | as described | VISUALLY ESTIMATED from text (moderate) | SI 2023 |
| Cocking frame: long stride with the lead knee ≈ 45° flexed, back leg extended on the toe, glove tucked at the chest, elbow at shoulder height with the forearm vertical | as described | VISUALLY ESTIMATED (medium) | Commons "Gerrit Cole 2018 (1)", 2018-09-30, CC BY 2.0, first-base side |
| Just after release: trunk flexed ≈ 40°, arm crossing down, lead leg firm, back toe dragging | as described | VISUALLY ESTIMATED (medium) | Commons "Gerrit Cole 2018 (3)" |
| Recovery: square to home in a fielding crouch | as described | VISUALLY ESTIMATED (medium) | Commons "Gerrit Cole 2018 (4)" |

Elite-pitcher norms (LITERATURE: Fleisig ISBS 2010 / ASMI; Clinician's Guide PMC6542879):
- **At foot contact:** stride 83 ± 4 % of height, lead knee 45 ± 9°, foot closed 17 ± 9°, pelvis 33° open, shoulders ≈ 15° closed (hip–shoulder separation ≈ 45–50°), shoulder abduction ≈ 93°, elbow ≈ 90°.
- **At release:** trunk 36 ± 7° forward and 23 ± 10° lateral toward the glove side, lead knee 35 ± 12°.
- **Timing:** max external rotation (MER) to release ≈ 50 ms (secondary source). Foot contact to release ≈ 0.14–0.16 s is an unverified prior.

### Key poses
set → initialMove → liftStart → liftPeak → drift → handBreak → footApproach → **footPlant** → armCock → **maxExternalRotation** → **release** → deceleration → torsoFollowThrough → backLegFollowThrough → **recovery**.

Times run from −1.1 s to +1.0 s relative to release: foot plant at −0.15 s, arm cock at −0.10 s, MER at −0.04 s.

**Simplification:** the motion starts at the post-pivot balance position. The windup's initial rocker step is omitted.

**On the mound:**
- The pivot ankle stays planted at the rubber from set until the stride lands.
- The lead foot lands 1.54 m ahead, at the mound's slope height, and stays there.
- The pelvis leads the chest (`chest yaw` holds the hip–shoulder separation), and the trunk tilts forward and toward the glove side at release.

## Gameplay vs presentation
- **The simulation stays authoritative.** Release time, release point, pitch and contact all come from gameplay.
- **Pitcher:**
  - The figure stands on the rubber at the mound top. The lead foot lands on the mound slope.
  - Each pitch's delivery is rebuilt with its release key's wrist target placed so that the **ball**, 0.13 m beyond the wrist in the fingers, meets the simulated release point. Four correction passes run before the wind-up starts. The feet and root are never moved for the fit.
  - Measured on the five presets: the ball misses the release point by ≤ 0.2 cm, with wrist corrections ≤ 2.5 cm.
  - A PlayMode test keeps the miss under 5 cm.
  - Timing: the throw press (−DeliveryLead) maps to set, the release time to release, and the rest plays in real seconds.
- **Batter:**
  - Stands at the measured stance position. The reference contact point then falls on the gameplay contact plane: (0.92, 0.78, 0.88) in the batter frame is (0.00, 0.78, 0.68) in world space; the plane is at 0.682.
  - Before the swing, the clip follows the expected contact time and holds at launch-ready.
  - When the player swings (at s, from the authoritative swing time), launch → contact maps onto [s, s + SwingDuration], starting from wherever the pre-swing motion was.
  - Hitting a different contact point, or the aim point on a miss, moves the hands, not the feet: up to 0.25 m, ramping in over the swing.
- **Swing path tilt ≠ `VerticalBatAngle`.**
  - Statcast swing path tilt is the angle of the sweet spot's *path plane* over the last 40 ms (34°).
  - `SwingParameters.VerticalBatAngle` (32°, TASK-004.5) is the *bat's own* tilt at contact. It is a collision parameter that tilts the line of centres.
  - They are related but not the same quantity. Neither one was changed to fit the other, and gameplay (ContactResolver, e_x, r_x, VBA) is untouched.

## Bat-path validation
Measured with `SwingPathMetrics` on the visual bat's sweet spot (1 ms samples, launch −0.15 s to contact). Tolerances were set before final tuning: bat speed ±10 %, swing length ±15 %, attack angle ±4°, attack direction ±6°, tilt ±6°.

| Metric | Reference (2024) | Presentation swing | Within tolerance |
|---|---|---|---|
| Bat speed | 68.9 mph | 63.6 mph (−7.7 %) | yes |
| Swing length (bat head) | 6.91 ft | 7.1 ft (+2.7 %) | yes |
| Attack angle | +8.9° | +5.7° | yes |
| Attack direction | +4.2° (oppo) | +9.4° (oppo) | yes, but with known slack: ±6° around +4.2° would also accept a slightly pulled swing |
| Swing path tilt | 34.0° | 33.5° | yes |

The first hand-authored swing measured 52 mph, 8.2 ft (sweet spot), +12°, 11° pull and 53° tilt, which is why the path is now constrained by the metrics. A review then caught that swing length had been measured on the sweet spot. Re-measured on the head it was 8.3 ft, so the arc was re-derived.

Clip time equals gameplay time: gameplay maps launch → contact onto `SwingDuration` = 0.15 s, the same as the clip.

Attack direction "+ = toward first base" means opposite field for right-handed hitters and pull for left-handed ones.

The test `ReferenceMotionTests.PresentationBatPathMatchesTheStatcastReference` guards these values. They are approximate, not identical: the hands and arms limit how far the bat can follow the ideal arc just after contact.

## Constraints and checks (`ReferenceMotionTests`)
- Planted feet move less than 1 cm during their plant windows (swing front foot, swing rear foot until launch, pitcher pivot foot, pitcher lead foot).
- The top hand stays within 6 cm of the grip target and the bottom hand within 15 cm of the top hand throughout the swing, for both hands.
- Knees never bend backward, elbows never fold through themselves (≥ 20°), and no joint position is NaN.
- No joint moves more than 5 cm per 1 ms of game time.
- Left-handed mirroring is exact.
- PlayMode: the visual bat's sweet spot is within 3 cm of the authoritative contact point at the contact time, and the pitcher's ball is within 5 cm of the release point at release.

## Transitions
These are not reference motion. They avoid snapping between pitches.
- After the recovery, the pitcher walks back to the rubber with a two-step blend; feet move one at a time, with a lift.
- After a swing or a take, the batter steps back into his stance the same way.
- If a new pitch is thrown while a figure is still away from its starting pose, it steps into the new motion: 0.6 s for the pitcher, 0.35 s for the batter. A very early throw makes the pitcher take two quick steps back (≈ 0.6 s for 1.4 m).

## Proportions
Segment lengths follow standard adult ratios (Drillis & Contini / Winter) for H = 1.85 m:
- upper arm 0.33 m (0.186 H; it was 0.29 m)
- forearm 0.26 m
- thigh 0.44 m
- shank 0.43 m
- shoulder height 1.50 m
- hip-joint height 0.93 m

They are generic, not the reference players' own (one is 1.75 m, the other ≈ 1.93 m).
