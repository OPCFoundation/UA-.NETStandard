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
        public static async Task<ArrayOf<NodeId>> DiscoverMotionDeviceSystemsAsync(
            ISession session, NodeId root, CancellationToken cancellationToken = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            int ns = session.NamespaceUris.GetIndex(RoboticsNamespaces.Robotics);
            if (ns < 0)
            {
                // The server does not expose the Robotics companion namespace.
                return ArrayOf<NodeId>.Empty;
            }
            var systemType = new NodeId(RoboticsModel.MotionDeviceSystemType, (ushort)ns);

            // ManagedBrowseAsync follows continuation points, so a server that caps the
            // number of references it returns per node cannot silently truncate discovery.
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await session.ManagedBrowseAsync(
                null, null, [root], 0, BrowseDirection.Forward,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true,
                (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);

            var systems = new List<NodeId>();
            if (results.Count > 0)
            {
                ArrayOf<ReferenceDescription> refs = results[0];
                for (int i = 0; i < refs.Count; i++)
                {
                    ReferenceDescription r = refs[i];
                    NodeId typeDef = ExpandedNodeId.ToNodeId(r.TypeDefinition, session.NamespaceUris);
                    if (typeDef == systemType)
                    {
                        NodeId child = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                        if (!child.IsNull)
                        {
                            systems.Add(child);
                        }
                    }
                }
            }
            return systems;
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
            int ns = namespaceUris.GetIndex(RoboticsNamespaces.Robotics);
            if (ns < 0 || typeDefinition.IsNull)
            {
                // The Robotics namespace is not present in the table, so no node can be
                // a known Robotics type. Return false instead of throwing.
                return false;
            }
            var robotics = (ushort)ns;
            if (typeDefinition == new NodeId(RoboticsModel.MotionDeviceSystemType, robotics))
            {
                name = "MotionDeviceSystem";
            }
            else if (typeDefinition == new NodeId(RoboticsModel.MotionDeviceType, robotics))
            {
                name = "MotionDevice";
            }
            else if (typeDefinition == new NodeId(RoboticsModel.AxisType, robotics))
            {
                name = "Axis";
            }
            else if (typeDefinition == new NodeId(RoboticsModel.ControllerType, robotics))
            {
                name = "Controller";
            }
            return name != null;
        }
    }
}
