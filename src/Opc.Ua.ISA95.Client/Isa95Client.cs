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

#pragma warning disable IDE0005 // Imports are required by target frameworks without matching implicit global usings.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;
#pragma warning restore IDE0005

namespace Opc.Ua.ISA95.Client
{
    /// <summary>
    /// Entry point for ISA-95 client discovery and direct Job Control clients.
    /// </summary>
    public sealed class Isa95Client
    {
        /// <summary>
        /// Initializes an ISA-95 client over an existing session.
        /// </summary>
        public Isa95Client(ISession session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Isa95EncodeableRegistration.Register(Session);
        }

        /// <summary>
        /// Gets the session used by this client.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Gets the telemetry context used by generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Discovers OPC-10030 common-model objects below a root node.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ArrayOf<Isa95CommonObjectEntry>> DiscoverCommonObjectsAsync(
            NodeId rootNodeId,
            bool recursive = true,
            CancellationToken ct = default)
        {
            if (rootNodeId.IsNull)
            {
                throw new ArgumentException("A root NodeId is required.", nameof(rootNodeId));
            }

            var entries = new List<Isa95CommonObjectEntry>();
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(rootNodeId);
            visited.Add(rootNodeId);

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                NodeId current = pending.Dequeue();
                (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                    await Session.ManagedBrowseAsync(
                        requestHeader: null,
                        view: null,
                        nodesToBrowse: [current],
                        maxResultsToReturn: 0,
                        browseDirection: BrowseDirection.Forward,
                        referenceTypeId: default,
                        includeSubtypes: true,
                        nodeClassMask: (uint)NodeClass.Object,
                        ct: ct).ConfigureAwait(false);

                if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
                {
                    throw new ServiceResultException(errors[0]);
                }
                if (descriptions.Count == 0)
                {
                    continue;
                }

                ArrayOf<ReferenceDescription> references = descriptions[0];
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];
                    NodeId nodeId = ToNodeId(reference.NodeId);
                    NodeId typeDefinitionId = ToNodeId(reference.TypeDefinition);
                    if (nodeId.IsNull)
                    {
                        continue;
                    }
                    (bool isCommonObject, Isa95CommonObjectKind kind) =
                        await TryGetCommonKindAsync(
                            typeDefinitionId,
                            ct).ConfigureAwait(false);
                    if (isCommonObject)
                    {
                        entries.Add(new Isa95CommonObjectEntry(
                            nodeId,
                            typeDefinitionId,
                            kind,
                            reference.BrowseName.Name ?? string.Empty));
                    }
                    if (recursive && visited.Add(nodeId))
                    {
                        pending.Enqueue(nodeId);
                    }
                }
            }

            return entries.ToArray().ToArrayOf();
        }

        /// <summary>
        /// Creates a generated PersonType client proxy.
        /// </summary>
        public PersonTypeClient CreatePersonClient(NodeId nodeId)
        {
            return new PersonTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates a generated EquipmentType client proxy.
        /// </summary>
        public EquipmentTypeClient CreateEquipmentClient(NodeId nodeId)
        {
            return new EquipmentTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates a generated PhysicalAssetType client proxy.
        /// </summary>
        public PhysicalAssetTypeClient CreatePhysicalAssetClient(NodeId nodeId)
        {
            return new PhysicalAssetTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates a generated MaterialLotType client proxy.
        /// </summary>
        public MaterialLotTypeClient CreateMaterialLotClient(NodeId nodeId)
        {
            return new MaterialLotTypeClient(Session, nodeId, Telemetry);
        }

        /// <summary>
        /// Creates a direct V1 Job Control client.
        /// </summary>
        public Isa95JobControlV1Client CreateJobControlV1Client(
            NodeId jobOrderReceiverId,
            NodeId jobResponseProviderId,
            NodeId jobResponseReceiverId)
        {
            return new Isa95JobControlV1Client(
                Session,
                jobOrderReceiverId,
                jobResponseProviderId,
                jobResponseReceiverId,
                Telemetry);
        }

        /// <summary>
        /// Creates a direct V2 Job Control client.
        /// </summary>
        public Isa95JobControlV2Client CreateJobControlV2Client(
            NodeId jobOrderReceiverId,
            NodeId jobResponseProviderId,
            NodeId jobResponseReceiverId)
        {
            return new Isa95JobControlV2Client(
                Session,
                jobOrderReceiverId,
                jobResponseProviderId,
                jobResponseReceiverId,
                Telemetry);
        }

        /// <summary>
        /// Finds direct children of <paramref name="rootNodeId"/> that implement
        /// the V1 or V2 Job Control endpoint object types.
        /// </summary>
        /// <remarks>
        /// ManagedBrowseAsync follows BrowseNext continuation points. Results
        /// retain every endpoint so callers can explicitly handle absent or
        /// ambiguous facets instead of silently selecting one.
        /// </remarks>
        public ValueTask<Isa95JobControlDiscovery> DiscoverJobControlAsync(
            NodeId rootNodeId,
            CancellationToken ct = default)
        {
            return DiscoverJobControlCoreAsync(
                rootNodeId,
                maxFolderDepth: 0,
                ct);
        }

        private async ValueTask<Isa95JobControlDiscovery> DiscoverJobControlCoreAsync(
            NodeId rootNodeId,
            int maxFolderDepth,
            CancellationToken ct)
        {
            if (rootNodeId.IsNull)
            {
                throw new ArgumentException("A root NodeId is required.", nameof(rootNodeId));
            }

            var pending = new Queue<(NodeId NodeId, int Depth)>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue((rootNodeId, 0));
            visited.Add(rootNodeId);

            var v1 = new List<Isa95JobControlEndpoint>();
            var v2 = new List<Isa95JobControlEndpoint>();
            NodeId v1OrderReceiver = ToNodeId(V1.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
            NodeId v1ResponseProvider = ToNodeId(V1.ObjectTypeIds.ISA95JobResponseProviderObjectType);
            NodeId v1ResponseReceiver = ToNodeId(V1.ObjectTypeIds.ISA95JobResponseReceiverObjectType);
            NodeId v2OrderReceiver = ToNodeId(V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
            NodeId v2OrderReceiverSubStates =
                ToNodeId(V2.ObjectTypeIds.ISA95JobOrderReceiverSubStatesType);
            NodeId v2ResponseProvider = ToNodeId(V2.ObjectTypeIds.ISA95JobResponseProviderObjectType);
            NodeId v2ResponseReceiver = ToNodeId(V2.ObjectTypeIds.ISA95JobResponseReceiverObjectType);
            NodeId folderType = Ua.ObjectTypeIds.FolderType;

            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                (NodeId current, int depth) = pending.Dequeue();
                (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                    await Session.ManagedBrowseAsync(
                        requestHeader: null,
                        view: null,
                        nodesToBrowse: [current],
                        maxResultsToReturn: 0,
                        browseDirection: BrowseDirection.Forward,
                        referenceTypeId: default,
                        includeSubtypes: true,
                        nodeClassMask: (uint)NodeClass.Object,
                        ct: ct).ConfigureAwait(false);

                if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
                {
                    throw new ServiceResultException(errors[0]);
                }

                if (descriptions.Count == 0)
                {
                    continue;
                }
                ArrayOf<ReferenceDescription> references = descriptions[0];
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];
                    ct.ThrowIfCancellationRequested();
                    NodeId instanceId = ToNodeId(reference.NodeId);
                    NodeId typeDefinitionId = ToNodeId(reference.TypeDefinition);
                    if (instanceId.IsNull || typeDefinitionId.IsNull)
                    {
                        continue;
                    }

                    string browseName = reference.BrowseName.Name ?? string.Empty;
                    (bool isV1Facet, Isa95JobControlFacet v1Facet) =
                        await TryGetFacetAsync(
                            typeDefinitionId,
                            v1OrderReceiver,
                            v1ResponseProvider,
                            v1ResponseReceiver,
                            ct).ConfigureAwait(false);
                    if (isV1Facet)
                    {
                        v1.Add(new Isa95JobControlEndpoint(
                            instanceId,
                            typeDefinitionId,
                            v1Facet,
                            browseName));
                    }
                    else
                    {
                        (bool isV2Facet, Isa95JobControlFacet v2Facet) =
                            await TryGetFacetAsync(
                                typeDefinitionId,
                                v2OrderReceiver,
                                v2ResponseProvider,
                                v2ResponseReceiver,
                                ct,
                                v2OrderReceiverSubStates).ConfigureAwait(false);
                        if (isV2Facet)
                        {
                            v2.Add(new Isa95JobControlEndpoint(
                                instanceId,
                                typeDefinitionId,
                                v2Facet,
                                browseName));
                        }
                        else if (depth < maxFolderDepth &&
                            await Session.NodeCache.IsTypeOfAsync(
                                typeDefinitionId,
                                folderType,
                                ct).ConfigureAwait(false) &&
                            visited.Add(instanceId))
                        {
                            pending.Enqueue((instanceId, depth + 1));
                        }
                    }
                }
            }

            return new Isa95JobControlDiscovery(
                v1.ToArray().ToArrayOf(),
                v2.ToArray().ToArrayOf());
        }

        /// <summary>
        /// Finds Job Control endpoint objects below the standard Objects folder.
        /// </summary>
        public ValueTask<Isa95JobControlDiscovery> DiscoverJobControlAsync(CancellationToken ct = default)
        {
            return DiscoverJobControlCoreAsync(
                Ua.ObjectIds.ObjectsFolder,
                maxFolderDepth: 1,
                ct);
        }

        private NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Session.NamespaceUris);
        }

        private async ValueTask<(bool Found, Isa95JobControlFacet Facet)> TryGetFacetAsync(
            NodeId typeDefinitionId,
            NodeId orderReceiverTypeId,
            NodeId responseProviderTypeId,
            NodeId responseReceiverTypeId,
            CancellationToken ct,
            NodeId additionalOrderReceiverTypeId = default)
        {
            if (await Session.NodeCache.IsTypeOfAsync(
                    typeDefinitionId,
                    orderReceiverTypeId,
                    ct).ConfigureAwait(false) ||
                (!additionalOrderReceiverTypeId.IsNull &&
                    await Session.NodeCache.IsTypeOfAsync(
                        typeDefinitionId,
                        additionalOrderReceiverTypeId,
                        ct).ConfigureAwait(false)))
            {
                return (true, Isa95JobControlFacet.JobOrderReceiver);
            }
            if (await Session.NodeCache.IsTypeOfAsync(
                typeDefinitionId,
                responseProviderTypeId,
                ct).ConfigureAwait(false))
            {
                return (true, Isa95JobControlFacet.JobResponseProvider);
            }
            if (await Session.NodeCache.IsTypeOfAsync(
                typeDefinitionId,
                responseReceiverTypeId,
                ct).ConfigureAwait(false))
            {
                return (true, Isa95JobControlFacet.JobResponseReceiver);
            }

            return (false, default);
        }

        private async ValueTask<(bool Found, Isa95CommonObjectKind Kind)> TryGetCommonKindAsync(
            NodeId typeDefinitionId,
            CancellationToken ct)
        {
            (ExpandedNodeId TypeId, Isa95CommonObjectKind Kind)[] mappings =
            [
                (ObjectTypeIds.PersonnelClassType, Isa95CommonObjectKind.PersonnelClass),
                (ObjectTypeIds.PersonType, Isa95CommonObjectKind.Person),
                (ObjectTypeIds.EquipmentClassType, Isa95CommonObjectKind.EquipmentClass),
                (ObjectTypeIds.EquipmentType, Isa95CommonObjectKind.Equipment),
                (ObjectTypeIds.PhysicalAssetClassType, Isa95CommonObjectKind.PhysicalAssetClass),
                (ObjectTypeIds.PhysicalAssetType, Isa95CommonObjectKind.PhysicalAsset),
                (ObjectTypeIds.MaterialClassType, Isa95CommonObjectKind.MaterialClass),
                (ObjectTypeIds.MaterialDefinitionType, Isa95CommonObjectKind.MaterialDefinition),
                (ObjectTypeIds.MaterialLotType, Isa95CommonObjectKind.MaterialLot),
                (ObjectTypeIds.MaterialSublotType, Isa95CommonObjectKind.MaterialSublot)
            ];
            foreach ((ExpandedNodeId typeId, Isa95CommonObjectKind mappedKind) in mappings)
            {
                if (await Session.NodeCache.IsTypeOfAsync(
                    typeDefinitionId,
                    ToNodeId(typeId),
                    ct).ConfigureAwait(false))
                {
                    return (true, mappedKind);
                }
            }
            return (false, default);
        }
    }

    /// <summary>
    /// Job Control endpoints found for the two ISA-95 Job Control namespaces.
    /// </summary>
    public sealed class Isa95JobControlDiscovery
    {
        /// <summary>
        /// Initializes a discovery result.
        /// </summary>
        public Isa95JobControlDiscovery(
            ArrayOf<Isa95JobControlEndpoint> v1Endpoints,
            ArrayOf<Isa95JobControlEndpoint> v2Endpoints)
        {
            V1Endpoints = v1Endpoints;
            V2Endpoints = v2Endpoints;
        }

        /// <summary>
        /// Gets endpoints with V1 type definitions.
        /// </summary>
        public ArrayOf<Isa95JobControlEndpoint> V1Endpoints { get; }

        /// <summary>
        /// Gets endpoints with V2 type definitions.
        /// </summary>
        public ArrayOf<Isa95JobControlEndpoint> V2Endpoints { get; }
    }

    /// <summary>
    /// Describes a discovered Job Control endpoint object.
    /// </summary>
    public sealed record Isa95JobControlEndpoint(
        NodeId NodeId,
        NodeId TypeDefinitionId,
        Isa95JobControlFacet Facet,
        string BrowseName);

    /// <summary>
    /// ISA-95 Job Control endpoint facets.
    /// </summary>
    public enum Isa95JobControlFacet
    {
        /// <summary>
        /// Job Order Receiver endpoint.
        /// </summary>
        JobOrderReceiver,

        /// <summary>
        /// Job Response Provider endpoint.
        /// </summary>
        JobResponseProvider,

        /// <summary>
        /// Job Response Receiver endpoint.
        /// </summary>
        JobResponseReceiver
    }

    /// <summary>
    /// A discovered OPC-10030 common-model object.
    /// </summary>
    public sealed record Isa95CommonObjectEntry(
        NodeId NodeId,
        NodeId TypeDefinitionId,
        Isa95CommonObjectKind Kind,
        string BrowseName);

    /// <summary>
    /// Primary OPC-10030 common-model object categories.
    /// </summary>
    public enum Isa95CommonObjectKind
    {
        PersonnelClass,
        Person,
        EquipmentClass,
        Equipment,
        PhysicalAssetClass,
        PhysicalAsset,
        MaterialClass,
        MaterialDefinition,
        MaterialLot,
        MaterialSublot
    }
}
