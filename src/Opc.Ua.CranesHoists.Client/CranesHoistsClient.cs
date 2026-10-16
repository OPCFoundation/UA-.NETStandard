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

[assembly: CLSCompliant(false)]

namespace Opc.Ua.CranesHoists.Client
{
    /// <summary>
    /// Provides bounded crane discovery, typed snapshots and explicit standard configuration writes.
    /// </summary>
    /// <remarks>
    /// The client borrows its session and never closes it. Namespace presence alone is not a capability claim.
    /// Optional absence is distinct from service/access failures and a present variable's bad quality.
    /// </remarks>
    public sealed partial class CranesHoistsClient
    {
        /// <summary>
        /// Creates a client over an existing connected session.
        /// </summary>
        public CranesHoistsClient(
            ISession session,
            ITelemetryContext telemetry,
            CranesHoistsClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new CranesHoistsClientOptions();
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the borrowed session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context used by generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the remote namespace table contains the released crane model.
        /// </summary>
        public bool SupportsModel => Session.NamespaceUris.GetIndex(CranesHoistsModel.NamespaceUri) >= 0;

        /// <summary>
        /// Returns the generated system proxy for advanced, namespace-correct access.
        /// </summary>
        public CraneMotionDeviceSystemTypeClient System(NodeId nodeId)
        {
            RequireNodeId(nodeId);
            return new CraneMotionDeviceSystemTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Returns the generated axis proxy for advanced access.
        /// </summary>
        public CraneAxisTypeClient Axis(NodeId nodeId)
        {
            RequireNodeId(nodeId);
            return new CraneAxisTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Discovers systems from the mandatory DeviceSet entry point, following vendor subtypes and continuations.
        /// </summary>
        public ValueTask<ArrayOf<CraneEntry>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportsModel)
            {
                return new ValueTask<ArrayOf<CraneEntry>>(ArrayOf<CraneEntry>.Empty);
            }
            NodeId deviceSet = NodeId.Create(Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, Session.NamespaceUris);
            return DiscoverAsync(deviceSet, cancellationToken);
        }

        /// <summary>
        /// Discovers crane systems under a supplied DeviceSet, Machines folder or application organizer.
        /// </summary>
        public async ValueTask<ArrayOf<CraneEntry>> DiscoverAsync(
            NodeId root,
            CancellationToken cancellationToken = default)
        {
            RequireNodeId(root);
            cancellationToken.ThrowIfCancellationRequested();
            if (!SupportsModel)
            {
                return [];
            }
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                root, Opc.Ua.ReferenceTypeIds.HierarchicalReferences, (uint)NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            var entries = new List<CraneEntry>();
            var seen = new HashSet<NodeId>();
            for (int i = 0; i < references.Count; i++)
            {
                ReferenceDescription reference = references[i];
                NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (!node.IsNull && seen.Add(node) &&
                    await IsTypeAsync(type, CraneElementKind.System, cancellationToken).ConfigureAwait(false))
                {
                    entries.Add(new CraneEntry(node, reference.BrowseName, reference.DisplayName, type,
                        CraneElementKind.System));
                }
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Reads all selected crane elements in one bounded system, retaining native topology and value quality.
        /// </summary>
        public async ValueTask<CraneSystemSnapshot> ReadSystemAsync(
            NodeId systemId,
            CancellationToken cancellationToken = default)
        {
            CraneEntry system = await ReadEntryAsync(systemId, cancellationToken).ConfigureAwait(false);
            if (system.Kind != CraneElementKind.System)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "The node is not a crane system.");
            }
            CraneElementSnapshot root = await ReadElementAsync(system, cancellationToken).ConfigureAwait(false);
            var queue = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { systemId };
            var elements = new List<CraneElementSnapshot>();
            queue.Enqueue(systemId);
            while (queue.Count > 0)
            {
                NodeId parent = queue.Dequeue();
                ArrayOf<ReferenceDescription> children = await BrowseAsync(
                    parent, Opc.Ua.ReferenceTypeIds.HierarchicalReferences, (uint)NodeClass.Object, cancellationToken)
                    .ConfigureAwait(false);
                for (int i = 0; i < children.Count; i++)
                {
                    ReferenceDescription child = children[i];
                    NodeId id = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                    if (id.IsNull || !visited.Add(id))
                    {
                        continue;
                    }
                    if (visited.Count > m_options.MaximumNodes)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTooManyOperations,
                            "The crane inventory exceeds the configured object bound.");
                    }
                    NodeId type = ExpandedNodeId.ToNodeId(child.TypeDefinition, Session.NamespaceUris);
                    CraneElementKind? kind = await ClassifyAsync(type, cancellationToken).ConfigureAwait(false);
                    if (kind == CraneElementKind.System)
                    {
                        continue;
                    }
                    queue.Enqueue(id);
                    if (kind.HasValue)
                    {
                        elements.Add(await ReadElementAsync(
                            new CraneEntry(id, child.BrowseName, child.DisplayName, type, kind.Value),
                            cancellationToken).ConfigureAwait(false));
                    }
                }
            }
            return new CraneSystemSnapshot(root, elements.ToArrayOf());
        }

