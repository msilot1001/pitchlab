using System;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;
using Pitchlab.Simulation.Pitching;
using UnityEngine;

namespace Pitchlab.Sandbox
{
    /// <summary>
    /// Presentation for the Hitting Sandbox: pitcher and batter mannequins, the ball in the pitcher's hand before
    /// release, contact cue, ball-follow camera and landing marker. It only reads <see cref="HittingLabController"/>
    /// state (pitch, swing, contact result, batted-ball flight) on the controller's clock and never changes it: poses
    /// are timed to the authoritative release and contact times, not the other way round.
    /// </summary>
    // LateUpdate already runs after the controller's Update (which places the ball). Order 100 only puts this Start after
    // the controller's and this LateUpdate after BaseballCamera's (camera changes apply next frame).
    [DefaultExecutionOrder(100)]
    public sealed class HittingLabPresentation : MonoBehaviour
    {
        private const float BallVisualScale = 1.6f;   // larger than real for readability; centre stays on the trajectory

        [SerializeField] private HittingLabController _lab;
        [SerializeField] private PlayerMannequin _mannequinPrefab;
        [SerializeField] private BaseballCamera _baseballCamera;

        private PlayerMannequin _pitcher, _batter;
        private Transform _sweetSpot;
        private ContactCue _cue;
        private LandingMarker _landing;
        private TrailRenderer _trail;
        private Transform _shadow;
        private float _ballDiameter;
        // Reference motions (Docs/MOTION_REFERENCE.md): normalized clips whose markers are pinned to authoritative times.
        private MotionClip _swingClip, _deliveryClip;
        private readonly MotionTimeline _deliveryTime = new MotionTimeline();
        private float _uStance, _uLaunch, _uContact, _uSwingEnd;
        private SwingAdjustment _adjust;    // procedural deformation that puts the sweet spot on the contact point (or PCI)
        private float _finishTilt, _targetResidual;
        private Vector3 _target;            // world point the sweet spot aims at, at the contact time
        private TrailRenderer _sweetTrail;  // debug: sweet-spot path
        private DefenseView _defense;
        private RunnerView _runners;
        /// <summary>Runners walk from their bags to their leads between plays in this long (real s; presentation only).</summary>
        private const double LeadWalk = 1.2;
        private bool _situationShown;
        private BaseOccupancy _shownBases;
        private double _leadWalkStart;
        private readonly System.Collections.Generic.List<MeshRenderer> _eventMarkers = new System.Collections.Generic.List<MeshRenderer>();
        private readonly System.Collections.Generic.List<Mesh> _builtMeshes = new System.Collections.Generic.List<Mesh>();
        private readonly MannequinPose _pose = new MannequinPose(), _from = new MannequinPose();
        // Last shown poses: a new pitch thrown before a figure has returned to its start blends from here (no snapping).
        private readonly MannequinPose _pitcherShown = new MannequinPose(), _batterShown = new MannequinPose();
        private readonly MannequinPose _pitcherFrom = new MannequinPose(), _batterFrom = new MannequinPose();
        private bool _pitcherTransition, _batterTransition;

        private HittingPitch _pitch;          // the pitch the presentation is currently showing
        private SwingInput? _swing;
        private Vector3 _pitcherRoot, _batterRoot;
        private bool _contactShown, _landingShown;
        private string _banner = string.Empty;
        private GUIStyle _bannerStyle;

        public PlayerMannequin Pitcher => _pitcher;
        public PlayerMannequin Batter => _batter;
        public RunnerView Runners => _runners;
        /// <summary>After a play ends its runners stay this long before the plate is the batter's again (s).</summary>
        public const double ResultPause = 1.5;
        public bool LandingShown => _landingShown;
        public bool ContactShown => _contactShown;
        /// <summary>Deformation of the reference swing for the current swing, and the sweet spot's miss of its target at contact (m).</summary>
        public SwingAdjustment SwingAdjustment => _adjust;
        public float TargetResidual => _targetResidual;
        /// <summary>The result line currently shown (set every frame from <see cref="Feedback"/>).</summary>
        public string Banner => _banner;
        public DefenseView Defense => _defense;
        public TrailRenderer BallTrail => _trail;

        private void Awake()
        {
            if (_lab == null || _mannequinPrefab == null || _baseballCamera == null)
            {
                UnityEngine.Debug.LogError("HittingLabPresentation is missing a reference; disabling.", this);
                enabled = false;
                return;
            }

            _swingClip = ReferenceMotions.ReferenceRightHandedSwing();
            _uStance = _swingClip.Marker("stance");
            _uLaunch = _swingClip.Marker("launch");
            _uContact = _swingClip.Marker("contact");
            _uSwingEnd = _swingClip.Marker("finish");

            _pitcher = Instantiate(_mannequinPrefab, transform);
            _pitcher.name = "Pitcher";
            _pitcher.Build();
            Equipment.AttachGlove(_pitcher.GloveAnchor);
            _batter = Instantiate(_mannequinPrefab, transform);
            _batter.name = "Batter";
            _batter.Build();
            _sweetSpot = Equipment.AttachBat(_batter.BatAnchor);
            _sweetTrail = _sweetSpot.gameObject.AddComponent<TrailRenderer>();
            _sweetTrail.time = 0.4f;
            _sweetTrail.widthMultiplier = 0.015f;
            _sweetTrail.minVertexDistance = 0.01f;
            _sweetTrail.sharedMaterial = PresentationMaterials.Get(new Color(1f, 0.85f, 0.2f), unlit: true);
            _sweetTrail.emitting = false;

            var cues = new GameObject("ContactCue");
            cues.transform.SetParent(transform, false);
            _cue = cues.AddComponent<ContactCue>();
            var landing = new GameObject("LandingMarker");
            landing.transform.SetParent(transform, false);
            _landing = landing.AddComponent<LandingMarker>();
        }

