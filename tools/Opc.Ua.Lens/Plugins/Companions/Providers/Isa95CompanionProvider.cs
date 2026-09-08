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
using Opc.Ua;
using Opc.Ua.ISA95.Client;
using Isa95 = Opc.Ua.ISA95;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// OPC-10030 identity inspection and version-specific Job Control sample tasks.
/// A job is stored only; this provider never starts or changes production jobs.
/// </summary>
internal sealed class Isa95CompanionProvider : ICompanionProvider
{
    public CompanionDescriptor Descriptor { get; } = new(
        "isa95", "ISA-95 and Job Control V1/V2", Isa95.Namespaces.ISA95,
        "OPC 10030 common model and separate OPC 10031 Job Control V1/V2 namespaces");

    public ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        return IndustrialCompanionAccess.DiscoverAsync(
            context, Descriptor.Id, s_types, [ObjectIds.ObjectsFolder], cancellationToken);
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        var client = new Isa95Client(context.Session, context.Telemetry);
        if (!IsJobEndpoint(target))
        {
            return await InspectCommonAsync(context, target, client, cancellationToken).ConfigureAwait(false);
        }

        var values = new List<CompanionValue> { new("Endpoint NodeId", Variant.From(target.NodeId)) };
        bool version2 = IsV2(target);
        if (IsOrderReceiver(target))
        {
            ArrayOf<CompanionValue> properties = await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, target.NodeId, version2 ? V2.Namespaces.ISA95JobControlV2 : V1.Namespaces.ISA95JobControlV1,
                ["JobOrderList"], cancellationToken).ConfigureAwait(false);
            Variant orders = properties[0].Value;
            if (orders.TryGetValue(out StatusCode absent))
            {
                values.Add(new CompanionValue("JobOrderList", Variant.From(absent)));
            }
            else
            {
                int count;
                if (version2 && orders.TryGetStructure(
                    context.Session.MessageContext, out ArrayOf<V2.ISA95JobOrderAndStateDataType> v2Orders))
                {
                    count = v2Orders.Count;
                }
                else if (!version2 && orders.TryGetStructure(
                    context.Session.MessageContext, out ArrayOf<V1.ISA95JobOrderDataType> v1Orders))
                {
                    count = v1Orders.Count;
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch, "The JobOrderList has the wrong type.");
                }
                IndustrialCompanionAccess.CheckFields(context, count);
                values.Add(new CompanionValue("Job order count", Variant.From(count)));
            }
        }
        Binding? binding = await FindBindingAsync(context, target, cancellationToken).ConfigureAwait(false);
        var operations = new List<CompanionOperation>
        {
            new("refresh", "Read ISA-95 instance again", CompanionOperationSafety.ReadOnly)
        };
        if (binding is not null)
        {
            values.Add(new CompanionValue("Job order receiver", Variant.From(binding.OrderReceiver)));
            values.Add(new CompanionValue("Job response provider", Variant.From(binding.ResponseProvider)));
            values.Add(new CompanionValue("Job response receiver", Variant.From(binding.ResponseReceiver)));
            if (IsOrderReceiver(target))
            {
                operations.Add(new CompanionOperation(
                    "store-sample-job", "Store an empty sample job (do not start)",
                    CompanionOperationSafety.SampleMutation, kSampleHint));
            }
            else if (IsResponseProvider(target))
            {
                operations.Add(new CompanionOperation(
                    "request-sample-response", "Request the sample job response",
                    CompanionOperationSafety.ReadOnly, kSampleHint));
            }
        }
        IndustrialCompanionAccess.CheckFields(context, values.Count);
        return new CompanionInspection(
            [.. values], [.. operations],
            binding is null
                ? "Typed endpoint found. Guided Job Control requires exactly one order receiver, response provider " +
                    "and response receiver of the same version beneath a shared direct parent."
                : "Separate V1/V2 clients use the displayed endpoint identities. Sample Store never starts a job; " +
                    "work-master selection, job parameters and execution policy remain application-specific.");
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operationId == "refresh")
        {
            IndustrialCompanionAccess.RequireNoInput(input);
            CompanionInspection inspection = await InspectAsync(context, target, cancellationToken)
                .ConfigureAwait(false);
            return new CompanionOperationResult(inspection.Summary, inspection.Values);
        }
        if (operationId is not ("store-sample-job" or "request-sample-response"))
        {
            throw IndustrialCompanionAccess.Unsupported("This ISA-95 operation is not supported.");
        }
        string sampleId = IndustrialCompanionAccess.SampleId(input);
        await IndustrialCompanionAccess.RequireTargetAsync(
            context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
        if (!IsJobEndpoint(target) ||
            (operationId == "store-sample-job" && !IsOrderReceiver(target)) ||
            (operationId == "request-sample-response" && !IsResponseProvider(target)))
        {
            throw IndustrialCompanionAccess.Unsupported("Select the matching Job Control endpoint for this task.");
        }
        IndustrialCompanionAccess.CheckFields(context, 3);
        Binding binding = await FindBindingAsync(context, target, cancellationToken).ConfigureAwait(false)
            ?? throw IndustrialCompanionAccess.Unsupported("A unique complete Job Control endpoint set is required.");
        var client = new Isa95Client(context.Session, context.Telemetry);
        ulong status;
        int responseCount = 0;
        if (IsV2(target))
        {
            Isa95JobControlV2Client jobs = client.CreateJobControlV2Client(
                binding.OrderReceiver, binding.ResponseProvider, binding.ResponseReceiver);
            if (operationId == "store-sample-job")
            {
                status = await jobs.StoreAsync(
                    new V2.ISA95JobOrderDataType { JobOrderID = sampleId }, ct: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                (V2.ISA95JobResponseDataType response, status) =
                    await jobs.RequestJobResponseByJobOrderIdAsync(sampleId, cancellationToken).ConfigureAwait(false);
                CheckJobStatus(status);
                if (response.JobOrderID != sampleId)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError, "A different job response was returned.");
                }
                responseCount = 1;
            }
        }
        else
        {
            Isa95JobControlV1Client jobs = client.CreateJobControlV1Client(
                binding.OrderReceiver, binding.ResponseProvider, binding.ResponseReceiver);
            if (operationId == "store-sample-job")
            {
                status = await jobs.ReceiveJobOrderAsync(
                    V1.ISA95JobOrderCommandEnum.Store, new V1.ISA95JobOrderDataType { ID = sampleId },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                (ArrayOf<V1.ISA95JobResponseDataType> responses, status) = await jobs.RequestJobResponseAsync(
                    sampleId, V1.ISA95JobOrderStateEnum.Undefined, cancellationToken).ConfigureAwait(false);
                CheckJobStatus(status);
                IndustrialCompanionAccess.CheckFields(context, responses.Count);
                foreach (V1.ISA95JobResponseDataType response in responses)
                {
                    if (response.JobOrderID != sampleId)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError, "A different job response was returned.");
                    }
                }
                responseCount = responses.Count;
            }
        }
        CheckJobStatus(status);
        cancellationToken.ThrowIfCancellationRequested();
        return new CompanionOperationResult(
            operationId == "store-sample-job"
                ? "The server accepted Store for the sample job. No Start command was issued."
                : "The matching sample job response was requested without changing job state.",
            [
                new("Job order ID", Variant.From(sampleId)),
                new("ISA-95 return status", Variant.From(status)),
                new("Response count", Variant.From(responseCount))
            ]);
    }

    private static async ValueTask<CompanionInspection> InspectCommonAsync(
        CompanionContext context,
        CompanionTarget target,
        Isa95Client client,
        CancellationToken cancellationToken)
    {
        ObjectTypeClient proxy;
        ArrayOf<string> properties;
        switch (target.TypeName)
        {
            case "Person":
                proxy = client.CreatePersonClient(target.NodeId);
                properties = [];
                break;
            case "Equipment":
                proxy = client.CreateEquipmentClient(target.NodeId);
                properties = ["EquipmentLevel"];
                break;
            case "Physical asset":
                proxy = client.CreatePhysicalAssetClient(target.NodeId);
                properties = ["FixedAssetId", "PhysicalLocation"];
                break;
            case "Material lot":
                proxy = client.CreateMaterialLotClient(target.NodeId);
                properties = ["Status", "StorageLocation"];
                break;
            default:
                throw IndustrialCompanionAccess.Unsupported("This ISA-95 common object kind is not supported.");
        }
        IndustrialCompanionAccess.CheckFields(context, properties.Count + 1);
        ArrayOf<CompanionValue> values = properties.Count == 0
            ? []
            : await IndustrialCompanionAccess.ReadPropertiesAsync(
                context, proxy.ObjectId, Isa95.Namespaces.ISA95, properties, cancellationToken).ConfigureAwait(false);
        return new CompanionInspection(
            [new("Source NodeId", Variant.From(proxy.ObjectId)), .. values],
            [new("refresh", "Read common instance again", CompanionOperationSafety.ReadOnly)],
            "Typed OPC-10030 identity and standard scalar properties only. Vendor-defined properties and " +
            "personnel/equipment/material authoring are not part of this guided task.");
    }

    private static async ValueTask<Binding?> FindBindingAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        var budget = new IndustrialBrowseBudget(context);
        var parents = new HashSet<NodeId>();
        Binding? found = null;
        ArrayOf<IndustrialCompanionType> facets = IsV2(target) ? s_v2Types : s_v1Types;
        await foreach (ReferenceDescription parent in IndustrialCompanionAccess.BrowseAsync(
            context, target.NodeId, BrowseDirection.Inverse, ReferenceTypeIds.HierarchicalReferences,
            NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
        {
            NodeId parentId = IndustrialCompanionAccess.LocalId(context, parent.NodeId);
            if (parentId.IsNull || !parents.Add(parentId))
            {
                continue;
            }
            if (parents.Count > 16)
            {
                throw IndustrialCompanionAccess.Limit("The Job Control parent search exceeds 16 parents.");
            }
            var endpoints = new Dictionary<string, HashSet<NodeId>>(StringComparer.Ordinal);
            await foreach (ReferenceDescription sibling in IndustrialCompanionAccess.BrowseAsync(
                context, parentId, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                NodeId siblingId = IndustrialCompanionAccess.LocalId(context, sibling.NodeId);
                if (siblingId.IsNull || sibling.NodeClass != NodeClass.Object)
                {
                    continue;
                }
                string? kind = await IndustrialCompanionAccess.ClassifyAsync(
                    context, sibling.TypeDefinition, facets, budget, cancellationToken).ConfigureAwait(false);
                if (kind is not null)
                {
                    if (!endpoints.TryGetValue(kind, out HashSet<NodeId>? ids))
                    {
                        ids = [];
                        endpoints.Add(kind, ids);
                    }
                    ids.Add(siblingId);
                }
            }
            string prefix = IsV2(target) ? "V2 " : "V1 ";
            NodeId order = Unique(endpoints, prefix + "order receiver");
            NodeId provider = Unique(endpoints, prefix + "response provider");
            NodeId receiver = Unique(endpoints, prefix + "response receiver");
            if (order.IsNull || provider.IsNull || receiver.IsNull ||
                (target.NodeId != order && target.NodeId != provider && target.NodeId != receiver))
            {
                continue;
            }
            var candidate = new Binding(order, provider, receiver);
            if (found is not null && found != candidate)
            {
                throw IndustrialCompanionAccess.Unsupported("The Job Control endpoint association is ambiguous.");
            }
            found = candidate;
        }
        return found;
    }

    private static NodeId Unique(Dictionary<string, HashSet<NodeId>> endpoints, string kind)
    {
        if (endpoints.TryGetValue(kind, out HashSet<NodeId>? nodes) && nodes.Count == 1)
        {
            foreach (NodeId node in nodes)
            {
                return node;
            }
        }
        return NodeId.Null;
    }

    private static bool IsJobEndpoint(CompanionTarget target)
    {
        return target.TypeName.StartsWith("V1 ", StringComparison.Ordinal) || IsV2(target);
    }

    private static bool IsV2(CompanionTarget target)
    {
        return target.TypeName.StartsWith("V2 ", StringComparison.Ordinal);
    }

    private static bool IsOrderReceiver(CompanionTarget target)
    {
        return target.TypeName.EndsWith("order receiver", StringComparison.Ordinal);
    }

    private static bool IsResponseProvider(CompanionTarget target)
    {
        return target.TypeName.EndsWith("response provider", StringComparison.Ordinal);
    }

    private static void CheckJobStatus(ulong status)
    {
        if (status != 0)
        {
            throw new ServiceResultException(StatusCodes.BadInvalidState, $"ISA-95 returned job status {status}.");
        }
    }

    private sealed record Binding(NodeId OrderReceiver, NodeId ResponseProvider, NodeId ResponseReceiver);

    private const string kSampleHint =
        "1–32 lowercase ASCII letters, digits or hyphens; ID becomes ualens-sample-<suffix>.";
    private static readonly ArrayOf<IndustrialCompanionType> s_v1Types =
    [
        new(V1.ObjectTypeIds.ISA95JobOrderReceiverObjectType, "V1 order receiver"),
        new(V1.ObjectTypeIds.ISA95JobResponseProviderObjectType, "V1 response provider"),
        new(V1.ObjectTypeIds.ISA95JobResponseReceiverObjectType, "V1 response receiver")
    ];
    private static readonly ArrayOf<IndustrialCompanionType> s_v2Types =
    [
        new(V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType, "V2 order receiver"),
        new(V2.ObjectTypeIds.ISA95JobOrderReceiverSubStatesType, "V2 order receiver"),
        new(V2.ObjectTypeIds.ISA95JobResponseProviderObjectType, "V2 response provider"),
        new(V2.ObjectTypeIds.ISA95JobResponseReceiverObjectType, "V2 response receiver")
    ];
    private static readonly ArrayOf<IndustrialCompanionType> s_types =
    [
        new(Isa95.ObjectTypeIds.PersonType, "Person"),
        new(Isa95.ObjectTypeIds.EquipmentType, "Equipment"),
        new(Isa95.ObjectTypeIds.PhysicalAssetType, "Physical asset"),
        new(Isa95.ObjectTypeIds.MaterialLotType, "Material lot"),
        .. s_v1Types,
        .. s_v2Types
    ];
}
