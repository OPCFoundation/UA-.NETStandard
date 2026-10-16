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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Bounded workflows over a machine's actual modern Jobs AddIn and standard remote file providers.
    /// </summary>
    [McpServerToolType]
    public sealed partial class MachineToolServiceTools
    {
        /// <summary>
        /// Inspects the native job catalog or explicitly published responses without issuing a job command.
        /// </summary>
        [McpServerTool(Name = "machinetool_query_jobs", ReadOnly = true, Destructive = false,
            Idempotent = true, OpenWorld = true)]
        [Description("Queries the machine's Machinery Jobs AddIn. Returns a native JobOrderList DataValue or " +
            "ISA95 response(s) with ReturnStatus. Preserves masks, quality and timestamps. Missing services fail " +
            "BadNotSupported; overflow fails without truncation. No job commands.")]
        public static Task<CallToolResult> QueryJobsAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId with modern jobs configured.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Orders lists jobs; other queries select explicit responses.")]
            [StringLength(32, MinimumLength = 1)] string query = "Orders",
            [Description("Required only for ResponseById; otherwise omit.")]
            [StringLength(1024, MinimumLength = 1)] string? jobOrderId = null,
            [Description("ResponsesByState only: native ISA95StateDataType array, including BrowsePath, StateText " +
                "and UInt32 StateNumber. Max 64 KiB; copy the full state path.")]
            JsonElement? states = null,
            [Description("Maximum complete order/response count; overflow fails.")]
            [Range(1, 128)] int maximumResults = 32,
            [Description("Whole UTF-8 JSON response cap; overflow fails.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected session name; omit only for a single session.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolServiceParameters.Query(query, jobOrderId, states.GetValueOrDefault());
                MachineToolParameters.Bound(maximumResults, 1, 128);
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                ArrayOf<V2.ISA95StateDataType> state = query == "ResponsesByState"
                    ? MachineToolServiceParameters.DecodeStates(
                        states.GetValueOrDefault(), client.Session.MessageContext) : [];
                JobManagementTypeClient management = await ManagementAsync(client, node, ct).ConfigureAwait(false);
                if (query == "Orders")
                {
                    V2.ISA95JobOrderReceiverObjectTypeClient receiver = await ReceiverAsync(client, management, ct)
                        .ConfigureAwait(false);
                    DataValue value = await OrdersAsync(client, receiver.ObjectId, ct).ConfigureAwait(false);
                    if (!value.WrappedValue.TryGetStructure(out ArrayOf<V2.ISA95JobOrderAndStateDataType> orders))
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "JobOrderList did not contain an ISA95JobOrderAndStateDataType array.");
                    }
                    Count(orders.Count, maximumResults);
                    return MachineToolJson.Result(new JsonObject
                    {
                        ["query"] = query,
                        ["count"] = orders.Count,
                        ["jobOrders"] = MachineToolServiceJson.Reading(client.Session.MessageContext, value)
                    }, maximumResponseBytes);
                }
                V2.ISA95JobResponseProviderObjectTypeClient provider = await management
                    .GetJobOrderResultsAsync(client.Telemetry, ct).ConfigureAwait(false) ??
                    throw Unsupported("JobOrderResults");
                if (query == "ResponseById")
                {
                    (V2.ISA95JobResponseDataType response, ulong status) = await provider
                        .RequestJobResponseByJobOrderIDAsync(jobOrderId!, ct).ConfigureAwait(false);
                    JsonObject result = MachineToolServiceJson.JobStatus(query, status);
                    result["response"] = MachineToolServiceJson.Encode(response, client.Session.MessageContext);
                    return MachineToolJson.Result(
                        result, maximumResponseBytes, !MachineToolServiceJson.Accepted(status));
                }
                (ArrayOf<V2.ISA95JobResponseDataType> responses, ulong returned) = await provider
                    .RequestJobResponseByJobOrderStateAsync(state, ct).ConfigureAwait(false);
                Count(responses.Count, maximumResults);
                JsonObject payload = MachineToolServiceJson.JobStatus(query, returned);
                payload["responses"] = MachineToolJson.Array(responses,
                    response => MachineToolServiceJson.Encode(response, client.Session.MessageContext));
                return MachineToolJson.Result(
                    payload, maximumResponseBytes, !MachineToolServiceJson.Accepted(returned));
            }, ct);
        }

        /// <summary>
        /// Requests exactly one of the eleven standard ISA-95 verbs after complete validation and explicit consent.
        /// </summary>
        [McpServerTool(Name = "machinetool_control_job", ReadOnly = false, Destructive = true,
            Idempotent = false, OpenWorld = true)]
        [Description("Controls the machine's ISA95 V2 job receiver. Returns ReturnStatus (UInt64 bitmap, " +
            "success bit=1) and accepted, not proof of physical execution. Requires confirm=true and server " +
            "authorization. Cancellation/timeout may leave outcome unknown; no retry or abort.")]
        public static Task<CallToolResult> ControlJobAsync(
            MachineToolClientAccessor accessor,
            [Description("MachineTool instance NodeId with the Machinery Jobs AddIn.")]
            [StringLength(1024, MinimumLength = 1)] string machineNodeId,
            [Description("Standard ISA95 V2 verb; use jobOrder for downloads, otherwise jobOrderId.")]
            [StringLength(32, MinimumLength = 1)] string operation,
            [Description("ID-based verbs only; omit for Store/StoreAndStart/Update.")]
            [StringLength(1024, MinimumLength = 1)] string? jobOrderId = null,
            [Description("Store/StoreAndStart/Update: native ISA95JobOrderDataType with masks, WorkMasterID " +
                "and parameter/resource arrays. Limits: 65536 UTF-8 bytes, 1024 per array, depth 32, 4096 values.")]
            JsonElement? jobOrder = null,
            [Description("Native LocalizedText comments: Text<=1024, Locale<=64 characters; total<=64 KiB.")]
            JsonElement? comments = null,
            [Description("Explicit consent; never bypasses server authorization.")]
            bool confirm = false,
            [Description("Whole UTF-8 receipt cap, checked before the command.")]
            [Range(1024, 1048576)] int maximumResponseBytes = 131072,
            [Description("Connected session whose identity the server authorizes.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return MachineToolJson.ExecuteAsync(async () =>
            {
                NodeId node = MachineToolParameters.Node(machineNodeId);
                MachineToolServiceParameters.Job(
                    operation, jobOrderId, jobOrder.GetValueOrDefault(), comments.GetValueOrDefault());
                MachineToolParameters.ResponseLimit(maximumResponseBytes);
                MachineToolParameters.Consent(confirm);
                _ = MachineToolJson.Result(
                    MachineToolServiceJson.JobStatus(operation, ulong.MaxValue - 1), maximumResponseBytes);
                ArgumentNullException.ThrowIfNull(accessor);
                MachineToolClient client = accessor.CreateClient(sessionName);
                await using var lifetime = client.ConfigureAwait(false);
                V2.ISA95JobOrderDataType? order = MachineToolServiceParameters.Present(jobOrder.GetValueOrDefault())
                    ? MachineToolServiceParameters.DecodeOrder(
                        jobOrder.GetValueOrDefault(), client.Session.MessageContext) : null;
                ArrayOf<LocalizedText> comment = MachineToolServiceParameters.DecodeComments(
                    comments.GetValueOrDefault(), client.Session.MessageContext);
                JobManagementTypeClient management = await ManagementAsync(client, node, ct).ConfigureAwait(false);
                V2.ISA95JobOrderReceiverObjectTypeClient receiver = await ReceiverAsync(client, management, ct)
                    .ConfigureAwait(false);
                ulong status = operation switch
                {
                    "Store" => await receiver.StoreAsync(order!, comment, ct).ConfigureAwait(false),
                    "StoreAndStart" => await receiver.StoreAndStartAsync(order!, comment, ct).ConfigureAwait(false),
                    "Update" => await receiver.UpdateAsync(order!, comment, ct).ConfigureAwait(false),
                    "Start" => await receiver.StartAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Stop" => await receiver.StopAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Abort" => await receiver.AbortAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Pause" => await receiver.PauseAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Resume" => await receiver.ResumeAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Clear" => await receiver.ClearAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    "Cancel" => await receiver.CancelAsync(jobOrderId!, comment, ct).ConfigureAwait(false),
                    _ => await receiver.RevokeStartAsync(jobOrderId!, comment, ct).ConfigureAwait(false)
                };
                return MachineToolJson.Result(MachineToolServiceJson.JobStatus(operation, status),
                    maximumResponseBytes, !MachineToolServiceJson.Accepted(status));
            }, ct);
        }

        /// <summary>
        /// Requires the actual namespace-qualified MachineryBuildingBlocks AddIn.
        /// </summary>
        private static async ValueTask<JobManagementTypeClient> ManagementAsync(
            MachineToolClient client, NodeId node, CancellationToken ct)
        {
            return await client.GetJobManagementAsync(node, ct).ConfigureAwait(false) ??
                throw Unsupported("MachineryBuildingBlocks/JobManagement");
        }

        /// <summary>
        /// Requires the declared receiver proxy without constructing a fictional response receiver.
        /// </summary>
        private static async ValueTask<V2.ISA95JobOrderReceiverObjectTypeClient> ReceiverAsync(
            MachineToolClient client, JobManagementTypeClient management, CancellationToken ct)
        {
            return await management.GetJobOrderControlAsync(client.Telemetry, ct).ConfigureAwait(false) ??
                throw Unsupported("JobOrderControl");
        }

        /// <summary>
        /// Reads the receiver's standard variable; generated object proxies expose its verbs, not scalar variables.
        /// </summary>
        private static async ValueTask<DataValue> OrdersAsync(
            MachineToolClient client, NodeId receiver, CancellationToken ct)
        {
            int ns = client.Session.NamespaceUris.GetIndex(V2.Namespaces.ISA95JobControlV2);
            if (ns < 0)
            {
                throw Unsupported("ISA95 Job Control V2");
            }
            ArrayOf<BrowsePath> paths =
            [
                new BrowsePath
                {
                    StartingNode = receiver,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(V2.BrowseNames.JobOrderList, (ushort)ns)
                            }
                        ]
                    }
                }
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await client.Session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct).ConfigureAwait(false);
            ClientBase.ValidateResponse(response.Results, paths);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, paths);
            BrowsePathResult path = response.Results[0];
            if (path.StatusCode == StatusCodes.BadNoMatch || path.StatusCode == StatusCodes.BadNotFound)
            {
                throw Unsupported("JobOrderList");
            }
            if (StatusCode.IsBad(path.StatusCode))
            {
                throw new ServiceResultException(path.StatusCode);
            }
            if (path.Targets.Count != 1 || path.Targets[0].RemainingPathIndex != uint.MaxValue ||
                path.Targets[0].TargetId.ServerIndex != 0)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                    "JobOrderList must resolve to one complete local target.");
            }
            NodeId list = ExpandedNodeId.ToNodeId(path.Targets[0].TargetId, client.Session.NamespaceUris);
            if (list.IsNull)
            {
                throw Unsupported("JobOrderList");
            }
            DataValue value = await client.Session.ReadValueAsync(list, ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
            return value;
        }

        /// <summary>
        /// Refuses overlarge inventories without discarding a hidden tail.
        /// </summary>
        private static void Count(int count, int maximum)
        {
            if (count > maximum)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The complete inventory exceeds the requested record bound.");
            }
        }

        /// <summary>
        /// Represents an absent optional service explicitly rather than returning an empty Good result.
        /// </summary>
        private static ServiceResultException Unsupported(string feature)
        {
            return new ServiceResultException(StatusCodes.BadNotSupported,
                $"The selected machine does not expose {feature}.");
        }
    }
}