        private void Start()
        {
            if (!_lab.enabled || _lab.BallTransform == null)
            {
                UnityEngine.Debug.LogError("HittingLabPresentation: the HittingLabController is disabled; disabling.", this);
                enabled = false;
                return;
            }

            _view = _baseballCamera.GetComponent<Camera>();
            Transform ball = _lab.BallTransform;
            float diameter = (float)(2.0 * BallProperties.Baseball.Radius) * BallVisualScale;
            _ballDiameter = diameter;
            ball.localScale = new Vector3(diameter, diameter, diameter);
            // Ground marker under a batted ball (where it is over the field), like broadcast/game ball shadows.
            GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = "BallShadow";
            PlayerMannequin.DestroyCollider(shadow);
            shadow.transform.SetParent(transform, false);
            shadow.GetComponent<MeshRenderer>().sharedMaterial = PresentationMaterials.Get(new Color(0.08f, 0.1f, 0.06f), unlit: true);
            shadow.SetActive(false);
            _shadow = shadow.transform;
            if (!ball.TryGetComponent(out _trail)) _trail = ball.gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.18f;
            _trail.widthMultiplier = diameter * 0.8f;
            _trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            _trail.sharedMaterial = PresentationMaterials.Get(new Color(1f, 1f, 1f), unlit: true);
            _trail.emitting = false;

            PlacePitcher(PitchPresets.All[0].ToInitialState().Position);   // idle: the pitcher waits in the set position
            PlaceBatter();
            _defense = new GameObject("Defense").AddComponent<DefenseView>();
            _defense.transform.SetParent(transform, false);
            _defense.Build(_mannequinPrefab, _pitcher, _pitcherShown, (t, pose) => _deliveryClip.Sample(_deliveryTime.U(t), pose), DefensiveAlignment.Standard);
            _runners = new GameObject("Runners").AddComponent<RunnerView>();
            _runners.transform.SetParent(transform, false);
            _runners.Build(_mannequinPrefab);
            _builtMeshes.AddRange(OutfieldDressing.Build(transform, HittingLabController.Field));
            ResetForPitch(null);
        }

        private Camera _view;

        private void LateUpdate() => FrameUpdate();

