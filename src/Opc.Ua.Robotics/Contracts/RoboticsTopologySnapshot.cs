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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Focused server-independent read projection of a discovered Robotics topology.
    /// </summary>
    public sealed record RoboticsTopologySnapshot
    {
        /// <summary>
        /// Motion-device systems in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSystemSnapshot> Systems { get; init; } = [];

        /// <summary>
        /// Controllers in the topology.
        /// </summary>
        public ArrayOf<ControllerSnapshot> Controllers { get; init; } = [];

        /// <summary>
        /// Motion devices in the topology.
        /// </summary>
        public ArrayOf<MotionDeviceSnapshot> MotionDevices { get; init; } = [];

        /// <summary>
        /// Axes in the topology.
        /// </summary>
        public ArrayOf<AxisSnapshot> Axes { get; init; } = [];

        /// <summary>
        /// Loads in the topology.
        /// </summary>
        public ArrayOf<LoadSnapshot> Loads { get; init; } = [];

        /// <summary>
        /// Power trains in the topology.
        /// </summary>
        public ArrayOf<PowerTrainSnapshot> PowerTrains { get; init; } = [];

        /// <summary>
        /// Motors in the topology.
        /// </summary>
        public ArrayOf<MotorSnapshot> Motors { get; init; } = [];

        /// <summary>
        /// Gears in the topology.
        /// </summary>
        public ArrayOf<GearSnapshot> Gears { get; init; } = [];

        /// <summary>
        /// Drives in the topology.
        /// </summary>
        public ArrayOf<DriveSnapshot> Drives { get; init; } = [];

        /// <summary>
        /// Safety states in the topology.
        /// </summary>
        public ArrayOf<SafetyStateSnapshot> SafetyStates { get; init; } = [];

        /// <summary>
        /// Task controls in the topology.
        /// </summary>
        public ArrayOf<TaskControlSnapshot> TaskControls { get; init; } = [];

        /// <summary>
        /// Task modules in the topology.
        /// </summary>
        public ArrayOf<TaskModuleSnapshot> TaskModules { get; init; } = [];

        /// <summary>
        /// Semantic Robotics relationships observed in the topology.
        /// </summary>
        public RoboticsRelationshipSnapshot Relationships { get; init; } = new();
    }
}
