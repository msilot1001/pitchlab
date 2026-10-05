using System;
using System.Collections.Generic;
using Pitchlab.Gameplay.Fielding;
using Pitchlab.Gameplay.Rules;
using Pitchlab.Simulation.Core;
using Pitchlab.Simulation.Field;

namespace Pitchlab.Gameplay.Play
{
    public enum DefensiveRole
    {
        /// <summary>Stays at (or near) his position.</summary>
        Hold,
        /// <summary>Plays the ball (the primary fielder, or a retriever of a loose ball).</summary>
        Primary,
        /// <summary>Has the ball.</summary>
        Holder,
        CoverBase,
        BackupBase,
        BackupFielder,
        /// <summary>Lines up between the outfielder and the target base to cut the throw off or relay it.</summary>
        Cutoff,
        /// <summary>Goes out toward a deep ball to relay it.</summary>
        Relay,
        /// <summary>Backs up the relay man in line.</summary>
        Trail,
    }

    /// <summary>One defender's job: a role, the base it concerns, and where he goes for it.</summary>
    public readonly struct RoleAssignment
    {
        public RoleAssignment(DefensivePosition position, DefensiveRole role, Base? at, Vector3d point)
        {
            Position = position;
            Role = role;
            At = at;
            Point = point;
        }

        public DefensivePosition Position { get; }
        public DefensiveRole Role { get; }
        public Base? At { get; }
        /// <summary>Where he goes (ground); for Primary/Holder his own route decides.</summary>
        public Vector3d Point { get; }

        public override string ToString()
        {
            string b = At is Base x ? $" {Bases.Name(x)}" : "";
            return Role switch
            {
                DefensiveRole.CoverBase => $"COVER{b}",
                DefensiveRole.BackupBase => $"BACKUP{b}",
                DefensiveRole.BackupFielder => "BACKUP FIELDER",
                DefensiveRole.Cutoff => $"CUTOFF{b}",
                DefensiveRole.Relay => $"RELAY{b}",
                DefensiveRole.Trail => $"TRAIL{b}",
                DefensiveRole.Primary => "PRIMARY",
                DefensiveRole.Holder => "BALL",
                _ => "HOLD",
            };
        }
    }

    /// <summary>
    /// The defense's coordination (TASK-008, Docs/DEFENSIVE_RESPONSIBILITIES.md): who covers each base, who backs up what,
    /// who is the cut-off or relay man for the likely throw, and who holds — a pure function of where the ball is (or will
    /// be fielded), who plays it, the likely throw and the runners. One defender plays the ball; nobody else chases it.
    /// </summary>
    public static class DefensiveCoordinator
    {
        /// <summary>A ball fielded beyond this distance from home is relayed rather than cut off (m; [I] ≈ 300 ft — over the
        /// outfielders' normal depth of 295–322 ft).</summary>
        public const double RelayDepth = 92.0;
        /// <summary>Cut-off for home stands this far up the line from the plate (m; 40–45 ft, REPORTED).</summary>
        public const double HomeCutoffDistance = 13.0;
        /// <summary>Relay man's distance from the outfielder on the line to the base (m; [I]).</summary>
        public const double RelayDistance = 45.0;
        /// <summary>Trail man behind the relay (m; ~30 ft, REPORTED).</summary>
        public const double TrailDistance = 9.0;
        /// <summary>Backups stand this far beyond the base on the throw line (m; [I]).</summary>
        public const double BackupDistance = 12.0;
        /// <summary>Fielder backups stand this far behind the fielder's take (m; [I]).</summary>
        public const double FielderBackupDistance = 10.0;

