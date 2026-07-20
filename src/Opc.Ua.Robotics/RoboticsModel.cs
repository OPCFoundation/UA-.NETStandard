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

namespace Opc.Ua.Robotics
{
    /// <summary>
    /// Key type NodeIds of the OPC 40010 Robotics companion model. A consumer
    /// builds instances from <c>BaseObjectState</c> plus these numeric type
    /// identifiers, resolved into the server's namespace table with
    /// <see cref="TypeNodeId"/>.
    /// </summary>
    public static class RoboticsModel
    {
        /// <summary>MotionDeviceSystemType — the robot cell / system aggregate.</summary>
        public const uint MotionDeviceSystemType = 1002;

        /// <summary>ControllerType — the control unit of a motion device system.</summary>
        public const uint ControllerType = 1003;

        /// <summary>MotionDeviceType — an articulated motion device (a robot).</summary>
        public const uint MotionDeviceType = 1004;

        /// <summary>AxisType — a single axis (joint) of a motion device.</summary>
        public const uint AxisType = 16601;

        /// <summary>
        /// Resolves one of the Robotics type identifiers into a NodeId in the
        /// Robotics namespace against the supplied server namespace table.
        /// </summary>
        /// <param name="identifier">A Robotics type numeric identifier (e.g. <see cref="MotionDeviceType"/>).</param>
        /// <param name="namespaceUris">The server's namespace table.</param>
        public static NodeId TypeNodeId(uint identifier, NamespaceTable namespaceUris)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            return NodeId.Create(identifier, RoboticsNamespaces.Robotics, namespaceUris);
        }
    }
}
