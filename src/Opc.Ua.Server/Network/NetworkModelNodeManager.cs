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
 * copies of the Software, and to permit persons to whom
 * the Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Projects provider snapshots into the standard Part 22 Resources model.
    /// </summary>
    public sealed partial class NetworkModelNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Namespace URI used for application-owned network instances.
        /// Standard Resources entry points remain namespace-zero nodes.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/NetworkModel";

        /// <summary>
        /// Creates a manager that consumes complete provider snapshots.
        /// </summary>
        /// <param name="server">The owning server.</param>
        /// <param name="configuration">The effective application configuration.</param>
        /// <param name="provider">The application-owned network model provider.</param>
        /// <param name="authorizeMappingEdits">The application mapping-edit policy.</param>
        public NetworkModelNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            INetworkModelProvider provider,
            Func<ISystemContext, bool> authorizeMappingEdits)
            : base(server, configuration, NamespaceUri)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_authorizeMappingEdits = authorizeMappingEdits
                ?? throw new ArgumentNullException(nameof(authorizeMappingEdits));
            m_networkLogger = server.Telemetry.CreateLogger<NetworkModelNodeManager>();
            m_applyGate = new SemaphoreSlim(1, 1);
            SystemContext.SystemHandle = this;
        }

        /// <summary>
        /// Asynchronously creates and registers the dynamic model below the existing
        /// namespace-zero Part 22 entry points, then starts consuming provider updates.
        /// </summary>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            try
            {
                NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                await ApplySnapshotAsync(snapshot, externalReferences, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                NetworkModelNodeManagerLog.ProviderReadFailed(m_networkLogger, exception);
                await MarkUnavailableAsync(ProviderFailureStatus(exception), cancellationToken).ConfigureAwait(false);
            }

            m_watchCancellation = new CancellationTokenSource();
            m_watchTask = WatchProviderAsync(m_watchCancellation.Token);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            CancellationTokenSource? cancellation = m_watchCancellation;
            if (cancellation is not null)
            {
                cancellation.Cancel();
            }

            if (m_watchTask is { } watchTask)
            {
                try
                {
                    await watchTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cancellation?.Dispose();
            m_applyGate.Dispose();
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }

        /// <summary>
        /// Consumes the provider's complete latest snapshots and applies each newer revision.
        /// </summary>
        private async Task WatchProviderAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (NetworkModelSnapshot snapshot in
                        m_provider.WatchAsync(cancellationToken).WithCancellation(cancellationToken)
                            .ConfigureAwait(false))
                    {
                        await ApplySnapshotAsync(snapshot, externalReferences: null, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        NetworkModelNodeManagerLog.ProviderWatchCompleted(m_networkLogger);
                        await MarkUnavailableAsync(StatusCodes.BadCommunicationError, cancellationToken)
                            .ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    NetworkModelNodeManagerLog.ProviderWatchFailed(m_networkLogger, exception);
                    await MarkUnavailableAsync(ProviderFailureStatus(exception), cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Retains a provider-supplied bad status or reports an unexpected acquisition failure.
        /// </summary>
        private static StatusCode ProviderFailureStatus(Exception exception)
        {
            return exception is ServiceResultException serviceResultException &&
                StatusCode.IsBad(serviceResultException.StatusCode)
                    ? serviceResultException.StatusCode
                    : StatusCodes.BadCommunicationError;
        }

        /// <summary>
        /// Marks every published provider value unavailable while retaining the last values,
        /// topology and acquisition timestamps. Only a newer valid snapshot clears the status.
        /// </summary>
        private async ValueTask MarkUnavailableAsync(StatusCode failure, CancellationToken cancellationToken)
        {
            await m_applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (EntityDefinition entity in m_entities.Values)
                {
                    foreach (NodeState node in CollectNodes(entity.Root))
                    {
                        if (node is BaseVariableState variable && variable.Parent is not MethodState)
                        {
                            variable.StatusCode = failure;
                        }
                    }
                    await entity.Root.ClearChangeMasksAsync(SystemContext, true, cancellationToken)
                        .ConfigureAwait(false);
                }
                foreach (NodeId id in s_lldpLocalVariableIds)
                {
                    BaseVariableState? variable = Server.DiagnosticsNodeManager
                        .FindPredefinedNode<BaseVariableState>(id);
                    if (variable is not null)
                    {
                        variable.StatusCode = failure;
                        await variable.ClearChangeMasksAsync(SystemContext, false, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                m_applyGate.Release();
            }
        }

        /// <summary>
        /// Serializes topology reconciliation and prevents an older acquisition from replacing
        /// a newer provider revision.
        /// </summary>
        private async ValueTask ApplySnapshotAsync(
            NetworkModelSnapshot snapshot,
            IDictionary<NodeId, IList<IReference>>? externalReferences,
            CancellationToken cancellationToken)
        {
            if (snapshot is null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            await m_applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (snapshot.Revision <= m_revision)
                {
                    return;
                }

                if (m_revision != long.MinValue && snapshot.Timestamp < m_timestamp)
                {
                    NetworkModelNodeManagerLog.StaleSnapshotRejected(
                        m_networkLogger, snapshot.Revision, snapshot.Timestamp, m_revision, m_timestamp);
                    return;
                }

                Dictionary<string, EntityDefinition> desired = BuildEntities(snapshot);
                // Parent replacement also invalidates separately registered children and their parent references.
                var replacedNodes = new HashSet<NodeId>(NodeIdComparer.Default);
                foreach (string key in m_entities.Keys.Except(desired.Keys).Reverse().ToArray())
                {
                    replacedNodes.UnionWith(CollectNodes(m_entities[key].Root).Select(node => node.NodeId));
                    await RemoveEntityAsync(key, cancellationToken).ConfigureAwait(false);
                }

                foreach ((string key, EntityDefinition definition) in desired)
                {
                    if (m_entities.TryGetValue(key, out EntityDefinition? existing) &&
                        existing.Root.NodeId == definition.Root.NodeId &&
                        !replacedNodes.Contains(definition.ParentId))
                    {
                        await SynchronizeChildrenAsync(definition.Root, existing.Root, cancellationToken)
                            .ConfigureAwait(false);
                        CopyValues(definition.Root, existing.Root);
                        m_entities[key] = existing with { ParentId = definition.ParentId };
                    }
                    else
                    {
                        if (m_entities.TryGetValue(key, out EntityDefinition? replaced))
                        {
                            replacedNodes.UnionWith(CollectNodes(replaced.Root).Select(node => node.NodeId));
                            await RemoveEntityAsync(key, cancellationToken).ConfigureAwait(false);
                        }

                        await AddEntityAsync(key, definition, externalReferences, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                foreach ((string key, EntityDefinition definition) in desired)
                {
                    Dictionary<NodeId, NodeState> retained = CollectNodes(m_entities[key].Root)
                        .ToDictionary(node => node.NodeId, NodeIdComparer.Default);
                    foreach (NodeState source in CollectNodes(definition.Root))
                    {
                        await SynchronizeCrossReferencesAsync(
                            source, retained[source.NodeId], externalReferences, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    await m_entities[key].Root.ClearChangeMasksAsync(SystemContext, true, cancellationToken)
                        .ConfigureAwait(false);
                }
                await ApplyLldpLocalValuesAsync(snapshot, cancellationToken).ConfigureAwait(false);
                m_revision = snapshot.Revision;
                m_timestamp = snapshot.Timestamp;
            }
            finally
            {
                m_applyGate.Release();
            }
        }

        /// <summary>
        /// Converts one coherent snapshot into interface, mapping, stream and LLDP entities.
        /// </summary>
        private Dictionary<string, EntityDefinition> BuildEntities(NetworkModelSnapshot snapshot)
        {
            var entities = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);
            foreach (NetworkMappingTableSnapshot table in snapshot.MappingTables)
            {
                BaseObjectState root = BuildMappingTable(table, snapshot);
                entities.Add(EntityKey("mapping", table.Id),
                    new EntityDefinition(ObjectIds.MappingTables, root));
            }

            foreach (NetworkInterfaceSnapshot networkInterface in snapshot.Interfaces)
            {
                foreach (NetworkSrClassSnapshot srClass in networkInterface.SrClasses)
                {
                    if (srClass.Id > 7 || srClass.Priority > 7)
                    {
                        throw new ServiceResultException(StatusCodes.BadOutOfRange,
                            "SR class identifiers and priorities must be between zero and seven.");
                    }
                }
                BaseObjectState root = BuildInterface(networkInterface, snapshot);
                entities.Add(EntityKey("interface", networkInterface.Id),
                    new EntityDefinition(ObjectIds.NetworkInterfaces, root));
            }

            foreach (NetworkStreamSnapshot stream in snapshot.Streams)
            {
                NodeId parentId = stream.IsTalker ? ObjectIds.TalkerStreams : ObjectIds.ListenerStreams;
                BaseObjectState root = BuildStream(stream, snapshot);
                entities.Add(EntityKey(stream.IsTalker ? "talker" : "listener", stream.Id),
                    new EntityDefinition(parentId, root));
            }

            if (snapshot.Lldp is { } lldp)
            {
                AddLldpEntities(entities, lldp, snapshot);
            }

            AddInterfaceReferences(entities, snapshot);
            return entities;
        }

        /// <summary>
        /// Adds one provider-owned entity and its standard external parent reference.
        /// </summary>
        private async ValueTask AddEntityAsync(
            string key,
            EntityDefinition entity,
            IDictionary<NodeId, IList<IReference>>? externalReferences,
            CancellationToken cancellationToken)
        {
            entity.Root.AddReference(entity.Root.ReferenceTypeId, true, entity.ParentId);
            await AddPredefinedNodeAsync(entity.Root, cancellationToken).ConfigureAwait(false);
            if (externalReferences is not null)
            {
                AddExternalReference(externalReferences, entity.ParentId,
                    new NodeStateReference(entity.Root.ReferenceTypeId, false, entity.Root.NodeId));
            }
            else
            {
                await Server.NodeManager.AddReferencesAsync(
                    entity.ParentId,
                    [new NodeStateReference(entity.Root.ReferenceTypeId, false, entity.Root.NodeId)],
                    cancellationToken).ConfigureAwait(false);
            }

            m_entities.Add(key, entity);
        }

        /// <summary>
        /// Removes a dynamic entity and its forward reference from the standard entry point.
        /// </summary>
        private async ValueTask RemoveEntityAsync(string key, CancellationToken cancellationToken)
        {
            if (!m_entities.TryGetValue(key, out EntityDefinition? entity))
            {
                return;
            }
            m_entities.Remove(key);

            await DeleteNodeAsync(SystemContext, entity.Root.NodeId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates an interface instance and its standard IETF and optional IEEE properties.
        /// </summary>
        private BaseObjectState BuildInterface(NetworkInterfaceSnapshot networkInterface, NetworkModelSnapshot snapshot)
        {
            var node = CreateObject(
                "interface",
                networkInterface.Id,
                networkInterface.IfName,
                ObjectTypeIds.IetfBaseNetworkInterfaceType,
                ReferenceTypeIds.Organizes);
            node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIetfBaseNetworkInterfaceType);
            AddVariable(node, "AdminStatus", DataTypeIds.InterfaceAdminStatus,
                Variant.From(networkInterface.AdminStatus), snapshot);
            AddVariable(node, "OperStatus", DataTypeIds.InterfaceOperStatus,
                Variant.From(networkInterface.OperStatus), snapshot);
            if (networkInterface.PhysicalAddress is { } physicalAddress)
            {
                AddVariable(node, "PhysAddress", DataTypeIds.String, physicalAddress, snapshot);
            }
            AddAnalog(node, "Speed", networkInterface.Speed, 4337968, "bit/s", "bit per second", snapshot);

            if (networkInterface.Ethernet is { } ethernet)
            {
                BaseObjectState ethernetPort = AddObject(
                    node, "EthernetPort", NodeIdFor("interface", networkInterface.Id, "ethernet"),
                    ObjectTypeIds.BaseObjectType, ReferenceTypeIds.HasComponent);
                ethernetPort.AddReference(
                    ReferenceTypeIds.HasInterface,
                    false,
                    ObjectTypeIds.IIeeeBaseEthernetPortType);
                AddAnalog(ethernetPort, "Speed", ethernet.Speed, 4534832, "Mbit/s", "megabit per second", snapshot);
                AddVariable(ethernetPort, "Duplex", DataTypeIds.Duplex, Variant.From(ethernet.Duplex), snapshot);
                AddVariable(ethernetPort, "MaxFrameLength", DataTypeIds.UInt16, ethernet.MaxFrameLength, snapshot);
                if (ethernet.NegotiationStatus is { } negotiationStatus)
                {
                    ethernetPort.AddReference(
                        ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIeeeAutoNegotiationStatusType);
                    AddVariable(ethernetPort, "NegotiationStatus", DataTypeIds.NegotiationStatus,
                        Variant.From(negotiationStatus), snapshot);
                }
            }

            if (networkInterface.VlanTagCapable is { } vlanTagCapable)
            {
                node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IBaseEthernetCapabilitiesType);
                AddVariable(node, "VlanTagCapable", DataTypeIds.Boolean, vlanTagCapable, snapshot);
            }
            if (networkInterface.VlanId is { } vlanId)
            {
                node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IVlanIdType);
                AddVariable(node, "VlanId", DataTypeIds.UInt16, vlanId, snapshot);
            }
            foreach (NetworkSrClassSnapshot srClass in networkInterface.SrClasses)
            {
                BaseObjectState srClassNode = AddObject(
                    node, $"SrClass{srClass.Id}",
                    NodeIdFor("interface", networkInterface.Id, $"sr-class-{srClass.Id}"),
                    ObjectTypeIds.BaseObjectType,
                    ReferenceTypeIds.HasComponent);
                srClassNode.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.ISrClassType);
                AddVariable(srClassNode, "Id", DataTypeIds.Byte, srClass.Id, snapshot);
                AddVariable(srClassNode, "Priority", DataTypeIds.Byte, srClass.Priority, snapshot);
                AddVariable(srClassNode, "Vid", DataTypeIds.UInt16, srClass.Vid, snapshot);
            }

            return node;
        }

        /// <summary>
        /// Creates a mapping-table instance with its read-only entries and capability-gated methods.
        /// </summary>
        private BaseObjectState BuildMappingTable(NetworkMappingTableSnapshot table, NetworkModelSnapshot snapshot)
        {
            var node = CreateObject(
                "mapping",
                table.Id,
                table.Id,
                ObjectTypeIds.PriorityMappingTableType,
                ReferenceTypeIds.Organizes);
            PriorityMappingEntryType[] entries = table.Entries.Span.ToArray();
            AddVariable(
                node,
                "PriorityMapppingEntries",
                DataTypeIds.PriorityMappingEntryType,
                Variant.FromStructure(ArrayOf.Wrapped(entries)),
                snapshot,
                property: true,
                valueRank: ValueRanks.OneDimension);

            if (table.CanEdit)
            {
                AddMappingMethod(node, table.Id, "AddPriorityMappingEntry", isDelete: false);
                AddMappingMethod(node, table.Id, "DeletePriorityMappingEntry", isDelete: true);
            }

            return node;
        }

        /// <summary>
        /// Creates a stream instance with its TSN identity, traffic, status and per-interface data.
        /// </summary>
        private BaseObjectState BuildStream(NetworkStreamSnapshot stream, NetworkModelSnapshot snapshot)
        {
            if (stream.VlanTag is { } tag && (tag.VlanId > 4095 || tag.PriorityCodePoint > 7))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange,
                    "Stream VLAN identifiers must be between zero and 4095 " +
                    "and priority code points between zero and seven.");
            }
            if (!NetworkModelSnapshotValidator.IsValidStreamId(stream.StreamId))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange,
                    "Stream identifiers must contain exactly eight bytes.");
            }
            if (!NetworkModelSnapshotValidator.IsValidSrClassId(stream.SrClassId))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange,
                    "Stream SR class identifiers must be between zero and seven.");
            }
            var node = CreateObject(
                stream.IsTalker ? "talker" : "listener",
                stream.Id,
                stream.Name,
                ObjectTypeIds.BaseObjectType,
                ReferenceTypeIds.Organizes);
            node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIeeeBaseTsnStreamType);
            AddVariable(node, "StreamId", DataTypeIds.Byte, ArrayOf.Wrapped(stream.StreamId.ToArray()),
                snapshot, valueRank: ValueRanks.OneDimension, dimensions: [8]);
            AddVariable(node, "StreamName", DataTypeIds.String, stream.Name, snapshot);
            AddVariable(node, "State", DataTypeIds.TsnStreamState, Variant.From(stream.State), snapshot);
            if (stream.AccumulatedLatency is { } latency)
            {
                AddVariable(node, "AccumulatedLatency", DataTypeIds.UInt32, latency, snapshot);
            }
            if (stream.SrClassId is { } srClassId)
            {
                AddVariable(node, "SrClassId", DataTypeIds.Byte, srClassId, snapshot);
            }
            if (stream.DestinationAddress.Length == 6)
            {
                node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIeeeTsnMacAddressType);
                AddVariable(node, "DestinationAddress", DataTypeIds.Byte,
                    ArrayOf.Wrapped(stream.DestinationAddress.ToArray()), snapshot,
                    valueRank: ValueRanks.OneDimension, dimensions: [6]);
                if (stream.SourceAddress.Length == 6)
                {
                    AddVariable(node, "SourceAddress", DataTypeIds.Byte,
                        ArrayOf.Wrapped(stream.SourceAddress.ToArray()), snapshot,
                        valueRank: ValueRanks.OneDimension, dimensions: [6]);
                }
            }

            if (stream.Traffic is { } traffic)
            {
                node.AddReference(
                    ReferenceTypeIds.HasInterface,
                    false,
                    ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType);
                AddVariable(node, "MaxIntervalFrames", DataTypeIds.UInt16, traffic.MaxIntervalFrames, snapshot);
                AddVariable(node, "MaxFrameSize", DataTypeIds.UInt32, traffic.MaxFrameSize, snapshot);
                AddVariable(node, "Interval", DataTypeIds.UnsignedRationalNumber,
                    Variant.FromStructure(new UnsignedRationalNumber
                    {
                        Numerator = traffic.Numerator,
                        Denominator = traffic.Denominator
                    }),
                    snapshot);
            }

            if (stream.Status is { } status)
            {
                node.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIeeeBaseTsnStatusStreamType);
                AddVariable(node, "FailureCode", DataTypeIds.TsnFailureCode,
                    Variant.From(status.FailureCode), snapshot);
                if (status.TalkerStatus is { } talkerStatus)
                {
                    AddVariable(node, "TalkerStatus", DataTypeIds.TsnTalkerStatus,
                        Variant.From(talkerStatus), snapshot);
                }
                if (status.ListenerStatus is { } listenerStatus)
                {
                    AddVariable(node, "ListenerStatus", DataTypeIds.TsnListenerStatus,
                        Variant.From(listenerStatus), snapshot);
                }

                byte[] identifiers = status.FailureSystemIdentifiers.ToList()
                    .SelectMany(identifier => identifier.ToArray())
                    .ToArray();
                MatrixOf<byte> identifierMatrix = identifiers.ToMatrixOf<byte>(
                    [status.FailureSystemIdentifiers.Count, 8]);
                AddVariable(node, "FailureSystemIdentifier", DataTypeIds.Byte, identifierMatrix, snapshot,
                    valueRank: ValueRanks.TwoDimensions,
                    dimensions: [(uint)status.FailureSystemIdentifiers.Count, 8]);
            }

            if (stream.VlanTag is { } vlanTag)
            {
                BaseObjectState vlan = AddObject(
                    node, "VlanTag", NodeIdFor(stream.IsTalker ? "talker" : "listener", stream.Id, "vlan-tag"),
                    ObjectTypeIds.BaseObjectType, ReferenceTypeIds.HasComponent);
                vlan.AddReference(ReferenceTypeIds.HasInterface, false, ObjectTypeIds.IIeeeTsnVlanTagType);
                AddVariable(vlan, "VlanId", DataTypeIds.UInt16, vlanTag.VlanId, snapshot);
                AddVariable(vlan, "PriorityCodePoint", DataTypeIds.Byte, vlanTag.PriorityCodePoint, snapshot);
            }

            foreach (NetworkStreamInterfaceSnapshot streamInterface in stream.Interfaces)
            {
                BaseObjectState configuration = AddObject(
                    node,
                    QualifiedComponentName(streamInterface.InterfaceName ?? streamInterface.Id),
                    NodeIdFor(stream.IsTalker ? "talker" : "listener", stream.Id,
                        $"interface-{streamInterface.Id}"),
                    ObjectTypeIds.BaseObjectType,
                    ReferenceTypeIds.HasComponent);
                configuration.AddReference(
                    ReferenceTypeIds.HasInterface,
                    false,
                    stream.IsTalker
                        ? ObjectTypeIds.IIeeeTsnInterfaceConfigurationTalkerType
                        : ObjectTypeIds.IIeeeTsnInterfaceConfigurationListenerType);
                AddVariable(configuration, "MacAddress", DataTypeIds.String, streamInterface.MacAddress, snapshot);
                if (streamInterface.InterfaceName is { } interfaceName)
                {
                    AddVariable(configuration, "InterfaceName", DataTypeIds.String, interfaceName, snapshot);
                }
                if (streamInterface.Offset is { } offset)
                {
                    AddVariable(configuration, stream.IsTalker ? "TimeAwareOffset" : "ReceiveOffset",
                        DataTypeIds.UInt32, offset, snapshot);
                }
            }

            return node;
        }

        /// <summary>
        /// Materializes LLDP local system information, ports and current remote neighbors.
        /// </summary>
        private void AddLldpEntities(
            IDictionary<string, EntityDefinition> entities,
            NetworkLldpSnapshot lldp,
            NetworkModelSnapshot snapshot)
        {
            foreach (NetworkLldpPortSnapshot port in lldp.Ports)
            {
                BaseObjectState portNode = CreateObject(
                    "lldp-port",
                    port.Id,
                    port.Id,
                    ObjectTypeIds.LldpPortInformationType,
                    ReferenceTypeIds.Organizes);
                AddVariable(portNode, "IetfBaseNetworkInterfaceName", DataTypeIds.String, port.IfName, snapshot,
                    property: true);
                AddVariable(portNode, "DestMacAddress", DataTypeIds.Byte,
                    ArrayOf.Wrapped(port.DestinationMacAddress.ToArray()), snapshot,
                    property: true, valueRank: ValueRanks.OneDimension, dimensions: [6]);
                AddVariable(portNode, "PortIdSubtype", DataTypeIds.PortIdSubtype,
                    Variant.From(port.PortIdSubtype), snapshot,
                    property: true);
                AddVariable(portNode, "PortId", DataTypeIds.String, port.PortId, snapshot, property: true);
                if (port.Description is { } description)
                {
                    AddVariable(portNode, "PortDescription", DataTypeIds.String, description, snapshot,
                        property: true);
                }

                BaseObjectState? remoteSystems = null;
                if (port.Neighbors.Count > 0)
                {
                    remoteSystems = AddObject(
                        portNode,
                        "RemoteSystemsData",
                        NodeIdFor("lldp-port", port.Id, "remote-systems"),
                        ObjectTypeIds.FolderType,
                        ReferenceTypeIds.HasComponent,
                        standardName: true);
                }

                entities.Add(EntityKey("lldp-port", port.Id),
                    new EntityDefinition(ObjectIds.LLDP_Ports, portNode));

                foreach (NetworkLldpNeighborSnapshot neighbor in port.Neighbors)
                {
                    string identity = $"{Uri.EscapeDataString(port.Id)}/{Uri.EscapeDataString(neighbor.Id)}";
                    BaseObjectState remote = CreateObject(
                        "lldp-neighbor",
                        identity,
                        neighbor.Id,
                        ObjectTypeIds.LldpRemoteSystemType,
                        ReferenceTypeIds.Organizes);
                    AddVariable(remote, "TimeMark", DataTypeIds.UInt32, neighbor.TimeMark, snapshot);
                    AddVariable(remote, "RemoteIndex", DataTypeIds.UInt32, neighbor.RemoteIndex, snapshot);
                    AddVariable(remote, "ChassisIdSubtype", DataTypeIds.ChassisIdSubtype,
                        Variant.From(neighbor.ChassisIdSubtype), snapshot);
                    AddVariable(remote, "ChassisId", DataTypeIds.String, neighbor.ChassisId, snapshot);
                    AddVariable(remote, "PortIdSubtype", DataTypeIds.PortIdSubtype,
                        Variant.From(neighbor.PortIdSubtype), snapshot);
                    AddVariable(remote, "PortId", DataTypeIds.String, neighbor.PortId, snapshot);
                    if (neighbor.SystemName is { } systemName)
                    {
                        AddVariable(remote, "SystemName", DataTypeIds.String, systemName, snapshot);
                    }
                    if (neighbor.SystemDescription is { } systemDescription)
                    {
                        AddVariable(remote, "SystemDescription", DataTypeIds.String, systemDescription, snapshot);
                    }

                    entities.Add(EntityKey("lldp-neighbor", identity),
                        new EntityDefinition(remoteSystems!.NodeId, remote));
                }
            }
        }

        /// <summary>
        /// Updates the existing diagnostics-owned LLDP local system without duplicating its standard children.
        /// </summary>
        private async ValueTask ApplyLldpLocalValuesAsync(
            NetworkModelSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            NetworkLldpSnapshot? local = snapshot.Lldp;
            Variant[] values = local is null
                ? [Variant.Null, Variant.Null, Variant.Null, Variant.Null]
                : [Variant.From(local.ChassisIdSubtype), local.ChassisId, local.SystemName, local.SystemDescription];
            for (int index = 0; index < s_lldpLocalVariableIds.Length; index++)
            {
                BaseVariableState variable = Server.DiagnosticsNodeManager
                    .FindPredefinedNode<BaseVariableState>(s_lldpLocalVariableIds[index]) ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The standard LLDP local-system node is missing.");
                variable.Value = values[index];
                variable.Timestamp = snapshot.Timestamp;
                variable.StatusCode = local is null ? StatusCodes.BadNotSupported : snapshot.StatusCode;
                await variable.ClearChangeMasksAsync(SystemContext, false, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Preserves explicit lower-layer and priority-table references without flattening inheritance.
        /// </summary>
        private void AddInterfaceReferences(
            IReadOnlyDictionary<string, EntityDefinition> entities,
            NetworkModelSnapshot snapshot)
        {
            foreach (NetworkInterfaceSnapshot networkInterface in snapshot.Interfaces)
            {
                NodeState interfaceNode = entities[EntityKey("interface", networkInterface.Id)].Root;
                if (networkInterface.MappingTableId is { } tableId)
                {
                    interfaceNode.AddReference(
                        ReferenceTypeIds.UsesPriorityMappingTable, false, NodeIdFor("mapping", tableId));
                }

                foreach (string lowerLayerId in networkInterface.LowerLayerIds)
                {
                    interfaceNode.AddReference(
                        ReferenceTypeIds.HasLowerLayerInterface, false, NodeIdFor("interface", lowerLayerId));
                }
            }
        }

        /// <summary>
        /// Installs the provider-backed Part 22 mapping method callbacks.
        /// </summary>
        private void AddMappingMethod(BaseObjectState tableNode, string tableId, string name, bool isDelete)
        {
            MethodState method = tableNode.CreateChild(
                SystemContext, new QualifiedName(name, 0), assignInstanceNodeIds: false) is MethodState generated
                    ? generated
                    : throw new ServiceResultException(StatusCodes.BadConfigurationError,
                        "The generated priority-mapping method is missing.");
            method.NodeId = NodeIdFor(tableNode.NodeId.ToString(), name);
            method.BrowseName = new QualifiedName(name, 0);
            method.DisplayName = new LocalizedText(name);
            method.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            method.Executable = true;
            method.UserExecutable = true;
            method.MethodDeclarationId = isDelete
                ? MethodIds.PriorityMappingTableType_DeletePriorityMappingEntry
                : MethodIds.PriorityMappingTableType_AddPriorityMappingEntry;
            method.InputArguments = CreateInputArguments(method, isDelete);
            method.OnReadUserExecutable = (ISystemContext context, NodeState _, ref bool value) =>
            {
                value = m_authorizeMappingEdits(context);
                return ServiceResult.Good;
            };
            method.OnCallMethodWithResultAsync = async (context, _, _, inputs, cancellationToken) =>
            {
                if (!m_authorizeMappingEdits(context))
                {
                    return new MethodInvocationResult(new ServiceResult(StatusCodes.BadUserAccessDenied));
                }
                if (!TryReadMappingArguments(inputs, isDelete, out PriorityMappingEntryType entry))
                {
                    return new MethodInvocationResult(new ServiceResult(StatusCodes.BadInvalidArgument));
                }

                StatusCode result = await m_provider.EditPriorityMappingAsync(
                    tableId, entry, isDelete, cancellationToken).ConfigureAwait(false);
                return new MethodInvocationResult(new ServiceResult(result));
            };
        }

        /// <summary>
        /// Builds standard InputArguments metadata for the selected mapping method.
        /// </summary>
        private PropertyState<ArrayOf<Argument>> CreateInputArguments(MethodState method, bool isDelete)
        {
            Argument[] arguments = isDelete
                ?
                [
                    new Argument
                    {
                        Name = "MappingUri",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = "PriorityLabel",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
                :
                [
                    new Argument
                    {
                        Name = "MappingUri",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = "PriorityLabel",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = "PriorityValue_PCP",
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new Argument
                    {
                        Name = "PriorityValue_DSCP",
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    }
                ];
            var argumentsProperty = PropertyState<ArrayOf<Argument>>.With<StructureBuilder<Argument>>(method);
            argumentsProperty.NodeId = NodeIdFor(method.NodeId.ToString(), BrowseNames.InputArguments);
            argumentsProperty.BrowseName = new QualifiedName(BrowseNames.InputArguments, 0);
            argumentsProperty.DisplayName = new LocalizedText(BrowseNames.InputArguments);
            argumentsProperty.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            argumentsProperty.TypeDefinitionId = VariableTypeIds.PropertyType;
            argumentsProperty.DataType = DataTypeIds.Argument;
            argumentsProperty.ValueRank = ValueRanks.OneDimension;
            argumentsProperty.Value = ArrayOf.Wrapped(arguments);
            return argumentsProperty;
        }

        /// <summary>
        /// Validates and converts the argument list to the provider contract's mapping structure.
        /// </summary>
        private static bool TryReadMappingArguments(
            ArrayOf<Variant> inputs,
            bool isDelete,
            out PriorityMappingEntryType entry)
        {
            entry = new PriorityMappingEntryType();
            if (inputs.Count != (isDelete ? 2 : 4) ||
                !inputs[0].TryGetValue(out string? mappingUri) ||
                !inputs[1].TryGetValue(out string? priorityLabel) ||
                string.IsNullOrWhiteSpace(mappingUri) ||
                string.IsNullOrWhiteSpace(priorityLabel))
            {
                return false;
            }

            entry.MappingUri = mappingUri;
            entry.PriorityLabel = priorityLabel;
            entry.PriorityValue_PCP = byte.MaxValue;
            entry.PriorityValue_DSCP = uint.MaxValue;
            if (!isDelete &&
                (!inputs[2].TryGetValue(out byte pcp) ||
                 !inputs[3].TryGetValue(out uint dscp) ||
                 (pcp != byte.MaxValue && pcp > 7) ||
                 (dscp != uint.MaxValue && dscp > 63)))
            {
                return false;
            }

            if (!isDelete)
            {
                inputs[2].TryGetValue(out byte pcpValue);
                inputs[3].TryGetValue(out uint dscpValue);
                entry.PriorityValue_PCP = pcpValue;
                entry.PriorityValue_DSCP = dscpValue;
            }
            return true;
        }

        /// <summary>
        /// Adds a standard scalar or array value node with snapshot quality and source time.
        /// </summary>
        private BaseVariableState AddVariable(
            BaseInstanceState parent,
            string name,
            NodeId dataType,
            Variant value,
            NetworkModelSnapshot snapshot,
            bool property = false,
            int valueRank = ValueRanks.Scalar,
            uint[]? dimensions = null)
        {
            BaseVariableState? generated = parent.CreateChild(
                SystemContext, new QualifiedName(name, 0), assignInstanceNodeIds: false)
                    is BaseVariableState typed ? typed : null;
            BaseVariableState variable = generated ??
                (property ? new PropertyState(parent) : new BaseDataVariableState(parent));
            variable.NodeId = NodeIdFor(parent.NodeId.ToString(), name);
            variable.BrowseName = new QualifiedName(name, 0);
            variable.DisplayName = new LocalizedText(name);
            variable.ReferenceTypeId = property
                ? ReferenceTypeIds.HasProperty
                : ReferenceTypeIds.HasComponent;
            variable.TypeDefinitionId = property
                ? VariableTypeIds.PropertyType
                : VariableTypeIds.BaseDataVariableType;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.ArrayDimensions = dimensions is null ? default : ArrayOf.Wrapped(dimensions);
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = value;
            variable.StatusCode = snapshot.StatusCode;
            variable.Timestamp = snapshot.Timestamp;
            if (generated is null)
            {
                parent.AddChild(variable);
            }
            return variable;
        }

        /// <summary>
        /// Adds a UInt64 analog value with a Part 22 engineering-units property.
        /// </summary>
        private void AddAnalog(
            BaseObjectState parent,
            string name,
            ulong value,
            int unitId,
            string unitName,
            string unitDescription,
            NetworkModelSnapshot snapshot)
        {
            BaseVariableState variable = AddVariable(
                parent, name, DataTypeIds.UInt64, value, snapshot);
            variable.TypeDefinitionId = VariableTypeIds.AnalogUnitType;
            AddVariable(
                variable,
                BrowseNames.EngineeringUnits,
                DataTypeIds.EUInformation,
                Variant.FromStructure(new EUInformation
                {
                    NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                    UnitId = unitId,
                    DisplayName = new LocalizedText("en", unitName),
                    Description = new LocalizedText("en", unitDescription)
                }),
                snapshot,
                property: true);
        }

        /// <summary>
        /// Creates an application-owned object with a stable string NodeId.
        /// </summary>
        private BaseObjectState CreateObject(
            string kind,
            string identity,
            string browseName,
            NodeId typeDefinitionId,
            NodeId referenceTypeId)
        {
            BaseObjectState node = typeDefinitionId == ObjectTypeIds.IetfBaseNetworkInterfaceType
                ? new IetfBaseNetworkInterfaceState(null)
                : typeDefinitionId == ObjectTypeIds.PriorityMappingTableType
                    ? new PriorityMappingTableState(null)
                    : typeDefinitionId == ObjectTypeIds.LldpLocalSystemType
                        ? new LldpLocalSystemState(null)
                        : typeDefinitionId == ObjectTypeIds.LldpPortInformationType
                            ? new LldpPortInformationState(null)
                            : typeDefinitionId == ObjectTypeIds.LldpRemoteSystemType
                                ? new LldpRemoteSystemState(null)
                                : new BaseObjectState(null);
            node.NodeId = NodeIdFor(kind, identity);
            node.BrowseName = new QualifiedName(browseName, NamespaceIndex);
            node.DisplayName = new LocalizedText(browseName);
            node.TypeDefinitionId = typeDefinitionId;
            node.ReferenceTypeId = referenceTypeId;
            return node;
        }

        /// <summary>
        /// Creates an application-owned object and attaches it to its dynamic parent.
        /// </summary>
        private BaseObjectState AddObject(
            BaseObjectState parent,
            string browseName,
            NodeId nodeId,
            NodeId typeDefinitionId,
            NodeId referenceTypeId,
            bool standardName = false)
        {
            var qualifiedName = new QualifiedName(browseName, standardName ? (ushort)0 : NamespaceIndex);
            BaseObjectState? generated = parent.CreateChild(
                SystemContext, qualifiedName, assignInstanceNodeIds: false) as BaseObjectState;
            BaseObjectState child = generated ?? new BaseObjectState(parent);
            child.NodeId = nodeId;
            child.BrowseName = qualifiedName;
            child.DisplayName = new LocalizedText(browseName);
            child.TypeDefinitionId = typeDefinitionId;
            child.ReferenceTypeId = referenceTypeId;
            if (generated is null)
            {
                parent.AddChild(child);
            }
            return child;
        }

        /// <summary>
        /// Reconciles optional children without detaching monitored items on retained nodes.
        /// </summary>
        private async ValueTask SynchronizeChildrenAsync(
            NodeState source,
            NodeState target,
            CancellationToken cancellationToken)
        {
            var sourceChildren = new List<BaseInstanceState>();
            source.GetChildren(SystemContext, sourceChildren);
            var targetChildren = new List<BaseInstanceState>();
            target.GetChildren(SystemContext, targetChildren);
            Dictionary<NodeId, BaseInstanceState> desired = sourceChildren
                .ToDictionary(node => node.NodeId, NodeIdComparer.Default);
            Dictionary<NodeId, BaseInstanceState> existing = targetChildren
                .ToDictionary(node => node.NodeId, NodeIdComparer.Default);

            foreach (BaseInstanceState child in targetChildren)
            {
                if (!desired.ContainsKey(child.NodeId))
                {
                    await DeleteNodeAsync(SystemContext, child.NodeId, cancellationToken).ConfigureAwait(false);
                }
            }
            foreach (BaseInstanceState child in sourceChildren)
            {
                if (existing.TryGetValue(child.NodeId, out BaseInstanceState? retained))
                {
                    await SynchronizeChildrenAsync(child, retained, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    child.Parent = target;
                    target.ReplaceChild(SystemContext, child);
                    await AddPredefinedNodeAsync(child, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Copies values and acquisition timestamps before publishing the reconciled tree.
        /// </summary>
        private void CopyValues(NodeState source, NodeState target)
        {
            Dictionary<NodeId, NodeState> targetNodes = CollectNodes(target)
                .ToDictionary(node => node.NodeId, NodeIdComparer.Default);
            foreach (NodeState sourceNode in CollectNodes(source))
            {
                if (sourceNode is not BaseVariableState sourceVariable ||
                    !targetNodes.TryGetValue(sourceNode.NodeId, out NodeState? targetNode) ||
                    targetNode is not BaseVariableState targetVariable)
                {
                    if (targetNodes.TryGetValue(sourceNode.NodeId, out targetNode))
                    {
                        targetNode.BrowseName = sourceNode.BrowseName;
                        targetNode.DisplayName = sourceNode.DisplayName;
                        targetNode.Description = sourceNode.Description;
                    }
                    continue;
                }
                targetVariable.Value = sourceVariable.Value;
                targetVariable.ArrayDimensions = sourceVariable.ArrayDimensions;
                targetVariable.StatusCode = sourceVariable.StatusCode;
                targetVariable.Timestamp = sourceVariable.Timestamp;
            }
        }

        /// <summary>
        /// Synchronizes capability, lower-layer and mapping-table references after a snapshot change.
        /// </summary>
        private async ValueTask SynchronizeCrossReferencesAsync(
            NodeState source,
            NodeState target,
            IDictionary<NodeId, IList<IReference>>? externalReferences,
            CancellationToken cancellationToken)
        {
            foreach (NodeId referenceTypeId in s_dynamicReferenceTypes)
            {
                var desired = new List<IReference>();
                source.GetReferences(SystemContext, desired, referenceTypeId, isInverse: false);
                var existing = new List<IReference>();
                target.GetReferences(SystemContext, existing, referenceTypeId, isInverse: false);
                foreach (IReference reference in existing)
                {
                    if (reference.ReferenceTypeId == referenceTypeId && !reference.IsInverse &&
                        !desired.Any(candidate => candidate.TargetId == reference.TargetId))
                    {
                        target.RemoveReference(referenceTypeId, false, reference.TargetId);
                        await Server.NodeManager.DeleteReferencesAsync(target.NodeId,
                        [
                            new ReferenceNode
                            {
                                ReferenceTypeId = referenceTypeId,
                                IsInverse = false,
                                TargetId = reference.TargetId
                            }
                        ], cancellationToken).ConfigureAwait(false);
                    }
                }
                foreach (IReference reference in desired)
                {
                    target.AddReferenceIfMissing(referenceTypeId, false, reference.TargetId);
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris);
                    if (PredefinedNodes.TryGetValue(targetId, out NodeState? local))
                    {
                        local.AddReferenceIfMissing(referenceTypeId, true, target.NodeId);
                    }
                    else if (externalReferences is not null)
                    {
                        AddExternalReference(externalReferences, targetId,
                            new NodeStateReference(referenceTypeId, true, target.NodeId));
                    }
                    else
                    {
                        await Server.NodeManager.AddReferencesAsync(
                            targetId, [new NodeStateReference(referenceTypeId, true, target.NodeId)],
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Collects a node subtree in parent-first order.
        /// </summary>
        private List<NodeState> CollectNodes(NodeState root)
        {
            var nodes = new List<NodeState> { root };
            for (int index = 0; index < nodes.Count; index++)
            {
                var children = new List<BaseInstanceState>();
                nodes[index].GetChildren(SystemContext, children);
                nodes.AddRange(children);
            }
            return nodes;
        }

        /// <summary>
        /// Adds an external standard-node reference to the shared startup graph.
        /// </summary>
        private static void AddExternalReference(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            NodeId parentId,
            IReference reference)
        {
            if (!externalReferences.TryGetValue(parentId, out IList<IReference>? references))
            {
                references = [];
                externalReferences.Add(parentId, references);
            }
            references.Add(reference);
        }

        /// <summary>
        /// Builds the stable entity key used to reconcile provider snapshots.
        /// </summary>
        private static string EntityKey(string kind, string identity)
        {
            return $"{kind}\u001f{identity}";
        }

        /// <summary>
        /// Builds a stable application-owned identifier from browse-path components.
        /// </summary>
        private NodeId NodeIdFor(params string[] components)
        {
            return new NodeId(string.Join("/", components.Select(Uri.EscapeDataString)), NamespaceIndex);
        }

        /// <summary>
        /// Normalizes a stream-interface browse name while retaining provider identity in its NodeId.
        /// </summary>
        private static string QualifiedComponentName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "Interface" : name;
        }

        /// <summary>
        /// Dynamic references whose targets can change between provider revisions.
        /// </summary>
        private static readonly NodeId[] s_dynamicReferenceTypes =
        [
            ReferenceTypeIds.HasInterface,
            ReferenceTypeIds.HasLowerLayerInterface,
            ReferenceTypeIds.UsesPriorityMappingTable
        ];

        /// <summary>
        /// Standard LLDP values remain owned and registered by the diagnostics manager.
        /// </summary>
        private static readonly NodeId[] s_lldpLocalVariableIds =
        [
            VariableIds.LLDP_LocalSystemData_ChassisIdSubtype,
            VariableIds.LLDP_LocalSystemData_ChassisId,
            VariableIds.LLDP_LocalSystemData_SystemName,
            VariableIds.LLDP_LocalSystemData_SystemDescription
        ];

        /// <summary>
        /// Provider used for initial reads, updates and mapping method calls.
        /// </summary>
        private readonly INetworkModelProvider m_provider;

        /// <summary>
        /// Explicit application authorization policy for mapping methods.
        /// </summary>
        private readonly Func<ISystemContext, bool> m_authorizeMappingEdits;

        /// <summary>
        /// Logger for provider acquisition and watch failures.
        /// </summary>
        private readonly ILogger m_networkLogger;

        /// <summary>
        /// Serializes complete snapshot application.
        /// </summary>
        private readonly SemaphoreSlim m_applyGate;

        /// <summary>
        /// Currently materialized provider-owned entities keyed by category and stable identity.
        /// </summary>
        private readonly Dictionary<string, EntityDefinition> m_entities = new(StringComparer.Ordinal);

        /// <summary>
        /// Cancellation source for the asynchronous provider watch.
        /// </summary>
        private CancellationTokenSource? m_watchCancellation;

        /// <summary>
        /// The asynchronous provider watch task.
        /// </summary>
        private Task? m_watchTask;

        /// <summary>
        /// The most recently applied provider revision.
        /// </summary>
        private long m_revision = long.MinValue;

        /// <summary>
        /// Acquisition timestamp of the most recently applied provider revision.
        /// </summary>
        private DateTime m_timestamp = DateTime.MinValue;

        /// <summary>
        /// Entity root and the standard entry point that organizes it.
        /// </summary>
        private sealed record EntityDefinition(NodeId ParentId, BaseObjectState Root);
    }
}
