using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Tracking;

namespace Pitchlab.Tests.Statcast
{
    /// <summary>The committed Statcast fixture (Assets/Game/Tests/Fixtures/Statcast) with per-game environments.</summary>
    public static class StatcastFixture
    {
        public const string Development = "development";
        public const string Holdout = "holdout";
        public const string Path = "Assets/Game/Tests/Fixtures/Statcast/statcast_validation.csv";
        public const string ActiveSpinPath = "Assets/Game/Tests/Fixtures/Statcast/active_spin_2024.csv";

        public readonly struct Row
        {
            public readonly string Role;
            public readonly string PitcherId;
            public readonly StatcastPitch Pitch;
            public readonly string Family;

            public Row(string role, string pitcherId, StatcastPitch pitch)
            {
                Role = role;
                PitcherId = pitcherId;
                Pitch = pitch;
                Family = FamilyOf(pitch.PitchType);
            }
        }

        private static List<Row> _rows;

        public static IReadOnlyList<Row> Rows => _rows ??= Load();

        /// <summary>
        /// Game-time air from the MLB Stats API feed (temperature, elevation; README). Humidity is not reported and is
        /// assumed 50 %; pressure is the standard atmosphere at the park elevation. Wind is not applied by default.
        /// </summary>
        public static EnvironmentState EnvironmentFor(string role) => role == Development
            ? Weather(fahrenheit: 72.0, elevationFeet: 1086.0)  // Chase Field, roof closed, no wind
            : Weather(fahrenheit: 46.0, elevationFeet: 55.0);   // Yankee Stadium, open, 16 mph out to LF

        private static EnvironmentState Weather(double fahrenheit, double elevationFeet) => EnvironmentState.FromWeather(
            (fahrenheit - 32.0) * 5.0 / 9.0,
            EnvironmentState.StandardAtmospherePressure(Units.FeetToMeters(elevationFeet)),
            0.5,
            Vector3d.Zero);

        /// <summary>
        /// Reported game wind (MLB feed: 16 mph "Out To LF") as a uniform vector: from home plate toward left field,
        /// i.e. along (−X, +Y)/√2 in the simulation frame. Field-level wind is unknown; used only as a sensitivity case.
        /// </summary>
        public static Vector3d HoldoutReportedWind => Units.MphToMetersPerSecond(16.0) * new Vector3d(-Math.Sqrt(0.5), Math.Sqrt(0.5), 0.0);

        /// <summary>Grouping only (pitch labels never reach the physics).</summary>
        public static string FamilyOf(string pitchType)
        {
            switch (pitchType)
            {
                case "FF": return "Four-seam";
                case "SI": case "FT": return "Sinker";
                case "FC": return "Cutter";
                case "SL": case "ST": case "SV": return "Slider/sweeper";
                case "CU": case "KC": case "CS": return "Curveball";
                case "CH": case "FS": case "FO": return "Changeup/splitter";
                default: return "Other";
            }
        }

        private static Dictionary<(string, string), double> _activeSpin;

        /// <summary>
        /// Savant 2024 Hawk-Eye spin-based active spin (fraction of spin that is transverse) for this pitcher and pitch
        /// type, or NaN when the leaderboard has no value.
        /// </summary>
        public static double ActiveSpin(string pitcherId, string pitchType)
        {
            if (_activeSpin == null)
            {
                _activeSpin = new Dictionary<(string, string), double>();
                string[] lines = File.ReadAllLines(ActiveSpinPath);
                string[] header = SplitQuoted(lines[0]);
                for (int line = 1; line < lines.Length; line++)
                {
                    string[] f = SplitQuoted(lines[line]);
                    for (int c = 3; c < header.Length && c < f.Length; c++)
                        if (f[c].Length > 0) _activeSpin[(f[1], header[c])] = double.Parse(f[c], CultureInfo.InvariantCulture) / 100.0;
                }
            }

            string column;
            switch (pitchType)
            {
                case "FF": column = "active_spin_fourseam"; break;
                case "SI": column = "active_spin_sinker"; break;
                case "FC": column = "active_spin_cutter"; break;
                case "CH": column = "active_spin_changeup"; break;
                case "FS": column = "active_spin_splitter"; break;
                case "CU": case "KC": column = "active_spin_curve"; break;
                case "SL": column = "active_spin_slider"; break;
                case "ST": column = "active_spin_sweeper"; break;
                case "SV": column = "active_spin_slurve"; break;
                default: return double.NaN;
            }

            return _activeSpin.TryGetValue((pitcherId, column), out double value) ? value : double.NaN;
        }

        // Minimal CSV split for the leaderboard export (quoted names contain commas).
        private static string[] SplitQuoted(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char ch in line)
            {
                if (ch == '"') quoted = !quoted;
                else if (ch == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(ch);
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private static List<Row> Load()
        {
            string[] lines = File.ReadAllLines(Path);
            string[] header = lines[0].Split(',');
            var index = new Dictionary<string, int>();
            for (int i = 0; i < header.Length; i++) index[header[i]] = i;

            var rows = new List<Row>(lines.Length - 1);
            for (int line = 1; line < lines.Length; line++)
            {
                if (lines[line].Length == 0) continue;
                string[] f = lines[line].Split(',');
                double D(string name) => double.Parse(f[index[name]], CultureInfo.InvariantCulture);
                var pitch = new StatcastPitch(f[index["pitch_type"]], f[index["p_throws"]], D("release_speed"), D("release_pos_x"), D("release_pos_y"), D("release_pos_z"),
                    D("release_extension"), D("vx0"), D("vy0"), D("vz0"), D("ax"), D("ay"), D("az"), D("release_spin_rate"), D("spin_axis"),
                    D("pfx_x"), D("pfx_z"), D("plate_x"), D("plate_z"));
                rows.Add(new Row(f[index["role"]], f[index["pitcher"]], pitch));
            }

            if (rows.Count == 0) throw new InvalidOperationException("Statcast fixture is empty.");
            return rows;
        }
    }
}
