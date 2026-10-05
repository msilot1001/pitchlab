using System;
using UnityEngine;

namespace Pitchlab.Presentation
{
    /// <summary>
    /// A reference motion as key poses on normalized time u ∈ [0, 1], with named semantic markers (e.g. "plant",
    /// "contact", "release"). Sampling uses a Catmull-Rom spline through the keys (smooth, no stop at each key).
    /// Gameplay maps authoritative event times onto markers with <see cref="MotionTimeline"/>; the clip itself has no
    /// seconds, so it works for any pitch speed or swing timing.
    /// </summary>
    public sealed class MotionClip
    {
        private readonly float[] _u;
        private readonly string[] _names;
        private readonly MannequinPose[] _poses;

        public MotionClip(string name, params (string Marker, float U, MannequinPose Pose)[] keys)
        {
            if (keys.Length < 2) throw new ArgumentException("A motion needs at least two keys.", nameof(keys));
            Name = name;
            _u = new float[keys.Length];
            _names = new string[keys.Length];
            _poses = new MannequinPose[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0 && !(keys[i].U > keys[i - 1].U)) throw new ArgumentException($"Key {i} ({keys[i].Marker}) is not after key {i - 1}.", nameof(keys));
                _u[i] = keys[i].U;
                _names[i] = keys[i].Marker;
                _poses[i] = keys[i].Pose;
            }
        }

        public string Name { get; }

        /// <summary>u of the named marker.</summary>
        public float Marker(string name)
        {
            int i = Array.IndexOf(_names, name);
            if (i < 0) throw new ArgumentException($"{Name} has no marker '{name}'.", nameof(name));
            return _u[i];
        }

        /// <summary>Pose at <paramref name="u"/> (clamped to the first/last key) written into <paramref name="into"/>; no allocation.</summary>
        public void Sample(float u, MannequinPose into)
        {
            int last = _u.Length - 1;
            if (u <= _u[0]) { MannequinPose.Blend(_poses[0], _poses[0], 0f, into); return; }
            if (u >= _u[last]) { MannequinPose.Blend(_poses[last], _poses[last], 0f, into); return; }
            int i = 0;
            while (u > _u[i + 1]) i++;
            float t = (u - _u[i]) / (_u[i + 1] - _u[i]);
            MannequinPose.Interpolate(_poses[Math.Max(i - 1, 0)], _poses[i], _poses[i + 1], _poses[Math.Min(i + 2, last)], t, true, into);
        }
    }

    /// <summary>
    /// Monotone piecewise-linear map from event time (s) to a clip's normalized time u: anchors pin markers to
    /// authoritative times (release, contact, …) and presentation time-warps between them.
    /// </summary>
    public sealed class MotionTimeline
    {
        private readonly double[] _time = new double[8];
        private readonly float[] _u = new float[8];
        private int _count;

        public void Clear() => _count = 0;

        /// <summary>Adds an anchor; anchors must be added in increasing time and non-decreasing u.</summary>
        public void Add(double time, float u)
        {
            if (_count > 0 && !(time > _time[_count - 1] && u >= _u[_count - 1]))
                throw new ArgumentException($"Anchor ({time}, {u}) is not after ({_time[_count - 1]}, {_u[_count - 1]}).");
            if (_count == _time.Length) throw new InvalidOperationException("Too many anchors.");
            _time[_count] = time;
            _u[_count++] = u;
        }

        public float U(double time)
        {
            if (_count == 0) return 0f;
            if (time <= _time[0]) return _u[0];
            for (int i = 1; i < _count; i++)
                if (time <= _time[i]) return Mathf.Lerp(_u[i - 1], _u[i], (float)((time - _time[i - 1]) / (_time[i] - _time[i - 1])));
            return _u[_count - 1];
        }
    }
}
