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
using Opc.Ua.Di.Client;

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 40010 Robotics companion model. Robotics
    /// types derive from OPC 40001-1 Industrial Automation, which derives from
    /// OPC 10000-100 Device Integration, so this client composes the DI client
    /// (<see cref="DiTopologyClient"/>) instead of reimplementing device
    /// discovery. A generic OpenUSD connector or viewer uses it to discover
    /// MotionDeviceSystem instances and to identify the Robotics type of a node
    /// it discovered, so it can label and drive a robot-cell twin without
    /// hard-coding NodeIds.
    /// </summary>
    /// <remarks>
    /// Every discovery and classification operation is subtype aware: a robot
    /// vendor that specialises the companion types (for example an
    /// <c>AcmeMotionDeviceType</c> derived from <c>MotionDeviceType</c>) is
    /// discovered and classified as the closest standard Robotics type.
    /// </remarks>
    public sealed class RoboticsClient
    {
        /// <summary>
        /// Creates a Robotics client over a connected session.
        /// </summary>
        /// <param name="session">
        /// The connected session.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used for logging.
        /// </param>
        public RoboticsClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Topology = new DiTopologyClient(session, telemetry);
        }

        /// <summary>
        /// Gets the connected session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the Device Integration topology client this Robotics client
        /// extends. Use it to walk the DI device topology that carries the
        /// Robotics instances.
        /// </summary>
        public DiTopologyClient Topology { get; }

        /// <summary>
        /// Discovers every MotionDeviceSystem below the DI <c>DeviceSet</c>,
        /// including instances of vendor subtypes.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<ArrayOf<NodeId>> DiscoverMotionDeviceSystemsAsync(
            CancellationToken cancellationToken = default)
        {
            return DiscoverMotionDeviceSystemsAsync(
                Topology.DeviceSetId, cancellationToken);
        }

        /// <summary>
        /// Discovers every MotionDeviceSystem below <paramref name="root"/>,
        /// including instances of vendor subtypes.
        /// </summary>
        /// <param name="root">
        /// The node to browse, typically the DI <c>DeviceSet</c> or the server
        /// Objects folder.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<ArrayOf<NodeId>> DiscoverMotionDeviceSystemsAsync(
            NodeId root,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(
                Session, root, RoboticsModel.MotionDeviceSystemType, cancellationToken);
        }

        /// <summary>
        /// Discovers every MotionDevice below <paramref name="root"/>, which is
        /// typically the <c>MotionDevices</c> folder of a MotionDeviceSystem.
        /// Instances of vendor subtypes are included.
        /// </summary>
        /// <param name="root">
        /// The node to browse.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<ArrayOf<NodeId>> DiscoverMotionDevicesAsync(
            NodeId root,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(
                Session, root, RoboticsModel.MotionDeviceType, cancellationToken);
        }

        /// <summary>
        /// Discovers every Controller below <paramref name="root"/>, which is
        /// typically the <c>Controllers</c> folder of a MotionDeviceSystem.
        /// Instances of vendor subtypes are included.
        /// </summary>
        /// <param name="root">
        /// The node to browse.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<ArrayOf<NodeId>> DiscoverControllersAsync(
            NodeId root,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(
                Session, root, RoboticsModel.ControllerType, cancellationToken);
        }

        /// <summary>
        /// Discovers every Axis below <paramref name="root"/>, which is
        /// typically the <c>Axes</c> folder of a MotionDevice. Instances of
        /// vendor subtypes are included.
        /// </summary>
        /// <param name="root">
        /// The node to browse.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task<ArrayOf<NodeId>> DiscoverAxesAsync(
            NodeId root,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(
                Session, root, RoboticsModel.AxisType, cancellationToken);
        }

        /// <summary>
        /// Maps a node's TypeDefinition to the friendly name of the closest
        /// standard Robotics type (<c>MotionDeviceSystem</c>,
        /// <c>MotionDevice</c>, <c>Axis</c>, <c>Controller</c>), following the
        /// server's type hierarchy so vendor subtypes are classified too.
        /// Returns <c>null</c> when the node is not a Robotics type.
        /// </summary>
        /// <param name="typeDefinition">
        /// The TypeDefinition NodeId of a discovered node.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<string?> GetRoboticsTypeNameAsync(
            NodeId typeDefinition,
            CancellationToken cancellationToken = default)
        {
            if (typeDefinition.IsNull)
            {
                return null;
            }
            if (TryGetRoboticsTypeName(
                typeDefinition, Session.NamespaceUris, out string? exact))
            {
                return exact;
            }

            // Probe the standard Robotics types most-derived first so a vendor
            // subtype resolves to its closest standard base.
            foreach ((uint identifier, string name) in s_classification)
            {
                NodeId candidate = RoboticsModel.TypeNodeId(
                    identifier, Session.NamespaceUris);
                if (!candidate.IsNull &&
                    await Session.NodeCache.IsTypeOfAsync(
                        typeDefinition, candidate, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return name;
                }
            }
            return null;
        }

        /// <summary>
        /// Browses the hierarchical children of <paramref name="root"/> and
        /// returns the NodeIds of those whose TypeDefinition is, or derives
        /// from, the Robotics <c>MotionDeviceSystemType</c>.
        /// <paramref name="root"/> is typically the DI <c>DeviceSet</c> or the
        /// server Objects folder.
        /// </summary>
        /// <param name="session">
        /// The connected session.
        /// </param>
        /// <param name="root">
        /// The node to browse.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public static Task<ArrayOf<NodeId>> DiscoverMotionDeviceSystemsAsync(
            ISession session, NodeId root, CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(
                session, root, RoboticsModel.MotionDeviceSystemType, cancellationToken);
        }

        /// <summary>
        /// Maps a node's TypeDefinition to a friendly Robotics type name
        /// (<c>MotionDeviceSystem</c>, <c>MotionDevice</c>, <c>Axis</c>,
        /// <c>Controller</c>), or returns <c>false</c> if it is not a known
        /// Robotics type in the supplied namespace table. This is an exact
        /// match that needs no server round-trip; use
        /// <see cref="GetRoboticsTypeNameAsync"/> to classify vendor subtypes.
        /// </summary>
        /// <param name="typeDefinition">
        /// The TypeDefinition NodeId of a discovered node.
        /// </param>
        /// <param name="namespaceUris">
        /// The namespace table that resolves the Robotics namespace index.
        /// </param>
        /// <param name="name">
        /// Receives the friendly type name when the method returns <c>true</c>.
        /// </param>
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

        private static async Task<ArrayOf<NodeId>> DiscoverAsync(
            ISession session,
            NodeId root,
            uint typeIdentifier,
            CancellationToken cancellationToken)
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
            var wantedType = new NodeId(typeIdentifier, (ushort)ns);

            // ManagedBrowseAsync follows continuation points, so a server that caps the
            // number of references it returns per node cannot silently truncate discovery.
            (ArrayOf<ArrayOf<ReferenceDescription>> results, _) = await session.ManagedBrowseAsync(
                null, null, [root], 0, BrowseDirection.Forward,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences, includeSubtypes: true,
                (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);

            var matches = new List<NodeId>();
            if (results.Count > 0)
            {
                ArrayOf<ReferenceDescription> refs = results[0];
                for (int i = 0; i < refs.Count; i++)
                {
                    ReferenceDescription r = refs[i];
                    NodeId typeDef = ExpandedNodeId.ToNodeId(r.TypeDefinition, session.NamespaceUris);
                    NodeId child = ExpandedNodeId.ToNodeId(r.NodeId, session.NamespaceUris);
                    if (typeDef.IsNull || child.IsNull)
                    {
                        continue;
                    }
                    // Accept vendor specialisations of the companion type as well;
                    // IsTypeOfAsync short-circuits on an exact match.
                    if (await session.NodeCache.IsTypeOfAsync(
                            typeDef, wantedType, cancellationToken).ConfigureAwait(false))
                    {
                        matches.Add(child);
                    }
                }
            }
            return matches;
        }

        private static readonly (uint Identifier, string Name)[] s_classification =
        [
            (RoboticsModel.AxisType, "Axis"),
            (RoboticsModel.ControllerType, "Controller"),
            (RoboticsModel.MotionDeviceType, "MotionDevice"),
            (RoboticsModel.MotionDeviceSystemType, "MotionDeviceSystem")
        ];
    }
}
