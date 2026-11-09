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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using WotMethods = Opc.Ua.WotCon.MethodIds;
using WotTypes = Opc.Ua.WotCon.ObjectTypeIds;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Explicit WoT asset and registry operations, separate from document conversion and generic registry tools.
    /// </summary>
    [McpServerToolType]
    public sealed class WotTools
    {
        /// <summary>
        /// Initializes tools over the host-owned session and file-transfer modules.
        /// </summary>
        public WotTools(OpcUaSessionManager sessions, McpFileTransfers transfers, OpcUaMcpOptions options)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Inspects legacy and registry support independently.
        /// </summary>
        [McpServerTool(Name = "wot_inspect_server", ReadOnly = true)]
        [Description("Discovers independent legacy WoT asset-management and WoT Registry entry points. " +
            "Returns actual roots and typed supported-binding/refresh properties; " +
            "a server may implement either or both. " +
            "Does not connect to discovered endpoints or enable protocol bindings.")]
        public Task<CallToolResult> InspectServerAsync(
            [Description("Optional explicitly located legacy management root.")] string? managementNodeId = null,
            [Description("Optional explicitly located WoT Registry root.")] string? registryNodeId = null,
            [Description("Named session; omit only for one connected session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = m_sessions.GetSessionOrThrow(sessionName);
                NodeId management = await WotAccess.RootAsync(session, false, managementNodeId, token)
                    .ConfigureAwait(false);
                NodeId registry = await WotAccess.RootAsync(session, true, registryNodeId, token).ConfigureAwait(false);
                JsonObject legacy = management.IsNull
                    ? new JsonObject()
                    : await WotAccess.PropertiesAsync(session, management, ["SupportedWoTBindings"], token)
                        .ConfigureAwait(false);
                JsonObject registryInfo = registry.IsNull
                    ? new JsonObject()
                    : await WotAccess.PropertiesAsync(session, registry,
                        ["AutoRefresh", "RefreshMode", "RefreshInterval", "RefreshGeneration", "LastRefreshTime",
                            "LastRefreshSummary", "DefaultAtomicity", "DeletePolicy", "ValidateFormat",
                            "ValidateCompatibility", "StrictValidation", "VocabularyVersion", "SelectedBindings"],
                        token)
                        .ConfigureAwait(false);
                if (IsInitialRefreshPending(registryInfo))
                {
                    registryInfo["error"] = false;
                }
                legacy["supported"] = !management.IsNull;
                registryInfo["supported"] = !registry.IsNull;
                return new JsonObject
                {
                    ["error"] = McpModelAccess.HasError(legacy) || McpModelAccess.HasError(registryInfo),
                    ["namespaceUri"] = WotCon.Namespaces.WotCon,
                    ["legacy"] = legacy,
                    ["registry"] = registryInfo
                };
            }, ct);
        }

        /// <summary>
        /// Identifies the expected initial refresh statuses without masking other property read failures.
        /// </summary>
        private static bool IsInitialRefreshPending(JsonObject registryInfo)
        {
            if (registryInfo["error"] is not JsonValue errorFlag ||
                !errorFlag.TryGetValue(out bool hasError) ||
                !hasError ||
                registryInfo["properties"] is not JsonObject properties)
            {
                return false;
            }

            foreach (string name in new[] { "LastRefreshTime", "LastRefreshSummary" })
            {
                if (properties[name] is not JsonObject details ||
                    details["supported"] is not JsonValue supportedFlag ||
                    !supportedFlag.TryGetValue(out bool supported) ||
                    !supported ||
                    details["dataValue"] is not JsonObject dataValue ||
                    dataValue["Status"] is not JsonObject status ||
                    status["Code"] is not JsonValue codeValue ||
                    !codeValue.TryGetValue(out uint code) ||
                    code != StatusCodes.BadWaitingForInitialData)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in properties)
            {
                if (property.Key is "LastRefreshTime" or "LastRefreshSummary" ||
                    property.Value is not JsonObject details ||
                    details["supported"] is not JsonValue supportedFlag ||
                    !supportedFlag.TryGetValue(out bool supported) ||
                    !supported)
                {
                    continue;
                }
                if (details["dataValue"] is not JsonObject dataValue)
                {
                    return false;
                }
                if (dataValue["Status"] is not JsonObject status)
                {
                    continue;
                }
                if (status["Code"] is not JsonValue codeValue || !codeValue.TryGetValue(out uint code))
                {
                    return false;
                }
                if (StatusCode.IsBad(new StatusCode(code)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Lists the legacy management object's registered assets.
        /// </summary>
        [McpServerTool(Name = "wot_list_assets", ReadOnly = true)]
        [Description("Lists registered legacy WoT assets, following server browse continuation points. " +
            "Returns a bounded live page of exact asset NodeIds, not the WoT Registry's logical resource list.")]
        public Task<CallToolResult> ListAssetsAsync(
            [Description("Optional legacy management root from wot_inspect_server.")] string? managementNodeId = null,
            [Description("Live result offset.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                return await McpCompanionTools.PageAsync(client.EnumerateAssetsAsync(token),
                    entry => new JsonObject { ["nodeId"] = entry.AssetId.ToString(), ["name"] = entry.Name },
                    offset, maxResults, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Inspects one asset's file and bounded property/action/event facet.
        /// </summary>
        [McpServerTool(Name = "wot_inspect_asset", ReadOnly = true)]
        [Description("Inspects an asset and returns WoTFile metadata plus a live affordance page. Actions include " +
            "argument metadata for existing Read/Write/Call tools. No action execution or external TD fetching.")]
        public Task<CallToolResult> InspectAssetAsync(
            [Description("Actual asset NodeId.")] string assetNodeId,
            [Description("Affordance facet to inspect.")] WotAssetFacet facet = WotAssetFacet.Properties,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Live affordance offset.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                NodeId node = WotAccess.Node(assetNodeId);
                Node info = await client.Session.ReadNodeAsync(node, NodeClass.Object, false, token)
                    .ConfigureAwait(false);
                WotAssetClient asset = await client.OpenAssetAsync(node, token).ConfigureAwait(false);
                JsonObject file = await McpModelAccess.PropertiesAsync(client.Session, asset.File.ObjectId,
                    [new QualifiedName("Size"), new QualifiedName("Writable"), new QualifiedName("UserWritable"),
                        new QualifiedName("OpenCount")], token).ConfigureAwait(false);
                JsonObject affordances = await McpCompanionTools.PageAsync(
                    WotAccess.AffordancesAsync(asset, facet, token), static entry => entry, offset, maxResults, token)
                    .ConfigureAwait(false);
                return new JsonObject
                {
                    ["error"] = McpModelAccess.HasError(file) || McpModelAccess.HasError(affordances),
                    ["assetNodeId"] = node.ToString(),
                    ["displayName"] = info.DisplayName.Text,
                    ["file"] = file,
                    ["facet"] = facet.ToString(),
                    ["affordances"] = affordances
                };
            }, ct);
        }

        /// <summary>
        /// Requests optional server-side endpoint discovery.
        /// </summary>
        [McpServerTool(Name = "wot_discover_endpoints", ReadOnly = true)]
        [Description("Invokes optional DiscoverAssets and returns a bounded page of endpoint strings. " +
            "This is server-side discovery only; returned endpoints are never probed or connected automatically.")]
        public Task<CallToolResult> DiscoverEndpointsAsync(
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Result offset.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(
                    WotMethods.WoTAssetConnectionManagementType_DiscoverAssets, [], token).ConfigureAwait(false);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    return McpModelAccess.MethodResult(call, client.Session.MessageContext);
                }
                if (call.OutputArguments.Count != 1 ||
                    !call.OutputArguments[0].TryGetValue(out ArrayOf<string> endpoints))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError, "DiscoverAssets returned no endpoints.");
                }
                JsonObject result = McpCompanionTools.Page(endpoints, endpoint => JsonValue.Create(endpoint),
                    offset, maxResults);
                result["statusCodeValue"] = call.StatusCode.Code;
                return result;
            }, ct);
        }

        /// <summary>
        /// Explicitly asks the server to test one endpoint.
        /// </summary>
        [McpServerTool(Name = "wot_test_connection", ReadOnly = false)]
        [Description("Tests an asset endpoint under server policy. Returns success and status. " +
            "May cause server-side network access; creates no asset or MCP-host connection.")]
        public Task<CallToolResult> TestConnectionAsync(
            [Description("Endpoint to test through the WoT server.")] string endpoint,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(
                    WotMethods.WoTAssetConnectionManagementType_ConnectionTest, [Variant.From(endpoint)], token)
                    .ConfigureAwait(false);
                JsonObject result = McpModelAccess.MethodResult(call, client.Session.MessageContext);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    if (call.OutputArguments.Count != 2 ||
                        !call.OutputArguments[0].TryGetValue(out bool success) ||
                        !call.OutputArguments[1].TryGetValue(out string status))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "Invalid ConnectionTest response.");
                    }
                    result["success"] = success;
                    result["status"] = status;
                    result["error"] = !success;
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Creates one empty legacy asset without automatically uploading a document.
        /// </summary>
        [McpServerTool(Name = "wot_create_asset", ReadOnly = false, Destructive = true)]
        [Description("Creates one empty WoT asset and returns its exact NodeId. Does not upload a Thing Description, " +
            "probe an endpoint or open another session. Uses an encrypted session and preserves the method status.")]
        public Task<CallToolResult> CreateAssetAsync(
            [Description("Asset name accepted by the server.")] string assetName,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CreateAsync(assetName, null, false, managementNodeId, sessionName, ct);
        }

        /// <summary>
        /// Explicitly requests the server's endpoint-to-asset workflow.
        /// </summary>
        [McpServerTool(Name = "wot_create_asset_for_endpoint", ReadOnly = false, Destructive = true)]
        [Description("Creates an asset for one endpoint and returns its NodeId/status. The server may contact the " +
            "endpoint, synthesize a TD and materialize the asset; this is not a read-only lookup.")]
        public Task<CallToolResult> CreateAssetForEndpointAsync(
            [Description("Asset name accepted by the server.")] string assetName,
            [Description("Endpoint the server is explicitly permitted to use.")] string endpoint,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return CreateAsync(assetName, endpoint, true, managementNodeId, sessionName, ct);
        }

        /// <summary>
        /// Removes one legacy asset through its owning manager.
        /// </summary>
        [McpServerTool(Name = "wot_delete_asset", ReadOnly = false, Destructive = true)]
        [Description("Deletes one legacy asset and returns the method status. Does not delete registry resources, " +
            "retry or cascade into other catalogues.")]
        public Task<CallToolResult> DeleteAssetAsync(
            [Description("Exact legacy asset NodeId to delete.")] string assetNodeId,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                McpModelAccess.RequireSecureMutation(client.Session);
                CallMethodResult result = await client.Proxy.CallWithResultAsync(
                    WotMethods.WoTAssetConnectionManagementType_DeleteAsset,
                    [Variant.From(WotAccess.Node(assetNodeId))], token).ConfigureAwait(false);
                return McpModelAccess.MethodResult(result, client.Session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Uploads a TD and preserves the actual CloseAndUpdate completion status.
        /// </summary>
        [McpServerTool(Name = "wot_upload_thing_description", ReadOnly = false, Destructive = true)]
        [Description("Uploads a root-confined Thing Description and calls CloseAndUpdate, " +
            "possibly replacing affordances. Returns bytes and actual completion status, " +
            "preserving committed results under late cancellation. No registry refresh.")]
        public Task<CallToolResult> UploadThingDescriptionAsync(
            [Description("Legacy asset NodeId.")] string assetNodeId,
            [Description("TD file beneath the host TransferRoot.")] string filePath,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                McpModelAccess.RequireSecureMutation(client.Session);
                StatusCode status = default;
                JsonObject result = await m_transfers.UploadAsync(filePath, async (source, cancellation) =>
                {
                    WotAssetClient asset = await client.OpenAssetAsync(WotAccess.Node(assetNodeId), cancellation)
                        .ConfigureAwait(false);
                    (long bytes, StatusCode committed) = await asset.File.UploadAndUpdateWithResultAsync(
                        source, m_options.MaxTransferBytes, ct: cancellation).ConfigureAwait(false);
                    status = committed;
                    return bytes;
                }, token).ConfigureAwait(false);
                result["assetNodeId"] = assetNodeId;
                result["statusCode"] = status.ToString();
                result["statusCodeValue"] = status.Code;
                return result;
            }, ct);
        }

        /// <summary>
        /// Downloads a legacy asset TD into a new root-confined file.
        /// </summary>
        [McpServerTool(Name = "wot_download_thing_description", ReadOnly = true)]
        [Description("Streams the asset's stored Thing Description into a new file beneath TransferRoot. " +
            "Enforces byte limits, handles short reads, never overwrites, and cleans incomplete local output.")]
        public Task<CallToolResult> DownloadThingDescriptionAsync(
            [Description("Legacy asset NodeId.")] string assetNodeId,
            [Description("New destination beneath TransferRoot.")] string filePath,
            [Description("Optional legacy management root.")] string? managementNodeId = null,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                return await m_transfers.DownloadAsync(filePath, async (target, cancellation) =>
                {
                    WotAssetClient asset = await client.OpenAssetAsync(WotAccess.Node(assetNodeId), cancellation)
                        .ConfigureAwait(false);
                    await asset.File.DownloadToStreamAsync(target, m_options.MaxTransferBytes, ct: cancellation)
                        .ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads WoT-specific resource/version state without changing projection.
        /// </summary>
        [McpServerTool(Name = "wot_read_resource_state", ReadOnly = true)]
        [Description("Returns WoT resource state: enabled/load status, desired/active versions, validation, digest, " +
            "projection root and bindings, retaining quality and optional absence. " +
            "Use xregistry_read for identity/epochs.")]
        public Task<CallToolResult> ReadResourceStateAsync(
            [Description("Logical WoT document or concrete version NodeId.")] string nodeId,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = WotAccess.Session(m_sessions, sessionName);
                NodeId node = WotAccess.Node(nodeId);
                await McpModelAccess.RequireTypeAsync(session, node, WotTypes.WoTDocumentType, token)
                    .ConfigureAwait(false);
                return await WotAccess.PropertiesAsync(session, node,
                    ["DocumentKind", "Enabled", "LoadState", "DesiredVersionId", "ActiveVersionId", "IsDefault",
                        "Ancestor", "Compatibility", "AutoRefresh", "RefreshGeneration", "LastRefreshTime",
                        "ContentDigest", "ValidationOutcome", "MaterializedNodeCount",
                        "RootNodeId", "SelectedBindings"],
                    token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Requests validation of the selected server-side document.
        /// </summary>
        [McpServerTool(Name = "wot_validate_resource", ReadOnly = false)]
        [Description("Invokes Validate on an exact WoT document node and returns its outcome/status. " +
            "Updates server validation state and requires encryption; this is not local document validation.")]
        public Task<CallToolResult> ValidateResourceAsync(
            [Description("WoT document/version NodeId to validate.")] string nodeId,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = WotAccess.Session(m_sessions, sessionName);
                McpModelAccess.RequireSecureMutation(session);
                NodeId node = WotAccess.Node(nodeId);
                await McpModelAccess.RequireTypeAsync(session, node, WotTypes.WoTDocumentType, token)
                    .ConfigureAwait(false);
                var proxy = new WoTDocumentTypeClient(session, node, m_sessions.Telemetry);
                (StatusCode status, WoTValidationOutcomeDataType outcome) = await proxy.ValidateWithResultAsync(token)
                    .ConfigureAwait(false);
                return new JsonObject
                {
                    ["nodeId"] = node.ToString(),
                    ["error"] = Failed(outcome.FormatOutcome) || Failed(outcome.CompatibilityOutcome),
                    ["statusCodeValue"] = status.Code,
                    ["outcome"] = McpCompanionJson.Encode(outcome, session.MessageContext)
                };
            }, ct);
        }

        /// <summary>
        /// Explicitly changes enabled state using the model's concurrency token.
        /// </summary>
        [McpServerTool(Name = "wot_set_enabled", ReadOnly = false, Destructive = true)]
        [Description("Sets Enabled with ExpectedEpoch and returns the method status, including refusals. " +
            "Zero requires allowUnconditional=true; does not invoke Refresh.")]
        public Task<CallToolResult> SetEnabledAsync(
            [Description("WoT document NodeId.")] string nodeId,
            [Description("Requested enabled state.")] bool enabled,
            [Description("Observed applicable Epoch.")] uint expectedEpoch,
            [Description("Explicitly allow a zero concurrency token.")] bool allowUnconditional = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return DocumentCommandAsync(nodeId, WotMethods.WoTDocumentType_SetEnabled,
                [Variant.From(enabled), Variant.From(expectedEpoch)],
                expectedEpoch, allowUnconditional, sessionName, ct);
        }

        /// <summary>
        /// Selects one default version without an implicit refresh or enable operation.
        /// </summary>
        [McpServerTool(Name = "wot_set_default_version", ReadOnly = false, Destructive = true)]
        [Description("Selects a default version with ExpectedEpoch and returns the method status. " +
            "Does not also enable or refresh; server-configured automatic behavior still applies.")]
        public Task<CallToolResult> SetDefaultVersionAsync(
            [Description("WoT document NodeId.")] string nodeId,
            [Description("Exact version id to select.")] string versionId,
            [Description("Observed applicable Epoch.")] uint expectedEpoch,
            [Description("Explicitly allow a zero concurrency token.")] bool allowUnconditional = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return DocumentCommandAsync(nodeId, WotMethods.WoTDocumentType_SetDefaultVersion,
                [Variant.From(versionId), Variant.From(expectedEpoch)],
                expectedEpoch, allowUnconditional, sessionName, ct);
        }

        /// <summary>
        /// Refreshes only an explicit scope and preserves partial outcomes and generation.
        /// </summary>
        [McpServerTool(Name = "wot_refresh", ReadOnly = false, Destructive = true)]
        [Description("Refreshes selected resources or options.allResources=true, passing ExpectedGeneration. " +
            "Preserves partial outcomes, method status and new generation; never retries. Returns up to maxResults " +
            "details with explicit truncation, not a cursor that would re-run the mutation.")]
        public Task<CallToolResult> RefreshAsync(
            [Description("Actual WoT Registry NodeId from discovery.")] string registryNodeId,
            [Description("Explicit selectors and refresh options.")] WotRefreshInput input,
            [Description("Observed RefreshGeneration; zero requires allowUnconditional.")] uint expectedGeneration,
            [Description("Explicitly allow a zero generation token.")] bool allowUnconditional = false,
            [Description("Maximum returned per-resource details, 1..500.")] int maxResults = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentNullException.ThrowIfNull(input);
                ValidateRefresh(input, maxResults);
                ISession session = WotAccess.Session(m_sessions, sessionName);
                McpModelAccess.RequireSecureMutation(session, expectedGeneration, allowUnconditional);
                NodeId root = WotAccess.Node(registryNodeId);
                await McpModelAccess.RequireTypeAsync(session, root, WotTypes.WoTRegistryType, token)
                    .ConfigureAwait(false);
                var client = new WotRegistryClient(session, root, m_sessions.Telemetry);
                ArrayOf<WoTResourceSelectorDataType> selection = input.Selection.ConvertAll(value =>
                    new WoTResourceSelectorDataType
                    {
                        Kind = value.Kind,
                        GroupId = value.GroupId,
                        ResourceId = value.ResourceId,
                        VersionId = value.VersionId,
                        Xid = value.Xid
                    });
                WotRegistryRefreshResult refreshed = await client.RefreshAsync(selection, new WoTRefreshOptionsDataType
                {
                    Atomicity = input.Atomicity,
                    Force = input.ForceRebuild,
                    DryRun = input.DryRun,
                    IncludeDependents = input.IncludeDependents,
                    DeletePolicy = input.DeletePolicy,
                    MaxParallelism = input.MaxParallelism,
                    Timeout = input.TimeoutMilliseconds
                }, expectedGeneration, input.RequestId, token).ConfigureAwait(false);
                var results = new JsonArray();
                for (int index = 0; index < refreshed.Results.Count && index < maxResults; index++)
                {
                    results.Add(McpCompanionJson.Encode(refreshed.Results[index], session.MessageContext));
                }
                return new JsonObject
                {
                    ["error"] = refreshed.HasFailures || refreshed.Summary.Failed > 0,
                    ["statusCodeValue"] = refreshed.MethodStatus.Code,
                    ["summary"] = McpCompanionJson.Encode(refreshed.Summary, session.MessageContext),
                    ["newGeneration"] = refreshed.NewGeneration,
                    ["results"] = results,
                    ["resultCount"] = refreshed.Results.Count,
                    ["truncated"] = refreshed.Results.Count > maxResults
                };
            }, ct);
        }

        /// <summary>
        /// Observes WoT lifecycle/failure and refresh-completed events using generated fields.
        /// </summary>
        [McpServerTool(Name = "wot_observe_events", ReadOnly = true)]
        [Description("Returns typed WoT events, including resource failures and refresh summaries, over at most " +
            "30 seconds/500 items. Removes only this call's monitored item.")]
        public Task<CallToolResult> ObserveEventsAsync(
            [Description("WoT Registry or document notifier NodeId.")] string notifierNodeId,
            [Description("Observation window, 1..30000 milliseconds.")] int durationMs = 1000,
            [Description("Maximum events, 1..500.")] int maxItems = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = WotAccess.Session(m_sessions, sessionName);
                NodeId notifier = WotAccess.Node(notifierNodeId);
                NodeId type = await McpModelAccess.TypeDefinitionAsync(session, notifier, token).ConfigureAwait(false);
                NodeId registryType = McpModelAccess.ModelNode(session, WotTypes.WoTRegistryType);
                NodeId documentType = McpModelAccess.ModelNode(session, WotTypes.WoTDocumentType);
                if (type != registryType && type != documentType &&
                    !await session.NodeCache.IsTypeOfAsync(type, registryType, token).ConfigureAwait(false) &&
                    !await session.NodeCache.IsTypeOfAsync(type, documentType, token).ConfigureAwait(false))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A WoT notifier is required.");
                }
                EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default.CreateChildScope();
                WotConEventRecordDecoders.RegisterWotConDecoders(registry, session.NamespaceUris);
                EventFilter filter = WoTResourceEventTypeRecord.EventFilters.Build(session.NamespaceUris, registry);
                filter.WhereClause = new ContentFilter();
                ContentFilterElement resourceEvents = filter.WhereClause.Push(FilterOperator.OfType,
                    Variant.From(McpModelAccess.ModelNode(session, WotTypes.WoTResourceEventType)));
                ContentFilterElement refreshEvents = filter.WhereClause.Push(FilterOperator.OfType,
                    Variant.From(McpModelAccess.ModelNode(session, WotTypes.WoTRefreshCompletedEventType)));
                filter.WhereClause.Push(FilterOperator.Or,
                    Variant.FromStructure(resourceEvents), Variant.FromStructure(refreshEvents));
                return await McpModelAccess.ObserveEventsAsync(
                    session, notifier, filter, durationMs, maxItems, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Creates an asset with no post-mutation reads that could mask completed success.
        /// </summary>
        private Task<CallToolResult> CreateAsync(
            string name, string? endpoint, bool useEndpoint, string? managementNodeId,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                ArrayOf<Variant> arguments = [Variant.From(name)];
                if (useEndpoint)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
                    arguments = [Variant.From(name), Variant.From(endpoint)];
                }
                WotConnectivityClient client = await WotAccess.ManagementAsync(
                    m_sessions, sessionName, managementNodeId, token).ConfigureAwait(false);
                McpModelAccess.RequireSecureMutation(client.Session);
                CallMethodResult call = await client.Proxy.CallWithResultAsync(
                    !useEndpoint ? WotMethods.WoTAssetConnectionManagementType_CreateAsset
                        : WotMethods.WoTAssetConnectionManagementType_CreateAssetForEndpoint,
                    arguments, token)
                    .ConfigureAwait(false);
                JsonObject result = McpModelAccess.MethodResult(call, client.Session.MessageContext);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    if (call.OutputArguments.Count != 1 ||
                        !call.OutputArguments[0].TryGetValue(out NodeId asset) || asset.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError,
                            "The completed create operation returned no asset identity.");
                    }
                    result["assetNodeId"] = asset.ToString();
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Sends one explicitly selected document command through its generated proxy.
        /// </summary>
        private Task<CallToolResult> DocumentCommandAsync(
            string nodeId, ExpandedNodeId method, ArrayOf<Variant> arguments, uint epoch, bool unconditional,
            string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = WotAccess.Session(m_sessions, sessionName);
                McpModelAccess.RequireSecureMutation(session, epoch, unconditional);
                NodeId node = WotAccess.Node(nodeId);
                await McpModelAccess.RequireTypeAsync(session, node, WotTypes.WoTDocumentType, token)
                    .ConfigureAwait(false);
                var proxy = new WoTDocumentTypeClient(session, node, m_sessions.Telemetry);
                CallMethodResult result = await proxy.CallWithResultAsync(method, arguments, token)
                    .ConfigureAwait(false);
                return McpModelAccess.MethodResult(result, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Validates scope and bounds before the server can start a refresh.
        /// </summary>
        private static void ValidateRefresh(WotRefreshInput input, int maxResults)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxResults, 500);
            if (input.Selection.IsNull || input.Selection.Count > 500 ||
                input.Selection.IsEmpty != input.AllResources)
            {
                throw new ArgumentException(
                    "Select resources, or explicitly set allResources with an empty selection.");
            }
            if (!Enum.IsDefined(input.Atomicity) || !Enum.IsDefined(input.DeletePolicy) ||
                input.MaxParallelism is 0 or > 32 || input.TimeoutMilliseconds is 0 or > 120_000)
            {
                throw new ArgumentException("Refresh enum, parallelism or timeout is outside the supported bounds.");
            }
            foreach (WotResourceSelection item in input.Selection)
            {
                if (item is null || !Enum.IsDefined(item.Kind) ||
                    (string.IsNullOrWhiteSpace(item.Xid) && string.IsNullOrWhiteSpace(item.GroupId)))
                {
                    throw new ArgumentException("Every selector needs a valid kind and a group or structural Xid.");
                }
            }
        }

        /// <summary>
        /// Interprets the model's explicit rejection and failure outcomes.
        /// </summary>
        private static bool Failed(WoTOutcomeEnum outcome)
        {
            return outcome is WoTOutcomeEnum.Failed or WoTOutcomeEnum.Rejected;
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly McpFileTransfers m_transfers;
        private readonly OpcUaMcpOptions m_options;
    }
}
