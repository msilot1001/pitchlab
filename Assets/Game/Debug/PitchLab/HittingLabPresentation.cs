using System;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Presentation;
using Pitchlab.Simulation.Batting;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
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
        private Vector3 _gripShift;         // figure-frame hand shift that puts the bat on the contact point (or PCI)
        private float _swingYaw, _finishTilt;
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
        public bool LandingShown => _landingShown;
        public bool ContactShown => _contactShown;
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
            ResetForPitch(null);
        }

        private Camera _view;

        private void LateUpdate() => FrameUpdate();

        /// <summary>Shows the controller's current state (after it has rendered this frame). Tests call it directly.</summary>
        public void FrameUpdate()
        {
            double t = _lab.CurrentPitch == null ? double.NegativeInfinity : _lab.RenderedSimTime;
            if (!ReferenceEquals(_lab.CurrentPitch, _pitch)) ResetForPitch(_lab.CurrentPitch);
            if (_lab.LastSwing.HasValue && !_swing.HasValue) OnSwing(_lab.LastSwing.Value, _lab.LastResult.Value);

            AnimatePitcher(t);
            AnimateBatter(t);

            // Before release the ball is in the pitcher's hand; from release on the controller renders the authoritative
            // trajectory (pitch, then batted ball).
            bool held = _pitch == null || t < 0.0;
            Transform ball = _lab.BallTransform;
            if (held) ball.position = _pitcher.BallAnchor.position;
            _trail.emitting = !held && !_landingShown;
            // Keep the ball a few pixels wide however far it flies (centre stays on the authoritative trajectory).
            float distance = Vector3.Distance(_view.transform.position, ball.position);
            float d = Mathf.Max(_ballDiameter, 0.005f * distance * _view.fieldOfView / 30f);
            ball.localScale = new Vector3(d, d, d);
            _trail.widthMultiplier = 0.8f * d;
            _trail.time = _contactShown ? 0.6f : 0.18f;
            bool shadow = _contactShown && !_landingShown && new Vector2(ball.position.x, ball.position.z).magnitude > 8f;
            _shadow.gameObject.SetActive(shadow);
            if (shadow)
            {
                _shadow.position = new Vector3(ball.position.x, 0.02f, ball.position.z);
                _shadow.localScale = new Vector3(2.5f * d, 0.005f, 2.5f * d);
            }

            if (_lab.LastResult is ContactResult r && r.IsContact)
            {
                if (!_contactShown && t >= r.BattedBall.Time) ShowContact(r);
                BattedBallResult flight = _lab.LastBattedBall;
                if (!_landingShown && flight != null && flight.Metrics.Landed && t >= flight.Flight.Final.Time) ShowLanding(flight);
            }
            else if (_lab.LastResult.HasValue && _swing.HasValue && t >= _swing.Value.StartTime + _lab.Swing.SwingDuration)
            {
                _banner = _lab.ResultSummary;
            }
        }

        private void ResetForPitch(HittingPitch pitch)
        {
            _pitch = pitch;
            _swing = null;
            _contactShown = false;
            _landingShown = false;
            _gripShift = Vector3.zero;
            _swingYaw = _finishTilt = 0f;
            _banner = string.Empty;
            _landing.Hide();
            _cue.Hide();
            _trail.Clear();
            _baseballCamera.ShowBatting();
            MannequinPose.Blend(_pitcherShown, _pitcherShown, 0f, _pitcherFrom);
            MannequinPose.Blend(_batterShown, _batterShown, 0f, _batterFrom);
            if (pitch != null) PlacePitcher(pitch.Flight.First.Position);
            _deliveryClip.Sample(_deliveryClip.Marker("set"), _from);
            _pitcherTransition = pitch != null && Away(_pitcherFrom, _from);
            _swingClip.Sample(_uStance, _from);
            _batterTransition = pitch != null && Away(_batterFrom, _from);
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
            Vector3 Figure(Vector3 world) => new Vector3(rubber.x - world.x, world.y - rubber.y, rubber.z - world.z);   // root faces home (yaw 180)
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
                correction = Vector3.ClampMagnitude(correction + new Vector3(-miss.x, miss.y, -miss.z), 0.2f);
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

        private void PlaceBatter()
        {
            bool left = _lab.Swing.Side == BatterSide.Left;
            _batter.LeftHanded = left;
            // Stance position (Baseball Savant batter positioning, reference hitter 2024): hips 24.7 in behind the front of
            // the plate and 27.7 in off its inside edge; the figure faces the pitcher, plate on its right (mirrored for lefties).
            float off = (float)Units.InchesToMeters(8.5 + 27.7);
            _batterRoot = new Vector3(left ? off : -off, 0f, (float)(PitchingGeometry.PlateFrontY - Units.InchesToMeters(24.7)));
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
            _swingYaw = Mathf.Clamp((float)(timing * _lab.Swing.SprayRate * Mathf.Rad2Deg), -30f, 30f);   // early (−) opens the hips further
            double vertical = double.IsNaN(result.VerticalOffset) ? 0.0 : result.VerticalOffset;
            _finishTilt = Mathf.Clamp((float)(vertical * 600.0), -20f, 20f);

            // Where the bat should be at contact time: on the ball for a hit; where the player aimed (the PCI, at the contact
            // plane) for a miss, so the bat visibly passes under, over or beside the ball. Reached by moving the hands (the
            // feet stay planted), relative to the reference contact pose.
            Vector3 target = SimulationSpace.ToUnity(result.IsContact ? result.BattedBall.Position : new Vector3d(swing.PciX, _pitch.ContactPlaneY, swing.PciZ));
            _batter.transform.position = _batterRoot;
            _swingClip.Sample(_uContact, _pose);
            _pose.Pelvis.y += _swingYaw;
            _pose.Chest.y += 0.5f * _swingYaw;   // exactly the pose shown at contact (see AnimateBatter)
            _batter.ApplyPose(_pose);
            _gripShift = Vector3.ClampMagnitude(_batter.WorldToFigure(target - _sweetSpot.position), 0.25f);
        }

        private void AnimateBatter(double t)
        {
            float weight = 0f;
            if (_pitch == null || double.IsNegativeInfinity(t))
            {
                _swingClip.Sample(_uStance, _pose);
            }
            else if (!_swing.HasValue)
            {
                PreSwing(t, _pose);
            }
            else
            {
                double s = _swing.Value.StartTime, c = s + _lab.Swing.SwingDuration;
                if (t < s) PreSwing(t, _pose);
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
                    weight = (float)(t < c ? (t - s) / (c - s) : Math.Max(0.0, 1.0 - (t - c) / 0.5));
                    float turn = (t < c ? weight : 1f) * (1f - back);
                    _pose.Pelvis.y += _swingYaw * turn;
                    _pose.Chest.y += 0.5f * _swingYaw * turn;
                    if (t > c) _pose.GripDirection = Quaternion.AngleAxis(-_finishTilt * Mathf.Clamp01((float)(t - c) / 0.3f) * (1f - back), Vector3.right) * _pose.GripDirection;
                    if (back > 0f)
                    {
                        _swingClip.Sample(_uStance, _from);
                        MannequinPose.StepBlend(_pose, _from, back, _pose);
                    }
                }
            }

            _pose.GripPoint += _gripShift * Mathf.SmoothStep(0f, 1f, weight);
            if (_batterTransition && !double.IsNegativeInfinity(t)) Transition(_batterFrom, t, 0.35);
            _batter.transform.position = _batterRoot;
            _batter.ApplyPose(_pose);
            MannequinPose.Blend(_pose, _pose, 0f, _batterShown);
        }

        /// <summary>Clip position before any swing: the reference timing relative to the expected contact, holding at launch-ready.</summary>
        private float PreSwingU(double t) =>
            Mathf.Min(_uLaunch, ReferenceMotions.SwingU((float)(t - _pitch.IdealContactTime)));

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
            _baseballCamera.Impulse(0.012f, 0.15f);
            _baseballCamera.Follow(_lab.BallTransform);
            _trail.Clear();
            _banner = _lab.ResultSummary;
        }

        private void ShowLanding(BattedBallResult flight)
        {
            _landingShown = true;
            BattedBallMetrics m = flight.Metrics;
            Vector3 point = SimulationSpace.ToUnity(new Vector3d(m.LandingX, m.LandingY, 0.0));
            _landing.Show(point, $"{Units.MetersToFeet(m.Distance):0} ft", _view);
        }

        private static float Ease(double u) => Mathf.SmoothStep(0f, 1f, (float)Math.Max(0.0, Math.Min(1.0, u)));

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_banner)) return;
            _bannerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 40f), _banner, _bannerStyle);
        }
    }
}
