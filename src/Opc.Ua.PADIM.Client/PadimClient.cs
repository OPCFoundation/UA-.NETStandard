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
using Opc.Ua.Client.Subscriptions;

namespace Opc.Ua.PADIM.Client
{
    /// <summary>
    /// Discovers standalone PA-DIM devices and retains the declared types of their values.
    /// </summary>
    /// <remarks>
    /// The caller owns the session, including managed-session reconnect and shutdown.
    /// This client never closes or disposes that session and never replays administration calls.
    /// </remarks>
    public sealed class PadimClient
    {
        /// <summary>
        /// Creates a client with an explicit bound on discovered nodes.
        /// </summary>
        public PadimClient(ISession session, ITelemetryContext telemetry, int maximumNodes = 4096)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            if (maximumNodes is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            m_maximumNodes = maximumNodes;
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException("The session must provide a message context.", nameof(session));
            RegisterDataTypes(messageContext.Factory, telemetry);
        }

        /// <summary>
        /// Gets the connected, caller-owned session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context used by generated clients.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets whether the connected server publishes PA-DIM.
        /// </summary>
        public bool IsSupported => Session.NamespaceUris.GetIndex(Namespaces.PADIM) >= 0;

        /// <summary>
        /// Creates the generated device proxy for nameplate, health and administration access.
        /// </summary>
        public PADIMTypeClient Device(NodeId nodeId)
        {
            RequireNode(nodeId);
            return new PADIMTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates the generated analog proxy, including optional zero-point adjustment.
        /// </summary>
        public AnalogSignalTypeClient AnalogSignal(NodeId nodeId)
        {
            RequireNode(nodeId);
            return new AnalogSignalTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates the generated positioner proxy without converting operation acceptance to completion.
        /// </summary>
        public ControlSignalTypeClient ControlSignal(NodeId nodeId)
        {
            RequireNode(nodeId);
            return new ControlSignalTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates typed optional configuration views for an analog or discrete process variable.
        /// </summary>
        public PadimProcessValueClient ProcessValue(NodeId nodeId)
        {
            RequireNode(nodeId);
            return new PadimProcessValueClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Observes a measurement or health value using the managed session's shared subscription.
        /// </summary>
        public IAsyncEnumerable<DataValueChange> ObserveValueAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            RequireNode(nodeId);
            if (Session is not ManagedSession managed)
            {
                throw new NotSupportedException("Value observation requires a managed session.");
            }
            return managed.DefaultStreaming.SubscribeDataChangesAsync(nodeId, ct: cancellationToken);
        }

        /// <summary>
        /// Requests a device-parameter reset through the generated method proxy.
        /// </summary>
        public ValueTask FactoryResetAsync(
            NodeId deviceId,
            ResetModeEnum mode,
            CancellationToken cancellationToken = default)
        {
            return Device(deviceId).FactoryResetAsync(mode, cancellationToken);
        }

        /// <summary>
        /// Requests zero-point adjustment without adding Machinery-specific event requirements.
        /// </summary>
        public ValueTask ZeroPointAdjustmentAsync(
            NodeId signalId,
            CancellationToken cancellationToken = default)
        {
            return AnalogSignal(signalId).ZeroPointAdjustmentAsync(cancellationToken);
        }

        /// <summary>
        /// Requests device-owned positioner start or abort through its generated proxy.
        /// </summary>
        public ValueTask AutoAdjustPositionerAsync(
            NodeId signalId,
            ExecutionModeEnum mode,
            CancellationToken cancellationToken = default)
        {
            return ControlSignal(signalId).AutoAdjustPositionerAsync(mode, cancellationToken);
        }

        /// <summary>
        /// Discovers devices and vendor subtypes through the DI DeviceSet, including modular subdevices.
        /// </summary>
        public ValueTask<ArrayOf<PadimEntry>> DiscoverDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId root = NodeId.Create(Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, Session.NamespaceUris);
            return DiscoverAsync(root, ObjectTypeIds.PADIMType, cancellationToken);
        }

        /// <summary>
        /// Discovers all signal families below a device's SignalSet, preserving vendor subtypes.
        /// </summary>
        public ValueTask<ArrayOf<PadimEntry>> DiscoverSignalsAsync(
            NodeId deviceId,
            CancellationToken cancellationToken = default)
        {
            return DiscoverAsync(deviceId, ObjectTypeIds.SignalType, cancellationToken);
        }

        /// <summary>
        /// Reads all immediate variables of an identification, condition, calibration or signal group.
        /// </summary>
        public async ValueTask<ArrayOf<PadimValue>> ReadValuesAsync(
            NodeId parentId,
            CancellationToken cancellationToken = default)
        {
            RequireNode(parentId);
            ArrayOf<ReferenceDescription> children = await BrowseAsync(
                parentId, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences, NodeClass.Variable, cancellationToken)
                .ConfigureAwait(false);
            var requests = new List<ReadValueId>(children.Count);
            for (int ii = 0; ii < children.Count; ii++)
            {
                requests.Add(new ReadValueId
                {
                    NodeId = LocalNode(children[ii].NodeId),
                    AttributeId = Attributes.Value
                });
            }
            if (requests.Count == 0)
            {
                return ArrayOf<PadimValue>.Empty;
            }
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both, requests.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != requests.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "Read returned an invalid result count.");
            }
            var values = new PadimValue[requests.Count];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new PadimValue(requests[ii].NodeId, children[ii].BrowseName, response.Results[ii]);
            }
            return values;
        }

