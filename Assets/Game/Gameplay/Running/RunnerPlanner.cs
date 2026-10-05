using System;
using System.Collections.Generic;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Running
{
    /// <summary>
    /// How a runner gets to a base from where he is (TASK-007): the same plan for prediction and execution. From his current
    /// leg, distance and velocity he runs the leg's remainder, rounding each intermediate base at no more than his rounding
    /// speed, and finishes at the destination stopped on it — or through it for first base (an overrun the rules allow) and
    /// home (scoring). A leg he will round is the banana route; a leg ending at his destination is straight.
    /// </summary>
    public static class RunnerPlanner
    {
        /// <summary>One planned leg.</summary>
        public readonly struct PlannedLeg
        {
            public PlannedLeg(BaseLeg leg, PathMotion motion)
            {
                Leg = leg;
                Motion = motion;
            }

            public BaseLeg Leg { get; }
            public PathMotion Motion { get; }
        }

        /// <summary>Does he run through <paramref name="destination"/> rather than stop on it?</summary>
        public static bool RunsThrough(Base destination, bool throughFirst) => destination == Base.Home || (destination == Base.First && throughFirst);

        /// <summary>Speed he should reach the end of the current leg with, going on to <paramref name="destination"/>.</summary>
        public static double EndSpeed(RunnerProfile p, Base legEnd, Base destination, bool throughFirst) =>
            legEnd != destination ? p.RoundingSpeed : RunsThrough(destination, throughFirst) ? double.NaN : 0.0;

        /// <summary>
        /// The plan from (leg, d, v) at <paramref name="time"/> to <paramref name="destination"/>, going forward (the current leg's
        /// end, then on around the bases). <paramref name="destination"/> must be the current leg's end or a later base.
        /// </summary>
        public static List<PlannedLeg> Plan(RunnerProfile p, BaseLeg leg, double d, double v, double time, Base destination, bool throughFirst)
        {
            var plan = new List<PlannedLeg>();
            for (int guard = 0; guard < 4; guard++)
            {
                // A stop on second or third is a slide; anything else brakes normally.
                bool slide = leg.To == destination && destination != Base.First && destination != Base.Home;
                var motion = new PathMotion(p, time, d, v, leg.Length, EndSpeed(p, leg.To, destination, throughFirst), double.NaN,
                    slide ? p.SlideDeceleration : double.NaN);
                plan.Add(new PlannedLeg(leg, motion));
                if (leg.To == destination) return plan;
                time = motion.ArrivalTime;
                v = motion.EndSpeed;
                d = 0.0;
                Base next = BaseLeg.Bases(leg.To);
                leg = BaseLeg.Of(leg.To, next != destination);
            }

            throw new ArgumentException("Destination is not ahead of the runner.", nameof(destination));
        }

        /// <summary>When he would touch <paramref name="destination"/> going there now.</summary>
        public static double ArrivalTime(RunnerProfile p, BaseLeg leg, double d, double v, double time, Base destination, bool throughFirst)
        {
            List<PlannedLeg> plan = Plan(p, leg, d, v, time, destination, throughFirst);
            return plan[plan.Count - 1].Motion.ArrivalTime;
        }

        /// <summary>Order of bases around the diamond for a runner who started at <paramref name="from"/> (home last when it
        /// is a destination): used to keep runners in order.</summary>
        public static int Progress(Base from, Base b) => b == Base.Home && from != Base.Home ? 4 : (int)b;

        /// <summary>The base after <paramref name="b"/> as a destination index, or −1 past home.</summary>
        public static Base Next(Base b) => BaseLeg.Bases(b);
    }
}