        /// <summary>
        /// Reads one actual crane element, including vendor subtypes.
        /// </summary>
        public async ValueTask<CraneElementSnapshot> ReadElementAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            CraneEntry entry = await ReadEntryAsync(nodeId, cancellationToken).ConfigureAwait(false);
            return await ReadElementAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes one explicitly selected standard request property; server authorization remains authoritative.
        /// </summary>
        public async ValueTask<StatusCode> ConfigureLimitAsync(
            NodeId element,
            CraneSignal signal,
            Variant value,
            CancellationToken cancellationToken = default)
        {
            if (!CraneSignals.IsRequest(signal))
            {
                throw new ArgumentOutOfRangeException(nameof(signal), "Only standard limit requests may be written.");
            }
            CraneEntry entry = await ReadEntryAsync(element, cancellationToken).ConfigureAwait(false);
            CraneElementKind required = signal == CraneSignal.SpeedLimitGlobalEnabled
                ? CraneElementKind.System
                : CraneElementKind.Axis;
            if (entry.Kind != required)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "The request targets the wrong type.");
            }
            ArrayOf<NodeId> targets = await ResolveAsync(
                element, [CraneSignals.Path(entry.Kind, signal, Session.NamespaceUris)], cancellationToken)
                .ConfigureAwait(false);
            if (targets[0].IsNull)
            {
                throw ServiceResultException.Create(StatusCodes.BadNotSupported, "The limit request is not exposed.");
            }
            WriteResponse response = await Session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = targets[0],
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value)
                }
            ], cancellationToken).ConfigureAwait(false);
            RequireResultCount(response.Results.Count, 1);
            ThrowIfBad(response.Results[0]);
            return response.Results[0];
        }

        /// <summary>
        /// Resolves an actual component identity without substituting its standard base type.
        /// </summary>
        private async ValueTask<CraneEntry> ReadEntryAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            RequireNodeId(nodeId);
            INode node = await Session.NodeCache.GetNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            NodeId type = ExpandedNodeId.ToNodeId(node.TypeDefinitionId, Session.NamespaceUris);
            CraneElementKind? kind = await ClassifyAsync(type, cancellationToken).ConfigureAwait(false);
            return kind.HasValue
                ? new CraneEntry(nodeId, node.BrowseName, node.DisplayName, type, kind.Value)
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "The node is not a crane element.");
        }

        /// <summary>
        /// Reads scalar leaves below generated object proxies, keeping absent nodes separate from bad values.
        /// </summary>
        private async ValueTask<CraneElementSnapshot> ReadElementAsync(
            CraneEntry entry,
            CancellationToken cancellationToken)
        {
            await RequireParameterSetAsync(entry, cancellationToken).ConfigureAwait(false);
            ArrayOf<CraneSignal> signals = CraneSignals.For(entry.Kind);
            var paths = new List<ArrayOf<QualifiedName>>(signals.Count);
            foreach (CraneSignal signal in signals)
            {
                paths.Add(CraneSignals.Path(entry.Kind, signal, Session.NamespaceUris));
            }
            ArrayOf<NodeId> ids = await ResolveAsync(entry.NodeId, paths.ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            var reads = new List<ReadValueId>();
            foreach (NodeId id in ids)
            {
                if (!id.IsNull)
                {
                    reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
                    reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.UserAccessLevel });
                }
            }
            ArrayOf<DataValue> values = [];
            if (reads.Count > 0)
            {
                ReadResponse response = await Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, reads.ToArrayOf(), cancellationToken).ConfigureAwait(false);
                RequireResultCount(response.Results.Count, reads.Count);
                values = response.Results;
            }
            var snapshots = new List<CraneSignalValue>(signals.Count);
            ArrayOf<EnumValueType> enumeration = [];
            int cursor = 0;
            for (int i = 0; i < signals.Count; i++)
            {
                if (ids[i].IsNull)
                {
                    snapshots.Add(new CraneSignalValue(signals[i], NodeId.Null, DataValue.Null, false));
                    continue;
                }
                DataValue value = values[cursor++];
                DataValue access = values[cursor++];
                ThrowIfBad(access.StatusCode);
                if (!access.WrappedValue.TryGetValue(out byte levels))
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "UserAccessLevel is not a Byte.");
                }
                snapshots.Add(new CraneSignalValue(
                    signals[i], ids[i], value, (levels & AccessLevels.CurrentWrite) != 0));
                if (signals[i] == CraneSignal.AxisFunction)
                {
                    ArrayOf<NodeId> metadata = await ResolveAsync(
                        ids[i], [[new QualifiedName(Opc.Ua.BrowseNames.EnumValues)]], cancellationToken)
                        .ConfigureAwait(false);
                    if (metadata[0].IsNull)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNoData, "Axis function metadata is missing.");
                    }
                    DataValue raw = await Session.ReadValueAsync(metadata[0], cancellationToken).ConfigureAwait(false);
                    ThrowIfBad(raw.StatusCode);
                    if (!raw.WrappedValue.TryGetValue(out enumeration, Session.MessageContext))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTypeMismatch, "EnumValues has the wrong type.");
                    }
                }
            }
            var relationships = new List<CraneRelationship>();
            NodeId relationship = entry.Kind == CraneElementKind.ProtectiveFunction
                ? NodeId.Create(ReferenceTypes.ProtectsAxis, CranesHoistsModel.NamespaceUri, Session.NamespaceUris)
                : Opc.Ua.ReferenceTypeIds.HasPhysicalComponent;
            if (entry.Kind is CraneElementKind.ProtectiveFunction or CraneElementKind.MotionDevice)
            {
                ArrayOf<ReferenceDescription> references = await BrowseAsync(
                    entry.NodeId, relationship, (uint)NodeClass.Object, cancellationToken).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    NodeId target = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                    if (!target.IsNull)
                    {
                        relationships.Add(new CraneRelationship(entry.NodeId, reference.ReferenceTypeId, target));
                    }
                }
            }
            CraneIdentificationSnapshot? identification = await ReadIdentificationAsync(entry, cancellationToken)
                .ConfigureAwait(false);
            return new CraneElementSnapshot(entry, snapshots.ToArrayOf(), relationships.ToArrayOf(), enumeration,
                identification);
        }

        /// <summary>
        /// Reads the existing DI-qualified Machinery nameplate without flattening bad fields into empty strings.
        /// </summary>
        private async ValueTask<CraneIdentificationSnapshot?> ReadIdentificationAsync(
            CraneEntry entry,
            CancellationToken cancellationToken)
        {
            ushort ns = CraneSignals.Index(Session.NamespaceUris, Di.Namespaces.OpcUaDi);
            ArrayOf<NodeId> nameplate = await ResolveAsync(entry.NodeId,
                [[new QualifiedName(Di.BrowseNames.Identification, ns)]], cancellationToken).ConfigureAwait(false);
            if (nameplate[0].IsNull)
            {
                if (entry.Kind is CraneElementKind.System or CraneElementKind.MotionDevice)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNoData, "The mandatory nameplate is absent.");
                }
                return null;
            }
            ArrayOf<NodeId> fields = await ResolveAsync(nameplate[0],
            [
                [new QualifiedName(Di.BrowseNames.Manufacturer, ns)],
                [new QualifiedName(Di.BrowseNames.SerialNumber, ns)],
                [new QualifiedName(Di.BrowseNames.ProductInstanceUri, ns)],
                [new QualifiedName(Di.BrowseNames.Model, ns)],
                [new QualifiedName(Di.BrowseNames.ProductCode, ns)]
            ], cancellationToken).ConfigureAwait(false);
            if (fields[0].IsNull || fields[1].IsNull ||
                (entry.Kind is CraneElementKind.System or CraneElementKind.MotionDevice && fields[2].IsNull))
            {
                throw ServiceResultException.Create(StatusCodes.BadNoData, "A required nameplate field is absent.");
            }
            var requests = new List<ReadValueId>();
            foreach (NodeId field in fields)
            {
                if (!field.IsNull)
                {
                    requests.Add(new ReadValueId { NodeId = field, AttributeId = Attributes.Value });
                }
            }
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, requests.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            RequireResultCount(response.Results.Count, requests.Count);
            var values = new DataValue[fields.Count];
            int index = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                values[i] = fields[i].IsNull ? DataValue.Null : response.Results[index++];
            }
            return new CraneIdentificationSnapshot(nameplate[0], values[0], values[1], values[2], values[3], values[4]);
        }

        /// <summary>
        /// Composes generated proxies and refuses to turn their missing mandatory child into empty success.
        /// </summary>
        private async ValueTask RequireParameterSetAsync(CraneEntry entry, CancellationToken cancellationToken)
        {
            BaseObjectTypeClient? parameters = entry.Kind switch
            {
                CraneElementKind.System => await System(entry.NodeId).GetParameterSetAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false),
                CraneElementKind.Axis => await Axis(entry.NodeId).GetParameterSetAsync(Telemetry, cancellationToken)
                    .ConfigureAwait(false),
                CraneElementKind.MotionDevice => await new CraneMotionDeviceTypeClient(Session, entry.NodeId, Telemetry)
                    .GetParameterSetAsync(Telemetry, cancellationToken).ConfigureAwait(false),
                CraneElementKind.Motor => await new CraneMotorTypeClient(Session, entry.NodeId, Telemetry)
                    .GetParameterSetAsync(Telemetry, cancellationToken).ConfigureAwait(false),
                CraneElementKind.Brake => await new BrakeTypeClient(Session, entry.NodeId, Telemetry)
                    .GetParameterSetAsync(Telemetry, cancellationToken).ConfigureAwait(false),
                CraneElementKind.ProtectiveFunction => await new ProtectiveFunctionTypeClient(
                    Session, entry.NodeId, Telemetry)
                    .GetParameterSetAsync(Telemetry, cancellationToken).ConfigureAwait(false),
                _ => null
            };
            if (parameters == null)
            {
                // The shared proxy reports an unresolved child as null. Resolve strictly to preserve
                // access/service failures rather than interpreting every bad status as optional absence.
                await ResolveAsync(entry.NodeId,
                [[new QualifiedName(Di.BrowseNames.ParameterSet,
                    CraneSignals.Index(Session.NamespaceUris, Di.Namespaces.OpcUaDi))]],
                    cancellationToken).ConfigureAwait(false);
                throw ServiceResultException.Create(
                    StatusCodes.BadNoData, "The mandatory ParameterSet is unavailable.");
            }
        }

        /// <summary>
        /// Classifies vendor types by their standard ancestor.
        /// </summary>
        private async ValueTask<CraneElementKind?> ClassifyAsync(NodeId type, CancellationToken cancellationToken)
        {
            if (!SupportsModel || type.IsNull)
            {
                return null;
            }
            for (CraneElementKind kind = CraneElementKind.System; kind <= CraneElementKind.ProtectiveFunction; kind++)
            {
                if (await IsTypeAsync(type, kind, cancellationToken).ConfigureAwait(false))
                {
                    return kind;
                }
            }
            return null;
        }

        /// <summary>
        /// Resolves the ancestor against the session's real namespace table.
        /// </summary>
        private ValueTask<bool> IsTypeAsync(NodeId type, CraneElementKind kind, CancellationToken cancellationToken)
        {
            NodeId expected = NodeId.Create(
                CraneSignals.TypeId(kind), CranesHoistsModel.NamespaceUri, Session.NamespaceUris);
            return type.IsNull
                ? new ValueTask<bool>(false)
                : Session.NodeCache.IsTypeOfAsync(type, expected, cancellationToken);
        }

        /// <summary>
        /// Resolves complete local browse paths; only genuine no-match/not-found statuses mean absence.
        /// </summary>
        private async ValueTask<ArrayOf<NodeId>> ResolveAsync(
            NodeId root,
            ArrayOf<ArrayOf<QualifiedName>> paths,
            CancellationToken cancellationToken)
        {
            var requests = new List<BrowsePath>(paths.Count);
            foreach (ArrayOf<QualifiedName> path in paths)
            {
                var elements = new List<RelativePathElement>(path.Count);
                foreach (QualifiedName name in path)
                {
                    elements.Add(new RelativePathElement
                    {
                        TargetName = name,
                        ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true
                    });
                }
                requests.Add(new BrowsePath { StartingNode = root, RelativePath = new RelativePath
                {
                    Elements = elements.ToArrayOf()
                } });
            }
            TranslateBrowsePathsToNodeIdsResponse response = await Session.TranslateBrowsePathsToNodeIdsAsync(
                null, requests.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            RequireResultCount(response.Results.Count, paths.Count);
            var result = new List<NodeId>(paths.Count);
            foreach (BrowsePathResult path in response.Results)
            {
                if (path.StatusCode == StatusCodes.BadNoMatch || path.StatusCode == StatusCodes.BadNotFound)
                {
                    result.Add(NodeId.Null);
                    continue;
                }
                ThrowIfBad(path.StatusCode);
                if (path.Targets.Count != 1 || path.Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNoMatch,
                        "Browse path from '{0}' to '{1}' has {2} targets (remaining index {3}).",
                        root, paths[result.Count][^1], path.Targets.Count,
                        path.Targets.Count == 0 ? uint.MaxValue : path.Targets[0].RemainingPathIndex);
                }
                NodeId id = ExpandedNodeId.ToNodeId(path.Targets[0].TargetId, Session.NamespaceUris);
                if (id.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotSupported, "Remote-server paths are not followed.");
                }
                result.Add(id);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Browses bounded pages and releases an outstanding continuation on every exit path.
        /// </summary>
        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId parent,
            NodeId referenceType,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            ByteString continuation = default;
            Exception? failure = null;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(null, null, m_options.BrowsePageSize,
                [
                    new BrowseDescription
                    {
                        NodeId = parent,
                        ReferenceTypeId = referenceType,
                        BrowseDirection = BrowseDirection.Forward,
                        IncludeSubtypes = true,
                        NodeClassMask = nodeClassMask,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
                RequireResultCount(response.Results.Count, 1);
                BrowseResult page = response.Results[0];
                var references = new List<ReferenceDescription>();
                int pages = 0;
                while (true)
                {
                    continuation = page.ContinuationPoint;
                    ThrowIfBad(page.StatusCode);
                    if (++pages > m_options.MaximumBrowsePages ||
                        references.Count + page.References.Count > m_options.MaximumNodes)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTooManyOperations, "Browse exceeds the configured result or page bound.");
                    }
                    references.AddRange(page.References);
                    if (continuation.IsNull || continuation.IsEmpty)
                    {
                        return references.ToArrayOf();
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [continuation], cancellationToken).ConfigureAwait(false);
                    RequireResultCount(next.Results.Count, 1);
                    page = next.Results[0];
                }
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                if (!continuation.IsNull && !continuation.IsEmpty)
                {
                    using CancellationTokenSource cleanup = m_timeProvider.CreateCancellationTokenSource(
                        TimeSpan.FromSeconds(5));
                    try
                    {
                        await Session.BrowseNextAsync(null, true, [continuation], cleanup.Token).ConfigureAwait(false);
                    }
                    catch (Exception) when (failure != null)
                    {
                        // Preserve the original access, service, bounds or caller-cancellation failure.
                    }
                }
            }
        }

        /// <summary>
        /// Rejects a null UA identity without treating its value type as Nullable.
        /// </summary>
        private static void RequireNodeId(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
        }

        /// <summary>
        /// Preserves operation status codes.
        /// </summary>
        private static void ThrowIfBad(StatusCode statusCode)
        {
            if (StatusCode.IsBad(statusCode))
            {
                throw new ServiceResultException(statusCode);
            }
        }

        /// <summary>
        /// Refuses malformed service responses rather than manufacturing missing values.
        /// </summary>
        private static void RequireResultCount(int actual, int expected)
        {
            if (actual != expected)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError, "The service result count is invalid.");
            }
        }

        private readonly CranesHoistsClientOptions m_options;
        private readonly TimeProvider m_timeProvider;
    }
}
