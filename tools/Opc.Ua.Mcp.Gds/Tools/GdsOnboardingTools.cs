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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using OnboardingMethods = Opc.Ua.Onboarding.MethodIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Part 21 registrar administration with encoded tickets accepted only through protected references.
    /// </summary>
    [McpServerToolType]
    public sealed class GdsOnboardingTools
    {
        /// <summary>
        /// Creates tools without connecting or registering any device.
        /// </summary>
        public GdsOnboardingTools(
            OpcUaSessionManager sessions, McpFileTransfers transfers, McpSecretReferences secrets,
            GdsMcpOptions options, TimeProvider? timeProvider = null)
        {
            m_access = new GdsAccess(sessions, transfers, secrets, options, timeProvider ?? TimeProvider.System);
        }

        /// <summary>
        /// Discovers actual registrar administration objects and subtypes.
        /// </summary>
        [McpServerTool(Name = "onboarding_list_registrars", ReadOnly = true, Destructive = false)]
        [Description("Discover Part 21 registrar administration objects by type. Returns a bounded live page " +
            "with qualified identities; example: rootNodeId='i=85', maxResults=20.")]
        public Task<CallToolResult> ListRegistrarsAsync(
            [Description(GdsToolDescriptions.RootNodeId)]
            string rootNodeId = "i=85",
            [Description(GdsToolDescriptions.Offset)]
            int offset = 0,
            [Description(GdsToolDescriptions.MaxResults)]
            int maxResults = 100,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Page(offset, maxResults);
                SessionRequestContext context = await m_access.CaptureAsync(
                    sessionName, McpCapability.Onboarding, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable contextScope = context.ConfigureAwait(false);
                ISession session = context.Session;
                return await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(session, GdsAccess.Node(rootNodeId),
                        GdsAccess.Type(GdsTarget.Registrar), maxDepth: 4, ct: token),
                    reference => McpModelAccess.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Returns advertised authority/method nodes and the exact resource binding for protected aliases.
        /// </summary>
        [McpServerTool(Name = "onboarding_inspect_registrar", ReadOnly = true, Destructive = false)]
        [Description("Inspect a typed registrar's advertised children and exact ticket-reference resource binding. " +
            "Returns public node metadata, never encoded tickets or device credentials.")]
        public Task<CallToolResult> InspectRegistrarAsync(
            [Description(GdsToolDescriptions.RegistrarNodeId)]
            string registrarNodeId,
            [Description(GdsToolDescriptions.Offset)]
            int offset = 0,
            [Description(GdsToolDescriptions.MaxResults)]
            int maxResults = 100,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Page(offset, maxResults);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    registrarNodeId, GdsTarget.Registrar, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                JsonObject result = await McpCompanionTools.PageAsync(
                    endpoint.Session.EnumerateReferencesAsync(endpoint.Proxy.ObjectId,
                        ReferenceTypeIds.HierarchicalReferences, ct: token),
                    reference => McpModelAccess.Reference(endpoint.Session, reference),
                    offset, maxResults, token).ConfigureAwait(false);
                result["resource"] = endpoint.Resource;
                result["sessionName"] = endpoint.Info.Name;
                result["sessionId"] = endpoint.SessionId.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Registers a bounded batch of protected encoded tickets.
        /// </summary>
        [McpServerTool(Name = "onboarding_register_tickets", ReadOnly = false, Destructive = true)]
        [Description("Register 1..100 encoded tickets via authorized references. Returns every per-ticket status " +
            "in input order; example: input={ticketReferences:['alias:ticket-a']}. No automatic onboarding chain.")]
        public Task<CallToolResult> RegisterTicketsAsync(
            [Description(GdsToolDescriptions.RegistrarNodeId)]
            string registrarNodeId,
            [Description("Protected ticket references to register, bound to the resource from registrar inspection; " +
                "e.g. {\"ticketReferences\":[\"alias:ticket-a\"]}. " +
                "Supply 1..100 references, never ticket bytes or paths. " +
                "Per-ticket result indexes follow this input order.")]
            GdsTicketInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return TicketsAsync(registrarNodeId, input, remove: false, sessionName, ct);
        }

        /// <summary>
        /// Removes the explicitly selected encoded tickets.
        /// </summary>
        [McpServerTool(Name = "onboarding_remove_tickets", ReadOnly = false, Destructive = true)]
        [Description("Remove exactly the referenced Part 21 tickets. Returns ordered per-ticket status codes " +
            "including partial failures; does not revoke unrelated credentials or disconnect devices.")]
        public Task<CallToolResult> RemoveTicketsAsync(
            [Description(GdsToolDescriptions.RegistrarNodeId)]
            string registrarNodeId,
            [Description("Protected references for the exact encoded tickets to remove, bound to this registrar; " +
                "e.g. {\"ticketReferences\":[\"alias:ticket-a\"]}. " +
                "Supply 1..100 references, never ticket bytes or paths. " +
                "Per-ticket result indexes follow this input order.")]
            GdsTicketInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return TicketsAsync(registrarNodeId, input, remove: true, sessionName, ct);
        }

        /// <summary>
        /// Materializes bounded ticket views only for one typed wire call and clears owned copies afterwards.
        /// </summary>
        private Task<CallToolResult> TicketsAsync(
            string registrarNodeId, GdsTicketInput input, bool remove, string? sessionName, CancellationToken ct)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                GdsAccess.Strings(input.TicketReferences);
                ArgumentOutOfRangeException.ThrowIfLessThan(input.TicketReferences.Count, 1);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    registrarNodeId, GdsTarget.Registrar, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                var buffers = new List<byte[]>(input.TicketReferences.Count);
                try
                {
                    int total = 0;
                    for (int index = 0; index < input.TicketReferences.Count; index++)
                    {
                        using ISecret ticket = await m_access.Secrets.ResolveAsync(
                            input.TicketReferences[index], endpoint.Resource, McpCapability.Onboarding, token)
                            .ConfigureAwait(false);
                        total = checked(total + ticket.Bytes.Length);
                        if (total > m_access.Options.MaxArtifactBytes)
                        {
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                        }
                        buffers.Add(ticket.Bytes.ToArray());
                    }
                    ArrayOf<ByteString> tickets = [.. buffers.ConvertAll(ByteString.From)];
                    CallMethodResult call = await endpoint.CallAsync(remove
                        ? OnboardingMethods.DeviceRegistrarAdminType_UnregisterTickets
                        : OnboardingMethods.DeviceRegistrarAdminType_RegisterTickets,
                        [new Variant(tickets)], token).ConfigureAwait(false);
                    JsonObject result = endpoint.Result(call);
                    result["resource"] = endpoint.Resource;
                    if (!StatusCode.IsBad(call.StatusCode))
                    {
                        ArrayOf<StatusCode> statuses = GdsJson.Statuses(call, 0);
                        if (statuses.Count != tickets.Count)
                        {
                            result["error"] = true;
                            result["outcome"] = "invalidPerTicketResultCount";
                            result["expectedResults"] = tickets.Count;
                        }
                        var items = new JsonArray();
                        for (int index = 0; index < Math.Min(statuses.Count, 100); index++)
                        {
                            StatusCode status = statuses[index];
                            JsonNode item = new JsonObject
                            {
                                ["index"] = index,
                                ["statusCode"] = status.ToString(),
                                ["statusCodeValue"] = status.Code
                            };
                            items.Add(item);
                            if (StatusCode.IsBad(status))
                            {
                                result["error"] = true;
                            }
                        }
                        result["results"] = items;
                        result["returnedResultCount"] = statuses.Count;
                    }
                    return result;
                }
                finally
                {
                    foreach (byte[] buffer in buffers)
                    {
                        CryptographicOperations.ZeroMemory(buffer);
                    }
                }
            }, ct);
        }

        private readonly GdsAccess m_access;
    }
}