        /// <summary>Shows the controller's current state (after it has rendered this frame). Tests call it directly.</summary>
        public void FrameUpdate()
        {
            double t = _lab.CurrentPitch == null ? double.NegativeInfinity : _lab.RenderedSimTime;
            // The next batter steps in (from his side) once the last plate appearance is over and the loop is ready: a new
            // person, in his stance — never the last batter's follow-through mirrored into the other box.
            PlayerProfile atBat = _lab.BatterAt(_lab.RenderedRealtime);
            BatterSide side = atBat?.Bats ?? _lab.Swing.Side;
            if (!ReferenceEquals(atBat, _shownBatter) || _batter.LeftHanded != (side == BatterSide.Left))
            {
                PlaceBatter(side);
                _freshBatter = true;
                _shownBatter = atBat;
            }

            if (!ReferenceEquals(_lab.CurrentPitch, _pitch)) ResetForPitch(_lab.CurrentPitch);
            if (_lab.LastSwing.HasValue && !_swing.HasValue) OnSwing(_lab.LastSwing.Value, _lab.LastResult.Value);
            else if (_lab.LastSwing is SwingInput checkedSwing && _swing.HasValue && !checkedSwing.CheckTime.Equals(_swing.Value.CheckTime)) _swing = checkedSwing;   // checked (TASK-024)

            if (!_defense.DrivesPitcher(_lab.LastDefense, t)) AnimatePitcher(t);
            AnimateBatter(t);
            bool swinging = _swing.HasValue && t >= _swing.Value.StartTime && t <= _swing.Value.StartTime + _lab.Swing.SwingDuration + 0.3;
            _sweetTrail.emitting = _lab.DebugView && swinging;
            if (!_lab.DebugView) _sweetTrail.Clear();

            // Before release the ball is in the pitcher's hand; from release on the controller renders the authoritative
            // trajectory (pitch, then batted ball).
            bool held = _pitch == null || t < 0.0;
            Transform ball = _lab.BallTransform;
            if (held) ball.position = _pitcher.BallAnchor.position;
            BallInPlay inPlay = _lab.LastPlay;
            double possession = _lab.LastFielding?.PossessionTime ?? double.PositiveInfinity;
            LiveDefense defense = _lab.LastDefense;
            // A trail while the ball is loose and actually moving (not in a glove or a hand, not at rest on the grass).
            bool moving = defense != null
                ? defense.AuthorityAt(t) != BallAuthority.Possessed && (defense.BallPositionAt(t) - defense.BallPositionAt(t - 0.05)).Length > 1e-3
                : inPlay == null || t < inPlay.EndTime;
            _trail.emitting = !held && moving;
            // Keep the ball a few pixels wide however far it flies (centre stays on the authoritative trajectory).
            float distance = Vector3.Distance(_view.transform.position, ball.position);
            float d = Mathf.Max(_ballDiameter, 0.005f * distance * _view.fieldOfView / 30f);
            ball.localScale = new Vector3(d, d, d);
            _trail.widthMultiplier = 0.8f * d;
            _trail.time = _contactShown ? 0.6f : 0.18f;
            bool shadow = _contactShown && (defense == null ? t < possession : defense.AuthorityAt(t) != BallAuthority.Possessed) && ball.position.y > 0.15f && new Vector2(ball.position.x, ball.position.z).magnitude > 8f;   // airborne only
            _shadow.gameObject.SetActive(shadow);
            if (shadow)
            {
                _shadow.position = new Vector3(ball.position.x, 0.02f, ball.position.z);
                _shadow.localScale = new Vector3(2.5f * d, 0.005f, 2.5f * d);
            }

            if (_lab.LastResult is ContactResult r && _lab.ContactStands)
            {
                if (!_contactShown && t >= r.BattedBall.Time) ShowContact(r);
                BallInPlay play = _lab.LastPlay;
                if (!_landingShown && play?.FirstGroundContact is BallEvent landing && t >= landing.Time && landing.Time < possession) ShowLanding(play, landing);
            }

            _banner = Feedback(t);
            BattingState state = _pitch == null ? BattingState.Ready : BattingStateMachine.At(t, _pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration);
            // Back to the batting view once the result pause is over (the next throw resets the rest).
            if (state == BattingState.Ready && _pitch != null && _baseballCamera.IsFollowing)
            {
                _baseballCamera.ShowBatting();
                _landing.Hide();
            }

            UpdateEventMarkers(t);

            // Defense (TASK-005): the catcher stands right in front of the batting camera, so here he is shown only when
            // he plays the ball (a dribbler); the primary defender is framed with the ball, which sits in his glove from
            // the possession moment.
            _defense.CatcherVisible = _contactShown && _lab.LastFielding?.Primary == DefensivePosition.C || !_lab.CameraBehindPlate;
            // A pitch not put in play ends in the catcher's glove where he can be seen (TASK-014; presentation only).
            // Not after the pitch touched the batter (a dead ball); a caught foul tip is received like a pitch (TASK-024).
            bool tipped = _lab.LastFoulTip, touched = _lab.LastTouch.HasValue && !_lab.ContactStands;
            bool received = _pitch != null && (!_lab.ContactStands || tipped) && !touched && !double.IsNaN(_catchTime) && !_lab.CameraBehindPlate && !double.IsNegativeInfinity(t);
            _defense.CatcherGloveWeight = received ? Mathf.SmoothStep(0f, 1f, (float)((t - _catchTime + CatcherReach) / CatcherReach)) : 0f;
            // A caught foul tip: the mitt takes it where its direct path crosses his plane (within the mitt, FoulTips.GloveReach).
            Vector3 catchPoint = tipped && _lab.LastResult is ContactResult tipResult && FoulTips.DirectPathAt(tipResult.BattedBall) is Vector3d tipAt
                ? SimulationSpace.ToUnity(tipAt) : _catchPoint;
            _defense.CatcherGlove = catchPoint;
            // Where they stand: the play's alignment while it is shown, the next situation's once it is over.
            LivePlay shown = _lab.LastLive;
            bool returning = shown != null && shown.IsOver && t > shown.EndTime;
            _defense.Alignment = (shown != null && !returning ? shown.Situation : _lab.Situation).Alignment;
            _defense.Clock = _lab.Clock;
            // Once the play is over the fielders jog back to the alignment during the result pause (back before the next pitch
            // can be thrown); whoever has the ball keeps it.
            _defense.Show(returning ? null : _lab.LastDefense, t, _lab.BallTransform, _lab.DebugView);
            // In his glove once it is there (a pitch out of his reach flies on).
            Vector3 glove = _defense.Figure(DefensivePosition.C).GloveAnchor.position;
            double inGlove = tipped && _lab.LastResult is ContactResult tip ? tip.BattedBall.Time + (CatcherGlovePlaneY - tip.BattedBall.Position.Y) / tip.BattedBall.Velocity.Y : _catchTime;
            if (received && t >= inGlove && Vector3.Distance(glove, catchPoint) < CatcherGloveReach) _lab.BallTransform.position = glove;
            _baseballCamera.FollowAlso(_contactShown ? _defense.Focus : null);

            // Runners (TASK-007): the batter figure hands over to the batter-runner when he starts for first.
            // When the play is over (and its result has been shown) the plate is the batter's again.
            LivePlay live = _lab.LastLive;
            bool over = live != null && live.IsOver && t > live.EndTime + ResultPause;
            bool running = !over && live != null && live.RunnerOf(Runner.Batter) != null && t >= live.ContactTime + live.Profile.BatterStartDelay;
            if (_batter.gameObject.activeSelf == running) _batter.gameObject.SetActive(!running);
            if (live != null && !over) _runners.Show(live, t, _batterRoot);
            else
            {
                // Between plays (GameLab): the runners of the game's situation walk from their bags to their leads.
                BaseOccupancy bases = _lab.Situation.Bases;
                double now = _lab.Clock();
                if (!_situationShown || !bases.Equals(_shownBases))
                {
                    _situationShown = true;
                    _shownBases = bases;
                    _leadWalkStart = now;
                }

                // During the delivery they settle into the secondary lead (in place), easing off after an unhit pitch.
                float secondary = _pitch == null || double.IsNegativeInfinity(t) ? 0f
                    : Mathf.SmoothStep(0f, 1f, (float)((t + 0.6) / 0.4)) * (1f - Mathf.SmoothStep(0f, 1f, (float)((t - _pitch.Flight.Duration) / 0.5)));
                _runners.ShowSituation(bases, Mathf.Clamp01((float)((now - _leadWalkStart) / LeadWalk)), secondary);
            }

            if (live != null && !over) _situationShown = false;
        }

