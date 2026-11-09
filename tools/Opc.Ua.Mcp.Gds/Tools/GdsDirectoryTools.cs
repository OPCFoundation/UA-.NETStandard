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
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Mcp.Serialization;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit application-directory workflows using the current named, encrypted UA session.
    /// </summary>
    [McpServerToolType]
    public sealed class GdsDirectoryTools
    {
        /// <summary>
        /// Creates tools over borrowed host services; construction performs no UA operation.
        /// </summary>
        public GdsDirectoryTools(
            OpcUaSessionManager sessions, McpFileTransfers transfers, McpSecretReferences secrets,
            GdsMcpOptions options, TimeProvider? timeProvider = null)
        {
            m_access = new GdsAccess(sessions, transfers, secrets, options, timeProvider ?? TimeProvider.System);
        }

        /// <summary>
        /// Discovers typed directories without relying on conventional instance names.
        /// </summary>
        [McpServerTool(Name = "gds_list_directories", ReadOnly = true, Destructive = false)]
        [Description("Discover typed GDS directories; returns a live page with actual node/type ids. " +
            "Example: rootNodeId='i=85', maxResults=20.")]
        public Task<CallToolResult> ListDirectoriesAsync(
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
                    sessionName, McpCapability.DirectoryAdministration, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable contextScope = context.ConfigureAwait(false);
                ISession session = context.Session;
                return await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(session, GdsAccess.Node(rootNodeId),
                        Gds.ObjectTypeIds.DirectoryType, ct: token),
                    reference => McpModelAccess.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Lists the actual directory's methods and children, preserving optional facets.
        /// </summary>
        [McpServerTool(Name = "gds_inspect_directory", ReadOnly = true, Destructive = false)]
        [Description("Inspect a directory's actual methods/children, including vendor subtypes. " +
            "Returns a bounded live page; does not infer support from a standard NodeId.")]
        public Task<CallToolResult> InspectDirectoryAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
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
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                JsonObject result = await McpCompanionTools.PageAsync(
                    endpoint.Session.EnumerateReferencesAsync(endpoint.Proxy.ObjectId,
                        ReferenceTypeIds.HierarchicalReferences, ct: token),
                    reference => McpModelAccess.Reference(endpoint.Session, reference),
                    offset, maxResults, token).ConfigureAwait(false);
                result["resource"] = endpoint.Resource;
                result["nodeId"] = endpoint.Proxy.ObjectId.ToString();
                result["sessionName"] = endpoint.Info.Name;
                return result;
            }, ct);
        }

        /// <summary>
        /// Queries application descriptions while preserving the server's cursor and reset epoch.
        /// </summary>
        [McpServerTool(Name = "gds_query_applications", ReadOnly = true, Destructive = false)]
        [Description("Query applications; returns typed records, nextRecordId and lastCounterResetTime. " +
            "Example: input={startingRecordId:0,maxRecords:20}; retain the returned reset epoch.")]
        public Task<CallToolResult> QueryApplicationsAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description("Application filters and server record cursor; maxRecords is 1..500. " +
                "E.g. {\"startingRecordId\":0,\"maxRecords\":20}. " +
                "Reuse nextRecordId only within the same reset epoch.")]
            GdsQueryInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                Validate(input);
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.DirectoryType_QueryApplications,
                    [new Variant(input.StartingRecordId), new Variant(input.MaxRecords),
                        new Variant(input.ApplicationName), new Variant(input.ApplicationUri),
                        new Variant(input.ApplicationType), new Variant(input.ProductUri),
                        new Variant(input.ServerCapabilities)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["lastCounterResetTime"] = (DateTime)GdsJson.Time(call, 0);
                    result["nextRecordId"] = GdsJson.UInt32(call, 1);
                    ArrayOf<ApplicationDescription> applications = GdsJson.Structures<ApplicationDescription>(
                        call, 2, endpoint.Context);
                    result["applications"] = McpCompanionTools.Page(applications,
                        app => McpCompanionJson.Encode(app, endpoint.Context),
                        maxResults: checked((int)input.MaxRecords));
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Queries ServerOnNetwork records without synthesizing a server-side continuation token.
        /// </summary>
        [McpServerTool(Name = "gds_query_servers", ReadOnly = true, Destructive = false)]
        [Description("Query server records; returns real RecordIds and lastCounterResetTime. " +
            "Use the last returned RecordId plus one for the next request, checking reset epochs.")]
        public Task<CallToolResult> QueryServersAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description("Server filters and record cursor; maxRecords is 1..500. " +
                "E.g. {\"startingRecordId\":0,\"maxRecords\":20}. applicationType is not sent by QueryServers; " +
                "continue with the last RecordId plus one only within the same reset epoch.")]
            GdsQueryInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                Validate(input);
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(GdsMethods.DirectoryType_QueryServers,
                    [new Variant(input.StartingRecordId), new Variant(input.MaxRecords),
                        new Variant(input.ApplicationName), new Variant(input.ApplicationUri),
                        new Variant(input.ProductUri), new Variant(input.ServerCapabilities)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["lastCounterResetTime"] = (DateTime)GdsJson.Time(call, 0);
                    result["servers"] = McpCompanionTools.Page(
                        GdsJson.Structures<ServerOnNetwork>(call, 1, endpoint.Context),
                        server => McpCompanionJson.Encode(server, endpoint.Context),
                        maxResults: checked((int)input.MaxRecords));
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Finds records for one exact application URI.
        /// </summary>
        [McpServerTool(Name = "gds_find_applications", ReadOnly = true, Destructive = false)]
        [Description("Find application registrations for one URI; returns a bounded live page of full records. " +
            "Example: applicationUri='urn:plant:client'.")]
        public Task<CallToolResult> FindApplicationsAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description("Exact registered application URI to find, e.g. 'urn:plant:client'; not a discovery URL.")]
            string applicationUri,
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
                GdsAccess.Text(applicationUri);
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.DirectoryType_FindApplications, [new Variant(applicationUri)], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["applications"] = McpCompanionTools.Page(
                        GdsJson.Structures<ApplicationRecordDataType>(call, 0, endpoint.Context),
                        app => McpCompanionJson.Encode(app, endpoint.Context), offset, maxResults);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads a specific application record.
        /// </summary>
        [McpServerTool(Name = "gds_get_application", ReadOnly = true, Destructive = false)]
        [Description("Get one registered application by its server-assigned applicationId. " +
            "Returns the full typed record and actual method status.")]
        public Task<CallToolResult> GetApplicationAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId id = GdsAccess.Node(applicationId);
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.DirectoryType_GetApplication, [new Variant(id)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    if (!GdsJson.Output(call, 0).TryGetStructure<ApplicationRecordDataType>(
                        endpoint.Context, out ApplicationRecordDataType? application))
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    }
                    result["application"] = McpCompanionJson.Encode(application, endpoint.Context);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Registers exactly one application without subsequent certificate issuance.
        /// </summary>
        [McpServerTool(Name = "gds_register_application", ReadOnly = false, Destructive = true)]
        [Description("Register one application explicitly; returns the server-assigned applicationId and status. " +
            "Does not issue credentials or approve certificate requests.")]
        public Task<CallToolResult> RegisterApplicationAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description("New registration fields: applicationUri and productUri are required; omit applicationId. " +
                "Supply localized applicationNames as the separate root array, not inside input. " +
                "Discovery URLs are stored without connecting.")]
            GdsApplicationInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ChangeApplicationAsync(directoryNodeId, input, update: false, sessionName, ct);
        }

        /// <summary>
        /// Replaces an existing application record without inventing optimistic concurrency.
        /// </summary>
        [McpServerTool(Name = "gds_update_application", ReadOnly = false, Destructive = true)]
        [Description("Replace one registration using input.applicationId and the complete record. " +
            "Returns actual status; omitted arrays become empty, not unchanged.")]
        public Task<CallToolResult> UpdateApplicationAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description("Complete replacement record including applicationId NodeId, applicationUri and productUri. " +
                "Supply applicationNames as the separate root array. Omitted discoveryUrls/serverCapabilities " +
                "become empty; this is not a partial update.")]
            GdsApplicationInput input,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return ChangeApplicationAsync(directoryNodeId, input, update: true, sessionName, ct);
        }

        /// <summary>
        /// Unregisters one application without any automatic follow-up operation.
        /// </summary>
        [McpServerTool(Name = "gds_unregister_application", ReadOnly = false, Destructive = true)]
        [Description("Unregister one application explicitly. Returns the real status and applicationId; " +
            "server authorization and related credential effects remain server decisions.")]
        public Task<CallToolResult> UnregisterApplicationAsync(
            [Description(GdsToolDescriptions.DirectoryNodeId)]
            string directoryNodeId,
            [Description(GdsToolDescriptions.ApplicationId)]
            string applicationId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                NodeId id = GdsAccess.Node(applicationId);
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(
                    GdsMethods.DirectoryType_UnregisterApplication, [new Variant(id)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                result["applicationId"] = id.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Shares typed record mapping between explicit registration and update tools.
        /// </summary>
        private Task<CallToolResult> ChangeApplicationAsync(
            string directoryNodeId, GdsApplicationInput input, bool update, string? sessionName, CancellationToken ct)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                if (!Enum.IsDefined(input.ApplicationType) ||
                    input.ApplicationNames.IsNull ||
                    input.ApplicationNames.Count is < 1 or > 100 ||
                    (!update && input.ApplicationId is not null))
                {
                    throw new ArgumentException("Application kind, names or registration id is invalid.");
                }
                var application = new ApplicationRecordDataType
                {
                    ApplicationId = update ? GdsAccess.Node(input.ApplicationId!) : NodeId.Null,
                    ApplicationUri = GdsAccess.Text(input.ApplicationUri),
                    ApplicationType = input.ApplicationType,
                    ProductUri = GdsAccess.Text(input.ProductUri),
                    ApplicationNames = input.ApplicationNames.ConvertAll(name =>
                    {
                        ArgumentNullException.ThrowIfNull(name);
                        if (name.Locale is not null)
                        {
                            GdsAccess.Text(name.Locale, allowEmpty: true);
                        }
                        return new LocalizedText(name.Locale, GdsAccess.Text(name.Text));
                    }),
                    DiscoveryUrls = GdsAccess.Strings(input.DiscoveryUrls),
                    ServerCapabilities = GdsAccess.Strings(input.ServerCapabilities)
                };
                GdsEndpoint endpoint = await EndpointAsync(directoryNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult call = await endpoint.CallAsync(update
                    ? GdsMethods.DirectoryType_UpdateApplication : GdsMethods.DirectoryType_RegisterApplication,
                    [Variant.FromStructure(application)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(call);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["applicationId"] = (update ? application.ApplicationId : GdsJson.Node(call, 0)).ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Resolves a DirectoryType or its advertised subtype.
        /// </summary>
        private ValueTask<GdsEndpoint> EndpointAsync(string node, string? name, CancellationToken ct)
        {
            return m_access.EndpointAsync(node, GdsTarget.Directory, name, ct);
        }

        /// <summary>
        /// Validates wire-level bounds before the query can allocate remote results.
        /// </summary>
        private static void Validate(GdsQueryInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            GdsAccess.Page(0, checked((int)input.MaxRecords));
            GdsAccess.Text(input.ApplicationName, allowEmpty: true);
            GdsAccess.Text(input.ApplicationUri, allowEmpty: true);
            GdsAccess.Text(input.ProductUri, allowEmpty: true);
            GdsAccess.Strings(input.ServerCapabilities);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(input.ApplicationType, 3u);
        }

        private readonly GdsAccess m_access;
    }
}
