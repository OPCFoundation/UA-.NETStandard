/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Client-side helper for browsing the well-known DI topology
    /// containers (<c>DeviceSet</c>, <c>NetworkSet</c>,
    /// <c>DeviceTopology</c>) and enumerating their children.
    /// Complements <see cref="DiDiscoveryClient"/> with a focused
    /// container-level surface.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DiDiscoveryClient"/>, which recurses the entire
    /// <c>Objects</c> folder, this client lists direct children of the
    /// DI well-known containers — useful when a client wants to display
    /// a topology tree rather than a flat device list.
    /// </remarks>
    public sealed class DiTopologyClient
    {
        /// <summary>
        /// Creates a topology client.
        /// </summary>
        public DiTopologyClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// The owning session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>Returns the resolved NodeId of <c>DeviceSet</c>.</summary>
        public NodeId DeviceSetId
            => NodeId.Create(
                Opc.Ua.Di.Objects.DeviceSet,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Session.NamespaceUris);

        /// <summary>Returns the resolved NodeId of <c>NetworkSet</c>.</summary>
        public NodeId NetworkSetId
            => NodeId.Create(
                Opc.Ua.Di.Objects.NetworkSet,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Session.NamespaceUris);

        /// <summary>Returns the resolved NodeId of <c>DeviceTopology</c>.</summary>
        public NodeId DeviceTopologyId
            => NodeId.Create(
                Opc.Ua.Di.Objects.DeviceTopology,
                Opc.Ua.Di.Namespaces.OpcUaDi,
                Session.NamespaceUris);

        /// <summary>
        /// Enumerates the direct device children of <c>DeviceSet</c>.
        /// </summary>
        public IAsyncEnumerable<TopologyEntry> EnumerateDevicesAsync(
            CancellationToken cancellationToken = default)
            {
            return BrowseChildrenAsync(DeviceSetId, cancellationToken);
            }

        /// <summary>
        /// Enumerates the direct children of <c>NetworkSet</c> (usually
        /// network or fieldbus instances).
        /// </summary>
        public IAsyncEnumerable<TopologyEntry> EnumerateNetworksAsync(
            CancellationToken cancellationToken = default)
            {
            return BrowseChildrenAsync(NetworkSetId, cancellationToken);
            }

        /// <summary>
        /// Enumerates the direct children of an arbitrary topology
        /// container or device — typically used to recurse the
        /// <c>DeviceTopology</c> tree.
        /// </summary>
        public IAsyncEnumerable<TopologyEntry> EnumerateChildrenAsync(
            NodeId parentNodeId,
            CancellationToken cancellationToken = default)
        {
            if (parentNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Parent NodeId is required.", nameof(parentNodeId));
            }
            return BrowseChildrenAsync(parentNodeId, cancellationToken);
        }

        private async IAsyncEnumerable<TopologyEntry> BrowseChildrenAsync(
            NodeId parentId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            BrowseDescription description = new BrowseDescription
            {
                NodeId = parentId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse response = await Session
                .BrowseAsync(
                    requestHeader: null,
                    view: null,
                    requestedMaxReferencesPerNode: 0,
                    nodesToBrowse: new[] { description }.ToArrayOf(),
                    ct: ct)
                .ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                yield break;
            }

            BrowseResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                yield break;
            }

            for (int i = 0; i < result.References.Count; i++)
            {
                ReferenceDescription reference = result.References[i];
                NodeId targetId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, Session.NamespaceUris);
                if (targetId.IsNull)
                {
                    continue;
                }
                NodeId typeDefinitionId = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, Session.NamespaceUris);
                yield return new TopologyEntry(
                    targetId,
                    reference.DisplayName.Text ?? string.Empty,
                    typeDefinitionId,
                    reference.BrowseName);
            }
        }
    }

    /// <summary>
    /// A topology child returned by <see cref="DiTopologyClient"/>.
    /// </summary>
    /// <param name="NodeId">The child's NodeId.</param>
    /// <param name="DisplayName">The child's display name.</param>
    /// <param name="TypeDefinitionId">
    /// The child's type definition NodeId — useful for filtering by
    /// concrete device type.
    /// </param>
    /// <param name="BrowseName">The child's browse name.</param>
    public sealed record TopologyEntry(
        NodeId NodeId,
        string DisplayName,
        NodeId TypeDefinitionId,
        QualifiedName BrowseName);
}
