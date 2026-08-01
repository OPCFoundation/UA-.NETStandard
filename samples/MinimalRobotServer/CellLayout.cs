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

namespace Robotics
{
    /// <summary>
    /// Fixed geometry of the sample cell, matching the prims authored in
    /// <c>Cell.usda</c>.
    /// </summary>
    /// <remarks>
    /// The separations here are what make the cell collision free, so they are stated once
    /// and asserted by the choreography tests rather than being spread through the code.
    /// </remarks>
    public static class CellLayout
    {
        /// <summary>
        /// The X position of the western transfer station, in metres.
        /// </summary>
        public const double TableAX = -2.6;

        /// <summary>
        /// The X position of the eastern transfer station, in metres.
        /// </summary>
        public const double TableBX = 2.6;

        /// <summary>
        /// The Y position of both transfer stations, in metres.
        /// </summary>
        public const double TableY = 1.2;

        /// <summary>
        /// The height of a table top's surface, in metres.
        /// </summary>
        public const double TableSurfaceZ = 0.770;

        /// <summary>
        /// The X offsets of the three part slots on a table, in metres.
        /// </summary>
        public static double[] SlotOffsetsX => [-0.22, 0.0, 0.22];

        /// <summary>
        /// The Y position a robot parks at to work a table, in metres.
        /// </summary>
        public const double TableApproachY = 0.35;

        /// <summary>
        /// The Y position of the eastbound travel lane, in metres.
        /// </summary>
        public const double EastboundLaneY = 0.35;

        /// <summary>
        /// The Y position of the westbound travel lane, in metres.
        /// </summary>
        public const double WestboundLaneY = -1.15;

        /// <summary>
        /// The half width of the shared corridor along X, in metres.
        /// </summary>
        /// <remarks>
        /// Everything beyond this on either side is an end zone, which only one robot may
        /// occupy. A robot working a table at <see cref="TableAX"/> keeps its arm within
        /// <see cref="DeployedArmHalfWidthX"/>, so a deployed arm never reaches the corridor.
        /// </remarks>
        public const double CorridorHalfWidthX = 1.3;

        /// <summary>
        /// The bounding radius of the platform with the arm folded for transport, in metres.
        /// </summary>
        public const double TransportRadius = 0.65;

        /// <summary>
        /// The furthest a deployed arm is allowed to reach along X from its platform, in
        /// metres.
        /// </summary>
        public const double DeployedArmHalfWidthX = 0.45;

        /// <summary>
        /// The X position of the western charging dock, in metres.
        /// </summary>
        public const double DockD1X = -0.8;

        /// <summary>
        /// The X position of the eastern charging dock, in metres.
        /// </summary>
        public const double DockD2X = 0.8;

        /// <summary>
        /// The Y position of both charging docks, in metres.
        /// </summary>
        public const double DockY = -2.0;

        /// <summary>
        /// Returns the cell position of a part resting in a table slot.
        /// </summary>
        /// <param name="table">The station the slot belongs to.</param>
        /// <param name="slot">The zero based slot index.</param>
        /// <returns>The slot centre, in metres.</returns>
        public static (double X, double Y, double Z) SlotPosition(CellStation table, int slot)
        {
            double[] offsets = SlotOffsetsX;
            int index = slot < 0 ? 0 : slot % offsets.Length;
            double tableX = table == CellStation.TableA ? TableAX : TableBX;
            return (tableX + offsets[index], TableY, TableSurfaceZ + 0.0175);
        }
    }

    /// <summary>
    /// The transfer stations a part can rest on.
    /// </summary>
    public enum CellStation
    {
        /// <summary>
        /// The western station.
        /// </summary>
        TableA,

        /// <summary>
        /// The eastern station.
        /// </summary>
        TableB
    }

    /// <summary>
    /// The exclusive areas of the cell a robot has to reserve before entering.
    /// </summary>
    public enum CellZone
    {
        /// <summary>
        /// No reservation held.
        /// </summary>
        None,

        /// <summary>
        /// The western end of the cell, including table A.
        /// </summary>
        EndZoneA,

        /// <summary>
        /// The eastbound lane of the shared corridor.
        /// </summary>
        CorridorEastbound,

        /// <summary>
        /// The westbound lane of the shared corridor.
        /// </summary>
        CorridorWestbound,

        /// <summary>
        /// The eastern end of the cell, including table B.
        /// </summary>
        EndZoneB,

        /// <summary>
        /// The western charging dock, which sits clear of table A's working area so a robot
        /// waiting for work does not block the station.
        /// </summary>
        DockA,

        /// <summary>
        /// The eastern charging dock, which sits clear of table B's working area.
        /// </summary>
        DockB
    }

    /// <summary>
    /// The step a robot is currently performing.
    /// </summary>
    public enum RobotCycleState
    {
        /// <summary>
        /// Waiting for work.
        /// </summary>
        Idle,

        /// <summary>
        /// Waiting for a zone reservation another robot holds.
        /// </summary>
        Blocked,

        /// <summary>
        /// Driving to the station it collects from.
        /// </summary>
        TravelToSource,

        /// <summary>
        /// Reaching the arm over the source slot.
        /// </summary>
        ReachToSource,

        /// <summary>
        /// Closing the jaws on the part.
        /// </summary>
        Grip,

        /// <summary>
        /// Lifting the part clear and folding the arm for transport.
        /// </summary>
        Lift,

        /// <summary>
        /// Driving to the station it delivers to.
        /// </summary>
        TravelToTarget,

        /// <summary>
        /// Reaching the arm over the target slot.
        /// </summary>
        ReachToTarget,

        /// <summary>
        /// Opening the jaws to release the part.
        /// </summary>
        Release,

        /// <summary>
        /// Folding the arm back for transport.
        /// </summary>
        Retreat,

        /// <summary>
        /// Driving to the charging dock.
        /// </summary>
        TravelToDock,

        /// <summary>
        /// Sitting on the dock taking charge.
        /// </summary>
        Charging
    }
}
