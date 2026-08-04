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
using Opc.Ua;

namespace Robotics
{
    /// <summary>
    /// A part circulating between the two transfer stations.
    /// </summary>
    public sealed class CellPart
    {
        /// <summary>
        /// Creates a part resting in a slot.
        /// </summary>
        /// <param name="id">The part identifier.</param>
        /// <param name="station">The station it starts on.</param>
        /// <param name="slot">The slot it starts in.</param>
        public CellPart(string id, CellStation station, int slot)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Station = station;
            Slot = slot;
            (X, Y, Z) = CellLayout.SlotPosition(station, slot);
        }

        /// <summary>
        /// The part identifier, matching the prim name under <c>/Cell/Parts</c>.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The station the part rests on, when it is not being carried.
        /// </summary>
        public CellStation Station { get; internal set; }

        /// <summary>
        /// The slot the part rests in, when it is not being carried.
        /// </summary>
        public int Slot { get; internal set; }

        /// <summary>
        /// The robot carrying the part, or <c>null</c> when it is not being carried.
        /// </summary>
        public string? CarriedBy { get; internal set; }

        /// <summary>
        /// Whether the part has been dropped and is lying on the floor.
        /// </summary>
        public bool OnFloor { get; internal set; }

        /// <summary>
        /// The X position of the part, in metres.
        /// </summary>
        public double X { get; internal set; }

        /// <summary>
        /// The Y position of the part, in metres.
        /// </summary>
        public double Y { get; internal set; }

        /// <summary>
        /// The Z position of the part, in metres.
        /// </summary>
        public double Z { get; internal set; }

        /// <summary>
        /// The orientation of the part about Z, in degrees.
        /// </summary>
        public double HeadingDegrees { get; internal set; }

        /// <summary>
        /// Whether the part is resting somewhere rather than being carried.
        /// </summary>
        public bool IsResting => CarriedBy == null;

