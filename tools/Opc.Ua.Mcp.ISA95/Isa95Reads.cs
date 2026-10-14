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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Mcp.Serialization;
using CommonTypes = Opc.Ua.ISA95.ObjectTypeIds;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Common-model placeholder reads and role-independent, generated status-event decoding.
    /// </summary>
    internal static class Isa95Reads
    {
        /// <summary>
        /// Reads only a validated common object and one page of its dynamic variable properties.
        /// </summary>
        public static async ValueTask<JsonObject> ReadCommonObjectAsync(
            Isa95Client client,
            NodeId objectId,
            int offset,
            int maxResults,
            CancellationToken ct)
        {
            ISession session = client.Session;
            ArrayOf<ReferenceDescription> types = await BrowseAsync(
                session, objectId, ReferenceTypeIds.HasTypeDefinition, NodeClass.ObjectType, ct).ConfigureAwait(false);
            NodeId typeId = types.Count == 1
                ? ExpandedNodeId.ToNodeId(types[0].NodeId, session.NamespaceUris) : NodeId.Null;
            Isa95CommonObjectKind kind = await CommonKindAsync(session, typeId, ct).ConfigureAwait(false);

            // OPC 10030 properties are vendor-named placeholders, not fixed generated proxy members.
            ArrayOf<ReferenceDescription> variables = await BrowseAsync(
                session, objectId, default, NodeClass.Variable, ct).ConfigureAwait(false);
            var seen = new HashSet<NodeId>();
            var local = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in variables)
            {
                NodeId node = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (reference.NodeId.ServerIndex == 0 && !node.IsNull && seen.Add(node))
                {
                    local.Add(reference);
                }
            }
            JsonObject page = McpCompanionTools.Page(local.ToArrayOf(), reference => new JsonObject
            {
                ["nodeId"] = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris).ToString(),
                ["browseName"] = reference.BrowseName.ToString(),
                ["referenceTypeId"] = reference.ReferenceTypeId.ToString()
            }, offset, maxResults);
            var reads = new List<ReadValueId>
            {
                new() { NodeId = objectId, AttributeId = Attributes.BrowseName },
                new() { NodeId = objectId, AttributeId = Attributes.DisplayName },
                new() { NodeId = objectId, AttributeId = Attributes.Description }
            };
            int end = System.Math.Min(local.Count, offset + maxResults);
            for (int index = offset; index < end; index++)
            {
                reads.Add(new ReadValueId
                {
                    NodeId = ExpandedNodeId.ToNodeId(local[index].NodeId, session.NamespaceUris),
                    AttributeId = Attributes.Value
                });
            }
            ArrayOf<ReadValueId> request = reads.ToArrayOf();
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both, request, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, request);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, request);
            JsonArray items = (JsonArray)page["items"]!;
            for (int index = 0; index < items.Count; index++)
            {
                items[index]!["value"] = McpCompanionJson.DataValue(
                    response.Results[index + 3], session.MessageContext);
            }
            return new JsonObject
            {
                ["nodeId"] = objectId.ToString(),
                ["typeDefinitionId"] = typeId.ToString(),
                ["kind"] = kind.ToString(),
                ["browseName"] = McpCompanionJson.DataValue(response.Results[0], session.MessageContext),
                ["displayName"] = McpCompanionJson.DataValue(response.Results[1], session.MessageContext),
                ["description"] = McpCompanionJson.DataValue(response.Results[2], session.MessageContext),
                ["properties"] = page
            };
        }

        /// <summary>
        /// Observes generated status-event records without constructing a three-role job-control wrapper.
        /// </summary>
        public static async IAsyncEnumerable<V2.ISA95JobOrderStatusEventTypeRecord> StatusEventsAsync(
            Isa95Client client,
            IStreamingSubscription streaming,
            NodeId notifier,
            [EnumeratorCancellation] CancellationToken ct)
        {
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default.CreateChildScope();
            V2.ISA95JobControlV2EventRecordDecoders.RegisterISA95JobControlV2Decoders(
                registry, client.Session.NamespaceUris);
            EventFilter filter = V2.ISA95JobOrderStatusEventTypeRecord.EventFilters.Build(
                client.Session.NamespaceUris, registry);
            NodeId declaredType = ExpandedNodeId.ToNodeId(
                V2.ObjectTypeIds.ISA95JobOrderStatusEventType, client.Session.NamespaceUris);
            await foreach (EventNotification notification in streaming.SubscribeEventsAsync(
                notifier, filter, null, ct).ConfigureAwait(false))
            {
                Variant[] fields = notification.Fields.ToArray() ?? [];
                var record = registry.Decode(fields) as V2.ISA95JobOrderStatusEventTypeRecord;
                if (record is null && !declaredType.IsNull)
                {
                    QualifiedName[][] paths = registry.StandardFields;
                    for (int index = 0; index < paths.Length && index < fields.Length; index++)
                    {
                        if (paths[index].Length > 0 &&
                            paths[index][^1].Name == BrowseNames.EventType &&
                            fields[index].TryGetValue(out NodeId eventType) &&
                            !eventType.IsNull &&
                            await client.Session.NodeCache.IsTypeOfAsync(eventType, declaredType, ct)
                                .ConfigureAwait(false))
                        {
                            record = registry.DecodeAs(declaredType, fields) as V2.ISA95JobOrderStatusEventTypeRecord;
                            break;
                        }
                    }
                }
                if (record is not null)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Reads all continuation pages of one reference set and preserves service errors.
        /// </summary>
        private static async ValueTask<ArrayOf<ReferenceDescription>> BrowseAsync(
            ISession session,
            NodeId node,
            NodeId referenceType,
            NodeClass nodeClass,
            CancellationToken ct)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, ArrayOf<ServiceResult> errors) =
                await session.ManagedBrowseAsync(
                    null, null, [node], 0, BrowseDirection.Forward, referenceType,
                    true, (uint)nodeClass, ct).ConfigureAwait(false);
            foreach (ServiceResult error in errors)
            {
                if (ServiceResult.IsBad(error))
                {
                    throw new ServiceResultException(error);
                }
            }
            return descriptions.Count > 0 ? descriptions[0] : [];
        }

        /// <summary>
        /// Checks a node's actual type against the common-model object families.
        /// </summary>
        private static async ValueTask<Isa95CommonObjectKind> CommonKindAsync(
            ISession session,
            NodeId typeId,
            CancellationToken ct)
        {
            if (!typeId.IsNull)
            {
                foreach ((ExpandedNodeId declared, Isa95CommonObjectKind kind) in s_commonKinds)
                {
                    NodeId node = ExpandedNodeId.ToNodeId(declared, session.NamespaceUris);
                    if (!node.IsNull &&
                        await session.NodeCache.IsTypeOfAsync(typeId, node, ct).ConfigureAwait(false))
                    {
                        return kind;
                    }
                }
            }
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The node is not an ISA-95 common object.");
        }

        private static readonly (ExpandedNodeId Type, Isa95CommonObjectKind Kind)[] s_commonKinds =
        [
            (CommonTypes.PersonnelClassType, Isa95CommonObjectKind.PersonnelClass),
            (CommonTypes.PersonType, Isa95CommonObjectKind.Person),
            (CommonTypes.EquipmentClassType, Isa95CommonObjectKind.EquipmentClass),
            (CommonTypes.EquipmentType, Isa95CommonObjectKind.Equipment),
            (CommonTypes.PhysicalAssetClassType, Isa95CommonObjectKind.PhysicalAssetClass),
            (CommonTypes.PhysicalAssetType, Isa95CommonObjectKind.PhysicalAsset),
            (CommonTypes.MaterialClassType, Isa95CommonObjectKind.MaterialClass),
            (CommonTypes.MaterialDefinitionType, Isa95CommonObjectKind.MaterialDefinition),
            (CommonTypes.MaterialLotType, Isa95CommonObjectKind.MaterialLot),
            (CommonTypes.MaterialSublotType, Isa95CommonObjectKind.MaterialSublot)
        ];
    }
}