        /// <summary>
        /// The compact result line, a pure function of the simulation time (identical at any frame rate). It builds with the
        /// play: timing and contact quality at contact; the fair/foul call at its decisive moment; the carry at the first
        /// bounce; the final distance at rest. Misses and takes read after the pitch.
        /// </summary>
        public string Feedback(double t)
        {
            if (_pitch == null || double.IsNegativeInfinity(t)) return "Click to pitch";
            BattingState state = BattingStateMachine.At(t, _pitch, _lab.LastSwing, _lab.LastResult, _lab.PlayEnd, _lab.Swing.SwingDuration);
            if (state == BattingState.Ready) return "Click to pitch";
            // Touched (TASK-024): the call is the rule's (PitchOutcomes.BeforePlay), shown from the touch.
            if (_lab.LastTouch is BodyHit touch && !_lab.ContactStands)
            {
                if (t < touch.Time) return string.Empty;
                (double bottom, double top) = _lab.Zone;
                PitchOutcome? touchCall = PitchOutcomes.BeforePlay(_pitch, _lab.LastSwing, _lab.LastResult, _lab.Swing.SwingDuration, touch, bottom, top);
                return touchCall == PitchOutcome.HitByPitch ? "Hit by pitch"
                    : touchCall == PitchOutcome.SwingingStrike ? "Swung · hit by the pitch · strike" : "Hit by the pitch in the zone · strike";
            }

            bool zone = StrikeZone.IsStrike(_pitch, _lab.Zone.Bottom, _lab.Zone.Top);
            if (!(_lab.LastResult is ContactResult r) || r.Outcome == ContactOutcome.CheckedSwing)
            {
                string take = _lab.LastResult.HasValue ? "Check swing" : "Take";
                return state == BattingState.Result ? (zone ? $"{take} · called strike" : $"{take} · ball") : string.Empty;
            }

            if (_lab.LastFoulTip) return t >= r.BattedBall.Time ? "Foul tip · strike" : string.Empty;
            string timing = ContactFeedback.Timing(r);
            if (!r.IsContact)
                return t >= _lab.LastSwing.Value.StartTime + _lab.Swing.SwingDuration ? (timing.Length > 0 ? $"Swing and miss · {timing}" : "Swing and miss") : string.Empty;
            if (t < r.BattedBall.Time) return string.Empty;

            string line = $"{Units.MetersPerSecondToMph(r.ExitSpeed):0} mph · {r.LaunchAngleDegrees:0}° · {timing}";
            if (ContactFeedback.Quality(r, _lab.Swing) is ContactQuality q) line += $" · {ContactFeedback.Describe(q)}";
            BallInPlay play = _lab.LastPlay;
            FieldingPlay fielding = _lab.LastFielding;
            // The first take the defense actually held (TASK-021: the fielding solve's possession may have been misplayed).
            BallTake? held = null;
            if (_lab.LastDefense != null)
                foreach (BallTake k in _lab.LastDefense.Takes)
                    if (k.Held)
                    {
                        held = k;
                        break;
                    }

            double possession = _lab.LastDefense != null ? held?.Time ?? double.PositiveInfinity : fielding?.PossessionTime ?? double.PositiveInfinity;
            // The call reads at its decisive moment — or when a defender takes the ball, if that is earlier.
            if (_lab.LastCall is FairFoulResult call && t >= Math.Min(call.At.Time, possession))
            {
                BallInPlayCall shown = fielding?.Call ?? call.Call;
                line = (shown == BallInPlayCall.HomeRun ? "HOME RUN" : shown == BallInPlayCall.Fair ? "FAIR" : "FOUL") + "  " + line;
            }

            if (play.FirstGroundContact is BallEvent landing && landing.Time < possession && t >= landing.Time) line += $" · {Units.MetersToFeet(_lab.ShownCarry):0} ft";
            if (_lab.LastLive != null)
                foreach (Misplay m in _lab.LastLive.Misplays)
                    if (t >= m.Time) line += $" · MISPLAY {Abbreviation(m.Fielder)} {m.What}";
            DefensivePosition? taker = held?.Fielder ?? fielding?.Primary;
            if (t >= possession && taker is DefensivePosition by)
                line += $" · {(_lab.LastLive?.Kind == LivePlay.BallKind.Caught || _lab.LastLive == null && fielding.Intercept.Kind == InterceptKind.FlyCatch ? "caught" : "fielded")} by {Abbreviation(by)}";
            else if (t >= play.EndTime && play.EndPhase == BallPhase.Rest) line += $" (rests {Units.MetersToFeet(play.FinalDistance):0} ft)";
            LiveThrow th = null;
            if (_lab.LastDefense != null)
                foreach (LiveThrow x in _lab.LastDefense.Throws)
                    if (t >= x.ReleaseTime && x.Target != null) th = x;
            if (th != null)
                line += $" · throw to {Abbreviation(th.Target.Value)}{(th.Caught ? t >= th.Catch.Time ? " ✓" : "" : t >= th.FirstContactTime ? " — not caught" : "")}";
            if (_lab.LastCall is FairFoulResult c && c.Call == BallInPlayCall.Fair && play.ClearedFence && t >= play.EndTime && t < possession) line += " · ground-rule double";
            if (LiveText.Calls(_lab.LastLive, t) is string calls && calls.Length > 0) line += $" · {calls}";
            return line;
        }

