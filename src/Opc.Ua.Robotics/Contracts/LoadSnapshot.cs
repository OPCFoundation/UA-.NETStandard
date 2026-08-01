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
    /// Read-model snapshot of a LoadType instance.
    /// </summary>
    public sealed record LoadSnapshot
    {
        /// <summary>
        /// The load instance NodeId.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The load mass, including status and timestamps.
        /// </summary>
        public DataValue Mass { get; init; } = DataValue.Null;

        /// <summary>
        /// Engineering metadata for the load mass.
        /// </summary>
        public RoboticsEngineeringValue MassEngineering { get; init; } = new();

        /// <summary>
        /// The load center of mass, including status and timestamps.
        /// </summary>
        public DataValue CenterOfMass { get; init; } = DataValue.Null;

        /// <summary>
        /// The load inertia, including status and timestamps.
        /// </summary>
        public DataValue Inertia { get; init; } = DataValue.Null;
    }
}
