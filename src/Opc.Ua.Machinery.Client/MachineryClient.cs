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
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 40001 Machinery specification series.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Discovery starts at the <c>Machines</c> folder, which OPC 40001-1
    /// organizes from the <c>Objects</c> folder — not from the Device
    /// Integration <c>DeviceSet</c>, the obvious but wrong guess. A machine
    /// that is also a DI device is reachable from both, and
    /// <see cref="Topology"/> is there for the second path.
    /// </para>
    /// <para>
    /// Nothing here assumes a particular machine type: OPC 40001-1 defines
    /// none. What identifies a machine is the set of building blocks it
    /// publishes, so every accessor resolves by browse path and returns
    /// <see langword="null"/> when the block is absent.
    /// </para>
    /// </remarks>
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Creates a Machinery client over a connected session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context used for logging.</param>
        public MachineryClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Topology = new DiTopologyClient(session, telemetry);
            MachineryEncodeableRegistration.Register(session);
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
        /// Gets the Device Integration topology client, for a machine that is
        /// also a DI device.
        /// </summary>
        public DiTopologyClient Topology { get; }

        /// <summary>
        /// Gets the NodeId of the OPC 40001-1 <c>Machines</c> folder, or
        /// <see cref="NodeId.Null"/> when the server does not publish the
        /// Machinery namespace.
        /// </summary>
        public NodeId MachinesFolderId
        {
            get
            {
                int namespaceIndex = Session.NamespaceUris.GetIndex(
                    Opc.Ua.Machinery.Namespaces.Machinery);
                return namespaceIndex < 0
                    ? NodeId.Null
                    : new NodeId(Objects.Machines, (ushort)namespaceIndex);
            }
        }

        /// <summary>
        /// Enumerates the machines below the <c>Machines</c> folder.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public IAsyncEnumerable<MachineEntry> EnumerateMachinesAsync(
            CancellationToken cancellationToken = default)
        {
            return EnumerateObjectsAsync(MachinesFolderId, cancellationToken);
        }

        /// <summary>
        /// Returns the NodeIds of the machines below the <c>Machines</c> folder.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<NodeId>> DiscoverMachinesAsync(
            CancellationToken cancellationToken = default)
        {
            var nodeIds = new List<NodeId>();
            await foreach (MachineEntry entry in EnumerateMachinesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                nodeIds.Add(entry.NodeId);
            }
            return nodeIds.ToArrayOf();
        }

        /// <summary>
        /// Enumerates the components below a machine's <c>Components</c>
        /// add-in. Returns nothing when the machine publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineEntry> EnumerateComponentsAsync(
            NodeId machine,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId components = await ResolveMachineryChildAsync(
                machine,
                MachineryBrowseNames.Components,
                cancellationToken).ConfigureAwait(false);
            if (components.IsNull)
            {
                yield break;
            }
            await foreach (MachineEntry entry in EnumerateObjectsAsync(
                    components,
                    cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Resolves a machine's <c>Monitoring</c> add-in, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveMonitoringAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            return ResolveMachineryChildAsync(
                machine,
                MachineryBrowseNames.Monitoring,
                cancellationToken);
        }

        /// <summary>
        /// Resolves a machinery item's <c>Identification</c> add-in, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveIdentificationAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            return ResolveChildAsync(
                machineryItem,
                new QualifiedName(
                    DiBrowseNames.Identification,
                    NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi)),
                cancellationToken);
        }

        internal ValueTask<NodeId> ResolveMachineryChildAsync(
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            ushort namespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Namespaces.Machinery);
            return ResolveChildAsync(
                parent,
                new QualifiedName(browseName, namespaceIndex),
                cancellationToken);
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
            return await ResolvePathAsync(parent, cancellationToken, browseName)
                .ConfigureAwait(false);
        }

        internal async ValueTask<NodeId> ResolvePathAsync(
            NodeId parent,
            CancellationToken cancellationToken,
            params QualifiedName[] browseNames)
        {
            if (parent.IsNull || browseNames.Length == 0)
            {
                return NodeId.Null;
            }

            var elements = new RelativePathElement[browseNames.Length];
            for (int ii = 0; ii < browseNames.Length; ii++)
            {
                elements[ii] = new RelativePathElement
                {
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = browseNames[ii]
                };
            }

            var path = new BrowsePath
            {
                StartingNode = parent,
                RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
            };

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new[] { path }.ToArrayOf(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode) ||
                response.Results[0].Targets.Count == 0)
            {
                return NodeId.Null;
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);
        }

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

        /// <summary>
        /// Resolves the OPC 40001-1 <c>MachineryBuildingBlocks</c> organizer
        /// of a machinery item, or <see cref="NodeId.Null"/> when the item
        /// publishes none.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 §7.1 makes this the one place every building block a
        /// machinery item supports is reachable from, which is what a client
        /// that wants to discover them rather than probe for them browses.
        /// </remarks>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveBuildingBlocksAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            return ResolveMachineryChildAsync(
                machineryItem,
                MachineryBuildingBlocksBrowseName,
                cancellationToken);
        }

        /// <summary>
        /// Enumerates the building blocks a machinery item publishes through
        /// its <c>MachineryBuildingBlocks</c> organizer.
        /// </summary>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineEntry> EnumerateBuildingBlocksAsync(
            NodeId machineryItem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId folder = await ResolveBuildingBlocksAsync(machineryItem, cancellationToken)
                .ConfigureAwait(false);
            await foreach (MachineEntry entry in EnumerateObjectsAsync(
                    folder,
                    cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// The browse name OPC 40001-1 §7.1 gives the building-block
        /// organizer. The model declares no type for it, so there is no
        /// generated constant to use.
        /// </summary>
        internal const string MachineryBuildingBlocksBrowseName = "MachineryBuildingBlocks";

        /// <summary>
        /// Reads the values of the named children of a node in one
        /// <c>TranslateBrowsePaths</c> and one batched <c>Read</c>.
        /// </summary>
        /// <remarks>
        /// Children the server does not publish are simply absent from the
        /// result: nearly everything OPC 40001 declares is optional, so a
        /// missing child is the normal case and not an error.
        /// </remarks>
        internal async ValueTask<Dictionary<QualifiedName, Variant>> ReadChildValuesAsync(
            NodeId parent,
            IReadOnlyList<QualifiedName> browseNames,
            NodeId referenceTypeId,
            CancellationToken cancellationToken)
        {
            var values = new Dictionary<QualifiedName, Variant>();
            if (parent.IsNull || browseNames.Count == 0)
            {
                return values;
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
                                ReferenceTypeId = referenceTypeId,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = browseNames[ii]
                            }
                        ]
                    }
                };
            }

            TranslateBrowsePathsToNodeIdsResponse translated = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);

            var indices = new List<int>();
            var reads = new List<ReadValueId>();
            for (int ii = 0; ii < translated.Results.Count; ii++)
            {
                BrowsePathResult result = translated.Results[ii];
                if (StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0)
                {
                    reads.Add(new ReadValueId
                    {
                        NodeId = ExpandedNodeId.ToNodeId(
                            result.Targets[0].TargetId,
                            Session.NamespaceUris),
                        AttributeId = Attributes.Value
                    });
                    indices.Add(ii);
                }
            }
            if (reads.Count == 0)
            {
                return values;
            }

            ReadResponse read = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: reads.ToArray().ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);

            for (int ii = 0; ii < read.Results.Count && ii < indices.Count; ii++)
            {
                if (StatusCode.IsGood(read.Results[ii].StatusCode))
                {
                    values[browseNames[indices[ii]]] = read.Results[ii].WrappedValue;
                }
            }
            return values;
        }

        internal static double? AsDouble(
            Dictionary<QualifiedName, Variant> values,
            QualifiedName name)
        {
            if (!values.TryGetValue(name, out Variant value))
            {
                return null;
            }
            if (value.TryGetValue(out double number))
            {
                return number;
            }
            return value.TryGetValue(out float single) ? single : null;
        }

        internal static string? AsString(
            Dictionary<QualifiedName, Variant> values,
            QualifiedName name)
        {
            return values.TryGetValue(name, out Variant value) &&
                value.TryGetValue(out string text)
                ? text
                : null;
        }

        internal IAsyncEnumerable<MachineEntry> EnumerateChildObjectsAsync(
            NodeId parent,
            CancellationToken cancellationToken)
        {
            return EnumerateObjectsAsync(parent, cancellationToken);
        }

        internal async IAsyncEnumerable<MachineEntry> EnumerateChildVariablesAsync(
            NodeId parent,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (MachineEntry entry in BrowseChildrenAsync(
                    parent,
                    NodeClass.Variable,
                    cancellationToken).ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        private IAsyncEnumerable<MachineEntry> EnumerateObjectsAsync(
            NodeId parent,
            CancellationToken cancellationToken)
        {
            return BrowseChildrenAsync(parent, NodeClass.Object, cancellationToken);
        }

        private IAsyncEnumerable<MachineEntry> BrowseChildrenAsync(
            NodeId parent,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            return BrowseChildrenAsync(
                parent,
                nodeClass,
                Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken);
        }

        private async IAsyncEnumerable<MachineEntry> BrowseChildrenAsync(
            NodeId parent,
            NodeClass nodeClass,
            NodeId referenceTypeId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (parent.IsNull)
            {
                yield break;
            }

            // A raw single BrowseAsync call silently drops everything past the
            // first continuation point on a server that paginates (a large or
            // busy address space). Browser.BrowseAsync(NodeId, ct) already
            // drains BrowseNext until the continuation point is exhausted, and
            // releases it if the enumeration is cancelled midway, so this
            // enumerator never returns a truncated child list.
            var browser = new Browser(Session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = referenceTypeId,
                IncludeSubtypes = true,
                NodeClassMask = (int)nodeClass,
                ResultMask = (uint)BrowseResultMask.All
            });

            // A bad browse status (e.g. the parent no longer exists) yields
            // an empty enumeration rather than an exception, matching every
            // other accessor's "return null/empty when the block is absent"
            // contract.
            ArrayOf<ReferenceDescription> references;
            try
            {
                references = await browser.BrowseAsync(parent, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (StatusCode.IsBad(ex.StatusCode))
            {
                yield break;
            }

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                var nodeId = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    Session.NamespaceUris);
                if (nodeId.IsNull)
                {
                    continue;
                }
                yield return new MachineEntry(
                    nodeId,
                    reference.BrowseName,
                    reference.DisplayName,
                    ExpandedNodeId.ToNodeId(
                        reference.TypeDefinition,
                        Session.NamespaceUris));
            }
        }
    }
}