        public static string Abbreviation(Base b) => Bases.Name(b);

        public static string Abbreviation(DefensivePosition p) => p switch
        {
            DefensivePosition.FirstBase => "1B", DefensivePosition.SecondBase => "2B", DefensivePosition.ThirdBase => "3B",
            DefensivePosition.Shortstop => "SS", DefensivePosition.LeftField => "LF", DefensivePosition.CenterField => "CF",
            DefensivePosition.RightField => "RF", _ => p.ToString(),
        };

        private void ResetForPitch(HittingPitch pitch)
        {
            _defense.EndPitcherReturn();
            _pitch = pitch;
            _swing = null;
            _contactShown = false;
            _landingShown = false;
            _adjust = default;
            _finishTilt = _targetResidual = 0f;
            _sweetTrail.Clear();
            _banner = string.Empty;
            _landing.Hide();
            _cue.Hide();
            _trail.Clear();
            _baseballCamera.ShowBatting();
            MannequinPose.Blend(_pitcherShown, _pitcherShown, 0f, _pitcherFrom);
            MannequinPose.Blend(_batterShown, _batterShown, 0f, _batterFrom);
            if (pitch != null) PlacePitcher(pitch.Flight.First.Position);
            (_catchTime, _catchPoint) = pitch != null ? CatcherCatch(pitch) : (double.NaN, Vector3.zero);
            _deliveryClip.Sample(_deliveryClip.Marker("set"), _from);
            _pitcherTransition = pitch != null && Away(_pitcherFrom, _from);
            _swingClip.Sample(_uStance, _from);
            _batterTransition = pitch != null && !_freshBatter && Away(_batterFrom, _from);
            _freshBatter = false;
        }

        /// <summary>
        /// Fits the reference delivery to this pitch's authoritative release: the figure stands on the rubber (mound top),
        /// the release key's hand target is the simulated release point, and any remaining reach shortfall becomes one small,
        /// constant root offset applied before the wind-up starts (no sliding during the motion).
        /// </summary>
        private void PlacePitcher(Vector3d releaseSim)
        {
            Vector3 release = SimulationSpace.ToUnity(releaseSim);
            var rubber = new Vector3(0f, FieldDressing.MoundTop, (float)PitchingGeometry.RubberFrontY);
            Vector3 plant = ReferenceMotions.FootPlant(0f);
            // A left-hander (TASK-018): the mirrored figure plays the right-handed delivery mirrored, so its release target is
            // the mirror image of his (mirrored, simulated) release point.
            bool left = (_lab.PitchPitcher ?? _lab.Game?.Pitcher)?.Throws == Hand.Left;
            _pitcher.LeftHanded = left;
            float side = left ? -1f : 1f;
            Vector3 Figure(Vector3 world) => new Vector3(side * (rubber.x - world.x), world.y - rubber.y, rubber.z - world.z);   // root faces home (yaw 180)
            float drop = FieldDressing.MoundTop - FieldDressing.MoundHeight(rubber.x - plant.x, rubber.z - plant.z);
            // The figure stands on the rubber; only the release wrist target is corrected (a few passes) until the ball in
            // the hand meets the simulated release point — the feet never move for the fit.
            Vector3 correction = Vector3.zero;
            _pitcher.transform.SetPositionAndRotation(rubber, Quaternion.Euler(0f, 180f, 0f));
            for (int pass = 0; pass < 4; pass++)
            {
                _deliveryClip = ReferenceMotions.ReferenceRightHandedPitchDelivery(Figure(release), drop, correction);
                _deliveryClip.Sample(_deliveryClip.Marker("release"), _pose);
                _pitcher.ApplyPose(_pose);
                Vector3 miss = release - _pitcher.BallAnchor.position;
                correction = Vector3.ClampMagnitude(correction + new Vector3(-side * miss.x, miss.y, -miss.z), 0.2f);
            }

            _deliveryClip = ReferenceMotions.ReferenceRightHandedPitchDelivery(Figure(release), drop, correction);
            ReleaseFitOffset = correction;
            _pitcherRoot = rubber;

            // Wind-up: the throw press (−DeliveryLead) shows "set", release is the authoritative release, then real seconds.
            _deliveryTime.Clear();
            _deliveryTime.Add(-_lab.DeliveryLead, _deliveryClip.Marker("set"));
            _deliveryTime.Add(0.0, _deliveryClip.Marker("release"));
            _deliveryTime.Add(ReferenceMotions.DeliveryEnd, _deliveryClip.Marker("recovery"));
        }

