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

namespace Opc.Ua.Scales.Client
{
    /// <summary>
    /// Client-side helpers for the OPC 40200 Weighing Technology (Scales)
    /// companion specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scale is reachable from two places, and a conforming server
    /// publishes it in both: as a Device Integration component below
    /// <c>DeviceSet</c>, and as an OPC 40001-1 machine below <c>Machines</c>.
    /// <see cref="EnumerateScalesAsync"/> looks in both and de-duplicates.
    /// A scale system is found the same way; its scales are below its
    /// <c>SubDevices</c> (<see cref="EnumerateSystemScalesAsync"/>).
    /// </para>
    /// <para>
    /// Most OPC 40200 members are optional. Readers return <see langword="null"/>
    /// or an empty result when the scale does not publish a member; method
    /// wrappers throw a <see cref="ServiceResultException"/> carrying the
    /// server's status when a call fails, and <c>Bad_NotSupported</c> when the
    /// scale does not publish the method at all.
    /// </para>
    /// </remarks>
    public sealed partial class ScalesClient
    {
        /// <summary>
        /// Creates a Scales client over a connected session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context used for logging.</param>
        public ScalesClient(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Topology = new DiTopologyClient(session, telemetry);
            ScalesEncodeableRegistration.Register(session);
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
        /// of a scale.
        /// </summary>
        public DiTopologyClient Topology { get; }

        /// <summary>
        /// Gets whether the server publishes the OPC 40200 Scales namespace.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.Scales) >= 0;

        /// <summary>
        /// Gets the NodeId of <c>ScaleDeviceType</c>, or
        /// <see cref="NodeId.Null"/> when the server does not publish the
        /// Scales namespace.
        /// </summary>
        public NodeId ScaleDeviceTypeId => ScalesTypeId(ObjectTypes.ScaleDeviceType);

        /// <summary>
        /// Gets the NodeId of <c>ScaleSystemType</c>, or
        /// <see cref="NodeId.Null"/>.
        /// </summary>
        public NodeId ScaleSystemTypeId => ScalesTypeId(ObjectTypes.ScaleSystemType);

        /// <summary>
        /// Gets the NodeId of the OPC 40001-1 <c>Machines</c> folder, or
        /// <see cref="NodeId.Null"/>.
        /// </summary>
        public NodeId MachinesFolderId
        {
            get
            {
                int index = Session.NamespaceUris.GetIndex(Opc.Ua.Machinery.Namespaces.Machinery);
                return index < 0
                    ? NodeId.Null
                    : new NodeId(Opc.Ua.Machinery.Objects.Machines, (ushort)index);
            }
        }

        /// <summary>
        /// Enumerates every scale and scale system the server publishes below
        /// the DI <c>DeviceSet</c> and the Machinery <c>Machines</c> folder.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<ScaleEntry> EnumerateScalesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var seen = new HashSet<NodeId>();
            foreach (NodeId root in new[] { Topology.DeviceSetId, MachinesFolderId })
            {
                if (root.IsNull)
                {
                    continue;
                }
                await foreach (ScaleEntry entry in EnumerateScalesUnderAsync(root, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (seen.Add(entry.NodeId))
                    {
                        yield return entry;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates the scales and scale systems directly below
        /// <paramref name="root"/>.
        /// </summary>
        /// <param name="root">The folder to look in.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<ScaleEntry> EnumerateScalesUnderAsync(
            NodeId root,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (root.IsNull || !IsSupported)
            {
                yield break;
            }
            await foreach (ReferenceDescription reference in BrowseChildrenAsync(root, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                ScaleEntry? entry = await ClassifyAsync(reference, cancellationToken).ConfigureAwait(false);
                if (entry != null)
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// Enumerates the scales of a scale system (its <c>SubDevices</c>) or
        /// the weighing modules of a scale.
        /// </summary>
        /// <param name="parent">The scale system or scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<ScaleEntry> EnumerateSystemScalesAsync(
            NodeId parent,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId subDevices = await ResolveChildAsync(
                parent,
                ScalesName(BrowseNames.SubDevices),
                cancellationToken).ConfigureAwait(false);
            await foreach (ScaleEntry entry in EnumerateScalesUnderAsync(subDevices, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return entry;
            }
        }

        /// <summary>
        /// Returns every scale and scale system the server publishes.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<ScaleEntry>> DiscoverScalesAsync(
            CancellationToken cancellationToken = default)
        {
            var entries = new List<ScaleEntry>();
            await foreach (ScaleEntry entry in EnumerateScalesAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Resolves the standard scale kind a type definition is, or derives
        /// from, so a vendor subtype of <c>CheckweigherType</c> is still
        /// recognised as a checkweigher.
        /// </summary>
        /// <param name="typeDefinition">The type definition.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The kind, or null when the type is no concrete scale type.</returns>
        public async ValueTask<ScaleKind?> GetKindAsync(
            NodeId typeDefinition,
            CancellationToken cancellationToken = default)
        {
            ushort ns = (ushort)Math.Max(0, Session.NamespaceUris.GetIndex(Namespaces.Scales));
            NodeId current = typeDefinition;
            for (int depth = 0; depth < 16 && !current.IsNull; depth++)
            {
                if (current.NamespaceIndex == ns &&
                    current.TryGetValue(out uint id) &&
                    ScalesModel.KindOf(id) is ScaleKind kind)
                {
                    return kind;
                }
                current = ExpandedNodeId.ToNodeId(
                    await Session.NodeCache.FindSuperTypeAsync(current, cancellationToken).ConfigureAwait(false),
                    Session.NamespaceUris);
            }
            return null;
        }

        private async ValueTask<ScaleEntry?> ClassifyAsync(ReferenceDescription reference, CancellationToken ct)
        {
            var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
            var typeDefinition = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
            if (nodeId.IsNull || typeDefinition.IsNull)
            {
                return null;
            }
            if (await Session.NodeCache.IsTypeOfAsync(typeDefinition, ScaleSystemTypeId, ct).ConfigureAwait(false))
            {
                return new ScaleEntry(nodeId, reference.BrowseName, reference.DisplayName, typeDefinition, null, true);
            }
            if (await Session.NodeCache.IsTypeOfAsync(typeDefinition, ScaleDeviceTypeId, ct).ConfigureAwait(false))
            {
                ScaleKind? kind = await GetKindAsync(typeDefinition, ct).ConfigureAwait(false);
                return new ScaleEntry(nodeId, reference.BrowseName, reference.DisplayName, typeDefinition, kind, false);
            }
            return null;
        }

        private NodeId ScalesTypeId(uint id)
        {
            int index = Session.NamespaceUris.GetIndex(Namespaces.Scales);
            return index < 0 ? NodeId.Null : new NodeId(id, (ushort)index);
        }

        /// <summary>
        /// Gets the index the server assigned <paramref name="namespaceUri"/>.
        /// </summary>
        /// <exception cref="ServiceResultException">The server does not publish it.</exception>
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

        internal QualifiedName ScalesName(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Namespaces.Scales));
        }

        internal QualifiedName DiName(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi));
        }

        internal QualifiedName MachineryName(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.Machinery.Namespaces.Machinery));
        }

        internal QualifiedName PackMLName(string name)
        {
            return new QualifiedName(name, NamespaceIndexOf(Opc.Ua.PackML.Namespaces.PackML));
        }

        internal async ValueTask<NodeId> ResolveChildAsync(NodeId parent, QualifiedName browseName, CancellationToken ct)
        {
            ArrayOf<NodeId> resolved = await ResolvePathsAsync(parent, [[browseName]], ct).ConfigureAwait(false);
            return resolved.Count > 0 ? resolved[0] : NodeId.Null;
        }

        /// <summary>
        /// Resolves several relative paths from one node in a single
        /// <c>TranslateBrowsePathsToNodeIds</c>. Unresolved paths come back as
        /// <see cref="NodeId.Null"/>, positionally aligned with the input.
        /// </summary>
        internal async ValueTask<ArrayOf<NodeId>> ResolvePathsAsync(
            NodeId start,
            IReadOnlyList<QualifiedName[]> paths,
            CancellationToken ct)
        {
            var results = new NodeId[paths.Count];
            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = NodeId.Null;
            }
            if (start.IsNull || paths.Count == 0)
            {
                return results.ToArrayOf();
            }
            var browsePaths = new BrowsePath[paths.Count];
            for (int ii = 0; ii < paths.Count; ii++)
            {
                var elements = new RelativePathElement[paths[ii].Length];
                for (int jj = 0; jj < elements.Length; jj++)
                {
                    elements[jj] = new RelativePathElement
                    {
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = paths[ii][jj]
                    };
                }
                browsePaths[ii] = new BrowsePath
                {
                    StartingNode = start,
                    RelativePath = new RelativePath { Elements = elements.ToArrayOf() }
                };
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths.ToArrayOf(), ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < response.Results.Count && ii < results.Length; ii++)
            {
                BrowsePathResult result = response.Results[ii];
                if (StatusCode.IsGood(result.StatusCode) && result.Targets.Count > 0)
                {
                    results[ii] = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
                }
            }
            return results.ToArrayOf();
        }

        /// <summary>
        /// Reads the values of several nodes in one <c>Read</c>; a null NodeId
        /// or a bad result reads as <see cref="Variant.Null"/>.
        /// </summary>
        internal async ValueTask<Variant[]> ReadValuesAsync(ArrayOf<NodeId> nodeIds, CancellationToken ct)
        {
            DataValue[] results = await ReadDataValuesAsync(nodeIds, ct).ConfigureAwait(false);
            var values = new Variant[results.Length];
            for (int ii = 0; ii < results.Length; ii++)
            {
                if (StatusCode.IsNotBad(results[ii].StatusCode))
                {
                    values[ii] = results[ii].WrappedValue;
                }
            }
            return values;
        }

        /// <summary>
        /// Reads the data values of several nodes in one <c>Read</c>, with
        /// source timestamps; a null NodeId reads as
        /// <c>Bad_NodeIdUnknown</c>.
        /// </summary>
        internal async ValueTask<DataValue[]> ReadDataValuesAsync(ArrayOf<NodeId> nodeIds, CancellationToken ct)
        {
            var results = new DataValue[nodeIds.Count];
            var toRead = new List<ReadValueId>();
            var positions = new List<int>();
            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                results[ii] = DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
                if (!nodeIds[ii].IsNull)
                {
                    toRead.Add(new ReadValueId { NodeId = nodeIds[ii], AttributeId = Attributes.Value });
                    positions.Add(ii);
                }
            }
            if (toRead.Count == 0)
            {
                return results;
            }
            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Source,
                toRead.ToArrayOf(),
                ct).ConfigureAwait(false);
            for (int ii = 0; ii < positions.Count; ii++)
            {
                results[positions[ii]] = ii < response.Results.Count
                    ? response.Results[ii]
                    : DataValue.FromStatusCode(StatusCodes.BadNoData);
            }
            return results;
        }

        /// <summary>
        /// Calls a method below <paramref name="objectId"/> by browse name and
        /// throws when it is missing or fails.
        /// </summary>
        internal async ValueTask<ArrayOf<Variant>> CallAsync(
            NodeId objectId,
            QualifiedName method,
            CancellationToken ct,
            params Variant[] inputs)
        {
            NodeId methodId = await ResolveChildAsync(objectId, method, ct).ConfigureAwait(false);
            if (methodId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "'{0}' publishes no method '{1}'.",
                    objectId,
                    method.Name ?? string.Empty);
            }
            CallResponse response = await Session.CallAsync(
                requestHeader: null,
                methodsToCall: new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = inputs.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct: ct).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, "The server returned no call result.");
            }
            CallMethodResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(new ServiceResult(result.StatusCode));
            }
            return result.OutputArguments;
        }

        /// <summary>
        /// Browses the hierarchical children of <paramref name="parent"/>,
        /// draining continuation points. A bad browse yields nothing.
        /// </summary>
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
                yield return references[ii];
            }
        }
    }
}
