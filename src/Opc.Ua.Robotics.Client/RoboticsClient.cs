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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 40010 Robotics companion model. A generic
    /// OpenUSD connector or viewer uses these to discover MotionDeviceSystem
    /// instances and to identify the Robotics type of a node it discovered, so it
    /// can label and drive a robot-cell twin without hard-coding NodeIds.
    /// </summary>
    public static class RoboticsClient
    {
        /// <summary>
        /// Browses the immediate hierarchical children of <paramref name="root"/>
        /// and returns the NodeIds of those whose TypeDefinition is the Robotics
        /// <c>MotionDeviceSystemType</c>. <paramref name="root"/> is typically the
        /// DI <c>DeviceSet</c> or the server Objects folder.
        /// </summary>
        public static async Task<IReadOnlyList<NodeId>> DiscoverMotionDeviceSystemsAsync(
            ISession session, NodeId root, CancellationToken cancellationToken = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            NodeId systemType = RoboticsModel.TypeNodeId(
                RoboticsModel.MotionDeviceSystemType, session.NamespaceUris);

            var desc = new BrowseDescription
            {
                NodeId = root,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseResponse response = await session.BrowseAsync(
                null!, null!, 0, new BrowseDescription[] { desc }, cancellationToken)
                .ConfigureAwait(false);

            var results = new List<NodeId>();
            if (response?.Results == null || response.Results.Count == 0)
            {
                return results;
            }
            ArrayOf<ReferenceDescription> refs = response.Results[0].References;
            for (int i = 0; i < refs.Count; i++)
            {
                ReferenceDescription r = refs[i];
                NodeId typeDef = ExpandedNodeId.ToNodeId(r.TypeDefinition, session.NamespaceUris);
                NodeId child = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                if (typeDef == systemType)
                {
                    results.Add(child);
                }
            }
            return results;
        }

        /// <summary>
        /// Maps a node's TypeDefinition to a friendly Robotics type name
        /// (<c>MotionDeviceSystem</c>, <c>MotionDevice</c>, <c>Axis</c>,
        /// <c>Controller</c>), or returns <c>false</c> if it is not a known
        /// Robotics type in the supplied namespace table.
        /// </summary>
        public static bool TryGetRoboticsTypeName(
            NodeId typeDefinition, NamespaceTable namespaceUris, out string? name)
        {
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            name = null;
            if (typeDefinition == RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceSystemType, namespaceUris))
            {
                name = "MotionDeviceSystem";
            }
            else if (typeDefinition == RoboticsModel.TypeNodeId(RoboticsModel.MotionDeviceType, namespaceUris))
            {
                name = "MotionDevice";
            }
            else if (typeDefinition == RoboticsModel.TypeNodeId(RoboticsModel.AxisType, namespaceUris))
            {
                name = "Axis";
            }
            else if (typeDefinition == RoboticsModel.TypeNodeId(RoboticsModel.ControllerType, namespaceUris))
            {
                name = "Controller";
            }
            return name != null;
        }
    }
}