        /// <summary>Release fit (presentation only): figure-frame correction of the release wrist target.</summary>
        public Vector3 ReleaseFitOffset { get; private set; }

        private void OnDestroy()
        {
            foreach (Mesh mesh in _builtMeshes) if (mesh != null) Destroy(mesh);   // runtime meshes are not owned by their GameObjects
        }

        private void PlaceBatter() => PlaceBatter(_lab.Swing.Side);

        /// <summary>A batter who has just stepped in shows his stance until the next pitch (not the last batter's motion).</summary>
        private bool _freshBatter;

        /// <summary>The plane (simulation Y, m) where the crouched catcher's glove takes a pitch (he sets up at −0.76 m).</summary>
        public const double CatcherGlovePlaneY = FoulTips.CatcherPlaneY;
        /// <summary>How long (s) his glove moves to the pitch before it arrives.</summary>
        private const double CatcherReach = 0.3;
        /// <summary>The glove (pocket) takes the ball only if it got within this distance (m) of it — the IK aims the glove
        /// anchor, within ≈ 2 cm (its sideways offset), as for every fielder's take.</summary>
        public const float CatcherGloveReach = 0.12f;
        private double _catchTime = double.NaN;
        private Vector3 _catchPoint;

        /// <summary>When and where the pitch reaches the catcher's glove plane (NaN if it never does).</summary>
        private static (double Time, Vector3 Point) CatcherCatch(HittingPitch pitch)
        {
            TrajectoryResult f = pitch.Flight;
            // In the dirt in front of him: he blocks it where it lands.
            if (f.Final.Position.Y > CatcherGlovePlaneY)
                return f.End == FlightEnd.ReachedGround ? (f.Final.Time, SimulationSpace.ToUnity(f.Final.Position)) : (double.NaN, Vector3.zero);
            double a = f.First.Time, b = f.Final.Time;
            for (int i = 0; i < 50; i++)
            {
                double m = 0.5 * (a + b);
                if (f.StateAt(m).Position.Y > CatcherGlovePlaneY) a = m;
                else b = m;
            }

            return (b, SimulationSpace.ToUnity(f.StateAt(b).Position));
        }
        private PlayerProfile _shownBatter;

        private void PlaceBatter(BatterSide side)
        {
            bool left = side == BatterSide.Left;
            _batter.LeftHanded = left;
            // Stance position (Baseball Savant batter positioning, reference hitter 2024): hips 24.7 in behind the front of
            // the plate and 27.7 in off its inside edge; the figure faces the pitcher, plate on its right (mirrored for lefties).
            float off = (float)(PitchingGeometry.PlateHalfWidth + Units.InchesToMeters(ReferenceMotions.StanceOffPlateEdgeInches));
            _batterRoot = new Vector3(left ? off : -off, 0f, (float)(PitchingGeometry.PlateFrontY - Units.InchesToMeters(ReferenceMotions.StanceBehindPlateFrontInches)));
            _batter.transform.SetPositionAndRotation(_batterRoot, Quaternion.identity);
        }

        private void AnimatePitcher(double t)
        {
            _deliveryClip.Sample(double.IsNegativeInfinity(t) ? _deliveryClip.Marker("set") : _deliveryTime.U(t), _pose);
            // After the recovery the pitcher walks back to the rubber (two steps) and waits in the set position.
            double back = t - ReferenceMotions.DeliveryEnd - 0.8;
            if (back > 0.0)
            {
                _deliveryClip.Sample(_deliveryClip.Marker("set"), _from);
                MannequinPose.StepBlend(_pose, _from, Ease(back / 1.2), _pose);
            }

            if (_pitcherTransition) Transition(_pitcherFrom, t, 0.6);   // e.g. still in the recovery 1.4 m off the rubber
            _pitcher.ApplyPose(_pose);
            MannequinPose.Blend(_pose, _pose, 0f, _pitcherShown);
        }

        /// <summary>Right after a new pitch is thrown, step from the previously shown pose into the new motion.</summary>
        private void Transition(MannequinPose from, double t, double seconds)
        {
            double since = t + _lab.DeliveryLead;
            if (_pitch != null && since < seconds) MannequinPose.StepBlend(from, _pose, Ease(since / seconds), _pose);
        }