        /// <summary>The state the coordinator decides from.</summary>
        public readonly struct Situation
        {
            public Situation(DefensivePosition ballPlayer, Vector3d ballPoint, Vector3d fieldedAt, bool infieldBall, bool flyCaught, Base likelyThrow,
                DefensivePosition? throwReceiver = null, Base? throwTarget = null)
            {
                BallPlayer = ballPlayer;
                BallPoint = ballPoint;
                FieldedAt = fieldedAt;
                InfieldBall = infieldBall;
                FlyCaught = flyCaught;
                LikelyThrow = likelyThrow;
                ThrowReceiver = throwReceiver;
                ThrowTarget = throwTarget;
            }

            /// <summary>Who plays or holds the ball.</summary>
            public DefensivePosition BallPlayer { get; }
            /// <summary>Where the ball is played from (the take point, or the holder).</summary>
            public Vector3d BallPoint { get; }
            /// <summary>Where the batted ball was fielded (decides cut-off or relay for the whole play).</summary>
            public Vector3d FieldedAt { get; }
            /// <summary>Fielded by an infielder, pitcher or catcher.</summary>
            public bool InfieldBall { get; }
            /// <summary>A caught fly (nobody needs to cover for the batter-runner).</summary>
            public bool FlyCaught { get; }
            /// <summary>The base the throw is most likely to go to.</summary>
            public Base LikelyThrow { get; }
            /// <summary>The receiver of a throw on its way (he keeps that job).</summary>
            public DefensivePosition? ThrowReceiver { get; }
            /// <summary>The base that throw goes to (null: to the cut-off or relay man).</summary>
            public Base? ThrowTarget { get; }
        }

