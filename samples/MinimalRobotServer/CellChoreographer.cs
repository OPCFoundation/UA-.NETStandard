/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Robotics
{
    /// <summary>
    /// Drives the two robots through a coordinated transfer cycle without letting them
    /// collide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cell is divided into two end zones and a shared corridor with one lane per
    /// direction of travel. A robot reserves the zone it is about to enter before it moves
    /// into it, and releases the zone behind it once it has left. An end zone admits one
    /// robot, so a deployed arm always has its half of the cell to itself; a corridor lane
    /// also admits one robot, so two robots travelling opposite ways pass on opposite sides
    /// of the aisle while two travelling the same way queue instead of overtaking.
    /// </para>
    /// <para>
    /// The arm must be folded into its transport envelope before a corridor lane can be
    /// reserved, which is what stops the arms sweeping through each other - the failure the
    /// previous free-running figure-eight paths had, where the platforms cleared by 1.2 m
    /// but the arms reach 1.61 m.
    /// </para>
    /// <para>
    /// Everything here is deterministic and driven by an explicit time step, so the tests
    /// can run a full cycle and assert the separation and reservation invariants directly.
    /// </para>
    /// </remarks>
    public sealed class CellChoreographer
    {
        private const double CruiseSpeed = 0.85;
        private const double Acceleration = 0.7;
        private const double ArrivalTolerance = 0.005;
        private const double GripSeconds = 0.7;
        private const double DwellSeconds = 0.4;
        private const double ProximitySlowdownDistance = 2.0;
        private const double SlowSpeed = 0.3;
        private const double BatteryDrainPerMetre = 0.35;
        private const double BatteryDrainPerSecond = 0.01;
        private const double ChargeRatePerSecond = 6.0;
        private const double ChargeThreshold = 25.0;
        private const double PartHeightOffset = 0.0175;

        private readonly List<RobotAgent> m_robots;
        private readonly List<CellPart> m_parts;
        private readonly Dictionary<CellZone, string> m_reservations = [];
        private readonly Random m_random;
        private readonly double m_faultProbability;
        private double m_now;

        /// <summary>
        /// Creates the choreographer with the cell staged warm: parts waiting on both
        /// stations, so each robot has work from the first tick instead of one of them idling
        /// until the other completes a full traverse.
        /// </summary>
        /// <param name="seed">Seed for the fault injection, so runs are reproducible.</param>
        /// <param name="faultProbability">
        /// The chance that a grip slips and drops the part, from 0 to 1.
        /// </param>
        public CellChoreographer(int seed = 20260801, double faultProbability = 0.06)
        {
            m_random = new Random(seed);
            m_faultProbability = faultProbability;
            m_robots =
            [
                new RobotAgent("R1", CellStation.TableA, CellStation.TableB,
                    CellLayout.DockD1X, CellLayout.TableAX),
                new RobotAgent("R2", CellStation.TableB, CellStation.TableA,
                    CellLayout.DockD2X, CellLayout.TableBX)
            ];
            m_parts =
            [
                new CellPart("Part01", CellStation.TableA, 0),
                new CellPart("Part02", CellStation.TableA, 1),
                new CellPart("Part03", CellStation.TableB, 0)
            ];
            foreach (RobotAgent robot in m_robots)
            {
                robot.HeldZone = ZoneAt(robot.X, robot.Y);
                m_reservations[robot.HeldZone] = robot.Id;
            }
        }

        /// <summary>
        /// The robots in the cell.
        /// </summary>
        public IReadOnlyList<RobotAgent> Robots => m_robots;

        /// <summary>
        /// The parts circulating in the cell.
        /// </summary>
        public IReadOnlyList<CellPart> Parts => m_parts;

        /// <summary>
        /// The cell performance counters.
        /// </summary>
        public CellKpis Kpis { get; } = new();

        /// <summary>
        /// Whether the emergency stop is asserted, which halts all motion.
        /// </summary>
        public bool EmergencyStop { get; set; }

        /// <summary>
        /// The robot holding the eastbound corridor lane, or <c>null</c>.
        /// </summary>
        public string? EastboundLaneOwner =>
            m_reservations.TryGetValue(CellZone.CorridorEastbound, out string? owner)
                ? owner
                : null;

        /// <summary>
        /// The robot holding the westbound corridor lane, or <c>null</c>.
        /// </summary>
        public string? WestboundLaneOwner =>
            m_reservations.TryGetValue(CellZone.CorridorWestbound, out string? owner)
                ? owner
                : null;

        /// <summary>
        /// Advances the whole cell by one time step.
        /// </summary>
        /// <param name="deltaSeconds">The step, in seconds.</param>
        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds <= 0.0)
            {
                return;
            }
            m_now += deltaSeconds;
            Kpis.ElapsedSeconds += deltaSeconds;

            foreach (RobotAgent robot in m_robots)
            {
                if (EmergencyStop)
                {
                    robot.Speed = 0.0;
                    continue;
                }
                AdvanceRobot(robot, deltaSeconds);
                if (robot.State is not (RobotCycleState.Idle or RobotCycleState.Blocked))
                {
                    Kpis.BusySeconds += deltaSeconds / m_robots.Count;
                }
            }

            Kpis.Utilisation = Kpis.ElapsedSeconds > 0.0
                ? Kpis.BusySeconds / Kpis.ElapsedSeconds
                : 0.0;
            UpdateCarriedParts();
        }

        /// <summary>
        /// Returns the pose of a robot's tool centre point.
        /// </summary>
        /// <param name="robot">The robot.</param>
        /// <returns>The tool centre point pose in cell coordinates.</returns>
        public static RigidTransform ToolCentrePointOf(RobotAgent robot)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }
            RigidTransform mount = RobotKinematics.CreateMountPose(
                robot.X, robot.Y, 0.0, robot.HeadingDegrees);
            return RobotKinematics.ComputeToolCentrePoint(mount, robot.Axes);
        }

        /// <summary>
        /// Returns the zone a position belongs to.
        /// </summary>
        /// <param name="x">The X position, in metres.</param>
        /// <param name="y">The Y position, in metres.</param>
        /// <returns>The zone.</returns>
        public static CellZone ZoneAt(double x, double y)        {
            // A dock is its own zone. A robot with no work parks there and releases the end
            // zone, so the station stays free for the other robot to deliver into - without
            // that, an idle robot standing next to its table deadlocks the whole cell.
            if (IsNear(x, y, CellLayout.DockD1X, CellLayout.DockY))
            {
                return CellZone.DockA;
            }
            if (IsNear(x, y, CellLayout.DockD2X, CellLayout.DockY))
            {
                return CellZone.DockB;
            }
            if (x < -CellLayout.CorridorHalfWidthX)
            {
                return CellZone.EndZoneA;
            }
            if (x > CellLayout.CorridorHalfWidthX)
            {
                return CellZone.EndZoneB;
            }
            return y > (CellLayout.EastboundLaneY + CellLayout.WestboundLaneY) / 2.0
                ? CellZone.CorridorEastbound
                : CellZone.CorridorWestbound;
        }

        private static bool IsNear(double x, double y, double atX, double atY)
        {
            const double dockRadius = 0.45;
            double dx = x - atX;
            double dy = y - atY;
            return ((dx * dx) + (dy * dy)) <= (dockRadius * dockRadius);
        }

        /// <summary>
        /// Whether two robots' footprints intersect, by the separating axis test over their
        /// oriented boxes.
        /// </summary>
        /// <param name="a">The first robot.</param>
        /// <param name="b">The second robot.</param>
        /// <returns><c>true</c> when the footprints overlap.</returns>
        public static bool FootprintsOverlap(RobotAgent a, RobotAgent b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }
            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            (double ax, double ay, double ahl, double ahw, double ah) = a.Footprint;
            (double bx, double by, double bhl, double bhw, double bh) = b.Footprint;
            double[] axes =
            [
                ah, ah + 90.0, bh, bh + 90.0
            ];

            foreach (double axisDegrees in axes)
            {
                double r = axisDegrees * (Math.PI / 180.0);
                double nx = Math.Cos(r);
                double ny = Math.Sin(r);
                double centreGap = Math.Abs(((bx - ax) * nx) + ((by - ay) * ny));
                double spread = Project(ahl, ahw, ah, nx, ny) + Project(bhl, bhw, bh, nx, ny);
                if (centreGap > spread)
                {
                    return false;
                }
            }
            return true;
        }

        private static double Project(
            double halfLength,
            double halfWidth,
            double headingDegrees,
            double nx,
            double ny)
        {
            double r = headingDegrees * (Math.PI / 180.0);
            double ux = Math.Cos(r);
            double uy = Math.Sin(r);
            return (halfLength * Math.Abs((ux * nx) + (uy * ny))) +
                (halfWidth * Math.Abs((-uy * nx) + (ux * ny)));
        }

        private void AdvanceRobot(RobotAgent robot, double dt)
        {
            DrainBattery(robot, dt);

            if (robot.DwellRemaining > 0.0)
            {
                robot.DwellRemaining -= dt;
                robot.Speed = 0.0;
                return;
            }

            switch (robot.State)
            {
                case RobotCycleState.Idle:
                    BeginNextCycle(robot);
                    break;
                case RobotCycleState.Blocked:
                case RobotCycleState.TravelToSource:
                case RobotCycleState.TravelToTarget:
                case RobotCycleState.TravelToDock:
                    Travel(robot, dt);
                    break;
                case RobotCycleState.ReachToSource:
                case RobotCycleState.ReachToTarget:
                    Reach(robot, dt);
                    break;
                case RobotCycleState.Grip:
                    ActuateGripper(robot, dt, closing: true);
                    break;
                case RobotCycleState.Release:
                    ActuateGripper(robot, dt, closing: false);
                    break;
                case RobotCycleState.Lift:
                case RobotCycleState.Retreat:
                    Stow(robot, dt);
                    break;
                case RobotCycleState.Charging:
                    Charge(robot, dt);
                    break;
                default:
                    robot.State = RobotCycleState.Idle;
                    break;
            }
        }

        private void BeginNextCycle(RobotAgent robot)
        {
            robot.Speed = 0.0;
            CellPart? part = FindCollectablePart(robot);
            if (part == null || robot.NeedsCharge)
            {
                // Nothing to collect, or the battery is low: park on the dock. This also
                // releases the end zone, so the other robot can deliver into the station
                // this robot serves.
                if (ZoneAt(robot.X, robot.Y) != DockZoneOf(robot))
                {
                    RouteTo(robot, robot.DockX, CellLayout.DockY);
                    robot.State = RobotCycleState.TravelToDock;
                }
                return;
            }

            robot.TargetSlot = part.Slot;
            robot.CycleStartedAt = m_now;
            double approachX = StationX(robot.Source);
            RouteTo(robot, approachX, CellLayout.TableApproachY);
            robot.State = RobotCycleState.TravelToSource;
        }

        private static CellZone DockZoneOf(RobotAgent robot)
        {
            return robot.DockX < 0.0 ? CellZone.DockA : CellZone.DockB;
        }

        private CellPart? FindCollectablePart(RobotAgent robot)
        {
            CellPart? best = null;
            foreach (CellPart part in m_parts)
            {
                if (!part.IsResting || part.OnFloor || part.Station != robot.Source)
                {
                    continue;
                }
                if (best == null || part.Slot < best.Slot)
                {
                    best = part;
                }
            }
            return best;
        }

        private void Travel(RobotAgent robot, double dt)
        {
            if (robot.Waypoints.Count == 0)
            {
                ArriveFromTravel(robot);
                return;
            }

            (double targetX, double targetY) = robot.Waypoints.Peek();
            double dx = targetX - robot.X;
            double dy = targetY - robot.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= ArrivalTolerance)
            {
                robot.X = targetX;
                robot.Y = targetY;
                _ = robot.Waypoints.Dequeue();
                if (robot.Waypoints.Count == 0)
                {
                    robot.Speed = 0.0;
                    ArriveFromTravel(robot);
                }
                return;
            }

            // Reserve the zone immediately ahead, not the zone of the final waypoint: a robot
            // travelling from one end of the cell to the other passes through the corridor,
            // and demanding the far end zone up front would have each robot waiting on the
            // zone the other is standing in.
            const double lookahead = 0.35;
            double nextX = robot.X + (dx / distance * lookahead);
            double nextY = robot.Y + (dy / distance * lookahead);
            CellZone next = ZoneAt(nextX, nextY);
            if (next != robot.HeldZone && !EnsureReservation(robot, next))
            {
                robot.Speed = 0.0;
                if (robot.State != RobotCycleState.Blocked)
                {
                    robot.ResumeState = robot.State;
                    robot.State = RobotCycleState.Blocked;
                }
                return;
            }
            if (robot.State == RobotCycleState.Blocked)
            {
                robot.State = robot.ResumeState;
            }
            robot.BlockedBy = null;

            double limit = SpeedLimitFor(robot);
            double braking = (robot.Speed * robot.Speed) / (2.0 * Acceleration);
            robot.Speed = distance <= braking
                ? Math.Max(0.05, robot.Speed - (Acceleration * dt))
                : Math.Min(limit, robot.Speed + (Acceleration * dt));

            double step = Math.Min(robot.Speed * dt, distance);
            robot.X += dx / distance * step;
            robot.Y += dy / distance * step;
            robot.DistanceTravelled += step;
            robot.HeadingDegrees = Math.Atan2(dy, dx) * (180.0 / Math.PI);

            CellZone occupied = ZoneAt(robot.X, robot.Y);
            if (occupied == robot.ReservedZone && occupied != robot.HeldZone)
            {
                ReleaseZone(robot.HeldZone, robot.Id);
                robot.HeldZone = occupied;
                robot.ReservedZone = CellZone.None;
            }
        }

        private void ArriveFromTravel(RobotAgent robot)
        {
            robot.Speed = 0.0;
            robot.HeadingDegrees = 90.0;
            switch (robot.State)
            {
                case RobotCycleState.TravelToSource:
                    robot.State = RobotCycleState.ReachToSource;
                    break;
                case RobotCycleState.TravelToTarget:
                    robot.State = RobotCycleState.ReachToTarget;
                    break;
                case RobotCycleState.TravelToDock:
                    robot.State = robot.NeedsCharge
                        ? RobotCycleState.Charging
                        : RobotCycleState.Idle;
                    break;
                default:
                    robot.State = RobotCycleState.Idle;
                    break;
            }
        }

        private void Reach(RobotAgent robot, double dt)
        {
            bool toSource = robot.State == RobotCycleState.ReachToSource;
            CellStation station = toSource ? robot.Source : robot.Target;
            int slot = toSource ? robot.TargetSlot : FirstFreeSlot(station);
            robot.TargetSlot = slot;

            (double slotX, double slotY, double slotZ) = CellLayout.SlotPosition(station, slot);
            CellPart? floorPart = toSource ? FindFloorPart(robot) : null;
            if (floorPart != null)
            {
                slotX = floorPart.X;
                slotY = floorPart.Y;
                slotZ = floorPart.Z;
            }

            double forward = slotY - robot.Y;
            double lateral = -(slotX - robot.X);
            var solved = new double[RobotKinematics.AxisCount];
            if (!RobotArmSolver.TrySolve(forward, lateral, slotZ, solved))
            {
                robot.State = RobotCycleState.Idle;
                return;
            }

            robot.ArmStowed = false;
            if (!StepAxesTowards(robot, solved, dt))
            {
                return;
            }
            robot.State = toSource ? RobotCycleState.Grip : RobotCycleState.Release;
            robot.DwellRemaining = DwellSeconds;
        }

        private void ActuateGripper(RobotAgent robot, double dt, bool closing)
        {
            double rate = dt / GripSeconds;
            robot.GripperOpening = closing
                ? Math.Max(0.0, robot.GripperOpening - rate)
                : Math.Min(1.0, robot.GripperOpening + rate);

            if (closing && robot.GripperOpening <= 0.0)
            {
                CellPart? part = FindFloorPart(robot) ?? FindCollectablePart(robot);
                if (part != null)
                {
                    part.CarriedBy = robot.Id;
                    part.OnFloor = false;
                    robot.HoldingPart = part.Id;
                }
                robot.State = RobotCycleState.Lift;
                robot.DwellRemaining = DwellSeconds;
                return;
            }
            if (!closing && robot.GripperOpening >= 1.0)
            {
                CellPart? part = FindCarriedPart(robot);
                if (part != null)
                {
                    part.CarriedBy = null;
                    part.Station = robot.Target;
                    part.Slot = robot.TargetSlot;
                    (part.X, part.Y, part.Z) =
                        CellLayout.SlotPosition(part.Station, part.Slot);
                    part.HeadingDegrees = 0.0;
                    CompleteCycle(robot);
                }
                robot.HoldingPart = null;
                robot.State = RobotCycleState.Retreat;
                robot.DwellRemaining = DwellSeconds;
            }
        }

        private void Stow(RobotAgent robot, double dt)
        {
            if (!StepAxesTowards(robot, RobotAgent.TransportPose, dt))
            {
                return;
            }
            robot.ArmStowed = true;

            if (robot.State == RobotCycleState.Lift)
            {
                // A seeded, non-cryptographic generator is exactly what is wanted here: the
                // injected grip faults have to be reproducible so a test can assert the
                // recovery path deterministically.
#pragma warning disable CA5394 // Do not use insecure randomness
                bool slipped = m_random.NextDouble() < m_faultProbability;
#pragma warning restore CA5394
                if (robot.HoldingPart != null && slipped)
                {
                    DropPart(robot);
                    robot.State = RobotCycleState.ReachToSource;
                    return;
                }
                RouteTo(robot, StationX(robot.Target), CellLayout.TableApproachY);
                robot.State = RobotCycleState.TravelToTarget;
                return;
            }

            robot.State = RobotCycleState.Idle;
        }

        private void DropPart(RobotAgent robot)
        {
            CellPart? part = FindCarriedPart(robot);
            if (part == null)
            {
                return;
            }
            part.CarriedBy = null;
            part.OnFloor = true;
            part.X = robot.X;
            part.Y = robot.Y + 0.85;
            part.Z = PartHeightOffset;
            robot.HoldingPart = null;
            robot.GripperOpening = 1.0;
            Kpis.FaultCount++;
        }

        private void Charge(RobotAgent robot, double dt)
        {
            robot.Speed = 0.0;
            robot.BatteryLevel = Math.Min(100.0, robot.BatteryLevel + (ChargeRatePerSecond * dt));
            if (robot.BatteryLevel >= 99.9)
            {
                robot.NeedsCharge = false;
                robot.State = RobotCycleState.Idle;
            }
        }

        private void CompleteCycle(RobotAgent robot)
        {
            double duration = m_now - robot.CycleStartedAt;
            Kpis.PartsMoved++;
            Kpis.CycleCount++;
            Kpis.LastCycleSeconds = duration;
            Kpis.TotalCycleSeconds += duration;
            Kpis.AverageCycleSeconds = Kpis.TotalCycleSeconds / Kpis.CycleCount;
            if (robot.BatteryLevel < ChargeThreshold)
            {
                robot.NeedsCharge = true;
            }
        }

        private void DrainBattery(RobotAgent robot, double dt)
        {
            if (robot.State == RobotCycleState.Charging)
            {
                return;
            }
            double drain = (BatteryDrainPerSecond * dt) +
                (BatteryDrainPerMetre * robot.Speed * dt);
            robot.BatteryLevel = Math.Max(0.0, robot.BatteryLevel - drain);
        }

        private double SpeedLimitFor(RobotAgent robot)
        {
            foreach (RobotAgent other in m_robots)
            {
                if (ReferenceEquals(other, robot))
                {
                    continue;
                }
                double dx = other.X - robot.X;
                double dy = other.Y - robot.Y;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < ProximitySlowdownDistance)
                {
                    return SlowSpeed;
                }
            }
            return CruiseSpeed;
        }

        private static bool StepAxesTowards(RobotAgent robot, double[] goal, double dt)
        {
            const double axisSpeed = 90.0;
            bool settled = true;
            for (int i = 0; i < robot.Axes.Length; i++)
            {
                double delta = goal[i] - robot.Axes[i];
                double step = axisSpeed * dt;
                if (Math.Abs(delta) <= step)
                {
                    robot.Axes[i] = goal[i];
                    continue;
                }
                robot.Axes[i] += Math.Sign(delta) * step;
                settled = false;
            }
            return settled;
        }

        private void RouteTo(RobotAgent robot, double destinationX, double destinationY)
        {
            robot.Waypoints.Clear();
            bool eastbound = destinationX > robot.X;
            double laneY = eastbound ? CellLayout.EastboundLaneY : CellLayout.WestboundLaneY;

            if (Math.Abs(robot.Y - laneY) > ArrivalTolerance)
            {
                robot.Waypoints.Enqueue((robot.X, laneY));
            }
            robot.Waypoints.Enqueue((destinationX, laneY));
            if (Math.Abs(destinationY - laneY) > ArrivalTolerance)
            {
                robot.Waypoints.Enqueue((destinationX, destinationY));
            }
        }

        private bool EnsureReservation(RobotAgent robot, CellZone zone)
        {
            if (zone == CellZone.None || zone == robot.HeldZone)
            {
                return true;
            }
            if (robot.ReservedZone == zone)
            {
                return true;
            }
            bool corridor = zone is CellZone.CorridorEastbound or CellZone.CorridorWestbound;
            if (corridor && !robot.ArmStowed)
            {
                robot.BlockedBy = robot.Id;
                return false;
            }
            if (m_reservations.TryGetValue(zone, out string? owner) && owner != robot.Id)
            {
                robot.BlockedBy = owner;
                return false;
            }
            m_reservations[zone] = robot.Id;
            robot.ReservedZone = zone;
            return true;
        }

        private void ReleaseZone(CellZone zone, string robotId)
        {
            if (zone != CellZone.None &&
                m_reservations.TryGetValue(zone, out string? owner) &&
                owner == robotId)
            {
                _ = m_reservations.Remove(zone);
            }
        }

        private CellPart? FindCarriedPart(RobotAgent robot)
        {
            foreach (CellPart part in m_parts)
            {
                if (part.CarriedBy == robot.Id)
                {
                    return part;
                }
            }
            return null;
        }

        private CellPart? FindFloorPart(RobotAgent robot)
        {
            foreach (CellPart part in m_parts)
            {
                if (part.OnFloor && Math.Abs(part.X - robot.X) < 1.0)
                {
                    return part;
                }
            }
            return null;
        }

        private int FirstFreeSlot(CellStation station)
        {
            int count = CellLayout.SlotOffsetsX.Length;
            var taken = new bool[count];
            foreach (CellPart part in m_parts)
            {
                if (part.IsResting && !part.OnFloor && part.Station == station &&
                    part.Slot >= 0 && part.Slot < count)
                {
                    taken[part.Slot] = true;
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (!taken[i])
                {
                    return i;
                }
            }
            return 0;
        }

        private void UpdateCarriedParts()
        {
            foreach (RobotAgent robot in m_robots)
            {
                CellPart? part = FindCarriedPart(robot);
                if (part == null)
                {
                    continue;
                }
                RigidTransform tcp = ToolCentrePointOf(robot);
                (part.X, part.Y, part.Z) = tcp.Origin;
                part.HeadingDegrees = robot.HeadingDegrees;
            }
        }

        private static double StationX(CellStation station)
        {
            return station == CellStation.TableA ? CellLayout.TableAX : CellLayout.TableBX;
        }
    }
}