        /// <summary>
        /// Reads the actual instance dictionary references, retaining namespace URI identities.
        /// </summary>
        public async ValueTask<ArrayOf<ExpandedNodeId>> ReadDictionaryEntriesAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await BrowseAsync(
                nodeId, Opc.Ua.Types.ReferenceTypeIds.HasDictionaryEntry, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false);
            var entries = new ExpandedNodeId[references.Count];
            for (int ii = 0; ii < entries.Length; ii++)
            {
                entries[ii] = NodeId.ToExpandedNodeId(LocalNode(references[ii].NodeId), Session.NamespaceUris);
            }
            return entries;
        }

        /// <summary>
        /// Enumerates children with namespace-qualified names for specialised calibration and condition proxies.
        /// </summary>
        public ValueTask<ArrayOf<ReferenceDescription>> BrowseChildrenAsync(
            NodeId parentId,
            CancellationToken cancellationToken = default)
        {
            return BrowseAsync(
                parentId, Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Unspecified, cancellationToken);
        }

        /// <summary>
        /// Traverses a bounded hierarchy and checks the actual type hierarchy, not namespace indexes.
        /// </summary>
        private async ValueTask<ArrayOf<PadimEntry>> DiscoverAsync(
            NodeId root,
            ExpandedNodeId requiredType,
            CancellationToken cancellationToken)
        {
            RequireNode(root);
            NodeId typeId = ExpandedNodeId.ToNodeId(requiredType, Session.NamespaceUris);
            RequireNode(typeId);
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId> { root };
            var entries = new List<PadimEntry>();
            pending.Enqueue(root);
            while (pending.Count != 0)
            {
                ArrayOf<ReferenceDescription> children = await BrowseAsync(
                    pending.Dequeue(),
                    Opc.Ua.Types.ReferenceTypeIds.HierarchicalReferences,
                    NodeClass.Object,
                    cancellationToken).ConfigureAwait(false);
                for (int ii = 0; ii < children.Count; ii++)
                {
                    ReferenceDescription child = children[ii];
                    NodeId id = LocalNode(child.NodeId);
                    if (!visited.Add(id))
                    {
                        continue;
                    }
                    if (visited.Count > m_maximumNodes)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations, "PA-DIM discovery exceeded its node bound.");
                    }
                    NodeId actualType = LocalNode(child.TypeDefinition);
                    if (await Session.NodeCache.IsTypeOfAsync(actualType, typeId, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        entries.Add(new PadimEntry(id, child.BrowseName, child.DisplayName, actualType));
                    }
                    pending.Enqueue(id);
                }
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Uses the shared continuation-point-aware browse path and rejects malformed responses.
        /// </summary>
        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceType,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            RequireNode(nodeId);
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(
                    null, null, (ArrayOf<NodeId>)[nodeId], 0,
                    BrowseDirection.Forward, referenceType, true, (uint)nodeClass, cancellationToken)
                    .ConfigureAwait(false);
            if (results.Count != 1 || errors.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "Browse returned an invalid result count.");
            }
            if (ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (results[0].Count > m_maximumNodes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations, "Browse exceeded the PA-DIM node bound.");
            }
            return results[0];
        }

        /// <summary>
        /// Rejects remote ExpandedNodeIds rather than accidentally addressing a local numeric identifier.
        /// </summary>
        private NodeId LocalNode(ExpandedNodeId nodeId)
        {
            if (nodeId.ServerIndex != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Cross-server PA-DIM references require another session.");
            }
            NodeId local = ExpandedNodeId.ToNodeId(nodeId, Session.NamespaceUris);
            RequireNode(local);
            return local;
        }

        /// <summary>
        /// Rejects absent node identifiers before issuing a wire request.
        /// </summary>
        private static void RequireNode(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("A local node identifier is required.", nameof(nodeId));
            }
        }

        /// <summary>
        /// Registers missing PA-DIM mappings or reuses a complete caller-provided factory.
        /// </summary>
        private static void RegisterDataTypes(IEncodeableFactory factory, ITelemetryContext telemetry)
        {
            IEncodeableFactoryBuilder builder = factory.Builder;
            bool supportsAdditiveCommit = builder is IAdditiveEncodeableFactoryBuilder;
            if (supportsAdditiveCommit)
            {
                builder.AddOpcUaPADIM().CommitIfAbsent();
            }
            IEncodeableFactory required = ServiceMessageContext.CreateEmpty(telemetry).Factory;
            required.Builder.AddOpcUaPADIM().Commit();
            foreach (ExpandedNodeId id in required.KnownTypeIds)
            {
                if (required.TryGetType(id, out IType? expected))
                {
                    bool incompatibleId =
                        (factory.TryGetEncodeableType(id, out IEncodeableType? actualStructure) &&
                            (expected is not IEncodeableType || !IsCompatibleType(expected, actualStructure))) ||
                        (factory.TryGetEnumeratedType(id, out IEnumeratedType? actualEnumeration) &&
                            (expected is not IEnumeratedType || !IsCompatibleType(expected, actualEnumeration)));
                    bool incompatibleXml = factory.TryGetType(expected.XmlName, out IType? actualXml) &&
                        !IsCompatibleType(expected, actualXml);
                    if (incompatibleId || incompatibleXml)
                    {
                        throw new InvalidOperationException(
                            $"The session factory has an incompatible PA-DIM mapping '{id}'.");
                    }
                }
            }
            foreach (ExpandedNodeId id in required.KnownTypeIds)
            {
                if (required.TryGetEncodeableType(id, out IEncodeableType? structure) &&
                    factory.TryGetEncodeableType(id, out IEncodeableType? actualStructure) &&
                    actualStructure.Type == structure.Type &&
                    factory.TryGetType(structure.XmlName, out IType? xmlStructure) &&
                    xmlStructure is IEncodeableType && xmlStructure.Type == structure.Type)
                {
                    continue;
                }
                if (required.TryGetEnumeratedType(id, out IEnumeratedType? enumeration) &&
                    factory.TryGetEnumeratedType(id, out IEnumeratedType? actualEnumeration) &&
                    actualEnumeration.Type == enumeration.Type &&
                    factory.TryGetType(enumeration.XmlName, out IType? xmlEnumeration) &&
                    xmlEnumeration is IEnumeratedType && xmlEnumeration.Type == enumeration.Type)
                {
                    continue;
                }
                if (!supportsAdditiveCommit)
                {
                    throw new NotSupportedException(
                        $"The session factory is missing PA-DIM mapping '{id}' and does not support additive commits.");
                }
                throw new InvalidOperationException(
                    $"The session factory has a missing or incompatible PA-DIM mapping '{id}' after registration.");
            }
        }

        /// <summary>
        /// Checks both the declared system type and the required activator capability.
        /// </summary>
        private static bool IsCompatibleType(IType expected, IType actual)
        {
            return actual.Type == expected.Type && (expected switch
            {
                IEncodeableType => actual is IEncodeableType,
                IEnumeratedType => actual is IEnumeratedType,
                _ => false
            });
        }

        /// <summary>The per-discovery and per-browse node bound.</summary>
        private readonly int m_maximumNodes;
    }
}
