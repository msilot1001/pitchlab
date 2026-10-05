using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>What a figure is doing at a moment, from authoritative state only: its ground velocity and the direction it
    /// wants to face (both world, horizontal), and how fast it may turn.</summary>
    public struct MotionSample
    {
        public Vector3 Velocity;
        public Vector3 Facing;
        /// <summary>Turn-rate limit (°/s).</summary>
        public float MaxTurnRate;
    }

    /// <summary>
    /// The presentation state that needs history — the gait phase (∫ cadence dt) and a heading that turns at a limited rate
    /// toward where the figure wants to face — sampled on a fixed time grid from the start of a play and extended lazily to
    /// the requested time. A pure function of play time: identical at any frame rate, in slow motion and when scrubbing back.
    /// No gameplay input but the samples; nothing feeds back into gameplay.
    /// </summary>
    public sealed class MotionTrack
    {
        /// <summary>Grid step (s).</summary>
        public const double Step = 1.0 / 120.0;
        /// <summary>Longest track kept (s); beyond it the last state holds.</summary>
        public const double MaxDuration = 60.0;

        private readonly List<float> _phase = new List<float>(), _yaw = new List<float>();
        private Func<double, MotionSample> _sample;
        private object _key;
        private double _start;

        /// <summary>Starts a new track at <paramref name="start"/> facing <paramref name="initialYaw"/> (°), unless it already
        /// follows <paramref name="key"/> (the play) from that start.</summary>
        public void Follow(object key, double start, float initialYaw, Func<double, MotionSample> sample)
        {
            if (ReferenceEquals(key, _key) && start == _start) return;
            _key = key;
            _start = start;
            _sample = sample;
            _phase.Clear();
            _yaw.Clear();
            _phase.Add(0f);
            _yaw.Add(initialYaw);
        }

        public bool Follows(object key) => ReferenceEquals(key, _key);

        /// <summary>Gait phase (cycles; two steps per cycle) and heading (° about +Y, Unity frame) at <paramref name="time"/>.</summary>
        public (float Phase, float Yaw) At(double time)
        {
            double u = Math.Max(0.0, Math.Min(time - _start, MaxDuration)) / Step;
            int i = (int)Math.Floor(u);
            Extend(i + 1);
            float f = (float)(u - i);
            return (Mathf.Lerp(_phase[i], _phase[i + 1], f), Mathf.LerpAngle(_yaw[i], _yaw[i + 1], f));
        }

        /// <summary>Turn rate (°/s) at <paramref name="time"/> (signed, + clockwise seen from above).</summary>
        public float TurnRate(double time)
        {
            float a = At(time - 0.05).Yaw, b = At(time).Yaw;
            return Mathf.DeltaAngle(a, b) / 0.05f;
        }

        private void Extend(int count)
        {
            while (_yaw.Count <= count)
            {
                int k = _yaw.Count - 1;
                double t = _start + k * Step;
                MotionSample s = _sample(t);
                float speed = new Vector2(s.Velocity.x, s.Velocity.z).magnitude;
                _phase.Add(_phase[k] + (float)Step * 0.5f * FieldingPoser.Cadence(speed) * Mathf.SmoothStep(0f, 1f, speed / 0.4f));
                float yaw = _yaw[k];
                if (new Vector2(s.Facing.x, s.Facing.z).sqrMagnitude > 1e-6f)
                {
                    float want = Mathf.Atan2(s.Facing.x, s.Facing.z) * Mathf.Rad2Deg;
                    yaw += Mathf.Clamp(Mathf.DeltaAngle(yaw, want), -s.MaxTurnRate * (float)Step, s.MaxTurnRate * (float)Step);
                }

                _yaw.Add(yaw);
            }
        }
    }
}
