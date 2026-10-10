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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Mcp.Serialization;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Twenty ISA-95 tools using independent, real endpoint roles and explicit job-control methods.
    /// </summary>
    [McpServerToolType]
    public sealed class Isa95Tools
    {
        /// <summary>
        /// Creates tools without binding or caching a session.
        /// </summary>
        public Isa95Tools(OpcUaSessionManager sessionManager)
        {
            m_sessions = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Discovers common-model objects and vendor subtypes.
        /// </summary>
        [McpServerTool(Name = "isa95_list_common_objects", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Discovers OPC 10030 personnel, equipment, physical-asset and material objects, including " +
            "classes and vendor subtypes. Root defaults to Objects. Returns a live page, not a persistent cursor.")]
        public Task<CallToolResult> ListCommonObjectsAsync(
            [Description("Optional local discovery root NodeId; omit to use the standard root.")]
            string? rootNodeId = null,
            [Description("Traverse descendant objects as well as the supplied root's children.")]
            bool recursive = true,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token => McpCompanionTools.Page(
                await CreateClient(sessionName).DiscoverCommonObjectsAsync(
                    rootNodeId is null ? ObjectIds.ObjectsFolder : Isa95Json.Node(rootNodeId), recursive, token)
                    .ConfigureAwait(false),
                Isa95Json.CommonEntry, offset, maxResults), ct);
        }

        /// <summary>
        /// Reads one common-model object's identity and published property values.
        /// </summary>
        [McpServerTool(Name = "isa95_read_common_object", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Reads a discovered ISA-95 common object's attributes " +
            "and a live page of its variable properties. Includes typed values, status and timestamps; " +
            "only supported common-model types and subtypes are accepted.")]
        public Task<CallToolResult> ReadCommonObjectAsync(
            [Description("Local ISA-95 common-object NodeId returned by discovery.")]
            string objectNodeId,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token => await Isa95Reads.ReadCommonObjectAsync(
                CreateClient(sessionName), Isa95Json.Node(objectNodeId), offset, maxResults, token)
                .ConfigureAwait(false), ct);
        }

        /// <summary>
        /// Discovers V1 and V2 endpoint roles without combining or substituting them.
        /// </summary>
        [McpServerTool(Name = "isa95_discover_job_endpoints", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Discovers actual V1/V2 JobOrderReceiver, JobResponseProvider and JobResponseReceiver objects. " +
            "Returns every role, including vendor subtypes, without choosing ambiguous matches or inventing IDs. " +
            "With an explicit root, inspects its direct children; " +
            "omitted root uses the client's standard folder search.")]
        public Task<CallToolResult> DiscoverJobEndpointsAsync(
            [Description("Optional local discovery root NodeId; omit to use the standard root.")]
            string? rootNodeId = null,
            Isa95JobVersion version = Isa95JobVersion.All,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!Enum.IsDefined(version))
                {
                    throw new ArgumentOutOfRangeException(nameof(version));
                }
                Isa95Client client = CreateClient(sessionName);
                Isa95JobControlDiscovery found = rootNodeId is null
                    ? await client.DiscoverJobControlAsync(token).ConfigureAwait(false)
                    : await client.DiscoverJobControlAsync(Isa95Json.Node(rootNodeId), token).ConfigureAwait(false);
                var entries = new List<Isa95Json.EndpointEntry>();
                if (version is Isa95JobVersion.All or Isa95JobVersion.V1)
                {
                    foreach (Isa95JobControlEndpoint endpoint in found.V1Endpoints)
                    {
                        entries.Add(new Isa95Json.EndpointEntry("V1", endpoint));
                    }
                }
                if (version is Isa95JobVersion.All or Isa95JobVersion.V2)
                {
                    foreach (Isa95JobControlEndpoint endpoint in found.V2Endpoints)
                    {
                        entries.Add(new Isa95Json.EndpointEntry("V2", endpoint));
                    }
                }
                return McpCompanionTools.Page(entries.ToArrayOf(), Isa95Json.Endpoint, offset, maxResults);
            }, ct);
        }

        /// <summary>
        /// Sends an explicit V1 command and typed job order to the actual order receiver.
        /// </summary>
        [McpServerTool(Name = "isa95_v1_receive_order", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: sends a finite V1 Store/StoreAndStart/Start/Update/Stop/Cancel/Clear command " +
            "and typed job order to a JobOrderReceiver. Only that receiver ID is used. " +
            "V1 descriptions cannot carry locales. " +
            "Returns the exact UInt64 model status; only bitmap 1 means success.")]
        public Task<CallToolResult> ReceiveV1OrderAsync(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            Isa95V1Command command,
            [Description("Complete typed job order; omitted optionals remain absent.")]
            Isa95JobOrderInput jobOrder,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!Enum.IsDefined(command))
                {
                    throw new ArgumentOutOfRangeException(nameof(command));
                }
                Isa95Client client = CreateClient(sessionName);
                var receiver = new V1.ISA95JobOrderReceiverObjectTypeClient(
                    client.Session, Isa95Json.Node(receiverNodeId), client.Telemetry);
                return Isa95Json.ReturnStatus(await receiver.ReceiveJobOrderAsync(
                    (V1.ISA95JobOrderCommandEnum)command,
                    Isa95InputMapper.OrderV1(jobOrder, client.Session.MessageContext), token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Queries the actual V1 response provider.
        /// </summary>
        [McpServerTool(Name = "isa95_v1_query_responses", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Queries a V1 JobResponseProvider by job ID and/or the finite V1 state selector. " +
            "Returns a live page of typed responses and the original UInt64 model status, including refusals.")]
        public Task<CallToolResult> QueryV1ResponsesAsync(
            [Description("Actual job-response provider NodeId returned by endpoint discovery.")]
            string providerNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string? jobOrderId = null,
            Isa95V1State state = Isa95V1State.Undefined,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                if (!Enum.IsDefined(state))
                {
                    throw new ArgumentOutOfRangeException(nameof(state));
                }
                Isa95Client client = CreateClient(sessionName);
                var provider = new V1.ISA95JobResponseProviderObjectTypeClient(
                    client.Session, Isa95Json.Node(providerNodeId), client.Telemetry);
                (ArrayOf<V1.ISA95JobResponseDataType> responses, ulong status) = await provider.RequestJobResponseAsync(
                    jobOrderId ?? string.Empty, (V1.ISA95JobOrderStateEnum)state, token).ConfigureAwait(false);
                var result = Isa95Json.ReturnStatus(status);
                result["responses"] = McpCompanionTools.Page(
                    responses, value => McpCompanionJson.Encode(value, client.Session.MessageContext),
                    offset, maxResults);
                return result;
            }, ct);
        }

        /// <summary>
        /// Sends a typed response to the actual V1 response receiver.
        /// </summary>
        [McpServerTool(Name = "isa95_v1_receive_response", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: delivers a complete typed job response to a V1 JobResponseReceiver, " +
            "not an order receiver or response provider. Preserves the raw UInt64 model return bitmap.")]
        public Task<CallToolResult> ReceiveV1ResponseAsync(
            [Description("Actual job-response receiver NodeId; not an order receiver.")]
            string receiverNodeId,
            [Description("Complete typed job response, including its job and response identifiers.")]
            Isa95JobResponseInput response,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Isa95Client client = CreateClient(sessionName);
                var receiver = new V1.ISA95JobResponseReceiverObjectTypeClient(
                    client.Session, Isa95Json.Node(receiverNodeId), client.Telemetry);
                return Isa95Json.ReturnStatus(await receiver.ReceiveJobResponseAsync(
                    Isa95InputMapper.ResponseV1(response, client.Session.MessageContext), token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Stores a V2 job order without starting it.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_store", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Store on the specified JobOrderReceiver with a complete typed order. " +
            "Omitted optional fields stay absent; explicit zero, false and empty values are preserved.")]
        public Task<CallToolResult> StoreV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Complete typed job order; omitted optionals remain absent.")]
            Isa95JobOrderInput jobOrder,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.StoreAsync(
                Isa95InputMapper.OrderV2(jobOrder, receiver.Session.MessageContext),
                Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Stores and starts a V2 job order.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_store_and_start", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 StoreAndStart. May immediately start production. " +
            "Uses only the specified JobOrderReceiver and preserves the UInt64 model status.")]
        public Task<CallToolResult> StoreAndStartV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Complete typed job order; omitted optionals remain absent.")]
            Isa95JobOrderInput jobOrder,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.StoreAndStartAsync(
                Isa95InputMapper.OrderV2(jobOrder, receiver.Session.MessageContext),
                Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Updates a V2 job order.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_update", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Update with a complete typed job order. " +
            "Server permissions and state restrictions apply; refusals remain MCP errors with the original bitmap.")]
        public Task<CallToolResult> UpdateV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Complete typed job order; omitted optionals remain absent.")]
            Isa95JobOrderInput jobOrder,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.UpdateAsync(
                Isa95InputMapper.OrderV2(jobOrder, receiver.Session.MessageContext),
                Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Starts an existing V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_start", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Start for an existing job. May start production; this is not a state read.")]
        public Task<CallToolResult> StartV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.StartAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Stops a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_stop", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Stop for a job on its JobOrderReceiver. Returns the original model status.")]
        public Task<CallToolResult> StopV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.StopAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Pauses a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_pause", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Pause for a job. Unsupported methods and invalid states remain errors.")]
        public Task<CallToolResult> PauseV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.PauseAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Resumes a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_resume", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Resume for a paused job. May resume production.")]
        public Task<CallToolResult> ResumeV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.ResumeAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Aborts a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_abort", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Abort. This can terminate a job; it is not an emergency-stop guarantee.")]
        public Task<CallToolResult> AbortV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.AbortAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Revokes a previously requested V2 start.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_revoke_start", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 RevokeStart on the job's order receiver. Preserves state-related refusals.")]
        public Task<CallToolResult> RevokeStartV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.RevokeStartAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Cancels a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_cancel", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Cancel for a job. Uses the order-receiver role only.")]
        public Task<CallToolResult> CancelV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.CancelAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Clears a V2 job.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_clear", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: invokes V2 Clear. Can remove job state; " +
            "the server decides whether clearing is allowed.")]
        public Task<CallToolResult> ClearV2Async(
            [Description("Actual job-order receiver NodeId returned by endpoint discovery.")]
            string receiverNodeId,
            [Description("Exact job-order identifier, not a NodeId; e.g. job-1.")]
            string jobOrderId,
            [Description("Optional localized command comments; omit to send none.")]
            Isa95CommentInput? comment = null,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ExecuteV2Async(receiverNodeId, sessionName, (receiver, token) => receiver.ClearAsync(
                Isa95InputMapper.Identifier(jobOrderId), Isa95InputMapper.Comment(comment), token), ct);
        }

        /// <summary>
        /// Queries V2 responses by ID or scoped state numbers using the provider role only.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_query_responses", ReadOnly = true, Destructive = false, Idempotent = true)]
        [Description("Queries the actual V2 JobResponseProvider. " +
            "Set exactly one of query.jobOrderId or query.states. State numbers are scoped by browse paths; " +
            "an explicit empty state list requests the server's unfiltered set. " +
            "Returns typed responses and the exact UInt64 model status; service Good alone does not imply acceptance.")]
        public Task<CallToolResult> QueryV2ResponsesAsync(
            [Description("Actual job-response provider NodeId returned by endpoint discovery.")]
            string providerNodeId,
            [Description("Select responses by jobOrderId or state paths, but not both.")]
            Isa95ResponseQueryInput query,
            [Description("Zero-based live result offset, 0..1000000.")]
            int offset = 0,
            [Description("Maximum returned items per live page, 1..500.")]
            int maxResults = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(query);
                if ((query.JobOrderId is null) == query.States.IsNull)
                {
                    throw new ArgumentException("Specify exactly one of jobOrderId or states.", nameof(query));
                }
                Isa95Client client = CreateClient(sessionName);
                var provider = new V2.ISA95JobResponseProviderObjectTypeClient(
                    client.Session, Isa95Json.Node(providerNodeId), client.Telemetry);
                ArrayOf<V2.ISA95JobResponseDataType> responses;
                ulong status;
                if (query.JobOrderId is not null)
                {
                    (V2.ISA95JobResponseDataType response, ulong returnStatus) =
                        await provider.RequestJobResponseByJobOrderIDAsync(
                            Isa95InputMapper.Identifier(query.JobOrderId), token).ConfigureAwait(false);
                    responses = response is null ? [] : [response];
                    status = returnStatus;
                }
                else
                {
                    (responses, status) = await provider.RequestJobResponseByJobOrderStateAsync(
                        Isa95InputMapper.States(query.States), token).ConfigureAwait(false);
                }
                var result = Isa95Json.ReturnStatus(status);
                result["responses"] = McpCompanionTools.Page(
                    responses, value => McpCompanionJson.Encode(value, client.Session.MessageContext),
                    offset, maxResults);
                return result;
            }, ct);
        }

        /// <summary>
        /// Delivers a typed V2 response to the actual response receiver.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_receive_response", ReadOnly = false, Destructive = true)]
        [Description("CONTROL: delivers a complete typed response to a V2 JobResponseReceiver. " +
            "Do not use a Machinery order-receiver ID: Machinery does not define a response receiver. " +
            "The response must explicitly supply its states array.")]
        public Task<CallToolResult> ReceiveV2ResponseAsync(
            [Description("Actual job-response receiver NodeId; not an order receiver.")]
            string receiverNodeId,
            [Description("Complete typed job response, including its job and response identifiers.")]
            Isa95JobResponseInput response,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Isa95Client client = CreateClient(sessionName);
                var receiver = new V2.ISA95JobResponseReceiverObjectTypeClient(
                    client.Session, Isa95Json.Node(receiverNodeId), client.Telemetry);
                return Isa95Json.ReturnStatus(await receiver.ReceiveJobResponseAsync(
                    Isa95InputMapper.ResponseV2(response, client.Session.MessageContext), token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Collects a bounded set of typed V2 status events.
        /// </summary>
        [McpServerTool(Name = "isa95_v2_observe_status", ReadOnly = true, Destructive = false)]
        [Description("Observes V2 JobOrderStatus events from an explicit notifier, or Server by default. " +
            "Includes concrete vendor event subtypes and typed order, response and scoped state data. " +
            "Maximum 30 seconds/500 items; releases the enumerator without disposing the shared subscription.")]
        public Task<CallToolResult> ObserveV2StatusAsync(
            [Description("Local event-notifier NodeId; e.g. i=2253 for the Server object.")]
            string? notifierNodeId = null,
            [Description("Observation window in milliseconds, 1..30000.")]
            int durationMs = 1000,
            [Description("Maximum observed items, 1..500.")]
            int maxItems = 100,
            [Description("Named OPC UA session; omit only when exactly one is connected.")]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Isa95Client client = CreateClient(sessionName);
                NodeId notifier = notifierNodeId is null ? ObjectIds.Server : Isa95Json.Node(notifierNodeId);
                var streaming = McpCompanionTools.GetStreaming(client.Session);
                return await McpCompanionTools.ObserveAsync(
                    inner => Isa95Reads.StatusEventsAsync(client, streaming, notifier, inner),
                    value => Isa95Json.StatusEvent(value, client.Session.MessageContext),
                    durationMs, maxItems, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Runs one explicit V2 receiver operation, with no other role identifiers.
        /// </summary>
        private Task<CallToolResult> ExecuteV2Async(
            string receiverNodeId,
            string? sessionName,
            Func<V2.ISA95JobOrderReceiverObjectTypeClient, CancellationToken, ValueTask<ulong>> operation,
            CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                Isa95Client client = CreateClient(sessionName);
                var receiver = new V2.ISA95JobOrderReceiverObjectTypeClient(
                    client.Session, Isa95Json.Node(receiverNodeId), client.Telemetry);
                return Isa95Json.ReturnStatus(await operation(receiver, token).ConfigureAwait(false));
            }, ct);
        }

        /// <summary>
        /// Registers model encodings on the current selected session and creates a lightweight client.
        /// </summary>
        private Isa95Client CreateClient(string? sessionName)
        {
            return new Isa95Client(m_sessions.GetSessionOrThrow(sessionName), m_sessions.Telemetry);
        }

        private readonly OpcUaSessionManager m_sessions;
    }
}