        /// <summary>A transition is needed only if the figure was shown away from the motion's start (feet or hips moved).</summary>
        private static bool Away(MannequinPose shown, MannequinPose start) =>
            (shown.LeftFoot - start.LeftFoot).magnitude + (shown.RightFoot - start.RightFoot).magnitude + (shown.PelvisOffset - start.PelvisOffset).magnitude > 0.05f
            || Mathf.Abs(Mathf.DeltaAngle(shown.Pelvis.y, start.Pelvis.y)) > 5f;

        private void OnSwing(SwingInput swing, ContactResult result)
        {
            _swing = swing;
            // Presentation reflects the authoritative swing: timing turns the body (early = more open, pulled), the vertical
            // offset tilts the finish (undercut = higher, topped = lower).
            double timing = double.IsNaN(result.TimingError) ? 0.0 : result.TimingError;
            float swingYaw = Mathf.Clamp((float)(timing * _lab.Swing.SprayRate * Mathf.Rad2Deg), -30f, 30f);   // early (−) opens the hips further
            double vertical = double.IsNaN(result.VerticalOffset) ? 0.0 : result.VerticalOffset;
            _finishTilt = Mathf.Clamp((float)(vertical * 600.0), -20f, 20f);

            // Where the sweet spot should be at contact time: where gameplay says it was — on the swing plane through the
            // PCI, at the ball's depth then (ContactResolver.SweetSpotAtContact). A flush hit puts it on the ball, an
            // off-barrel or under/over hit shows the offset, a miss passes where the player aimed; never snapped to the ball.
            // Reached by a bounded deformation of the reference contact pose (feet stay planted; see SwingTargeting).
            // Outside the timing window (MissTiming) the ball can be metres away; the bat then swings through the PCI at the
            // contact plane.
            Vector3 target = SimulationSpace.ToUnity(result.Outcome == ContactOutcome.MissTiming
                ? new Vector3d(swing.PciX, _pitch.ContactPlaneY, swing.PciZ)
                : ContactResolver.SweetSpotAtContact(_pitch, swing, _lab.Swing));
            _batter.transform.position = _batterRoot;
            _target = target;
            _adjust = SwingTargeting.Solve(_batter, _sweetSpot, _swingClip, _uContact, target, swingYaw, _pose, out _targetResidual);
        }

        /// <summary>How long (s) a checked bat keeps moving while it is braked (presentation).</summary>
        private const double CheckStop = 0.04;

        private void AnimateBatter(double t)
        {
            if (_pitch == null || double.IsNegativeInfinity(t) || _freshBatter)
            {
                _swingClip.Sample(_uStance, _pose);
            }
            else if (!_swing.HasValue)
            {
                PreSwing(t, _pose);
            }
            else
            {
                double s = _swing.Value.StartTime, c = s + _lab.Swing.SwingDuration, stop = _swing.Value.CheckTime;
                if (t < s) PreSwing(t, _pose);
                else if (!double.IsNaN(stop))
                {
                    // Checked (TASK-024): the bat runs on to the check (plus CheckStop while it is braked), holds, then he
                    // resets to his stance. Before the offer point by construction: the pose never reaches contact.
                    double held = Math.Min(t, stop + CheckStop);
                    _swingClip.Sample(Mathf.Lerp(PreSwingU(s), _uContact, (float)((held - s) / (c - s))), _pose);
                    float weight = (float)((held - s) / (c - s));
                    SwingTargeting.Apply(_adjust, Mathf.SmoothStep(0f, 1f, weight), weight, _pose);
                    if (t > stop + 0.3)
                    {
                        _swingClip.Sample(_uStance, _from);
                        MannequinPose.StepBlend(_pose, _from, Ease((t - stop - 0.3) / 0.6), _pose);
                    }
                }
                else
                {
                    // Launch → contact is pinned to the authoritative swing: start wherever the pre-swing motion was at the
                    // press, reach the contact marker exactly at the contact time, then play the finish in real seconds.
                    float u = t < c
                        ? Mathf.Lerp(PreSwingU(s), _uContact, (float)((t - s) / (c - s)))
                        : Mathf.Min(_uSwingEnd, _uContact + (float)(t - c) / (ReferenceMotions.SwingEnd - ReferenceMotions.SwingStart));
                    _swingClip.Sample(u, _pose);
                    // Timing turn and finish tilt fade in with the swing and out with the return to stance.
                    float back = t > c + 2.5 ? Ease((t - c - 2.5) / 1.0) : 0f;
                    float weight = (float)(t < c ? (t - s) / (c - s) : Math.Max(0.0, 1.0 - (t - c) / 0.5));
                    float turn = (t < c ? weight : 1f) * (1f - back);
                    SwingTargeting.Apply(_adjust, Mathf.SmoothStep(0f, 1f, weight), turn, _pose);
                    if (t > c) _pose.GripDirection = Quaternion.AngleAxis(-_finishTilt * Mathf.Clamp01((float)(t - c) / 0.3f) * (1f - back), Vector3.right) * _pose.GripDirection;
                    if (back > 0f)
                    {
                        _swingClip.Sample(_uStance, _from);
                        MannequinPose.StepBlend(_pose, _from, back, _pose);
                    }
                }
            }

            if (_batterTransition && !double.IsNegativeInfinity(t)) Transition(_batterFrom, t, 0.35);
            _batter.transform.position = _batterRoot;
            _batter.ApplyPose(_pose);
            MannequinPose.Blend(_pose, _pose, 0f, _batterShown);
        }

