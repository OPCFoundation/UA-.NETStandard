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
    /// Read-model snapshot of a MotorType instance.
    /// </summary>
    public sealed record MotorSnapshot
    {
        /// <summary>
        /// The motor identification.
        /// </summary>
        public RoboticsComponentIdentification Identification { get; init; } = new();

        /// <summary>
        /// The MotorTemperature value, including status and timestamps.
        /// </summary>
        public DataValue MotorTemperature { get; init; } = DataValue.Null;

        /// <summary>
        /// The MotorTemperature variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId MotorTemperatureId { get; init; } = NodeId.Null;

        /// <summary>
        /// The engineering units and range of MotorTemperature.
        /// </summary>
        public RoboticsEngineeringValue MotorTemperatureEngineering { get; init; } = new();

        /// <summary>
        /// The optional BrakeReleased value, including status and timestamps.
        /// </summary>
        public DataValue BrakeReleased { get; init; } = DataValue.Null;

        /// <summary>
        /// The BrakeReleased variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId BrakeReleasedId { get; init; } = NodeId.Null;

        /// <summary>
        /// The optional EffectiveLoadRate value, including status and timestamps.
        /// </summary>
        public DataValue EffectiveLoadRate { get; init; } = DataValue.Null;

        /// <summary>
        /// The EffectiveLoadRate variable to subscribe to, or <see cref="NodeId.Null"/> when not published.
        /// </summary>
        public NodeId EffectiveLoadRateId { get; init; } = NodeId.Null;
    }
}