        /// <summary>
        /// The order in which the part came to rest on its current station.
        /// </summary>
        /// <remarks>
        /// The cell works its buffers first-in-first-out. Picking by slot instead starves
        /// whichever part never lands in the lowest slot: a spare seeded beside the one the
        /// robots shuttle would sit untouched for the life of the process.
        /// </remarks>
        internal long RestingSequence { get; set; }
    }
    /// <summary>
    /// Counters describing how the cell is performing.
    /// </summary>
    public sealed class CellKpis
    {
        /// <summary>
        /// The number of completed transfers.
        /// </summary>
        public int PartsMoved { get; internal set; }

        /// <summary>
        /// The number of transfers each robot has completed.
        /// </summary>
        public int CycleCount { get; internal set; }

        /// <summary>
        /// The duration of the most recently completed transfer, in seconds.
        /// </summary>
        public double LastCycleSeconds { get; internal set; }

        /// <summary>
        /// The mean transfer duration, in seconds.
        /// </summary>
        public double AverageCycleSeconds { get; internal set; }

        /// <summary>
        /// The fraction of elapsed time a robot has been doing something other than
        /// waiting, in the range 0 to 1.
        /// </summary>
        public double Utilisation { get; internal set; }

        /// <summary>
        /// The number of grip faults injected.
        /// </summary>
        public int FaultCount { get; internal set; }

        internal double BusySeconds { get; set; }

        internal double ElapsedSeconds { get; set; }

        internal double TotalCycleSeconds { get; set; }
    }

    /// <summary>
    /// One mobile manipulator in the cell.
    /// </summary>
    public sealed class RobotAgent
    {
        internal RobotAgent(
            string id,
            CellStation source,
            CellStation target,
            double dockX,
            double homeX)
        {
            Id = id;
            Source = source;
            Target = target;
            DockX = dockX;
            HomeX = homeX;
            X = dockX;
            Y = CellLayout.DockY;
            HeadingDegrees = 90.0;
            BatteryLevel = 100.0;
            Axes = new double[RobotKinematics.AxisCount];
            TransportPose.Span.CopyTo(Axes);
        }

        /// <summary>
        /// The arm pose held while travelling, folded back over the platform so the robot
        /// stays inside <see cref="CellLayout.TransportRadius"/>.
        /// </summary>
        /// <remarks>
        /// Held as a single immutable value rather than rebuilt per access: the
        /// choreography reads it on every tick of every robot, and an <c>ArrayOf</c> can be
        /// shared safely where a cached <c>double[]</c> would hand every caller a writable
        /// reference to the one copy.
        /// </remarks>
        public static ArrayOf<double> TransportPose { get; }
            = new double[] { 0.0, -100.0, 125.0, 0.0, 65.0, 0.0 };

        /// <summary>
        /// The robot identifier, matching its mount prim name.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The station this robot collects from.
        /// </summary>
        public CellStation Source { get; }

        /// <summary>
        /// The station this robot delivers to.
        /// </summary>
        public CellStation Target { get; }

        /// <summary>
        /// The step the robot is performing.
        /// </summary>
        public RobotCycleState State { get; internal set; } = RobotCycleState.Idle;

        /// <summary>
        /// The X position of the platform, in metres.
        /// </summary>
        public double X { get; internal set; }

        /// <summary>
        /// The Y position of the platform, in metres.
        /// </summary>
        public double Y { get; internal set; }

        /// <summary>
        /// The heading of the platform about Z, in degrees.
        /// </summary>
        public double HeadingDegrees { get; internal set; }

        /// <summary>
        /// The current travel speed, in metres per second.
        /// </summary>
        public double Speed { get; internal set; }

        /// <summary>
        /// The distance the platform has travelled in total, in metres.
        /// </summary>
        public double DistanceTravelled { get; internal set; }

        /// <summary>
        /// The six axis positions, in degrees.
        /// </summary>
        public double[] Axes { get; }

        /// <summary>
        /// How far the jaws are open, from 0 for closed to 1 for fully open.
        /// </summary>
        public double GripperOpening { get; internal set; } = 1.0;

        /// <summary>
        /// The remaining charge, as a percentage.
        /// </summary>
        public double BatteryLevel { get; internal set; }

        /// <summary>
        /// The part the robot is carrying, or <c>null</c>.
        /// </summary>
        public string? HoldingPart { get; internal set; }

        /// <summary>
        /// The robot whose reservation this robot is waiting on, or <c>null</c>.
        /// </summary>
        public string? BlockedBy { get; internal set; }

        /// <summary>
        /// The zone the robot currently occupies.
        /// </summary>
        public CellZone HeldZone { get; internal set; } = CellZone.None;

        /// <summary>
        /// The zone the robot has reserved to move into next.
        /// </summary>
        public CellZone ReservedZone { get; internal set; } = CellZone.None;

        /// <summary>
        /// Whether the arm is folded within the transport envelope.
        /// </summary>
        public bool ArmStowed { get; internal set; } = true;

        /// <summary>
        /// The platform pose most recently handed to the twin, if any.
        /// </summary>
        /// <remarks>
        /// A carried part has to be drawn relative to the platform the *viewer* has, not
        /// the one the simulation has. The platform pose and the part pose leave the server
        /// on different loops, so a part drawn from the newer of the pair visibly runs
        /// ahead of the gripper holding it - a quarter of a metre of it while driving,
        /// which is three part widths.
        /// </remarks>
        internal (double X, double Y, double HeadingDegrees)? PublishedPose { get; set; }

        internal double DockX { get; }

        internal double HomeX { get; }

        internal Queue<(double X, double Y)> Waypoints { get; } = new();

        /// <summary>
        /// The number of waypoints still to visit.
        /// </summary>
        public int WaypointCount => Waypoints.Count;

        internal double DwellRemaining { get; set; }

        internal double CycleStartedAt { get; set; }

        internal RobotCycleState ResumeState { get; set; } = RobotCycleState.Idle;

        internal bool NeedsCharge { get; set; }

        internal int TargetSlot { get; set; }

        /// <summary>
        /// The bounding radius the robot currently occupies, in metres.
        /// </summary>
        /// <remarks>
        /// A stowed arm keeps the robot inside the platform envelope; a deployed arm claims
        /// its working width instead. The choreography only lets an arm deploy inside an end
        /// zone the robot has exclusively reserved.
        /// </remarks>
        public double OccupiedRadius => ArmStowed
            ? CellLayout.TransportRadius
            : CellLayout.TransportRadius + CellLayout.DeployedArmHalfWidthX;

        /// <summary>
        /// The oriented footprint the robot occupies, as a centre, a half length along its
        /// heading, a half width across it and the heading itself.
        /// </summary>
        /// <remarks>
        /// A circle around the platform is far too pessimistic for a machine that is nearly
        /// twice as long as it is wide, and it would force the cell to be laid out with
        /// clearances no real installation would need. A deployed arm extends the box
        /// forwards, since that is where the arm actually reaches.
        /// </remarks>
        public (double X, double Y, double HalfLength, double HalfWidth, double HeadingDegrees)
            Footprint
        {
            get
            {
                const double halfLength = 0.575;
                const double halfWidth = 0.300;
                if (ArmStowed)
                {
                    return (X, Y, halfLength, halfWidth, HeadingDegrees);
                }

                const double reach = 1.200;
                double centreShift = (reach - halfLength) / 2.0;
                double radians = HeadingDegrees * (System.Math.PI / 180.0);
                return (
                    X + (centreShift * System.Math.Cos(radians)),
                    Y + (centreShift * System.Math.Sin(radians)),
                    (reach + halfLength) / 2.0,
                    CellLayout.DeployedArmHalfWidthX + 0.05,
                    HeadingDegrees);
            }
        }
    }
}