        public static RoleAssignment[] Assign(in Situation s, DefensiveAlignment alignment)
        {
            var roles = new RoleAssignment[DefensiveAlignment.Count];
            var taken = new bool[DefensiveAlignment.Count];
            DefensivePosition player = s.BallPlayer;
            void Set(DefensivePosition p, DefensiveRole role, Base? at, Vector3d point)
            {
                if (taken[(int)p]) return;
                if (role == DefensiveRole.CoverBase && Covered(at.Value)) return;   // one per base
                roles[(int)p] = new RoleAssignment(p, role, at, point);
                taken[(int)p] = true;
            }

            bool Covered(Base b) => Array.Exists(roles, r => r.Role == DefensiveRole.CoverBase && r.At == b);
            Set(player, DefensiveRole.Primary, null, s.BallPoint);
            Vector3d ball = s.BallPoint;
            bool leftSide = ball.X < 0.0;
            Vector3d fielded = s.FieldedAt;
            bool deep = !s.InfieldBall && new Vector3d(fielded.X, fielded.Y, 0.0).Length > RelayDepth;
            Vector3d Bag(Base b) => FieldLayout.BasePosition(b);
            Base target = s.LikelyThrow;

            // --- a throw on its way: the receiver keeps his job (the base he covers, or the cut-off/relay spot) ---------------
            if (s.ThrowReceiver is DefensivePosition receiver && s.ThrowTarget is Base thrownTo)
                Set(receiver, DefensiveRole.CoverBase, thrownTo, Bag(thrownTo));

            // --- the likely throw's cut-off or relay (outfield balls, while an outfielder has or plays the ball) ---------------
            bool outfielderHasIt = DefensiveDecision.IsOutfielder(player);
            if (!s.InfieldBall && deep)
            {
                // Relay: SS on balls to the left side and centre, 2B on the right side; the other middle infielder trails.
                if (outfielderHasIt && s.ThrowTarget == null)
                {
                    DefensivePosition relay = fielded.X <= 2.0 ? DefensivePosition.Shortstop : DefensivePosition.SecondBase;
                    DefensivePosition trail = relay == DefensivePosition.Shortstop ? DefensivePosition.SecondBase : DefensivePosition.Shortstop;
                    if (s.ThrowReceiver == trail) (relay, trail) = (trail, relay);
                    Vector3d dir = Unit(Bag(target) - ball);
                    double length = (Bag(target) - ball).Length;
                    Vector3d at = ball + dir * Math.Min(RelayDistance, 0.5 * length);
                    Set(relay, DefensiveRole.Relay, target, at);
                    Set(trail, DefensiveRole.Trail, target, at + dir * TrailDistance);
                }

                // The first baseman: the cut-off for a play at home (in line from the relay), else he trails the batter-runner
                // to second.
                if (target == Base.Home)
                    Set(DefensivePosition.FirstBase, DefensiveRole.Cutoff, Base.Home, CutoffPoint(ball, Base.Home));
                else
                    Set(DefensivePosition.FirstBase, DefensiveRole.CoverBase, Base.Second, Bag(Base.Second));
            }
            else if (!s.InfieldBall && outfielderHasIt && s.ThrowTarget == null)
            {
                DefensivePosition cutoff = s.ThrowReceiver ?? target switch
                {
                    Base.Home => player == DefensivePosition.LeftField ? DefensivePosition.ThirdBase : DefensivePosition.FirstBase,
                    Base.Third => DefensivePosition.Shortstop,
                    _ => player == DefensivePosition.RightField ? DefensivePosition.SecondBase : DefensivePosition.Shortstop,
                };
                Set(cutoff, DefensiveRole.Cutoff, target, CutoffPoint(ball, target));
            }

            // --- base coverage ---------------------------------------------------------------------------------------------------
            // Home: the catcher, or the pitcher when the catcher plays the ball.
            Set(player == DefensivePosition.C ? DefensivePosition.P : DefensivePosition.C, DefensiveRole.CoverBase, Base.Home, Bag(Base.Home));
            // On an outfield ball with the first baseman busy (the cut-off for home, trailing to second, or holding the ball as
            // the cut-off man) the second baseman covers first behind the batter-runner and the shortstop second.
            bool infieldBall = s.InfieldBall;
            bool firstBasemanBusy = !infieldBall && taken[(int)DefensivePosition.FirstBase];
            // Second: of SS and 2B, the one not playing the ball (on infield balls the pivot rule: ball to the left side → 2B,
            // to the pitcher → SS); the one not cutting off on an outfield single.
            DefensivePosition[] secondOrder = s.InfieldBall
                ? (player != DefensivePosition.P && (leftSide || player == DefensivePosition.ThirdBase || player == DefensivePosition.Shortstop)
                    ? new[] { DefensivePosition.SecondBase, DefensivePosition.Shortstop }
                    : new[] { DefensivePosition.Shortstop, DefensivePosition.SecondBase })
                : (player == DefensivePosition.RightField || firstBasemanBusy
                    ? new[] { DefensivePosition.Shortstop, DefensivePosition.SecondBase }
                    : new[] { DefensivePosition.SecondBase, DefensivePosition.Shortstop });
            void CoverSecond()
            {
                foreach (DefensivePosition p in secondOrder)
                    if (!taken[(int)p] && !Covered(Base.Second))
                    {
                        Set(p, DefensiveRole.CoverBase, Base.Second, Bag(Base.Second));
                        break;
                    }
            }

            // First: the first baseman; on an infield ball the pitcher when the first baseman plays it; on an outfield ball the
            // second baseman when the first baseman is busy.
            void CoverFirst()
            {
                foreach (DefensivePosition p in infieldBall && player == DefensivePosition.FirstBase
                             ? new[] { DefensivePosition.P }
                             : new[] { DefensivePosition.FirstBase, DefensivePosition.SecondBase })
                    if (!taken[(int)p] && !Covered(Base.First))
                    {
                        Set(p, DefensiveRole.CoverBase, Base.First, Bag(Base.First));
                        break;
                    }
            }

            if (firstBasemanBusy)
            {
                CoverFirst();
                CoverSecond();
            }
            else
            {
                CoverSecond();
                CoverFirst();
            }

            // Second still open (a deep ball with a play at home: SS relay, 2B trail, 1B home cut-off): the outfielder farthest
            // from the ball goes to second [S]. First stays open on such a ball (the batter-runner is past it) [S].
            if (!Covered(Base.Second))
            {
                DefensivePosition? far = null;
                foreach (DefensivePosition of in new[] { DefensivePosition.LeftField, DefensivePosition.CenterField, DefensivePosition.RightField })
                    if (!taken[(int)of] && (far == null || (alignment[of] - ball).Length > (alignment[far.Value] - ball).Length)) far = of;
                if (far is DefensivePosition f) Set(f, DefensiveRole.CoverBase, Base.Second, Bag(Base.Second));
            }

            // Third: the third baseman, else the shortstop, else the pitcher.
            foreach (DefensivePosition p in new[] { DefensivePosition.ThirdBase, DefensivePosition.Shortstop, DefensivePosition.P })
                if (!taken[(int)p] && !Covered(Base.Third))
                {
                    Set(p, DefensiveRole.CoverBase, Base.Third, Bag(Base.Third));
                    break;
                }

            // --- backups -----------------------------------------------------------------------------------------------------------
            if (s.InfieldBall)
            {
                // Infield ball: RF backs up first, CF second, LF third (beyond the base on the line from the ball); the pitcher
                // backs up home when the play is there (the outfielders back up the other bases).
                Set(DefensivePosition.RightField, DefensiveRole.BackupBase, Base.First, Behind(ball, Base.First));
                Set(DefensivePosition.CenterField, DefensiveRole.BackupBase, Base.Second, Behind(ball, Base.Second));
                Set(DefensivePosition.LeftField, DefensiveRole.BackupBase, Base.Third, Behind(ball, Base.Third));
                if (target == Base.Home) Set(DefensivePosition.P, DefensiveRole.BackupBase, Base.Home, Behind(ball, Base.Home));
            }
            else
            {
                // Outfield ball: the adjacent outfielders back up the fielder; the pitcher backs up the likely throw's base.
                Vector3d away = Unit(new Vector3d(ball.X, ball.Y, 0.0));
                Vector3d behind = ball + away * FielderBackupDistance;
                foreach (DefensivePosition of in Adjacent(player))
                    Set(of, DefensiveRole.BackupFielder, null, behind + (of == DefensivePosition.CenterField ? Vector3d.Zero : Side(away, of) * 3.0));
                Set(DefensivePosition.P, DefensiveRole.BackupBase, target, Behind(ball, target));
                // The far outfielder backs up second (unless the pitcher already does).
                if (target != Base.Second)
                    foreach (DefensivePosition of in new[] { DefensivePosition.LeftField, DefensivePosition.CenterField, DefensivePosition.RightField })
                        if (!taken[(int)of])
                        {
                            Set(of, DefensiveRole.BackupBase, Base.Second, Behind(ball, Base.Second));
                            break;
                        }
            }

            // --- everyone else holds -------------------------------------------------------------------------------------------
            for (int i = 0; i < roles.Length; i++)
                if (!taken[i]) roles[i] = new RoleAssignment((DefensivePosition)i, DefensiveRole.Hold, null, alignment[(DefensivePosition)i]);
            return roles;
        }

