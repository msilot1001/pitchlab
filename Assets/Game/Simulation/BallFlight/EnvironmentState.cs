using System;
using Pitchlab.Simulation.Core;

namespace Pitchlab.Simulation.BallFlight
{
    /// <summary>Uniform atmosphere and gravity for a flight (SI). Gravity acts along -Z.</summary>
    public readonly struct EnvironmentState
    {
        /// <summary>Standard gravity, m/s².</summary>
        public const double StandardGravity = 9.80665;

        /// <summary>Air density, kg/m³.</summary>
        public readonly double AirDensity;
        /// <summary>Gravitational acceleration magnitude, m/s².</summary>
        public readonly double Gravity;
        /// <summary>Air velocity relative to the ground, m/s, in the simulation frame.</summary>
        public readonly Vector3d Wind;

        public EnvironmentState(double airDensity, double gravity, Vector3d wind)
        {
            if (!(airDensity >= 0.0)) throw new ArgumentOutOfRangeException(nameof(airDensity));
            if (double.IsNaN(gravity)) throw new ArgumentOutOfRangeException(nameof(gravity));
            AirDensity = airDensity;
            Gravity = gravity;
            Wind = wind;
        }

        /// <summary>Sea level, 21 °C (70 °F), 50 % relative humidity, still air: ρ ≈ 1.194 kg/m³.</summary>
        public static EnvironmentState Standard => FromWeather(21.0, 101325.0, 0.5, Vector3d.Zero);

        /// <summary>No air: gravity only.</summary>
        public static EnvironmentState Vacuum => new EnvironmentState(0.0, StandardGravity, Vector3d.Zero);

        /// <summary>Environment from station weather, using <see cref="MoistAirDensity"/>.</summary>
        public static EnvironmentState FromWeather(double temperatureCelsius, double stationPressurePascals, double relativeHumidity, Vector3d wind) =>
            new EnvironmentState(MoistAirDensity(temperatureCelsius, stationPressurePascals, relativeHumidity), StandardGravity, wind);

        /// <summary>
        /// Moist-air density (kg/m³) as an ideal mixture of dry air and water vapour. Saturation vapour pressure
        /// uses the Buck (1996) equation. <paramref name="relativeHumidity"/> is a 0–1 fraction.
        /// </summary>
        public static double MoistAirDensity(double temperatureCelsius, double stationPressurePascals, double relativeHumidity)
        {
            const double dryAirGasConstant = 287.058;     // J/(kg·K)
            const double waterVapourGasConstant = 461.495; // J/(kg·K)
            double t = temperatureCelsius;
            double saturation = 611.21 * Math.Exp((18.678 - t / 234.5) * (t / (257.14 + t)));
            double vapour = relativeHumidity * saturation;
            double kelvin = t + 273.15;
            return (stationPressurePascals - vapour) / (dryAirGasConstant * kelvin) + vapour / (waterVapourGasConstant * kelvin);
        }
    }
}
