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
                manager.CheckManagementAccess,
                options.XRegistryEvents);
            m_strategy = registry is IWotVersionedRegistryService
                ? new VersionedStrategy(this)
                : new Strategy(this);
            m_engine = new XRegistryProjectionEngine(context, m_strategy, RegistryNodeIdPath);
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

        /// <summary>
        /// Reconciles only the browseable AddressSpace. Registry change
        /// transitions queued by the NodeManager remain the event authority.
        /// </summary>
        public ValueTask ReconcileProjectionAsync(CancellationToken ct)
        {
            return m_engine.ReconcileProjectionAsync(ct);
        }

        /// <summary>
        /// Reconciles the exact immutable registry transition supplied by a change event.
        /// </summary>
        public ValueTask ReconcileAsync(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot current,
            CancellationToken ct)
        {
            return m_engine.ReconcileAsync(
                m_strategy.CaptureProjectionGeneration(current),
                m_strategy.CreateEventSnapshot(previous),
                ct);
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

        private void ConfigureResourceNode(
            ResourceState node,
            WotResource resource,
            WotResourceVersion? version,
            bool concreteVersion)
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
            string versionId = concreteVersion
                ? version?.VersionId ?? string.Empty
                : string.Empty;
            document.Validate?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnValidateAsync(
                    groupId,
                    resourceId,
                    versionId,
                    c,
                    ot,
                    t);
            document.SetEnabled?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnSetEnabledAsync(groupId, resourceId, c, i, t);
            document.SetDefaultVersion?.OnCallMethod2Async =
                (c, m, o, i, ot, t) => OnSetDefaultVersionAsync(groupId, resourceId, c, i, t);

            ApplyWotResourceProperties(document, resource);
        }

        private void ApplyWotResourceProperties(WoTDocumentState node, WotResource resource)
        {
            ApplyWotResourceProperties(node, resource, resource.DefaultVersion);
        }

        private void ApplyWotResourceProperties(
            WoTDocumentState node,
            WotResource resource,
            WotResourceVersion? version)
        {

            XRegistryProjectionEngine.SetValue(node.DocumentKind, resource.Kind);
            XRegistryProjectionEngine.SetValue(node.Enabled, resource.Enabled);
            XRegistryProjectionEngine.SetValue(node.LoadState, resource.LoadState);
            XRegistryProjectionEngine.SetValue(node.DesiredVersionId, resource.DesiredVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.ActiveVersionId, resource.ActiveVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.IsDefault, version is not null &&
                string.Equals(version.VersionId, resource.DefaultVersionId, StringComparison.Ordinal));
            XRegistryProjectionEngine.SetValue(node.ContentDigest, version is null ? ByteString.Empty : version.Digest);
            if (node.ValidationOutcome is not null)
            {
                node.ValidationOutcome.Value = version?.Validation!;
            }
            XRegistryProjectionEngine.SetValue(node.MaterializedNodeCount, (uint)resource.MaterializedNodeCount);
            XRegistryProjectionEngine.SetValue(node.RootNodeId, resource.RootNodeId);
            XRegistryProjectionEngine.SetValue(node.RefreshGeneration, resource.RefreshGeneration);
            XRegistryProjectionEngine.SetValue(node.LastRefreshTime, (DateTimeUtc)resource.LastRefreshTime);

            if (node is ThingDescriptionFileState td)
            {
                bool useResourceDocumentId =
                    m_registry is not IWotVersionedRegistryService ||
                    resource.Versions.All(candidate =>
                        string.IsNullOrWhiteSpace(candidate.DocumentId));
                bool useResourceTitle =
                    m_registry is not IWotVersionedRegistryService ||
                    resource.Versions.All(candidate =>
                        string.IsNullOrWhiteSpace(candidate.Title));
                XRegistryProjectionEngine.SetValue(
                    td.ThingId,
                    SelectVersionMetadata(
                        version?.DocumentId,
                        useResourceDocumentId ? resource.ThingId : null));
                XRegistryProjectionEngine.SetValue(
                    td.ThingTitle,
                    SelectVersionMetadata(
                        version?.Title,
                        useResourceTitle ? resource.Title : null));
                XRegistryProjectionEngine.SetValue(
                    td.BaseUri,
                    version?.BaseUri ?? string.Empty);
            }
            else if (node is ThingModelFileState tmNode)
            {
                bool useResourceTitle =
                    m_registry is not IWotVersionedRegistryService ||
                    resource.Versions.All(candidate =>
                        string.IsNullOrWhiteSpace(candidate.Title));
                XRegistryProjectionEngine.SetValue(
                    tmNode.ModelTitle,
                    SelectVersionMetadata(
                        version?.Title,
                        useResourceTitle ? resource.Title : null));
                XRegistryProjectionEngine.SetValue(
                    tmNode.ModelVersion,
                    version?.ModelVersion ?? string.Empty);
                XRegistryProjectionEngine.SetValue(tmNode.DerivedTypeNodeId, resource.RootNodeId);
            }
        }

        private static string SelectVersionMetadata(string? value, string? fallback)
        {
            return !string.IsNullOrWhiteSpace(value)
                ? value!
                : !string.IsNullOrWhiteSpace(fallback)
                    ? fallback!
                    : string.Empty;
        }

        private WotResourceFileManager CreateResourceFile(WoTDocumentState node, WotResource resource)
        {
            return CreateResourceFile(node, resource, resource.DefaultVersion);
        }

        private WotResourceFileManager CreateResourceFile(
            WoTDocumentState node,
            WotResource resource,
            WotResourceVersion? version)
        {
            string groupId = resource.GroupId;
            string resourceId = resource.ResourceId;
            string versionId = version?.VersionId ?? string.Empty;
            WoTDocumentKindEnum kind = resource.Kind;
            return new WotResourceFileManager(
                node,
                m_options.Bounds.MaxOpenFileHandles,
                m_options.Bounds.MaxDocumentBytes,
                m_manager.CheckManagementAccess,
                (key, offset, count, token) => m_registry.ReadContentChunkAsync(key, offset, count, token),
                (bytes, baseline, baselineIncarnation, session, token) => CommitDocumentAsync(
                    groupId,
                    resourceId,
                    versionId,
                    kind,
                    bytes,
                    baseline,
                    baselineIncarnation,
                    token));
        }

        private async ValueTask<ServiceResult> OnValidateAsync(
            string groupId,
            string resourceId,
            string versionId,
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
                if (m_registry is IWotVersionedRegistryService versioned &&
                    !string.IsNullOrEmpty(versionId))
                {
                    outcome = await versioned.ValidateVersionAsync(
                            groupId,
                            resourceId,
                            versionId,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    WotResource? resource = m_registry.Current.FindResource(
                        groupId,
                        resourceId);
                    if (!string.IsNullOrEmpty(versionId) &&
                        !string.Equals(
                            resource?.DefaultVersionId,
                            versionId,
                            StringComparison.Ordinal))
                    {
                        return StatusCodes.BadNotSupported;
                    }
                    outcome = await m_registry.ValidateResourceAsync(groupId, resourceId, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (ServiceResultException ex)
            {
                return ex.Result;
            }
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
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

        private async ValueTask<WotResourceCommitResult> CommitDocumentAsync(
            string groupId,
            string resourceId,
            string versionId,
            WoTDocumentKindEnum kind,
            byte[] content,
            string baselineContentKey,
            Guid? baselineVersionIncarnation,
            CancellationToken ct)
        {
            var request = new WotUpsertResourceRequest
            {
                GroupId = groupId,
                ResourceId = resourceId,
                VersionId = versionId,
                ExpectedVersionDigestHex = baselineContentKey,
                ExpectedVersionIncarnation = baselineVersionIncarnation,
                Kind = kind,
                Content = ByteString.From(content),
                ContentType = kind == WoTDocumentKindEnum.ThingModel
                    ? "application/tm+json"
                    : "application/td+json",
                Format = kind == WoTDocumentKindEnum.ThingModel ? "WoT-TM/1.1" : "WoT-TD/1.1",
                SetAsDefault = false
            };
            WotRegistryMutationResult result = await m_registry
                .UpsertResourceAsync(request, ct).ConfigureAwait(false);
            ServiceResult serviceResult =
                result.Outcome is WoTOutcomeEnum.Rejected or WoTOutcomeEnum.Failed
                ? ServiceResult.Create(StatusCodes.BadInvalidState, result.Message)
                : ServiceResult.Good;
            WotResource? committedResource = ServiceResult.IsGood(serviceResult)
                ? result.Resource ?? m_registry.Current.FindResource(groupId, resourceId)
                : null;
            string? committedVersionId = string.IsNullOrEmpty(versionId)
                ? committedResource?.DesiredVersionId ?? committedResource?.DefaultVersionId
                : versionId;
            WotResourceVersion? committedVersion =
                committedResource?.FindVersion(committedVersionId);
            return new WotResourceCommitResult(serviceResult, committedVersion);
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

        private class Strategy :
            IXRegistryProjectionStrategy,
            IXRegistryProjectionGenerationProvider,
            IXRegistryProjectionEventMetadataProvider
        {
            public Strategy(WotRegistryProjection projection)
            {
                m_projection = projection;
            }

            public IXRegistryProjectionSnapshot Current => new SnapshotAdapter(
                m_projection.m_registry.Current,
                SupportsVersions);

            public XRegistryProjectionGeneration CaptureProjectionGeneration()
            {
                return CaptureProjectionGeneration(m_projection.m_registry.Current);
            }

            public XRegistryProjectionGeneration CaptureProjectionGeneration(
                WotRegistrySnapshot snapshot)
            {
                return new XRegistryProjectionGeneration(
                    new SnapshotAdapter(snapshot, SupportsVersions),
                    CreateEventSnapshot(snapshot));
            }

            public XRegistryProjectionEventSnapshot CaptureEventSnapshot()
            {
                return CreateEventSnapshot(m_projection.m_registry.Current);
            }

            public XRegistryProjectionEventSnapshot CreateEventSnapshot(
                WotRegistrySnapshot snapshot)
            {
                ImmutableArray<XRegistryProjectionEventGroup> groups = snapshot.Groups.Values
                    .OrderBy(group => group.GroupId, StringComparer.Ordinal)
                    .Select(CreateEventGroup)
                    .ToImmutableArray();
                return new XRegistryProjectionEventSnapshot(
                    "/",
                    checked((uint)snapshot.Generation),
                    snapshot.Labels,
                    groups);
            }

            private XRegistryProjectionEventGroup CreateEventGroup(WotResourceGroup group)
            {
                return new XRegistryProjectionEventGroup(
                    group.GroupId,
                    group.Xid,
                    checked((uint)group.Epoch),
                    group.Labels,
                    false,
                    group.Resources.Values
                        .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
                        .Select(CreateEventResource)
                        .ToImmutableArray())
                {
                    SourceNodeId = m_projection.GroupNodeId(group.GroupId),
                    SourceName = group.Name
                };
            }

            private XRegistryProjectionEventResource CreateEventResource(WotResource resource)
            {
                WotResourceVersion? defaultVersion = resource.DefaultVersion;
                return new XRegistryProjectionEventResource(
                    resource.GroupId,
                    resource.ResourceId,
                    resource.Xid,
                    checked((uint)(defaultVersion?.Epoch ?? 0)),
                    checked((uint)resource.MetaEpoch),
                    resource.MetaLabels,
                    false,
                    resource.DefaultVersionId,
                    resource.Versions
                        .Select(version => CreateEventVersion(resource, version))
                        .ToImmutableArray())
                {
                    SourceNodeId = defaultVersion is null
                        ? NodeId.Null
                        : m_projection.ResourceNodeId(
                            resource.GroupId,
                            resource.ResourceId,
                            defaultVersion.VersionId),
                    SourceName = resource.Name,
                    Name = resource.Name,
                    Description = resource.Description,
                    MetaCreatedAt = resource.MetaCreatedAt,
                    MetaModifiedAt = resource.MetaModifiedAt
                };
            }

            private XRegistryProjectionEventVersion CreateEventVersion(
                WotResource resource,
                WotResourceVersion version)
            {
                ImmutableSortedDictionary<string, string>.Builder attributes =
                    ImmutableSortedDictionary.CreateBuilder<string, string>(
                        StringComparer.Ordinal);
                string digestHex = version.DigestHex;
                if (version.HasContent && !string.IsNullOrEmpty(digestHex))
                {
                    attributes[m_projection.m_options.XRegistryEvents
                        .ResourceDocumentAttributeName] = digestHex;
                }
                if (!string.IsNullOrEmpty(version.ContentType))
                {
                    attributes["contenttype"] = version.ContentType;
                }
                if (!string.IsNullOrEmpty(version.Format))
                {
                    attributes["format"] = version.Format;
                }
                return new XRegistryProjectionEventVersion(
                    version.VersionId,
                    $"{resource.Xid}/versions/{version.VersionId}",
                    checked((uint)version.Epoch),
                    attributes.ToImmutable())
                {
                    SourceNodeId = m_projection.ResourceNodeId(
                        resource.GroupId,
                        resource.ResourceId,
                        version.VersionId),
                    SourceName = version.VersionId,
                    Labels = version.Labels,
                    CreatedAt = version.CreatedAt,
                    ModifiedAt = version.ModifiedAt
                };
            }

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
                return m_projection.CreateResourceNode(
                    groupNode,
                    ((IResourceAdapter)resource).Resource);
            }

            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
            }

            public void ConfigureResourceNode(ResourceState node, IXRegistryProjectionResource resource)
            {
                var adapter = (IResourceAdapter)resource;
                m_projection.ConfigureResourceNode(
                    node,
                    adapter.Resource,
                    adapter.Version,
                    adapter.IsConcreteVersion);
                if (node is WoTDocumentState document)
                {
                    m_projection.ApplyWotResourceProperties(
                        document,
                        adapter.Resource,
                        adapter.Version);
                }
            }

            public IXRegistryProjectedResourceFile? CreateResourceFile(
                ResourceState node,
                IXRegistryProjectionResource resource)
            {
                if (node is not WoTDocumentState document)
                {
                    return null;
                }
                var adapter = (IResourceAdapter)resource;
                return new ResourceFileAdapter(
                    m_projection.CreateResourceFile(
                        document,
                        adapter.Resource,
                        adapter.Version),
                    adapter.Resource,
                    adapter.Version?.VersionId ?? string.Empty);
            }

            protected bool SupportsVersions =>
                m_projection.m_registry is IWotVersionedRegistryService;

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
                WotResource? resource = await m_projection.m_registry
                    .TryCreateResourceAsync(
                        groupId,
                        resourceId,
                        m_projection.KindForGroup(groupId),
                        ct)
                    .ConfigureAwait(false);
                return resource is null ? null : new LegacyResourceAdapter(resource);
            }

            public async ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                WotResourceGroup? group = m_projection.m_registry.Current.FindGroup(groupId);
                if (m_projection.m_registry is not IWotVersionedRegistryService versioned)
                {
                    return null;
                }
                (WotResource Resource, WotResourceVersion Version)? created =
                    await versioned.TryCreateVersionAsync(
                        groupId,
                        resourceId,
                        versionId,
                        group?.Kind ?? m_projection.KindForGroup(groupId),
                        ct)
                    .ConfigureAwait(false);
                return created is null
                    ? null
                    : new ResourceAdapter(created.Value.Resource, created.Value.Version);
            }

            public async ValueTask<(IXRegistryProjectionResource Resource, bool Created)>
                GetOrCreateResourceAsync(
                string groupId,
                string resourceId,
                CancellationToken ct)
            {
                (WotResource resource, bool created) = await m_projection.m_registry
                    .GetOrCreateResourceAsync(
                        groupId,
                        resourceId,
                        m_projection.KindForGroup(groupId),
                        ct)
                    .ConfigureAwait(false);
                return (new LegacyResourceAdapter(resource), created);
            }

            public async ValueTask<(IXRegistryProjectionResource Resource, bool Created)>
                GetOrCreateResourceAsync(
                    string groupId,
                    string resourceId,
                    string versionId,
                    CancellationToken ct)
            {
                WotResourceGroup? group = m_projection.m_registry.Current.FindGroup(groupId);
                if (m_projection.m_registry is not IWotVersionedRegistryService versioned)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The registry service does not support Version-aware creation.");
                }
                (WotResource resource, WotResourceVersion version, bool created) =
                    await versioned.GetOrCreateVersionAsync(
                        groupId,
                        resourceId,
                        versionId,
                        group?.Kind ?? m_projection.KindForGroup(groupId),
                        ct)
                    .ConfigureAwait(false);
                return (new ResourceAdapter(resource, version), created);
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

            public async ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                CancellationToken ct)
            {
                if (m_projection.m_registry is not IWotVersionedRegistryService versioned)
                {
                    return StatusCodes.BadNotSupported;
                }
                WotRegistryMutationResult result = await versioned.DeleteProjectedEntityAsync(
                        groupId,
                        resourceId,
                        versionId,
                        deleteLogicalResource,
                        epoch,
                        ct)
                    .ConfigureAwait(false);
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

            public async ValueTask<ServiceResult> AddVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                if (m_projection.m_registry is not IWotVersionedRegistryService versioned)
                {
                    return StatusCodes.BadNotSupported;
                }
                try
                {
                    return ToServiceResult(await versioned.AddVersionLabelAsync(
                            groupId,
                            resourceId,
                            versionId,
                            key,
                            value,
                            epoch,
                            ct)
                        .ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public async ValueTask<ServiceResult> RemoveVersionLabelAsync(
                string groupId,
                string resourceId,
                string versionId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                if (m_projection.m_registry is not IWotVersionedRegistryService versioned)
                {
                    return StatusCodes.BadNotSupported;
                }
                try
                {
                    return ToServiceResult(await versioned.RemoveVersionLabelAsync(
                            groupId,
                            resourceId,
                            versionId,
                            key,
                            epoch,
                            ct)
                        .ConfigureAwait(false));
                }
                catch (ServiceResultException ex)
                {
                    return ex.Result;
                }
            }

            public ValueTask<ServiceResult> AddResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                string value,
                long? epoch,
                CancellationToken ct)
            {
                return AddResourceLabelAsync(groupId, resourceId, key, value, epoch, ct);
            }

            public ValueTask<ServiceResult> RemoveResourceMetaLabelAsync(
                string groupId,
                string resourceId,
                string key,
                long? epoch,
                CancellationToken ct)
            {
                return RemoveResourceLabelAsync(groupId, resourceId, key, epoch, ct);
            }

            private readonly WotRegistryProjection m_projection;
        }

        private sealed class VersionedStrategy :
            Strategy,
            IXRegistryVersionedProjectionStrategy
        {
            public VersionedStrategy(WotRegistryProjection projection)
                : base(projection)
            {
            }
        }

        private NodeId GroupNodeId(string groupId)
        {
            return new NodeId(
                $"{RegistryNodeIdPath}/groups/{groupId}",
                m_modelNs);
        }

        private NodeId ResourceNodeId(
            string groupId,
            string resourceId,
            string versionId)
        {
            string path = $"{RegistryNodeIdPath}/groups/{groupId}/resources/{resourceId}";
            if (m_registry is not IWotVersionedRegistryService)
            {
                return new NodeId(path, m_modelNs);
            }
            return new NodeId(
                $"{path}/versions/{versionId}",
                m_modelNs);
        }

        private sealed class SnapshotAdapter : IXRegistryProjectionSnapshot
        {
            public SnapshotAdapter(
                WotRegistrySnapshot snapshot,
                bool versioned)
            {
                m_snapshot = snapshot;
                m_versioned = versioned;
            }

            public ImmutableSortedDictionary<string, string> Labels => m_snapshot.Labels;

            public IEnumerable<IXRegistryProjectionGroup> Groups
                => m_snapshot.Groups.Values.Select(
                    group => new GroupAdapter(group, m_versioned));

            private readonly WotRegistrySnapshot m_snapshot;
            private readonly bool m_versioned;
        }

        private sealed class GroupAdapter : IXRegistryProjectionGroup
        {
            public GroupAdapter(
                WotResourceGroup group,
                bool versioned = true)
            {
                Group = group;
                m_versioned = versioned;
            }

            public WotResourceGroup Group { get; }
            public string GroupId => Group.GroupId;
            public string Xid => Group.Xid;
            public string Name => Group.Name;
            public string Description => Group.Description;
            public long Epoch => Group.Epoch;
            public ImmutableSortedDictionary<string, string> Labels => Group.Labels;

            public IEnumerable<IXRegistryProjectionResource> Resources
                => Group.Resources.Values
                    .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
                    .SelectMany(ProjectResource);

            private IEnumerable<IXRegistryProjectionResource> ProjectResource(
                WotResource resource)
            {
                if (!m_versioned)
                {
                    yield return new LegacyResourceAdapter(resource);
                    yield break;
                }
                foreach (WotResourceVersion version in resource.Versions)
                {
                    yield return new ResourceAdapter(resource, version);
                }
            }

            private readonly bool m_versioned;
        }

        private interface IResourceAdapter
        {
            WotResource Resource { get; }
            WotResourceVersion? Version { get; }
            bool IsConcreteVersion { get; }
        }

        private sealed class ResourceAdapter :
            IXRegistryProjectionResource,
            IXRegistryProjectionResourceMeta,
            IResourceAdapter
        {
            public ResourceAdapter(WotResource resource, WotResourceVersion version)
            {
                Resource = resource;
                Version = version;
            }

            public WotResource Resource { get; }
            public WotResourceVersion Version { get; }
            public bool IsConcreteVersion => true;
            public string GroupId => Resource.GroupId;
            public string ResourceId => Resource.ResourceId;
            public string Xid => $"{Resource.Xid}/versions/{Version.VersionId}";
            public string Name => Resource.Name;
            public string Description => Resource.Description;
            public string VersionId => Version.VersionId;
            public string Format => Version.Format;
            public string ContentType => Version.ContentType;
            public long Epoch => Version.Epoch;
            public DateTime CreatedAt => Version.CreatedAt;
            public DateTime ModifiedAt => Version.ModifiedAt;
            public ImmutableSortedDictionary<string, string> Labels => Version.Labels;
            public long MetaEpoch => Resource.MetaEpoch;
            public ImmutableSortedDictionary<string, string> MetaLabels => Resource.MetaLabels;
            public DateTime MetaCreatedAt => Resource.MetaCreatedAt;
            public DateTime MetaModifiedAt => Resource.MetaModifiedAt;
            public bool IsDefaultVersion => string.Equals(
                Resource.DefaultVersionId,
                Version.VersionId,
                StringComparison.Ordinal);
        }

        private sealed class LegacyResourceAdapter :
            IXRegistryProjectionResource,
            IXRegistryProjectionResourceMeta,
            IResourceAdapter
        {
            public LegacyResourceAdapter(WotResource resource)
            {
                Resource = resource;
                Version = resource.DefaultVersion;
            }

            public WotResource Resource { get; }
            public WotResourceVersion? Version { get; }
            public bool IsConcreteVersion => false;
            public string GroupId => Resource.GroupId;
            public string ResourceId => Resource.ResourceId;
            public string Xid => Resource.Xid;
            public string Name => Resource.Name;
            public string Description => Resource.Description;
            public string VersionId => Version?.VersionId ?? string.Empty;
            public string Format => Version?.Format ?? string.Empty;
            public string ContentType => Version?.ContentType ?? string.Empty;
            public long Epoch => Resource.MetaEpoch;
            public DateTime CreatedAt => Version?.CreatedAt ?? Resource.MetaCreatedAt;
            public DateTime ModifiedAt => Version?.ModifiedAt ?? Resource.MetaModifiedAt;
            public ImmutableSortedDictionary<string, string> Labels => Resource.MetaLabels;
            public long MetaEpoch => Resource.MetaEpoch;
            public ImmutableSortedDictionary<string, string> MetaLabels => Resource.MetaLabels;
            public DateTime MetaCreatedAt => Resource.MetaCreatedAt;
            public DateTime MetaModifiedAt => Resource.MetaModifiedAt;
            public bool IsDefaultVersion => true;
        }

        private sealed class ResourceFileAdapter :
            IXRegistryProjectedResourceFile,
            IXRegistryProjectedContentlessResourceFile,
            IXRegistryProjectedResourceFileHandleForwarder
        {
            public ResourceFileAdapter(
                WotResourceFileManager file,
                WotResource resource,
                string versionId)
            {
                m_file = file;
                m_versionId = versionId;
                WotResourceVersion? version = resource.FindVersion(versionId);
                ApplyResource(version is null
                    ? new LegacyResourceAdapter(resource)
                    : new ResourceAdapter(resource, version));
            }

            public ServiceResult TryOpenWriteHandle(ISystemContext context, out uint fileHandle)
            {
                NodeId sessionId = context is ISessionSystemContext sessionContext
                    ? sessionContext.SessionId.GetValueOrDefault()
                    : NodeId.Null;
                return m_file.TryOpenWriteHandle(sessionId, out fileHandle);
            }

            public ServiceResult TryOpenContentlessWriteHandle(
                ISystemContext context,
                out uint fileHandle)
            {
                NodeId sessionId = context is ISessionSystemContext sessionContext
                    ? sessionContext.SessionId.GetValueOrDefault()
                    : NodeId.Null;
                return m_file.TryOpenContentlessWriteHandle(sessionId, out fileHandle);
            }

            public void ApplyResource(IXRegistryProjectionResource resource)
            {
                var adapter = (IResourceAdapter)resource;
                WotResourceVersion? version = string.IsNullOrEmpty(m_versionId)
                    ? adapter.Resource.DefaultVersion
                    : adapter.Resource.FindVersion(m_versionId);
                m_file.UpdatePersistedContent(version, version?.ContentType);
            }

            public void Dispose()
            {
                m_file.Dispose();
            }

            // --- IXRegistryProjectedResourceFileHandleForwarder ---

            ServiceResult IXRegistryProjectedResourceFileHandleForwarder.ForwardOpen(
                ISystemContext context, MethodState method, NodeId objectId,
                byte mode, ref uint fileHandle)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardOpen(context, method, objectId, mode, ref fileHandle);
            }

            ValueTask<ServiceResult> IXRegistryProjectedResourceFileHandleForwarder.ForwardCloseAsync(
                ISystemContext context, MethodState method, NodeId objectId,
                uint fileHandle, CancellationToken cancellationToken)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardCloseAsync(context, method, objectId, fileHandle, cancellationToken);
            }

            ValueTask<(ServiceResult Status, ByteString Data)>
                IXRegistryProjectedResourceFileHandleForwarder.ForwardReadAsync(
                ISystemContext context, MethodState method, NodeId objectId,
                uint fileHandle, int length, CancellationToken cancellationToken)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardReadAsync(context, method, objectId, fileHandle, length, cancellationToken);
            }

            ServiceResult IXRegistryProjectedResourceFileHandleForwarder.ForwardWrite(
                ISystemContext context, MethodState method, NodeId objectId,
                uint fileHandle, ByteString data)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardWrite(context, method, objectId, fileHandle, data);
            }

            ServiceResult IXRegistryProjectedResourceFileHandleForwarder.ForwardGetPosition(
                ISystemContext context, MethodState method, NodeId objectId,
                uint fileHandle, ref ulong position)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardGetPosition(context, method, objectId, fileHandle, ref position);
            }

            ServiceResult IXRegistryProjectedResourceFileHandleForwarder.ForwardSetPosition(
                ISystemContext context, MethodState method, NodeId objectId,
                uint fileHandle, ulong position)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardSetPosition(context, method, objectId, fileHandle, position);
            }

            private readonly WotResourceFileManager m_file;
            private readonly string m_versionId;
        }

        private readonly WotRegistryNodeManager m_manager;
        private readonly IWotRegistryService m_registry;
        private readonly WotRegistryServerOptions m_options;
        private readonly ushort m_modelNs;
        private readonly Strategy m_strategy;
        private readonly XRegistryProjectionEngine m_engine;
    }
}
