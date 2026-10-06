using System;
using System.Linq;
using NUnit.Framework;
using Pitchlab.Gameplay.Hitting;
using Pitchlab.Gameplay.Play;
using Pitchlab.Gameplay.Players;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.BallFlight;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Pitching;

namespace Pitchlab.Tests
{
    /// <summary>
    /// TASK-024: hit by pitch, foul tip, check swing — the rules (OBR), the batter's body, the catcher's glove and the offer
    /// point, decided by gameplay from the authoritative pitch, swing and contact.
    /// </summary>
    public class PlateEventsTests
    {
        private static readonly EnvironmentState Env = EnvironmentState.Standard;
        private static readonly double Tall = PlayerProfile.ReferenceHeightInches;

        private static HittingPitch Aimed(double x, double z, PitchInput preset) => HittingPitch.Create(PitchTargets.Aim(preset, x, z, Env), Env);

        // ── The rules ─────────────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void AHitByPitchEndsThePlateAppearanceAndAFoulTipIsAStrike()
        {
            Assert.AreEqual((default(Count), PlateAppearanceEnd.HitByPitch), new Count(3, 2).After(PitchOutcome.HitByPitch));
            Assert.AreEqual((new Count(1, 1), PlateAppearanceEnd.None), new Count(1, 0).After(PitchOutcome.FoulTip), "a strike");
            Assert.AreEqual((default(Count), PlateAppearanceEnd.Strikeout), new Count(0, 2).After(PitchOutcome.FoulTip), "strike three — unlike a foul");
            Assert.AreEqual((new Count(0, 2), PlateAppearanceEnd.None), new Count(0, 2).After(PitchOutcome.Foul));
        }

