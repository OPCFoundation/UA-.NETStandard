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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;
using RegistryMethods = Opc.Ua.XRegistry.MethodIds;
using RegistryTypes = Opc.Ua.XRegistry.ObjectTypeIds;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Domain-neutral, bounded registry operations on the caller's current named UA session.
    /// </summary>
    [McpServerToolType]
    public sealed class XRegistryTools
    {
        /// <summary>
        /// Initializes the tools from host-owned sessions and transfer policy.
        /// </summary>
        public XRegistryTools(OpcUaSessionManager sessions, McpFileTransfers transfers, OpcUaMcpOptions options)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Discovers actual registry roots and vendor subtypes.
        /// </summary>
        [McpServerTool(Name = "xregistry_discover", ReadOnly = true)]
        [Description("Finds xRegistry roots below a browse root, including domain/vendor subtypes. " +
            "Returns a bounded live page of NodeIds; scans at most 1000 references and never follows federation.")]
        public Task<CallToolResult> DiscoverAsync(
            [Description("Root to search below; defaults to Objects (i=85).")] string rootNodeId = "i=85",
            [Description("Additional hierarchy depth, 0..6.")] int maxDepth = 3,
            [Description("Live result offset.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session; omit only for one connected session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                return await McpCompanionTools.PageAsync(
                    McpModelAccess.DiscoverAsync(
                        session, Node(rootNodeId), RegistryTypes.RegistryType, maxDepth, token),
                    reference => McpModelAccess.Reference(session, reference), offset, maxResults, token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Enumerates groups, logical resources, concrete versions or entity labels.
        /// </summary>
        [McpServerTool(Name = "xregistry_list", ReadOnly = true)]
        [Description("Lists one collection of an actual registry entity: Groups, Resources, Versions, or Labels. " +
            "Follows continuation points, preserves live paging, and distinguishes logical resources from versions.")]
        public Task<CallToolResult> ListAsync(
            [Description("Parent registry/group/resource NodeId.")] string nodeId,
            [Description("Collection to enumerate.")] RegistryCollection collection,
            [Description("Live result offset.")] int offset = 0,
            [Description("Page size, 1..500.")] int maxResults = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                JsonObject result = await McpCompanionTools.PageAsync(
                    RegistryAccess.ListAsync(session, Node(nodeId), collection, token),
                    static entry => entry, offset, maxResults, token).ConfigureAwait(false);
                result["error"] = McpModelAccess.HasError(result);
                return result;
            }, ct);
        }

        /// <summary>
        /// Reads role-aware registry metadata without downloading document content.
        /// </summary>
        [McpServerTool(Name = "xregistry_read", ReadOnly = true)]
        [Description("Returns entity role, identity, label container, Epoch/MetaEpoch and typed metadata " +
            "with quality and optional absence. Content keys and federation pointers are not connection instructions.")]
        public Task<CallToolResult> ReadAsync(
            [Description("Actual registry entity NodeId.")] string nodeId,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(
                async token => await RegistryAccess.ReadAsync(
                    RegistryAccess.Session(m_sessions, sessionName), Node(nodeId), token).ConfigureAwait(false),
                ct);
        }

        /// <summary>
        /// Creates one group, optionally using the explicitly requested idempotent create operation.
        /// </summary>
        [McpServerTool(Name = "xregistry_create_group", ReadOnly = false, Destructive = true)]
        [Description("Creates one group and returns its NodeId/status. ifNotExists selects GetOrCreateGroup. " +
            "Requires encryption; no resource creation, upload or refresh.")]
        public Task<CallToolResult> CreateGroupAsync(
            [Description("Actual registry root NodeId.")] string registryNodeId,
            [Description("Group identifier accepted by this registry.")] string groupId,
            [Description("Explicitly use GetOrCreateGroup instead of CreateGroup.")] bool ifNotExists = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                RegistryAccess.Mutation(session);
                NodeId owner = Node(registryNodeId);
                await RegistryAccess.RequireRoleAsync(session, owner, RegistryRole.Registry, token)
                    .ConfigureAwait(false);
                var proxy = new RegistryTypeClient(session, owner, m_sessions.Telemetry);
                CallMethodResult call = await proxy.CallWithResultAsync(
                    ifNotExists
                        ? RegistryMethods.RegistryType_GetOrCreateGroup : RegistryMethods.RegistryType_CreateGroup,
                    [Variant.From(groupId)], token).ConfigureAwait(false);
                JsonObject result = McpModelAccess.MethodResult(call, session.MessageContext);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["groupNodeId"] = OutputNode(call, 0).ToString();
                    result["created"] = !ifNotExists || OutputBoolean(call, 1);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Creates one concrete resource version without retaining an open file handle.
        /// </summary>
        [McpServerTool(Name = "xregistry_create_resource", ReadOnly = false, Destructive = true)]
        [Description("Creates a Resource/Version identity or explicitly gets an existing version. " +
            "Returns the exact Version NodeId and assigned version id; no file open, upload, enable or refresh. " +
            "Empty versionId assigns a new version for CreateResource or selects the default for GetOrCreateResource.")]
        public Task<CallToolResult> CreateResourceAsync(
            [Description("Owning group NodeId.")] string groupNodeId,
            [Description("Logical resource identifier.")] string resourceId,
            [Description("Explicit version id, or empty for assignment/default selection.")] string versionId = "",
            [Description("Explicitly select GetOrCreateResource.")] bool ifNotExists = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                RegistryAccess.Mutation(session);
                NodeId owner = Node(groupNodeId);
                await RegistryAccess.RequireRoleAsync(session, owner, RegistryRole.Group, token).ConfigureAwait(false);
                var proxy = new GroupTypeClient(session, owner, m_sessions.Telemetry);
                CallMethodResult call = await proxy.CallWithResultAsync(
                    ifNotExists
                        ? RegistryMethods.GroupType_GetOrCreateResource : RegistryMethods.GroupType_CreateResource,
                    [Variant.From(resourceId), Variant.From(versionId), Variant.From(false)], token)
                    .ConfigureAwait(false);
                JsonObject result = McpModelAccess.MethodResult(call, session.MessageContext);
                if (!StatusCode.IsBad(call.StatusCode))
                {
                    result["versionNodeId"] = OutputNode(call, 0).ToString();
                    if (call.OutputArguments.Count < 3 ||
                        !call.OutputArguments[1].TryGetValue(out string assignedVersion) ||
                        !call.OutputArguments[2].TryGetValue(out uint handle))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "Invalid resource creation result.");
                    }
                    if (handle != 0)
                    {
                        await new ResourceTypeClient(session, OutputNode(call, 0), m_sessions.Telemetry)
                            .CloseAfterFailureAsync(handle).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The server opened a file that was not requested.");
                    }
                    result["versionId"] = assignedVersion;
                    result["created"] = !ifNotExists || OutputBoolean(call, 3);
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Uploads to an explicit concrete version, not a mutable default-version alias.
        /// </summary>
        [McpServerTool(Name = "xregistry_upload_document", ReadOnly = false, Destructive = true)]
        [Description("Uploads document bytes to an exact Version and returns transfer/commit metadata. " +
            "Requires encryption and TransferRoot; no atomic Epoch check or automatic refresh. " +
            "Remote Close may commit partial data on failure.")]
        public Task<CallToolResult> UploadAsync(
            [Description("Concrete version NodeId from create/list; not a logical Resource.")] string versionNodeId,
            [Description("Source file beneath the configured TransferRoot.")] string filePath,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                RegistryAccess.Mutation(session);
                NodeId node = Node(versionNodeId);
                CallMethodResult? commit = null;
                JsonObject result = await m_transfers.UploadAsync(filePath, async (source, cancellation) =>
                {
                    await RegistryAccess.RequireRoleAsync(session, node, RegistryRole.Version, cancellation)
                        .ConfigureAwait(false);
                    var file = new ResourceTypeClient(session, node, m_sessions.Telemetry);
                    (long bytes, CallMethodResult status) = await file.UploadFromWithResultAsync(
                        source, m_options.MaxTransferBytes, ct: cancellation).ConfigureAwait(false);
                    commit = status;
                    return bytes;
                }, token).ConfigureAwait(false);
                result["versionNodeId"] = node.ToString();
                result["commit"] = McpModelAccess.MethodResult(
                    commit ?? throw new InvalidOperationException("The upload returned no commit result."),
                    session.MessageContext);
                return result;
            }, ct);
        }

        /// <summary>
        /// Downloads content without putting large document bodies into the conversation.
        /// </summary>
        [McpServerTool(Name = "xregistry_download_document", ReadOnly = true)]
        [Description("Downloads a resource/default-version or concrete version to a new TransferRoot file. Returns " +
            "artifact metadata. Byte-limited, no overwrite or federation; incomplete output is removed.")]
        public Task<CallToolResult> DownloadAsync(
            [Description("Resource or version file NodeId.")] string nodeId,
            [Description("New destination file beneath TransferRoot.")] string filePath,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                NodeId node = Node(nodeId);
                return await m_transfers.DownloadAsync(filePath, async (target, cancellation) =>
                {
                    await McpModelAccess.RequireTypeAsync(session, node, RegistryTypes.ResourceType, cancellation)
                        .ConfigureAwait(false);
                    await new ResourceTypeClient(session, node, m_sessions.Telemetry)
                        .ReadDocumentAsync(target, m_options.MaxTransferBytes, ct: cancellation).ConfigureAwait(false);
                }, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Resolves a content key with bounded ranged reads in the explicitly selected registry namespace.
        /// </summary>
        [McpServerTool(Name = "xregistry_resolve_content", ReadOnly = true)]
        [Description("Resolves an opaque content key by ranged reads and returns root-confined artifact metadata. " +
            "The registry namespace is explicit; the key is not an Xid. Missing content is an error.")]
        public Task<CallToolResult> ResolveContentAsync(
            [Description("Registry's advertised content namespace URI.")] string registryNamespaceUri,
            [Description("Raw content identifier encoded as base64, not document bytes.")] string contentIdBase64,
            [Description("New destination beneath TransferRoot.")] string filePath,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(contentIdBase64);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(contentIdBase64.Length, 4096);
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                var client = new GenericXRegistryClient(session, registryNamespaceUri, m_sessions.Telemetry);
                var key = ByteString.FromBase64(contentIdBase64);
                return await m_transfers.DownloadAsync(
                    filePath, async (target, cancellation) => await client.ResolveResourceToAsync(
                    key,
                    target,
                    m_options.MaxTransferBytes,
                    ct: cancellation)
                        .ConfigureAwait(false), token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Sets one label on the entity's actual label container.
        /// </summary>
        [McpServerTool(Name = "xregistry_set_label", ReadOnly = false, Destructive = true)]
        [Description("Sets a label and returns method status. Resources use MetaLabels/MetaEpoch; other roles use " +
            "Labels/Epoch. Zero counter requires force=true; document bytes are unchanged.")]
        public Task<CallToolResult> SetLabelAsync(
            [Description("Registry, group, logical resource or supported version NodeId.")] string nodeId,
            [Description("Label key.")] string key,
            [Description("Label value.")] string value,
            [Description("Observed applicable Epoch or MetaEpoch.")] uint expectedEpoch,
            [Description("Explicitly permit an unconditional zero counter.")] bool force = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return LabelAsync(nodeId, key, value, false, expectedEpoch, force, sessionName, ct);
        }

        /// <summary>
        /// Removes one label without changing document content.
        /// </summary>
        [McpServerTool(Name = "xregistry_remove_label", ReadOnly = false, Destructive = true)]
        [Description("Removes a label with its Epoch/MetaEpoch check and returns method status. Requires encryption; " +
            "zero counter needs force=true. Missing support is explicit.")]
        public Task<CallToolResult> RemoveLabelAsync(
            [Description("Registry entity NodeId.")] string nodeId,
            [Description("Label key.")] string key,
            [Description("Observed applicable Epoch or MetaEpoch.")] uint expectedEpoch,
            [Description("Explicitly permit an unconditional zero counter.")] bool force = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return LabelAsync(nodeId, key, null, true, expectedEpoch, force, sessionName, ct);
        }

        /// <summary>
        /// Deletes a group and its complete contents explicitly.
        /// </summary>
        [McpServerTool(Name = "xregistry_delete_group", ReadOnly = false, Destructive = true)]
        [Description("Deletes a verified group and its contents, returning method status. Uses ExpectedEpoch and " +
            "encryption; zero requires force=true.")]
        public Task<CallToolResult> DeleteGroupAsync(
            [Description("Group NodeId, never a resource or version.")] string nodeId,
            [Description("Observed group Epoch.")] uint expectedEpoch,
            [Description("Explicitly permit an unconditional zero counter.")] bool force = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return DeleteAsync(nodeId, RegistryRole.Group, expectedEpoch, force, sessionName, ct);
        }

        /// <summary>
        /// Deletes one logical resource together with every version.
        /// </summary>
        [McpServerTool(Name = "xregistry_delete_resource", ReadOnly = false, Destructive = true)]
        [Description("Deletes a logical Resource and every Version with MetaEpoch, returning method status. " +
            "Ambiguous legacy roles are rejected; zero requires force=true.")]
        public Task<CallToolResult> DeleteResourceAsync(
            [Description("Logical Resource NodeId, not its default Version.")] string nodeId,
            [Description("Observed Resource MetaEpoch.")] uint expectedEpoch,
            [Description("Explicitly permit an unconditional zero counter.")] bool force = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return DeleteAsync(nodeId, RegistryRole.Resource, expectedEpoch, force, sessionName, ct);
        }

        /// <summary>
        /// Deletes only one concrete version.
        /// </summary>
        [McpServerTool(Name = "xregistry_delete_version", ReadOnly = false, Destructive = true)]
        [Description("Deletes only a concrete Version with its Epoch, returning status and default/last-version " +
            "refusals. Requires a typed Versions hierarchy; never substitutes whole-Resource deletion.")]
        public Task<CallToolResult> DeleteVersionAsync(
            [Description("Concrete Version NodeId.")] string nodeId,
            [Description("Observed Version Epoch.")] uint expectedEpoch,
            [Description("Explicitly permit an unconditional zero counter.")] bool force = false,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return DeleteAsync(nodeId, RegistryRole.Version, expectedEpoch, force, sessionName, ct);
        }

        /// <summary>
        /// Observes native registry lifecycle events with generated field selection.
        /// </summary>
        [McpServerTool(Name = "xregistry_observe_events", ReadOnly = true)]
        [Description("Returns typed native xRegistry lifecycle events over at most 30 seconds/500 items. " +
            "Removes only this call's monitored item.")]
        public Task<CallToolResult> ObserveEventsAsync(
            [Description("Registry, group or resource event-notifier NodeId.")] string notifierNodeId,
            [Description("Observation window, 1..30000 milliseconds.")] int durationMs = 1000,
            [Description("Maximum notifications, 1..500.")] int maxItems = 100,
            [Description("Named session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                NodeId notifier = Node(notifierNodeId);
                _ = await RegistryAccess.RoleAsync(session, notifier, token).ConfigureAwait(false);
                EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default.CreateChildScope();
                registry.RegisterxRegistryDecoders(session.NamespaceUris);
                EventFilter filter = XRegistryEventTypeRecord.EventFilters.Build(session.NamespaceUris, registry);
                return await McpModelAccess.ObserveEventsAsync(
                    session, notifier, filter, durationMs, maxItems, token).ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Dispatches the appropriate inherited label method once.
        /// </summary>
        private Task<CallToolResult> LabelAsync(
            string nodeId, string key, string? value, bool remove,
            uint epoch, bool force, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                ArrayOf<Variant> args = [Variant.From(key), Variant.From(epoch)];
                if (!remove)
                {
                    ArgumentNullException.ThrowIfNull(value);
                    args = [Variant.From(key), Variant.From(value), Variant.From(epoch)];
                }
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                RegistryAccess.Mutation(session, epoch, force);
                NodeId node = Node(nodeId);
                RegistryRole role = await RegistryAccess.RoleAsync(session, node, token).ConfigureAwait(false);
                NodeId container = await RegistryAccess.ChildAsync(session, node,
                    role == RegistryRole.Resource ? "MetaLabels" : "Labels", token).ConfigureAwait(false);
                if (container.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "This entity has no label container.");
                }
                var proxy = new AttributesTypeClient(session, container, m_sessions.Telemetry);
                CallMethodResult result = await proxy.CallWithResultAsync(
                    remove
                        ? RegistryMethods.AttributesType_RemoveAttribute : RegistryMethods.AttributesType_AddAttribute,
                    args, token).ConfigureAwait(false);
                return McpModelAccess.MethodResult(result, session.MessageContext);
            }, ct);
        }

        /// <summary>
        /// Verifies the deletion scope before sending the model's Delete method.
        /// </summary>
        private Task<CallToolResult> DeleteAsync(
            string nodeId, RegistryRole role, uint epoch, bool force, string? sessionName, CancellationToken ct)
        {
            return McpCompanionTools.ExecuteAsync(async token =>
            {
                ISession session = RegistryAccess.Session(m_sessions, sessionName);
                RegistryAccess.Mutation(session, epoch, force);
                NodeId node = Node(nodeId);
                await RegistryAccess.RequireRoleAsync(session, node, role, token).ConfigureAwait(false);
                ObjectTypeClient proxy = role == RegistryRole.Group
                    ? new GroupTypeClient(session, node, m_sessions.Telemetry)
                    : new ResourceTypeClient(session, node, m_sessions.Telemetry);
                CallMethodResult call = await proxy.CallWithResultAsync(
                    role == RegistryRole.Group ? RegistryMethods.GroupType_Delete : RegistryMethods.ResourceType_Delete,
                    [Variant.From(epoch)], token).ConfigureAwait(false);
                JsonObject result = McpModelAccess.MethodResult(call, session.MessageContext);
                result["nodeId"] = node.ToString();
                result["scope"] = role.ToString();
                return result;
            }, ct);
        }

        /// <summary>
        /// Parses an explicit non-null local identifier.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        private static NodeId Node(string text)
        {
            NodeId node = OpcUaJsonHelper.ParseNodeId(text);
            return node.IsNull ? throw new ArgumentException("A non-null NodeId is required.", nameof(text)) : node;
        }

        /// <summary>
        /// Requires an actual non-null node identity from a successful method result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static NodeId OutputNode(CallMethodResult result, int index)
        {
            if (result.OutputArguments.Count <= index ||
                !result.OutputArguments[index].TryGetValue(out NodeId node) ||
                node.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The method returned no node identity.");
            }
            return node;
        }

        /// <summary>
        /// Requires the generated method's exact Boolean result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static bool OutputBoolean(CallMethodResult result, int index)
        {
            if (result.OutputArguments.Count <= index || !result.OutputArguments[index].TryGetValue(out bool value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The method returned no Boolean result.");
            }
            return value;
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly McpFileTransfers m_transfers;
        private readonly OpcUaMcpOptions m_options;
    }
}
