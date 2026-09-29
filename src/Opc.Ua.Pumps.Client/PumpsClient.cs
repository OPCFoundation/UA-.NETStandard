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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Pumps.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 40223 Pumps and Vacuum Pumps
    /// companion specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pump is reachable from two places, and a conforming server publishes
    /// it in both: as a Device Integration device below <c>DeviceSet</c>, and
    /// as an OPC 40001-1 machine below <c>Machines</c>.
    /// <see cref="EnumeratePumpsAsync"/> looks in both and de-duplicates, so
    /// a pump that appears twice is returned once.
    /// </para>
    /// <para>
    /// Almost every variable OPC 40223 declares is optional. Every accessor
    /// here therefore returns <see langword="null"/> - or an empty
    /// <see cref="PumpValueSet"/> - when the pump does not publish the group,
    /// rather than throwing. The one exception is
    /// <see cref="NamespaceIndexOf"/>, which throws when the server does not
    /// publish the Pumps namespace at all: that is a wrong-server error, not a
    /// missing-option one.
    /// </para>
    /// </remarks>
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Creates a Pumps client over a connected session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context used for logging.</param>
        public PumpsClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Topology = new DiTopologyClient(session, telemetry);
            PumpsEncodeableRegistration.Register(session);
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
        /// Gets the Device Integration topology client, for the DeviceSet side
        /// of a pump.
        /// </summary>
        public DiTopologyClient Topology { get; }

        /// <summary>
        /// Gets whether the server publishes the OPC 40223 Pumps namespace.
        /// </summary>
        public bool IsSupported =>
            Session.NamespaceUris.GetIndex(Namespaces.Pumps) >= 0;

        /// <summary>
        /// Gets the NodeId of <c>PumpType</c>, or <see cref="NodeId.Null"/>
        /// when the server does not publish the Pumps namespace.
        /// </summary>
        public NodeId PumpTypeId =>
            PumpsModel.TypeNodeId(ObjectTypes.PumpType, Session.NamespaceUris);

        /// <summary>
        /// Gets the NodeId of the OPC 40001-1 <c>Machines</c> folder, or
        /// <see cref="NodeId.Null"/> when the server does not publish the
        /// Machinery namespace.
        /// </summary>
        public NodeId MachinesFolderId
        {
            get
            {
                int index = Session.NamespaceUris.GetIndex(
                    Opc.Ua.Machinery.Namespaces.Machinery);
                return index < 0
                    ? NodeId.Null
                    : new NodeId(Opc.Ua.Machinery.Objects.Machines, (ushort)index);
            }
        }

        /// <summary>
        /// Enumerates every pump the server publishes, from the DI
        /// <c>DeviceSet</c> and the Machinery <c>Machines</c> folder.
        /// </summary>
        /// <remarks>
        /// A pump organized into both folders - which OPC 40223 encourages -
        /// is returned once.
        /// </remarks>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<PumpEntry> EnumeratePumpsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var seen = new HashSet<NodeId>();
            foreach (NodeId root in new[] { Topology.DeviceSetId, MachinesFolderId })
            {
                if (root.IsNull)
                {
                    continue;
                }
                await foreach (PumpEntry entry in EnumeratePumpsUnderAsync(
                        root,
                        cancellationToken).ConfigureAwait(false))
                {
                    if (seen.Add(entry.NodeId))
                    {
                        yield return entry;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates the pumps directly below <paramref name="root"/>.
        /// </summary>
        /// <remarks>
        /// A child qualifies when its type definition is, or derives from,
        /// <c>PumpType</c> - so a vendor subtype is found too, which probing for
        /// the exact type would miss. <c>MultiPumpType</c> does not qualify: it
        /// is a functional group below <c>Operational</c>, not a pump.
        /// </remarks>
        /// <param name="root">
        /// The folder to look in, typically the DI <c>DeviceSet</c>, the
        /// Machinery <c>Machines</c> folder, or the <c>Objects</c> folder.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<PumpEntry> EnumeratePumpsUnderAsync(
            NodeId root,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId pumpType = PumpTypeId;
            if (root.IsNull || pumpType.IsNull)
            {
                yield break;
            }

            await foreach (ReferenceDescription reference in BrowseChildrenAsync(
                    root,
                    NodeClass.Object,
                    cancellationToken).ConfigureAwait(false))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var typeDefinition = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition,
                    Session.NamespaceUris);
                if (nodeId.IsNull || typeDefinition.IsNull)
                {
                    continue;
                }
                if (await IsPumpTypeAsync(typeDefinition, pumpType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    yield return new PumpEntry(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName,
                        typeDefinition);
                }
            }
        }

        /// <summary>
        /// Returns the NodeIds of every pump the server publishes.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<NodeId>> DiscoverPumpsAsync(
            CancellationToken cancellationToken = default)
        {
            var nodeIds = new List<NodeId>();
            await foreach (PumpEntry entry in EnumeratePumpsAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                nodeIds.Add(entry.NodeId);
            }
            return nodeIds.ToArrayOf();
        }

        /// <summary>
        /// Gets whether <paramref name="typeDefinition"/> is, or derives from,
        /// <c>PumpType</c>.
        /// </summary>
        /// <remarks>
        /// <c>MultiPumpType</c> is deliberately not accepted: despite the
        /// name it is a functional group describing one pump's role inside a
        /// multi-pump set, not a pump, and it lives below
        /// <c>Operational</c> rather than in a device folder.
        /// </remarks>
        /// <param name="typeDefinition">The type definition to classify.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<bool> IsPumpAsync(
            NodeId typeDefinition,
            CancellationToken cancellationToken = default)
        {
            return IsPumpTypeAsync(typeDefinition, PumpTypeId, cancellationToken);
        }

        private async ValueTask<bool> IsPumpTypeAsync(
            NodeId typeDefinition,
            NodeId pumpType,
            CancellationToken cancellationToken)
        {
            if (typeDefinition.IsNull || pumpType.IsNull)
            {
                return false;
            }
            return await Session.NodeCache
                .IsTypeOfAsync(typeDefinition, pumpType, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the pump's <c>Identification</c> add-in, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveIdentificationAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            return ResolveDiGroupAsync(
                pump,
                PumpsModel.DiGroups.Identification,
                cancellationToken);
        }

        /// <summary>
        /// Resolves one of the <c>PumpType</c> groups that live in the Device
        /// Integration namespace - <c>Identification</c>,
        /// <c>Configuration</c>, <c>Maintenance</c> or <c>Operational</c>.
        /// </summary>
        /// <param name="pump">The pump to inspect.</param>
        /// <param name="browseName">
        /// One of the <see cref="PumpsModel.DiGroups"/> constants.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveDiGroupAsync(
            NodeId pump,
            string browseName,
            CancellationToken cancellationToken = default)
        {
            return ResolveChildAsync(
                pump,
                new QualifiedName(
                    browseName,
                    NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi)),
                cancellationToken);
        }

        /// <summary>
        /// Resolves one of the <c>PumpType</c> groups that live in the Pumps
        /// namespace - <c>Events</c>, <c>Ports</c> or <c>Documentation</c> -
        /// or any nested Pumps-namespace child.
        /// </summary>
        /// <param name="parent">The node to inspect.</param>
        /// <param name="browseName">
        /// A browse name, normally one of the generated
        /// <see cref="BrowseNames"/> constants.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolvePumpsChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken = default)
        {
            return ResolveChildAsync(
                parent,
                new QualifiedName(browseName, NamespaceIndexOf(Namespaces.Pumps)),
                cancellationToken);
        }

        /// <summary>
        /// Gets the index the server assigned <paramref name="namespaceUri"/>.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI to look up.</param>
        /// <exception cref="ServiceResultException">
        /// The server does not publish the namespace.
        /// </exception>
        internal ushort NamespaceIndexOf(string namespaceUri)
        {
            int index = Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The server does not publish the namespace '{0}'.",
                    namespaceUri);
            }
            return (ushort)index;
        }

        internal async ValueTask<NodeId> ResolveChildAsync(
            NodeId parent,
            QualifiedName browseName,
            CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                return NodeId.Null;
            }
            ArrayOf<NodeId> resolved = await ResolveChildrenAsync(
                parent,
                new[] { browseName },
                cancellationToken).ConfigureAwait(false);
            return resolved.Count > 0 ? resolved[0] : NodeId.Null;
        }

        /// <summary>
        /// Resolves several children of one parent in a single
        /// <c>TranslateBrowsePathsToNodeIds</c>. Unresolved entries come back
        /// as <see cref="NodeId.Null"/>, positionally aligned with
        /// <paramref name="browseNames"/>.
        /// </summary>
        internal async ValueTask<ArrayOf<NodeId>> ResolveChildrenAsync(
            NodeId parent,
            IReadOnlyList<QualifiedName> browseNames,
            CancellationToken cancellationToken)
        {
            var results = new NodeId[browseNames.Count];
            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = NodeId.Null;
            }
            if (parent.IsNull || browseNames.Count == 0)
            {
                return results.ToArrayOf();
            }

            var paths = new BrowsePath[browseNames.Count];
            for (int ii = 0; ii < browseNames.Count; ii++)
            {
                paths[ii] = new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId =
                                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = browseNames[ii]
                            }
                        ]
                    }
                };
            }

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);

            for (int ii = 0; ii < response.Results.Count && ii < results.Length; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                if (StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0)
                {
                    results[ii] = ExpandedNodeId.ToNodeId(
                        result.Targets[0].TargetId,
                        Session.NamespaceUris);
                }
            }
            return results.ToArrayOf();
        }

        /// <summary>
        /// Browses the hierarchical children of <paramref name="parent"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Browser"/> drains BrowseNext until the continuation
        /// point is exhausted, so a paginating server does not silently
        /// truncate the result - which a single raw <c>Browse</c> would. A bad
        /// status for the parent itself (it is gone, say) yields nothing
        /// rather than throwing, matching the "absent means empty" contract of
        /// every accessor here. A failed service call - a lost session, a
        /// timeout, a transport error - says nothing about the parent and
        /// propagates as a <see cref="ServiceResultException"/>.
        /// </remarks>
        internal async IAsyncEnumerable<ReferenceDescription> BrowseChildrenAsync(
            NodeId parent,
            NodeClass nodeClassMask,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                yield break;
            }

            var browser = new Browser(Session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (int)nodeClassMask,
                ResultMask = (uint)BrowseResultMask.All
            });

            // The managed overload reports a bad status for the node in
            // Errors and throws only when the service call itself fails.
            ResultSet<ArrayOf<ReferenceDescription>> result = await browser
                .BrowseAsync(new[] { parent }.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            if (result.Results.Count == 0 ||
                (result.Errors.Count > 0 && StatusCode.IsBad(result.Errors[0].StatusCode)))
            {
                yield break;
            }

            ArrayOf<ReferenceDescription> references = result.Results[0];
            for (int ii = 0; ii < references.Count; ii++)
            {
                yield return references[ii];
            }
        }

        internal static string NameOf(QualifiedName browseName)
        {
            return browseName.Name ?? string.Empty;
        }
    }
}