        [Test]
        public void HitByPitchWithTheBasesLoadedForcesInARun()
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), 7);
            g.Set(3, Half.Top, 1, BaseOccupancy.Loaded, 0, 0);
            PlayerProfile batter = g.Batter, onFirst = g.RunnerOn(Simulation.Field.Base.First), onSecond = g.RunnerOn(Simulation.Field.Base.Second);
            Assert.AreEqual(PlateAppearanceEnd.HitByPitch, g.Pitch(PitchOutcome.HitByPitch));
            Assert.AreEqual((1, 0), (g.AwayScore, g.HomeScore), "forced home (OBR 5.05(b)(2))");
            Assert.IsTrue(g.Bases.Equals(BaseOccupancy.Loaded));
            Assert.AreEqual(1, g.Outs);
            Assert.AreSame(batter, g.RunnerOn(Simulation.Field.Base.First));
            Assert.AreSame(onFirst, g.RunnerOn(Simulation.Field.Base.Second));
            Assert.AreSame(onSecond, g.RunnerOn(Simulation.Field.Base.Third));
            StringAssert.Contains("hit by pitch", g.Completed.Last().Result);
        }

        [Test]
        public void AFoulTipOnStrikeTwoIsAStrikeout()
        {
            var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), 7);
            g.Pitch(PitchOutcome.CalledStrike);
            g.Pitch(PitchOutcome.SwingingStrike);
            Assert.AreEqual(PlateAppearanceEnd.Strikeout, g.Pitch(PitchOutcome.FoulTip));
            Assert.AreEqual(1, g.Outs);
            StringAssert.Contains("foul tip", g.Completed.Last().Result);
        }

        // ── The batter's body ─────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void AnInsidePitchHitsTheBatterOnHisSideOnly()
        {
            // A right-handed hitter stands on the −X side (catcher's view): a fastball well inside at chest height reaches him;
            // the same pitch misses a left-handed hitter, and a pitch down the middle touches neither.
            HittingPitch inside = Aimed(-0.85, 1.2, PitchPresets.FourSeam), middle = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            BodyHit? hit = BatterBody.FirstTouch(inside, BatterSide.Right, Tall);
            Assert.IsTrue(hit.HasValue, "inside to a right-handed hitter");
            Assert.Less(hit.Value.BallCentre.X, 0.0);
            Assert.Less(hit.Value.BallCentre.Y, PitchingGeometry.PlateFrontY, "past the front of the plate");
            Assert.IsNull(BatterBody.FirstTouch(inside, BatterSide.Left, Tall), "a left-handed hitter is on the other side");
            Assert.IsNull(BatterBody.FirstTouch(middle, BatterSide.Right, Tall));
            Assert.IsNull(BatterBody.FirstTouch(middle, BatterSide.Left, Tall));
            // Mirrored for a left-handed hitter.
            Assert.IsTrue(BatterBody.FirstTouch(Aimed(0.85, 1.2, PitchPresets.FourSeam), BatterSide.Left, Tall).HasValue);
        }

        [Test]
        public void TheTouchIsTheFirstContactWithTheBody()
        {
            // At the touch the ball reaches the part (within a step), not before.
            HittingPitch inside = Aimed(-0.85, 1.2, PitchPresets.FourSeam);
            BodyHit hit = BatterBody.FirstTouch(inside, BatterSide.Right, Tall).Value;
            BatterBody.Part part = BatterBody.Parts(BatterSide.Right, Tall).First(p => p.Name == hit.Part);
            double r = BallProperties.Baseball.Radius;
            Assert.IsTrue(part.Touches(inside.Flight.StateAt(hit.Time).Position, r));
            foreach (BatterBody.Part p in BatterBody.Parts(BatterSide.Right, Tall))
                Assert.IsFalse(p.Touches(inside.Flight.StateAt(hit.Time - BatterBody.Step).Position, r), $"{p.Name} one step earlier");
        }

        private static readonly double Duration = SwingParameters.Default.SwingDuration;

        [Test]
        public void TheCallOfAPitchThatTouchesTheBatter()
        {
            HittingPitch ball = Aimed(-0.85, 1.2, PitchPresets.FourSeam), strike = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            const double touchTime = 0.45;
            var touch = new BodyHit(touchTime, new Vector3d(-0.8, -0.2, 1.2), "torso");   // beside the plate
            double bottom = StrikeZone.Bottom, top = StrikeZone.Top;
            var miss = new ContactResult(ContactOutcome.MissOffBarrel, 0.0, double.NaN, double.NaN, 0.0, default);
            SwingInput SwingWithOfferAt(double offer) => new SwingInput(offer + SwingInput.OfferLead - Duration, -0.5, 1.0);
            Assert.AreEqual(PitchOutcome.HitByPitch, PitchOutcomes.BeforePlay(ball, null, null, Duration, touch, bottom, top), "no swing, out of the zone: first base");
            Assert.AreEqual(PitchOutcome.Ball, PitchOutcomes.BeforePlay(ball, null, null, Duration, null, bottom, top), "not touched: the call");
            // The ball is dead at the touch: a swing counts only if it had reached the offer point by then.
            Assert.AreEqual(PitchOutcome.SwingingStrike, PitchOutcomes.BeforePlay(ball, SwingWithOfferAt(touchTime - 0.01), miss, Duration, touch, bottom, top), "he had swung: a strike (Strike (e))");
            Assert.AreEqual(PitchOutcome.SwingingStrike, PitchOutcomes.BeforePlay(ball, SwingWithOfferAt(touchTime), miss, Duration, touch, bottom, top), "offered at the touch");
            Assert.AreEqual(PitchOutcome.HitByPitch, PitchOutcomes.BeforePlay(ball, SwingWithOfferAt(touchTime + 0.01), miss, Duration, touch, bottom, top), "his swing came after the touch: no swing");
            SwingInput checkedSwing = SwingWithOfferAt(touchTime - 0.01).CheckedAt(touchTime - 0.03);
            var checkedResult = new ContactResult(ContactOutcome.CheckedSwing, 0.0, double.NaN, double.NaN, 0.0, default);
            Assert.AreEqual(PitchOutcome.HitByPitch, PitchOutcomes.BeforePlay(ball, checkedSwing, checkedResult, Duration, touch, bottom, top), "a checked swing is no swing");
            // In the zone when it touches him (5.05(b)(2)(A)): the ball's place at the touch, not where it crossed the plate.
            var overPlate = new BodyHit(touchTime, new Vector3d(0.15, 0.2, 0.9), "hands");
            Assert.AreEqual(PitchOutcome.CalledStrike, PitchOutcomes.BeforePlay(ball, null, null, Duration, overPlate, bottom, top), "touched inside the zone: a strike (Strike (f))");
            Assert.AreEqual(PitchOutcome.CalledStrike, PitchOutcomes.BeforePlay(ball, checkedSwing, checkedResult, Duration, overPlate, bottom, top));
            Assert.AreEqual(PitchOutcome.HitByPitch, PitchOutcomes.BeforePlay(strike, null, null, Duration, touch, bottom, top), "crossed the zone's front, touched him outside it");
        }

        [Test]
        public void TheZoneVolumeIsOverThePlate()
        {
            double b = StrikeZone.Bottom, t = StrikeZone.Top, front = PitchingGeometry.PlateFrontY;
            Assert.IsTrue(StrikeZone.ContainsAt(new Vector3d(0.0, 0.5 * front, 0.5 * (b + t)), b, t));
            Assert.IsFalse(StrikeZone.ContainsAt(new Vector3d(0.0, -0.2, 0.5 * (b + t)), b, t), "behind the plate");
            Assert.IsFalse(StrikeZone.ContainsAt(new Vector3d(0.0, front + 0.2, 0.5 * (b + t)), b, t), "in front of it");
            Assert.IsFalse(StrikeZone.ContainsAt(new Vector3d(0.5, 0.5 * front, 0.5 * (b + t)), b, t), "beside it");
            Assert.IsFalse(StrikeZone.ContainsAt(new Vector3d(0.0, 0.5 * front, t + 0.2), b, t), "above it");
        }

        // ── The foul tip ──────────────────────────────────────────────────────────────────────────────────────────────

        private static ContactResult Tipped(HittingPitch pitch, Vector3d deflection, double speedFactor = 0.8)
        {
            BallState at = pitch.IdealContactState;
            var v = at.Velocity * speedFactor + deflection;
            return new ContactResult(ContactOutcome.Contact, 0.0, 0.05, 0.06, 0.05, new BallState(at.Time, at.Position, v, at.Spin));
        }

        [Test]
        public void ATipStraightBackIsCaughtAndOneOffTheMittIsNot()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            Assert.IsTrue(FoulTips.IsCaught(pitch, Tipped(pitch, Vector3d.Zero)), "along the pitch's path: into the waiting mitt");
            Assert.IsFalse(FoulTips.IsCaught(pitch, Tipped(pitch, new Vector3d(0.0, 0.0, 12.0))), "deflected up and over the mitt");
            Assert.IsFalse(FoulTips.IsCaught(pitch, Tipped(pitch, new Vector3d(0.0, 40.0, 0.0), 0.0)), "a ball going forward is no tip");
            Assert.IsFalse(FoulTips.IsCaught(pitch, new ContactResult(ContactOutcome.MissUnder, 0.0, 0.0, 0.1, 0.0, default)), "no contact");
            // The reach is the mitt's: just inside caught, just outside not (straight-line geometry at his plane).
            Vector3d glove = FoulTips.GloveAt(pitch).Value;
            ContactResult side = Tipped(pitch, new Vector3d(4.0, 0.0, 0.0));
            double off = (FoulTips.DirectPathAt(side.BattedBall).Value - glove).Length;
            Assert.AreEqual(off <= FoulTips.GloveReach, FoulTips.IsCaught(pitch, side));
            Assert.AreEqual(PitchOutcome.FoulTip, PitchOutcomes.BeforePlay(pitch, new SwingInput(0.2, 0.0, 0.75), Tipped(pitch, Vector3d.Zero), Duration, null, StrikeZone.Bottom, StrikeZone.Top));
            Assert.IsNull(PitchOutcomes.BeforePlay(pitch, new SwingInput(0.2, 0.0, 0.75), Tipped(pitch, new Vector3d(0.0, 0.0, 12.0)), Duration, null, StrikeZone.Bottom, StrikeZone.Top), "a foul back: the play decides");
        }

        [Test]
        public void APitchInTheDirtIsNeverAFoulTip()
        {
            HittingPitch dirt = Aimed(0.0, -0.3, PitchPresets.Curveball);
            Assert.IsNull(FoulTips.GloveAt(dirt), "it never reaches his plane in the air");
            BallState at = dirt.Flight.StateAt(dirt.Flight.Final.Time - 0.05);
            Assert.IsFalse(FoulTips.IsCaught(dirt, new ContactResult(ContactOutcome.Contact, 0.0, 0.0, 0.06, 0.05, new BallState(at.Time, at.Position, at.Velocity * 0.8, at.Spin))));
        }

        // ── The check swing ───────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void ASwingStoppedBeforeTheOfferIsNoSwing()
        {
            HittingPitch pitch = Aimed(0.0, 0.75, PitchPresets.FourSeam);
            SwingParameters p = SwingParameters.Default;
            double start = pitch.IdealContactTime - p.SwingDuration;
            var swing = new SwingInput(start, 0.0, 0.75);
            ContactResult full = ContactResolver.Resolve(pitch, swing, p);
            Assert.IsTrue(full.IsContact, "the unchecked swing hits it");

            double offer = swing.OfferTime(p.SwingDuration);
            Assert.AreEqual(start + p.SwingDuration - SwingInput.OfferLead, offer, 1e-12);
            ContactResult stopped = ContactResolver.Resolve(pitch, swing.CheckedAt(offer - 0.001), p);
            Assert.AreEqual(ContactOutcome.CheckedSwing, stopped.Outcome);
            Assert.IsFalse(PitchOutcomes.Offered(swing.CheckedAt(offer - 0.001), stopped));
            Assert.AreEqual(PitchOutcome.CalledStrike, PitchOutcomes.Of(pitch, swing.CheckedAt(offer - 0.001), stopped, null), "a take: the zone call");
            Assert.AreEqual(ContactOutcome.CheckedSwing, ContactResolver.Resolve(pitch, swing.CheckedAt(offer), p).Outcome, "exactly at the offer point: still in time");
            // Too late: the swing goes on exactly as if unchecked.
            ContactResult late = ContactResolver.Resolve(pitch, swing.CheckedAt(offer + 0.001), p);
            Assert.AreEqual(full.Outcome, late.Outcome);
            Assert.AreEqual(full.BattedBall.Velocity, late.BattedBall.Velocity);
        }

        [Test]
        public void ACheckedSwingOnABallIsABallAndCanBeHitByThePitch()
        {
            HittingPitch inside = Aimed(-0.85, 1.2, PitchPresets.FourSeam);
            var swing = new SwingInput(0.2, -0.3, 1.0, 0.21);
            ContactResult r = ContactResolver.Resolve(inside, swing, SwingParameters.Default);
            Assert.AreEqual(ContactOutcome.CheckedSwing, r.Outcome);
            Assert.AreEqual(PitchOutcome.HitByPitch, PitchOutcomes.BeforePlay(inside, swing, r, Duration, BatterSide.Right, Tall, StrikeZone.Bottom, StrikeZone.Top));
        }

        // ── The CPU batter ────────────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void SimulatedGamesHaveHitByPitchesFoulTipsAndCheckedSwings()
        {
            // The production path (GameSimulator): each event happens, each only as the rules allow it.
            int checks = 0, tips = 0, hbp = 0;
            for (int seed = 6001; seed <= 6006; seed++)
            {
                var g = new GameState(GenericRosters.Away(), GenericRosters.Home(), seed);
                var sim = new GameSimulator(g);
                while (!g.IsOver)
                {
                    PlayerProfile batter = g.Batter;
                    PitchOutcome o = sim.PlayPitch();
                    if (sim.LastContact is ContactResult c && c.Outcome == ContactOutcome.CheckedSwing)
                    {
                        checks++;
                        Assert.That(o, Is.EqualTo(PitchOutcome.Ball).Or.EqualTo(PitchOutcome.CalledStrike).Or.EqualTo(PitchOutcome.HitByPitch), "a checked swing is a take");
                    }

                    if (o == PitchOutcome.FoulTip)
                    {
                        tips++;
                        Assert.IsTrue(sim.LastContact.Value.IsContact);
                    }

                    if (o == PitchOutcome.HitByPitch)
                    {
                        hbp++;
                        Assert.IsTrue(BatterBody.FirstTouch(sim.LastPitch, batter.Bats, batter.HeightInches).HasValue);
                        Assert.IsFalse(StrikeZone.IsStrike(sim.LastPitch, batter.ZoneBottom, batter.ZoneTop));
                    }
                }
            }

            TestContext.WriteLine($"checks {checks}, foul tips {tips}, hit by pitch {hbp}");
            Assert.Greater(checks, 0);
            Assert.Greater(tips, 0);
            Assert.Greater(hbp, 0);
        }

        [Test]
        public void TheCpuBatterChecksOnlyASwingHeTookForAStrikeAndBeforeTheOffer()
        {
            int checks = 0, balls = 0;
            PlayerProfile batter = GenericRosters.Home().Lineup[4];
            SwingParameters p = SwingParameters.For(batter);
            for (int k = 0; k < 400; k++)
            {
                PitchInput preset = PitchPresets.All[k % 5];
                double x = -0.45 + 0.9 * ((k * 37) % 100) / 100.0, z = 0.2 + 1.2 * ((k * 53) % 100) / 100.0;
                HittingPitch pitch = Aimed(x, z, preset);
                SeedStream stream = CpuBatter.StreamFor(11, batter.Id, k, 1);
                BatterPlan plan = CpuBatter.Plan(CpuBatter.Observe(pitch), batter, new Count(1, 1), p, pitch.ContactPlaneY, ref stream);
                if (double.IsNaN(plan.CheckTime)) continue;
                checks++;
                Assert.IsTrue(plan.Swing);
                Assert.GreaterOrEqual(plan.StrikeBelief, 0.5, "he swung at it as a strike");
                Assert.LessOrEqual(plan.CheckTime, plan.SwingStart + p.SwingDuration - SwingInput.OfferLead + 1e-12, "before the offer point");
                Assert.GreaterOrEqual(plan.CheckTime, plan.SwingStart);
                Assert.AreEqual(ContactOutcome.CheckedSwing, ContactResolver.Resolve(pitch, plan.Input.Value, p).Outcome);
                if (!StrikeZone.IsStrike(pitch, batter.ZoneBottom, batter.ZoneTop)) balls++;
            }

            TestContext.WriteLine($"{checks} checked swings in 400 pitches, {balls} of them balls");
            Assert.Greater(checks, 0);
            Assert.Greater(balls, checks / 2, "he checks mostly on balls: what he sees late is the pitch leaving the zone");
        }
    }
}