        /// <summary>Cut-off spot for a throw from <paramref name="from"/> to <paramref name="b"/>: in line, 13 m up from the
        /// plate for home; otherwise midway, 12–30 m from the base.</summary>
        public static Vector3d CutoffPoint(Vector3d from, Base b)
        {
            Vector3d bag = FieldLayout.BasePosition(b);
            Vector3d toThrower = new Vector3d(from.X - bag.X, from.Y - bag.Y, 0.0);
            double length = toThrower.Length;
            Vector3d dir = length > 1e-6 ? toThrower / length : new Vector3d(0.0, 1.0, 0.0);
            double d = b == Base.Home ? HomeCutoffDistance : Math.Max(12.0, Math.Min(30.0, 0.5 * length));
            return bag + dir * Math.Min(d, Math.Max(0.0, length - 5.0));
        }

        /// <summary>A backup spot: <see cref="BackupDistance"/> beyond the base on the line from the ball.</summary>
        public static Vector3d Behind(Vector3d ball, Base b)
        {
            Vector3d bag = FieldLayout.BasePosition(b);
            return bag + Unit(bag - ball) * BackupDistance;
        }

        private static IEnumerable<DefensivePosition> Adjacent(DefensivePosition of)
        {
            switch (of)
            {
                case DefensivePosition.LeftField: yield return DefensivePosition.CenterField; break;
                case DefensivePosition.RightField: yield return DefensivePosition.CenterField; break;
                case DefensivePosition.CenterField:
                    yield return DefensivePosition.LeftField;
                    yield return DefensivePosition.RightField;
                    break;
            }
        }

        private static Vector3d Side(Vector3d away, DefensivePosition of)
        {
            var left = new Vector3d(-away.Y, away.X, 0.0);
            return of == DefensivePosition.LeftField ? left : -1.0 * left;
        }

        private static Vector3d Unit(Vector3d v)
        {
            var flat = new Vector3d(v.X, v.Y, 0.0);
            double l = flat.Length;
            return l > 1e-9 ? flat / l : new Vector3d(0.0, 1.0, 0.0);
        }
    }
}
