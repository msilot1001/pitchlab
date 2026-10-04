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
        private PoseSequence _delivery;
        private MannequinPose _stance, _load, _stride, _contact, _follow;
        private readonly MannequinPose _pose = new MannequinPose(), _from = new MannequinPose(), _to = new MannequinPose();

        private HittingPitch _pitch;          // the pitch the presentation is currently showing
        private SwingInput? _swing;
        private Vector3 _pitcherRoot, _batterRoot, _batterShift;
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

            _delivery = new PoseSequence(
                (-Mathf.Max((float)_lab.DeliveryLead, 0.8f), MannequinPoses.PitchSet), (-0.75f, MannequinPoses.PitchLegLift), (-0.3f, MannequinPoses.PitchStride),
                (-0.12f, MannequinPoses.PitchArmCock), (0f, MannequinPoses.PitchRelease), (0.35f, MannequinPoses.PitchFollowThrough),
                (1.4f, MannequinPoses.PitchFollowThrough), (2.4f, MannequinPoses.PitchSet));
            _stance = MannequinPoses.BatStance;
            _load = MannequinPoses.BatLoad;
            _stride = MannequinPoses.BatStride;

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

            PlacePitcher(PitchPresets.All[0].ToInitialState().Position);
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
            _batterShift = Vector3.zero;
            _banner = string.Empty;
            _landing.Hide();
            _cue.Hide();
            _trail.Clear();
            _baseballCamera.ShowBatting();
            if (pitch != null) PlacePitcher(pitch.Flight.First.Position);
        }

        /// <summary>Puts the pitcher on the mound so that the throwing hand reaches the simulated release point at release.</summary>
        private void PlacePitcher(Vector3d release)
        {
            var mound = new Vector3(0f, 0f, (float)PitchingGeometry.RubberFrontY + 0.25f);
            _pitcher.transform.SetPositionAndRotation(mound, Quaternion.Euler(0f, 180f, 0f));
            _pitcher.ApplyPose(MannequinPoses.PitchRelease);
            Vector3 offset = SimulationSpace.ToUnity(release) - _pitcher.BallAnchor.position;
            // Presentation fit: slide the figure (not the simulation) within limits; the mound is not modelled in height.
            offset = new Vector3(Mathf.Clamp(offset.x, -1f, 1f), Mathf.Clamp(offset.y, -0.35f, 0.35f), Mathf.Clamp(offset.z, -1f, 1f));
            _pitcherRoot = mound + offset;
            _pitcher.transform.position = _pitcherRoot;
        }

        private void PlaceBatter()
        {
            bool left = _lab.Swing.Side == BatterSide.Left;
            _batter.LeftHanded = left;
            // In the box beside the plate, facing it: third-base side (−X) for a right-handed hitter.
            _batterRoot = new Vector3(left ? 0.85f : -0.85f, 0f, 0.35f);
            _batter.transform.SetPositionAndRotation(_batterRoot, Quaternion.Euler(0f, left ? -90f : 90f, 0f));
        }

        private void AnimatePitcher(double t)
        {
            _delivery.Sample(double.IsNegativeInfinity(t) ? _delivery.Start : (float)t, _pose);
            _pitcher.ApplyPose(_pose);
        }

        private void OnSwing(SwingInput swing, ContactResult result)
        {
            _swing = swing;
            // Presentation reflects the authoritative swing: timing turns the body (early = more open, pulled), the vertical
            // offset tilts the finish (undercut = higher, topped = lower).
            double timing = double.IsNaN(result.TimingError) ? 0.0 : result.TimingError;
            float yaw = Mathf.Clamp((float)(timing * _lab.Swing.SprayRate * Mathf.Rad2Deg), -30f, 30f);
            _contact = MannequinPoses.BatContact;
            _contact.Pelvis.y += yaw;
            _contact.Chest.y += 0.5f * yaw;
            _follow = MannequinPoses.BatFollowThrough;
            _follow.Pelvis.y += yaw;
            double vertical = double.IsNaN(result.VerticalOffset) ? 0.0 : result.VerticalOffset;
            _follow.GripDirection = Quaternion.AngleAxis(Mathf.Clamp((float)(vertical * 600.0), -20f, 20f), Vector3.right) * _follow.GripDirection;

            // Where the bat should be at contact time: on the ball for a hit; where the player aimed (the PCI, at the contact
            // plane) for a miss, so the bat visibly passes under, over or beside the ball.
            Vector3 target = SimulationSpace.ToUnity(result.IsContact ? result.BattedBall.Position : new Vector3d(swing.PciX, _pitch.ContactPlaneY, swing.PciZ));
            _batter.transform.position = _batterRoot;
            _batter.ApplyPose(_contact);
            Vector3 delta = target - _sweetSpot.position;
            _batterShift = new Vector3(Mathf.Clamp(delta.x, -0.3f, 0.3f), Mathf.Clamp(delta.y, -0.3f, 0.3f), Mathf.Clamp(delta.z, -0.4f, 0.4f));
        }

        private void AnimateBatter(double t)
        {
            float shift = 0f;
            if (_pitch == null || double.IsNegativeInfinity(t))
            {
                MannequinPose.Blend(_stance, _stance, 0f, _pose);
            }
            else if (!_swing.HasValue)
            {
                PreSwing(t, _pose);
            }
            else
            {
                double s = _swing.Value.StartTime, c = s + _lab.Swing.SwingDuration;
                if (t < s) PreSwing(t, _pose);
                else if (t < c)
                {
                    PreSwing(s, _from);
                    MannequinPose.Blend(_from, _contact, Ease((t - s) / (c - s)), _pose);
                }
                else if (t < c + 0.3) MannequinPose.Blend(_contact, _follow, Ease((t - c) / 0.3), _pose);
                else if (t < c + 2.5) MannequinPose.Blend(_follow, _follow, 0f, _pose);
                else MannequinPose.Blend(_follow, _stance, Ease((t - c - 2.5) / 0.8), _pose);
                // Slide toward the contact point around contact only (bump: 0 → 1 at contact → 0).
                shift = (float)(t < s - 0.05 ? 0.0 : t < c ? (t - s + 0.05) / (c - s + 0.05) : Math.Max(0.0, 1.0 - (t - c) / 0.6));
            }

            _batter.transform.position = _batterRoot + _batterShift * Mathf.SmoothStep(0f, 1f, shift);
            _batter.ApplyPose(_pose);
        }

        /// <summary>Stance → load before release → stride as the pitch arrives; back to stance once it has passed.</summary>
        private void PreSwing(double t, MannequinPose into)
        {
            double stride = Math.Max(0.15, _pitch.IdealContactTime - _lab.Swing.SwingDuration - 0.02);
            if (t < -0.5) MannequinPose.Blend(_stance, _stance, 0f, into);
            else if (t < 0.0) MannequinPose.Blend(_stance, _load, Ease((t + 0.5) / 0.5), into);
            else if (t < stride) MannequinPose.Blend(_load, _stride, Ease(t / stride), into);
            else if (t < _pitch.Flight.Duration + 0.3) MannequinPose.Blend(_stride, _stride, 0f, into);
            else MannequinPose.Blend(_stride, _stance, Ease((t - _pitch.Flight.Duration - 0.3) / 0.6), into);
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
