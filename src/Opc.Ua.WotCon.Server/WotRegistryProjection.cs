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
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Adapts the WoT Connectivity registry service to the shared xRegistry projection engine.
    /// </summary>
    internal sealed class WotRegistryProjection : IDisposable
    {
        public WotRegistryProjection(
            WotRegistryNodeManager manager,
            IWotRegistryService registry,
            WotRegistryServerOptions options)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_modelNs = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            var context = new XRegistryProjectionContext(
                manager.SystemContext,
                manager.Server.NamespaceUris,
                m_modelNs,
                async (node, ct) =>
                {
                    await manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
                },
                async (nodeId, ct) =>
                {
                    await manager.DeleteNodeAsync(manager.SystemContext, nodeId, ct).ConfigureAwait(false);
                },
                manager.CheckManagementAccess);
            m_engine = new XRegistryProjectionEngine(context, new Strategy(this), RegistryNodeIdPath);
        }

        /// <summary>
        /// Binds the projection to the well-known registry Object.
        /// </summary>
        public ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            return m_engine.AttachAsync(registryNode, ct);
        }

        /// <summary>
        /// Finds the browseable resource node used as an event source.
        /// </summary>
        public NodeState EventSourceFor(string? xid)
        {
            return m_engine.EventSourceFor(xid);
        }

        /// <summary>
        /// Reconciles the browseable projection with the current registry snapshot.
        /// </summary>
        public ValueTask ReconcileAsync(CancellationToken ct)
        {
            return m_engine.ReconcileAsync(ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_engine.Dispose();
        }

        internal static void LinkMethodArguments(NodeState? node, ISystemContext context)
        {
            XRegistryProjectionEngine.LinkMethodArguments(node, context);
        }

        private GroupState CreateGroupNode(BaseObjectState registryNode, WotResourceGroup group)
        {
            bool tm = group.Kind == WoTDocumentKindEnum.ThingModel;
            GroupState node = tm
                ? new ThingModelGroupState(registryNode)
                : new ThingDescriptionGroupState(registryNode);
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                tm ? ObjectTypeIds.ThingModelGroupType : ObjectTypeIds.ThingDescriptionGroupType,
                m_manager.Server.NamespaceUris);
            return node;
        }

        private WoTDocumentState CreateResourceNode(GroupState groupNode, WotResource resource)
        {
            bool tm = resource.Kind == WoTDocumentKindEnum.ThingModel;
            WoTDocumentState node = tm
                ? new ThingModelFileState(groupNode)
                : new ThingDescriptionFileState(groupNode);
            node.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                tm ? ObjectTypeIds.ThingModelFileType : ObjectTypeIds.ThingDescriptionFileType,
                m_manager.Server.NamespaceUris);
            return node;
        }

        private void ConfigureResourceNode(ResourceState node, WotResource resource)
        {
            if (node is not WoTDocumentState document)
            {
                return;
            }

            document.AddDesiredVersionId(m_manager.SystemContext)
                .AddActiveVersionId(m_manager.SystemContext)
                .AddIsDefault(m_manager.SystemContext)
                .AddContentDigest(m_manager.SystemContext)
                .AddValidationOutcome(m_manager.SystemContext)
                .AddMaterializedNodeCount(m_manager.SystemContext)
                .AddRootNodeId(m_manager.SystemContext)
                .AddRefreshGeneration(m_manager.SystemContext)
                .AddLastRefreshTime(m_manager.SystemContext);
            document.AddValidate(m_manager.SystemContext);
            document.AddSetEnabled(m_manager.SystemContext);
            document.AddSetDefaultVersion(m_manager.SystemContext);

            if (document is ThingDescriptionFileState td)
            {
                td.AddThingTitle(m_manager.SystemContext)
                    .AddBaseUri(m_manager.SystemContext);
            }
            else if (document is ThingModelFileState tmNode)
            {
                tmNode.AddModelTitle(m_manager.SystemContext)
                    .AddModelVersion(m_manager.SystemContext)
                    .AddDerivedTypeNodeId(m_manager.SystemContext);
            }

            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            document.Validate?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnValidateAsync(groupId, resourceId, c, ot, t);
            document.SetEnabled?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnSetEnabledAsync(groupId, resourceId, c, i, t);
            document.SetDefaultVersion?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnSetDefaultVersionAsync(groupId, resourceId, c, i, t);

            ApplyWotResourceProperties(document, resource);
        }

        private void ApplyWotResourceProperties(WoTDocumentState node, WotResource resource)
        {
            WotResourceVersion? version = resource.DefaultVersion;

            XRegistryProjectionEngine.SetValue(node.DocumentKind, resource.Kind);
            XRegistryProjectionEngine.SetValue(node.Enabled, resource.Enabled);
            XRegistryProjectionEngine.SetValue(node.LoadState, resource.LoadState);
            XRegistryProjectionEngine.SetValue(node.DesiredVersionId, resource.DesiredVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.ActiveVersionId, resource.ActiveVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.IsDefault, version is not null &&
                string.Equals(version.VersionId, resource.DefaultVersionId, StringComparison.Ordinal));
            XRegistryProjectionEngine.SetValue(node.ContentDigest, version is null ? ByteString.Empty : version.Digest);
            if (resource.Validation is not null)
            {
                XRegistryProjectionEngine.SetValue(node.ValidationOutcome, resource.Validation);
            }
            XRegistryProjectionEngine.SetValue(node.MaterializedNodeCount, (uint)resource.MaterializedNodeCount);
            XRegistryProjectionEngine.SetValue(node.RootNodeId, resource.RootNodeId);
            XRegistryProjectionEngine.SetValue(node.RefreshGeneration, resource.RefreshGeneration);
            XRegistryProjectionEngine.SetValue(node.LastRefreshTime, (DateTimeUtc)resource.LastRefreshTime);

            if (node is ThingDescriptionFileState td)
            {
                XRegistryProjectionEngine.SetValue(td.ThingId, resource.ThingId ?? string.Empty);
                XRegistryProjectionEngine.SetValue(td.ThingTitle, resource.Title ?? string.Empty);
            }
            else if (node is ThingModelFileState tmNode)
            {
                XRegistryProjectionEngine.SetValue(tmNode.ModelTitle, resource.Title ?? string.Empty);
                XRegistryProjectionEngine.SetValue(tmNode.DerivedTypeNodeId, resource.RootNodeId);
            }
        }

        private WotResourceFileManager CreateResourceFile(WoTDocumentState node, WotResource resource)
        {
            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            WoTDocumentKindEnum kind = resource.Kind;
            return new WotResourceFileManager(
                node,
                m_options.Bounds.MaxOpenFileHandles,
                m_options.Bounds.MaxDocumentBytes,
                m_manager.CheckManagementAccess,
                (key, offset, count, token) => m_registry.ReadContentChunkAsync(key, offset, count, token),
                (bytes, session, token) => CommitDocumentAsync(groupId, resourceId, kind, bytes, token));
        }

        private async ValueTask<ServiceResult> OnValidateAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            List<Variant> output,
            CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "Validate");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            WoTValidationOutcomeDataType outcome;
            try
            {
                outcome = await m_registry.ValidateResourceAsync(groupId, resourceId, ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileAsync(ct).ConfigureAwait(false);
            output.Clear();
#pragma warning disable CS0618 // Validate generated proxy expects a direct structure Variant.
            output.Add(new Variant(outcome));
#pragma warning restore CS0618
            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnSetEnabledAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "SetEnabled");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            if (GetBoolOrNull(input, 0) is not { } enabled)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The Enabled argument is required.");
            }
            WotRegistryMutationResult result = await m_registry
                .SetEnabledAsync(groupId, resourceId, enabled, OptionalEpoch(input, 1), ct)
                .ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> OnSetDefaultVersionAsync(
            string groupId,
            string resourceId,
            ISystemContext context,
            ArrayOf<Variant> input,
            CancellationToken ct)
        {
            ServiceResult access = m_manager.CheckManagementAccess(context, "SetDefaultVersion");
            if (ServiceResult.IsBad(access))
            {
                return access;
            }
            string? versionId = GetString(input, 0);
            if (string.IsNullOrEmpty(versionId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument, "The VersionId argument is required.");
            }
            WotRegistryMutationResult result = await m_registry
                .SetDefaultVersionAsync(groupId, resourceId, versionId!, OptionalEpoch(input, 1), ct)
                .ConfigureAwait(false);
            return ToServiceResult(result);
        }

        private async ValueTask<ServiceResult> CommitDocumentAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            byte[] content,
            CancellationToken ct)
        {
            var request = new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                Kind = kind,
                Content = ByteString.From(content),
                ContentType = kind == WoTDocumentKindEnum.ThingModel
                    ? "application/tm+json"
                    : "application/td+json",
                Format = kind == WoTDocumentKindEnum.ThingModel ? "WoT-TM/1.1" : "WoT-TD/1.1",
                SetAsDefault = true
            };
            WotRegistryMutationResult result = await m_registry
                .UpsertResourceAsync(request, ct).ConfigureAwait(false);
            return result.Outcome is WoTOutcomeEnum.Rejected or WoTOutcomeEnum.Failed
                ? ServiceResult.Create(StatusCodes.BadInvalidState, result.Message)
                : ServiceResult.Good;
        }

        private WoTDocumentKindEnum KindForGroup(string groupId)
        {
            return string.Equals(NormalizeId(groupId), WotRegistryGroups.ThingModels, StringComparison.Ordinal)
                ? WoTDocumentKindEnum.ThingModel
                : WoTDocumentKindEnum.ThingDescription;
        }

        private static string NormalizeId(string id)
        {
            return WotRegistryService.NormalizeSegment(id, nameof(id));
        }

        private static string? GetString(ArrayOf<Variant> input, int index)
        {
            return index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is string s
                ? s : null;
        }

        private static bool? GetBoolOrNull(ArrayOf<Variant> input, int index)
        {
            return index < input.Count && input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) is bool b
                ? b : null;
        }

        private static long? OptionalEpoch(ArrayOf<Variant> input, int index)
        {
            if (index >= input.Count)
            {
                return null;
            }
            return input[index].AsBoxedObject(Variant.BoxingBehavior.Legacy) switch
            {
                uint u => u == 0 ? null : u,
                int i => i == 0 ? null : i,
                long l => l == 0 ? null : l,
                _ => null
            };
        }

        private static ServiceResult ToServiceResult(WotRegistryMutationResult result)
        {
            return result.Outcome switch
            {
                WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning or WoTOutcomeEnum.Unchanged
                    => ServiceResult.Good,
                WoTOutcomeEnum.Rejected
                    => ServiceResult.Create(StatusCodes.BadInvalidState, result.Message),
                _ => ServiceResult.Create(StatusCodes.BadNodeIdUnknown, result.Message)
            };
        }

        private const string RegistryNodeIdPath = "WoTRegistry";

        private sealed class Strategy : IXRegistryProjectionStrategy
        {
            public Strategy(WotRegistryProjection projection)
            {
                m_projection = projection;
            }

            public IXRegistryProjectionSnapshot Current => new SnapshotAdapter(m_projection.m_registry.Current);

            public GroupState CreateGroupNode(
                BaseObjectState registryNode,
                IXRegistryProjectionGroup group)
            {
                return m_projection.CreateGroupNode(registryNode, ((GroupAdapter)group).Group);
            }

            public ResourceState CreateResourceNode(
                GroupState groupNode,
                IXRegistryProjectionResource resource)
            {
                return m_projection.CreateResourceNode(groupNode, ((ResourceAdapter)resource).Resource);
            }

            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
            }

            public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
            {
                m_projection.ConfigureResourceNode(node, ((ResourceAdapter)resource).Resource);
            }

            public IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                if (node is not WoTDocumentState document)
                {
                    return null;
                }
                WotResource wotResource = ((ResourceAdapter)resource).Resource;
                return new ResourceFileAdapter(
                    m_projection.CreateResourceFile(document, wotResource),
                    wotResource);
            }

            public async ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                WotResourceGroup? group = await m_projection.m_registry
                    .TryCreateGroupAsync(groupId, m_projection.KindForGroup(groupId), cancellationToken: ct)
                    .ConfigureAwait(false);
                return group is null ? null : new GroupAdapter(group);
            }

            public async ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                bool existed = m_projection.m_registry.Current.FindGroup(NormalizeId(groupId)) is not null;
                WotResourceGroup group = await m_projection.m_registry
                    .GetOrCreateGroupAsync(groupId, m_projection.KindForGroup(groupId), cancellationToken: ct)
                    .ConfigureAwait(false);
                return (new GroupAdapter(group), !existed);
            }

            public async ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                WotResourceGroup? group = m_projection.m_registry.Current.FindGroup(groupId);
                WotResource? resource = await m_projection.m_registry
                    .TryCreateResourceAsync(groupId, resourceId,
                        group?.Kind ?? m_projection.KindForGroup(groupId), ct)
                    .ConfigureAwait(false);
                return resource is null ? null : new ResourceAdapter(resource);
            }

            public async ValueTask<(IXRegistryProjectionResource Resource, bool Created)> GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                WotResourceGroup? group = m_projection.m_registry.Current.FindGroup(groupId);
                (WotResource resource, bool created) = await m_projection.m_registry
                    .GetOrCreateResourceAsync(groupId, resourceId,
                        group?.Kind ?? m_projection.KindForGroup(groupId), ct)
                    .ConfigureAwait(false);
                return (new ResourceAdapter(resource), created);
            }

            public async ValueTask<ServiceResult> DeleteGroupAsync(
                string groupId,
                long? epoch,
                CancellationToken ct)
            {
                WotRegistryMutationResult result = await m_projection.m_registry
                    .DeleteGroupAsync(groupId, epoch, ct).ConfigureAwait(false);
                return ToServiceResult(result);
            }

            public async ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                WotRegistryMutationResult result = await m_projection.m_registry
                    .DeleteResourceAsync(groupId, resourceId, epoch, ct).ConfigureAwait(false);
                return ToServiceResult(result);
            }

            public async ValueTask<ServiceResult> AddRegistryLabelAsync(
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .AddRegistryLabelAsync(key, value, epoch, ct).ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> RemoveRegistryLabelAsync(
                string key,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .RemoveRegistryLabelAsync(key, epoch, ct).ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> AddGroupLabelAsync(
                string groupId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .AddGroupLabelAsync(groupId, key, value, epoch, ct).ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> RemoveGroupLabelAsync(
                string groupId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .RemoveGroupLabelAsync(groupId, key, epoch, ct).ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> AddResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .AddResourceLabelAsync(groupId, resourceId, key, value, epoch, ct)
                        .ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> RemoveResourceLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                try
                {
                    return ToServiceResult(await m_projection.m_registry
                        .RemoveResourceLabelAsync(groupId, resourceId, key, epoch, ct)
                        .ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            private readonly WotRegistryProjection m_projection;
        }

        private sealed class SnapshotAdapter : IXRegistryProjectionSnapshot
        {
            public SnapshotAdapter(WotRegistrySnapshot snapshot)
            {
                m_snapshot = snapshot;
            }

            public ImmutableSortedDictionary<string, string> Labels => m_snapshot.Labels;

            public IEnumerable<IXRegistryProjectionGroup> Groups
                => m_snapshot.Groups.Values.Select(group => new GroupAdapter(group));

            private readonly WotRegistrySnapshot m_snapshot;
        }

        private sealed class GroupAdapter : IXRegistryProjectionGroup
        {
            public GroupAdapter(WotResourceGroup group)
            {
                Group = group;
            }

            public WotResourceGroup Group { get; }
            public string GroupId => Group.GroupId;
            public string Xid => Group.Xid;
            public string Name => Group.Name;
            public string Description => Group.Description;
            public long Epoch => Group.Epoch;
            public ImmutableSortedDictionary<string, string> Labels => Group.Labels;

            public IEnumerable<IXRegistryProjectionResource> Resources
                => Group.Resources.Values.Select(resource => new ResourceAdapter(resource));
        }

        private sealed class ResourceAdapter : IXRegistryProjectionResource
        {
            public ResourceAdapter(WotResource resource)
            {
                Resource = resource;
            }

            public WotResource Resource { get; }
            public string GroupId => Resource.GroupId;
            public string ResourceId => Resource.ResourceId;
            public string Xid => Resource.Xid;
            public string Name => Resource.Name;
            public string Description => Resource.Description;
            public string VersionId => Resource.DefaultVersionId ?? string.Empty;
            public string Format => Resource.DefaultVersion?.Format ?? string.Empty;
            public string ContentType => Resource.DefaultVersion?.ContentType ?? "application/td+json";
            public long Epoch => Resource.Epoch;
            public DateTime CreatedAt => Resource.DefaultVersion?.CreatedAt ?? default;
            public DateTime ModifiedAt => Resource.DefaultVersion?.ModifiedAt ?? DateTime.UtcNow;
            public ImmutableSortedDictionary<string, string> Labels => Resource.Labels;
        }

        private sealed class ResourceFileAdapter : IXRegistryProjectedResourceFile
        {
            public ResourceFileAdapter(WotResourceFileManager file, WotResource resource)
            {
                m_file = file;
                ApplyResource(new ResourceAdapter(resource));
            }

            public ServiceResult TryOpenWriteHandle(ISystemContext context, out uint fileHandle)
            {
                NodeId sessionId = context is ISessionSystemContext sessionContext
                    ? sessionContext.SessionId.GetValueOrDefault()
                    : NodeId.Null;
                return m_file.TryOpenWriteHandle(sessionId, out fileHandle);
            }

            public void ApplyResource(IXRegistryProjectionResource resource)
            {
                WotResource wotResource = ((ResourceAdapter)resource).Resource;
                WotResourceVersion? version = wotResource.DefaultVersion;
                WotResourceVersion? active = wotResource.ActiveVersion ?? version;
                m_file.UpdatePersistedContent(active, version?.ContentType);
            }

            public void Dispose()
            {
                m_file.Dispose();
            }

            private readonly WotResourceFileManager m_file;
        }

        private readonly WotRegistryNodeManager m_manager;
        private readonly IWotRegistryService m_registry;
        private readonly WotRegistryServerOptions m_options;
        private readonly ushort m_modelNs;
        private readonly XRegistryProjectionEngine m_engine;
    }
}