        /// <summary>Clip position before any swing: the reference timing relative to the expected contact, holding at launch-ready.
        /// A pitch that never reaches the contact plane (in the dirt) is timed to the end of its flight.</summary>
        private float PreSwingU(double t) =>
            Mathf.Min(_uLaunch, ReferenceMotions.SwingU((float)(t - (_pitch.ReachesContactPlane ? _pitch.IdealContactTime : _pitch.Flight.Final.Time))));

        /// <summary>Stance → load → stride → plant toward the expected contact; after a take, back to the stance.</summary>
        private void PreSwing(double t, MannequinPose into)
        {
            _swingClip.Sample(PreSwingU(t), into);
            double passed = t - _pitch.Flight.Duration - 0.3;
            if (passed > 0.0)
            {
                _swingClip.Sample(_uStance, _from);
                MannequinPose.StepBlend(into, _from, Ease(passed / 1.0), into);
            }
        }

        private void ShowContact(ContactResult r)
        {
            _contactShown = true;
            Vector3 point = SimulationSpace.ToUnity(r.BattedBall.Position);
            _cue.Play(point, (float)Math.Min(1.0, r.ExitSpeed / 45.0));
            if (_lab.LastFoulTip) return;   // into the catcher's mitt: the batting view stays (TASK-024)
            _baseballCamera.Impulse(0.012f, 0.15f);
            _baseballCamera.Follow(_lab.BallTransform);
            _trail.Clear();
        }

        private void ShowLanding(BallInPlay play, BallEvent landing)
        {
            _landingShown = true;
            Vector3d p = landing.Before.Position;
            Vector3 point = SimulationSpace.ToUnity(new Vector3d(p.X, p.Y, 0.0));
            _landing.Show(point, $"{Units.MetersToFeet(_lab.ShownCarry):0} ft", _view);
        }

        /// <summary>
        /// Debug (T): a small marker at every event of the play reached so far — ground impacts (white), wall impacts (red),
        /// slide → roll (blue), rest / out of play (yellow), clearing the fence (red). Visual only, pooled, no colliders.
        /// </summary>
        private void UpdateEventMarkers(double t)
        {
            BallInPlay play = _lab.DebugView ? _lab.LastPlay : null;
            int shown = 0;
            if (play != null)
                foreach (BallEvent e in play.Events)
                {
                    if (e.Time > t) break;
                    if (shown == _eventMarkers.Count)
                    {
                        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        go.name = "EventMarker";
                        PlayerMannequin.DestroyCollider(go);
                        go.transform.SetParent(transform, false);
                        go.transform.localScale = Vector3.one * 0.35f;
                        _eventMarkers.Add(go.GetComponent<MeshRenderer>());
                    }

                    MeshRenderer marker = _eventMarkers[shown++];
                    marker.gameObject.SetActive(true);
                    marker.transform.position = SimulationSpace.ToUnity(e.Before.Position);
                    Color color = e.Kind == BallEventKind.GroundImpact ? Color.white
                        : e.Kind == BallEventKind.SlideToRoll ? new Color(0.3f, 0.6f, 1f)
                        : e.Kind == BallEventKind.Rest || e.Kind == BallEventKind.LeftPlay ? new Color(1f, 0.85f, 0.2f)
                        : new Color(1f, 0.25f, 0.2f);
                    Material material = PresentationMaterials.Get(color, unlit: true);
                    if (marker.sharedMaterial != material) marker.sharedMaterial = material;
                }

            for (int i = shown; i < _eventMarkers.Count; i++) _eventMarkers[i].gameObject.SetActive(false);
        }

        /// <summary>Debug event markers currently shown (tests).</summary>
        public int EventMarkersShown
        {
            get
            {
                int n = 0;
                foreach (MeshRenderer m in _eventMarkers) if (m.gameObject.activeSelf) n++;
                return n;
            }
        }

        private static float Ease(double u) => Mathf.SmoothStep(0f, 1f, (float)Math.Max(0.0, Math.Min(1.0, u)));

        private void OnGUI()
        {
            if (_lab.DebugView && _swing.HasValue)
            {
                // Debug (T): where the visual bat aims (contact point on a hit, the PCI on a miss) and how close it gets.
                GUI.Label(new Rect(Screen.width - 430f, 10f, 420f, 60f),
                    $"Visual target ({_target.x:+0.000;-0.000}, {_target.y:0.000}, {_target.z:0.000}) world m\n" +
                    $"Sweet spot at contact: {_targetResidual * 100f:0.0} cm off   turn {_adjust.BodyYaw:+0;-0}°  barrel {_adjust.BatYaw:+0;-0}°/{_adjust.BatPitch:+0;-0}°  hands {_adjust.HandShift.magnitude * 100f:0} cm",
                    GUI.skin.box);
            }

            if (string.IsNullOrEmpty(_banner)) return;
            _bannerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 40f), _banner, _bannerStyle);
        }
    }
}
