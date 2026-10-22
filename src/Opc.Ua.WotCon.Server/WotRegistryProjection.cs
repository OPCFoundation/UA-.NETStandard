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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Adapts the WoT Connectivity registry service to the shared xRegistry projection engine.
    /// </summary>
    internal sealed partial class WotRegistryProjection : IDisposable
    {
        /// <summary>
        /// Initializes a new <c>WotRegistryProjection</c> instance.
        /// </summary>
        /// <param name="manager">The manager used to initialize the instance.</param>
        /// <param name="registry">The registry used to initialize the instance.</param>
        /// <param name="options">The WoT registry server options used to initialize the instance.</param>
        public WotRegistryProjection(
            WotRegistryNodeManager manager,
            IWotRegistryService registry,
            WotRegistryServerOptions options)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = manager.Server.Telemetry.CreateLogger<WotRegistryProjection>();
            m_modelNs = (ushort)manager.Server.NamespaceUris.GetIndex(Namespaces.WotCon);
            var context = new XRegistryProjectionContext(
                manager.SystemContext,
                manager.Server.NamespaceUris,
                m_modelNs,
                manager.AddPredefinedNodeAsync,
                async (nodeId, ct) => await manager.DeleteNodeAsync(manager.SystemContext, nodeId, ct)
                    .ConfigureAwait(false),
                manager.CheckManagementAccess,
                options.XRegistryEvents)
            {
                ProjectionDispatcher = manager.DispatchProjectionAsync
            };
            m_strategy = UsesVersionedProjection
                ? new VersionedStrategy(this)
                : new Strategy(this);
            m_engine = new XRegistryProjectionEngine(context, m_strategy, RegistryNodeIdPath);
        }

        private bool UsesVersionedProjection => m_registry is IWotVersionedRegistryService or IWotTypedRegistryService;

        /// <summary>
        /// Binds the projection to the well-known registry Object.
        /// </summary>
        /// <param name="ct">The cancellation token passed to the asynchronous operation.</param>
        /// <param name="registryNode">The registry node.</param>
        public ValueTask AttachAsync(BaseObjectState registryNode, CancellationToken ct)
        {
            WireTypedRegistryMethods(registryNode);
            return m_engine.AttachAsync(registryNode, ct);
        }

        /// <summary>
        /// Finds the exact Version used as a WoT failure source without substituting an ancestor.
        /// </summary>
        /// <param name="xid">The resource identifier.</param>
        /// <param name="versionId">The version ID.</param>
        public NodeState EventSourceForFailure(string? xid, string? versionId)
        {
            if (!UsesVersionedProjection)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "WoT failure events require an exact-Version registry projection.");
            }
            if (string.IsNullOrEmpty(xid) || string.IsNullOrEmpty(versionId))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "A WoT failure event requires both Resource Xid and VersionId.");
            }
            NodeState source = m_engine.EventSourceFor($"{xid}/versions/{versionId}");
            if (source is not ResourceState)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                    "The exact Version for the WoT failure event is not projected.");
            }
            return source;
        }

        /// <summary>
        /// Gets the node identifier of a resource version in the projection.
        /// </summary>
        /// <param name="resource">The WoT resource.</param>
        /// <param name="version">The WoT resource version.</param>
        internal ExpandedNodeId GetVersionNodeId(WotResource resource, WotResourceVersion version)
        {
            if (!UsesVersionedProjection)
            {
                return ExpandedNodeId.Null;
            }
            NodeId nodeId = ResourceNodeId(resource.GroupId, resource.ResourceId, version.VersionId);
            return m_manager.FindPredefinedNode<WoTDocumentState>(nodeId) is null
                ? ExpandedNodeId.Null
                : NodeId.ToExpandedNodeId(nodeId, m_manager.Server.NamespaceUris);
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
        /// Mutation handlers await this before returning so callers can read
        /// the committed state without waiting for background reconciliation.
        /// </summary>
        public ValueTask ReconcileProjectionAsync(CancellationToken ct)
        {
            return m_engine.ReconcileProjectionAsync(ct);
        }

        /// <summary>
        /// Discards the closing Session's Version handles and logical Resource pins without committing writes.
        /// </summary>
        /// <param name="ct">The cancellation token passed to the asynchronous operation.</param>
        /// <param name="sessionId">The session identifier.</param>
        public ValueTask DiscardSessionAsync(NodeId sessionId, CancellationToken ct)
        {
            return m_engine.DiscardSessionAsync(sessionId, ct);
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

        /// <summary>
        /// Links projected method argument properties using the XRegistry projection implementation.
        /// </summary>
        /// <param name="node">The source node associated with the registration or view check.</param>
        /// <param name="context">The system context used for the operation.</param>
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
            concreteVersion &= document.Versions is null;

            if (document.Versions is { } versions)
            {
                versions.TypeDefinitionId = ExpandedNodeId.ToNodeId(
                    resource.Kind == WoTDocumentKindEnum.ThingModel
                        ? ObjectTypeIds.ThingModelVersionsType : ObjectTypeIds.ThingDescriptionVersionsType,
                    m_manager.Server.NamespaceUris);
            }

            document.AddDesiredVersionId(m_manager.SystemContext)
                .AddActiveVersionId(m_manager.SystemContext)
                .AddIsDefault(m_manager.SystemContext)
                .AddContentDigest(m_manager.SystemContext)
                .AddValidationOutcome(m_manager.SystemContext)
                .AddMaterializedNodeCount(m_manager.SystemContext)
                .AddRootNodeId(m_manager.SystemContext)
                .AddRefreshGeneration(m_manager.SystemContext)
                .AddLastRefreshTime(m_manager.SystemContext)
                .AddDependencySnapshot(m_manager.SystemContext)
                .AddLastDependencyAttempt(m_manager.SystemContext)
                .AddValidate(m_manager.SystemContext)
                .AddSetEnabled(m_manager.SystemContext)
                .AddSetDefaultVersion(m_manager.SystemContext);

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
            document.DependencySnapshot!.OnSimpleReadValueAsync =
                (context, _, ct) => ReadDependencyObservationAsync(context, groupId, resourceId, versionId, false, ct);
            document.LastDependencyAttempt!.OnSimpleReadValueAsync =
                (context, _, ct) => ReadDependencyObservationAsync(context, groupId, resourceId, versionId, true, ct);
            BindPublishedResourceProperties(document, resource, versionId);

            // The versioned strategy supplies a Version adapter for the logical Resource too.
            if (!concreteVersion || document.Versions is not null)
            {
                document.AddProjectionMembershipDigest(m_manager.SystemContext);
                document.ProjectionMembershipDigest!.OnSimpleReadValueAsync =
                    (context, _, ct) => ReadProjectionMembershipDigestAsync(context, document.NodeId, resource.Xid, ct);
            }

            ApplyWotResourceProperties(document, resource, version, concreteVersion);
        }

        private ValueTask<AttributeSimpleReadResult> ReadProjectionMembershipDigestAsync(
            ISystemContext context, NodeId resourceNodeId, string resourceXid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context is ServerSystemContext { OperationContext: not null })
            {
                foreach (IAsyncNodeManager manager in m_manager.Server.NodeManager.AsyncNodeManagers)
                {
                    if (manager is IWotCanonicalViewReadImage image &&
                        image.TryGetMembershipDigest(resourceNodeId, out ByteString capturedDigest))
                    {
                        return new ValueTask<AttributeSimpleReadResult>(
                            new AttributeSimpleReadResult(ServiceResult.Good, Variant.From(capturedDigest)));
                    }
                }
                return new ValueTask<AttributeSimpleReadResult>(
                    new AttributeSimpleReadResult(StatusCodes.BadWaitingForInitialData, Variant.Null));
            }
            WotRegistrySnapshot snapshot = m_registry.Current;
            ByteString payload = snapshot.CanonicalViewGraphState;
            if (!payload.IsNull && payload.Length != 0)
            {
                ImmutableDictionary<string, ByteString> digests = m_membershipDigests.GetValue(
                    snapshot, static current => new Lazy<ImmutableDictionary<string, ByteString>>(
                        () => CreateMembershipDigestLookup(current.CanonicalViewGraphState),
                        LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                if (digests.TryGetValue(resourceXid, out ByteString digest))
                {
                    return new ValueTask<AttributeSimpleReadResult>(
                        new AttributeSimpleReadResult(ServiceResult.Good, Variant.From(digest)));
                }
            }
            return new ValueTask<AttributeSimpleReadResult>(
                new AttributeSimpleReadResult(StatusCodes.BadWaitingForInitialData, Variant.Null));
        }

        private static ImmutableDictionary<string, ByteString> CreateMembershipDigestLookup(ByteString payload)
        {
            var digests = ImmutableDictionary.CreateBuilder<string, ByteString>(StringComparer.Ordinal);
            foreach (WotCanonicalViewPublication view in WotCanonicalViewState.Parse(payload).Views)
            {
                if (view.Active)
                {
                    digests.Add(view.ResourceXid, view.MembershipDigest);
                }
            }
            return digests.ToImmutable();
        }

        private async ValueTask<AttributeSimpleReadResult> ReadDependencyObservationAsync(
            ISystemContext context,
            string groupId,
            string resourceId,
            string versionId,
            bool attempt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ServiceResult status = m_manager.CheckManagementAccess(
                context, attempt ? "ReadLastDependencyAttempt" : "ReadDependencySnapshot");
            Variant value = Variant.Null;
            if (ServiceResult.IsGood(status))
            {
                if (!m_manager.Coordinator.SupportsDependencySnapshots)
                {
                    status = StatusCodes.BadNotSupported;
                }
                else
                {
                    WotResource? resource = m_registry.Current.FindResource(groupId, resourceId);
                    WotResourceVersion? version = string.IsNullOrEmpty(versionId)
                        ? resource?.DefaultVersion
                        : resource?.FindVersion(versionId);
                    if (resource is null || version is null)
                    {
                        status = StatusCodes.BadNodeIdUnknown;
                    }
                    else
                    {
                        WotDependencySnapshot? observation = attempt
                            ? version.LastDependencyAttempt : version.DependencySnapshot;
                        if (observation is null)
                        {
                            status = StatusCodes.BadWaitingForInitialData;
                        }
                        else
                        {
                            if (context is SessionSystemContext
                                {
                                    OperationContext: OperationContext operationContext
                                })
                            {
                                status = await ValidateDependencyObservationAccessAsync(
                                    operationContext,
                                    groupId,
                                    resourceId,
                                    versionId,
                                    resource,
                                    version,
                                    observation,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            if (ServiceResult.IsGood(status))
                            {
                                value = Variant.FromStructure(observation.ToDataType());
                            }
                        }
                    }
                }
            }
            return new AttributeSimpleReadResult(status, value);
        }

        /// <summary>
        /// Authorizes complete local origin and exact-Version pins before disclosing an observation.
        /// </summary>
        /// <param name="operationContext">The remote caller's operation context.</param>
        /// <param name="groupId">The source group identity.</param>
        /// <param name="resourceId">The source resource identity.</param>
        /// <param name="versionId">The requested exact Version, or empty for the default Version.</param>
        /// <param name="sourceResource">The source resource captured for this read.</param>
        /// <param name="sourceVersion">The source Version captured for this read.</param>
        /// <param name="observation">The complete observation to authorize.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async ValueTask<ServiceResult> ValidateDependencyObservationAccessAsync(
            OperationContext operationContext,
            string groupId,
            string resourceId,
            string versionId,
            WotResource sourceResource,
            WotResourceVersion sourceVersion,
            WotDependencySnapshot observation,
            CancellationToken cancellationToken)
        {
            var checkedNodeIds = new HashSet<NodeId>();
            WotRegistrySnapshot registrySnapshot = m_registry.Current;
            WotResource? currentSourceResource = registrySnapshot.FindResource(groupId, resourceId);
            WotResourceVersion? currentSourceVersion = string.IsNullOrEmpty(versionId)
                ? currentSourceResource?.DefaultVersion
                : currentSourceResource?.FindVersion(versionId);
            if (!ReferenceEquals(currentSourceResource, sourceResource) ||
                !ReferenceEquals(currentSourceVersion, sourceVersion))
            {
                return StatusCodes.BadUserAccessDenied;
            }

            async ValueTask<ServiceResult> ValidateNodeAsync(NodeId nodeId)
            {
                if (!checkedNodeIds.Add(nodeId))
                {
                    return ServiceResult.Good;
                }
                if (m_manager.FindPredefinedNode<NodeState>(nodeId) is null)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                ServiceResult permission;
                try
                {
                    permission = await m_manager.ValidateRolePermissionsAsync(
                        operationContext,
                        nodeId,
                        PermissionType.Read | PermissionType.Browse,
                        cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsGood(permission))
                    {
                        var handle = await m_manager.GetManagerHandleAsync(
                            nodeId, cancellationToken).ConfigureAwait(false);
                        if (handle is null)
                        {
                            return StatusCodes.BadUserAccessDenied;
                        }
                        (bool handled, ServiceResult? result) = await m_manager.TryValidatePermissionsAsync(
                            operationContext,
                            handle,
                            PermissionType.Read | PermissionType.Browse,
                            null,
                            permissionsOnly: true,
                            logger: null,
                            validateAccessRestrictions: true,
                            cancellationToken).ConfigureAwait(false);
                        permission = handled && result is not null
                            ? result
                            : new ServiceResult(StatusCodes.BadUserAccessDenied);
                    }
                }
                catch (ServiceResultException)
                {
                    return StatusCodes.BadUserAccessDenied;
                }
                return ServiceResult.IsGood(permission)
                    ? ServiceResult.Good
                    : new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            WotRegistryOrigin? configuredOrigin = m_manager.Coordinator.RegistryOrigin;
            ServiceResult sourceGroupPermission = await ValidateNodeAsync(
                GroupNodeId(groupId)).ConfigureAwait(false);
            if (ServiceResult.IsBad(sourceGroupPermission))
            {
                return sourceGroupPermission;
            }
            ServiceResult sourceVersionPermission = await ValidateNodeAsync(
                ResourceNodeId(groupId, resourceId, sourceVersion.VersionId)).ConfigureAwait(false);
            if (ServiceResult.IsBad(sourceVersionPermission))
            {
                return sourceVersionPermission;
            }
            var checkedIdentities = new HashSet<(
                string OriginUri,
                string ServerUri,
                ExpandedNodeId RegistryNodeId,
                string VersionXid,
                ExpandedNodeId VersionNodeId)>();

            foreach (WotDependencyTargetPin target in observation.Targets.ToList())
            {
                WotRegistryOrigin? origin = target.OriginRegistry;
                if (origin is null ||
                    configuredOrigin is null ||
                    !string.Equals(origin.OriginUri, configuredOrigin.OriginUri, StringComparison.Ordinal) ||
                    !string.Equals(origin.ServerUri, configuredOrigin.ServerUri, StringComparison.Ordinal) ||
                    !origin.RegistryNodeId.Equals(configuredOrigin.RegistryNodeId) ||
                    target.VersionNodeId.IsNull ||
                    target.VersionNodeId.ServerIndex != 0)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                var identity = (
                    origin.OriginUri,
                    origin.ServerUri,
                    origin.RegistryNodeId,
                    target.VersionXid,
                    target.VersionNodeId);
                if (!checkedIdentities.Add(identity))
                {
                    continue;
                }

                NodeId originNodeId = ExpandedNodeId.ToNodeId(
                    origin.RegistryNodeId, m_manager.Server.NamespaceUris);
                NodeId targetVersionNodeId = ExpandedNodeId.ToNodeId(
                    target.VersionNodeId, m_manager.Server.NamespaceUris);
                if (originNodeId.IsNull ||
                    targetVersionNodeId.IsNull ||
                    !origin.RegistryNodeId.Equals(NodeId.ToExpandedNodeId(
                        originNodeId, m_manager.Server.NamespaceUris)))
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                WotResource? targetResource = null;
                WotResourceVersion? targetVersion = null;
                foreach (WotResource candidate in registrySnapshot.AllResources())
                {
                    foreach (WotResourceVersion candidateVersion in candidate.Versions)
                    {
                        if (!string.Equals(
                            WotDependencyGraph.VersionXid(candidate, candidateVersion),
                            target.VersionXid,
                            StringComparison.Ordinal))
                        {
                            continue;
                        }
                        if (targetVersion is not null)
                        {
                            return StatusCodes.BadUserAccessDenied;
                        }
                        targetResource = candidate;
                        targetVersion = candidateVersion;
                    }
                }

                if (targetResource is null || targetVersion is null)
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                NodeId expectedVersionNodeId = ResourceNodeId(
                    targetResource.GroupId, targetResource.ResourceId, targetVersion.VersionId);
                if (!expectedVersionNodeId.Equals(targetVersionNodeId) ||
                    !target.VersionNodeId.Equals(NodeId.ToExpandedNodeId(
                        expectedVersionNodeId, m_manager.Server.NamespaceUris)))
                {
                    return StatusCodes.BadUserAccessDenied;
                }

                ServiceResult originPermission = await ValidateNodeAsync(originNodeId).ConfigureAwait(false);
                if (ServiceResult.IsBad(originPermission))
                {
                    return originPermission;
                }
                ServiceResult groupPermission = await ValidateNodeAsync(
                    GroupNodeId(targetResource.GroupId)).ConfigureAwait(false);
                if (ServiceResult.IsBad(groupPermission))
                {
                    return groupPermission;
                }
                ServiceResult versionPermission = await ValidateNodeAsync(
                    targetVersionNodeId).ConfigureAwait(false);
                if (ServiceResult.IsBad(versionPermission))
                {
                    return versionPermission;
                }
            }

            return ReferenceEquals(m_registry.Current, registrySnapshot) &&
                ReferenceEquals(m_manager.Coordinator.RegistryOrigin, configuredOrigin)
                ? ServiceResult.Good
                : new ServiceResult(StatusCodes.BadUserAccessDenied);
        }

        private void ApplyWotResourceProperties(
            WoTDocumentState node,
            WotResource resource,
            WotResourceVersion? version,
            bool concreteVersion)
        {
            XRegistryProjectionEngine.SetValue(node.DocumentKind, resource.Kind);
            XRegistryProjectionEngine.SetValue(node.Enabled, resource.Enabled);
            XRegistryProjectionEngine.SetValue(node.LoadState,
                concreteVersion && node.Versions is null && version?.HasValidationFailure == true
                    ? WoTLoadStateEnum.Failed : resource.LoadState);
            XRegistryProjectionEngine.SetValue(node.DesiredVersionId, resource.DesiredVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.ActiveVersionId, resource.ActiveVersionId ?? string.Empty);
            XRegistryProjectionEngine.SetValue(node.IsDefault, version is not null &&
                string.Equals(version.VersionId, resource.DefaultVersionId, StringComparison.Ordinal));
            XRegistryProjectionEngine.SetValue(node.ContentDigest, version is null ? ByteString.Empty : version.Digest);
            node.ValidationOutcome?.Value = version?.Validation!;
            XRegistryProjectionEngine.SetValue(node.MaterializedNodeCount, (uint)resource.MaterializedNodeCount);
            XRegistryProjectionEngine.SetValue(node.RootNodeId, resource.RootNodeId);
            XRegistryProjectionEngine.SetValue(node.RefreshGeneration, resource.RefreshGeneration);
            XRegistryProjectionEngine.SetValue(node.LastRefreshTime, (DateTimeUtc)resource.LastRefreshTime);

            if (node is ThingDescriptionFileState td)
            {
                bool useResourceDocumentId =
                    !UsesVersionedProjection ||
                    resource.Versions.All(candidate =>
                        string.IsNullOrWhiteSpace(candidate.DocumentId));
                bool useResourceTitle =
                    !UsesVersionedProjection ||
                    resource.Versions.All(candidate =>
                        string.IsNullOrWhiteSpace(candidate.Title));
                XRegistryProjectionEngine.SetValue(
                    td.ThingId,
                    resource.SourceId ??
                    SelectVersionMetadata(
                        version?.DocumentId, useResourceDocumentId ? resource.ThingId : null));
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
                XRegistryProjectionEngine.SetValue(tmNode.ModelId, resource.SourceId ?? string.Empty);
                bool useResourceTitle =
                    !UsesVersionedProjection ||
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
                m_registry.ReadContentChunkAsync,
                (bytes, baseline, baselineIncarnation, session, token) => CommitDocumentAsync(
                    groupId,
                    resourceId,
                    versionId,
                    kind,
                    bytes,
                    baseline,
                    baselineIncarnation,
                    token),
                acquireVersionLease: m_registry is IWotRegistryVersionLeaseProvider leases
                    ? (snapshot, token) => leases.AcquireVersionLeaseAsync(groupId, resourceId, snapshot, token)
                    : null);
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
            ServiceResult status = ServiceResult.Good;
            string selectedVersionId = string.IsNullOrEmpty(versionId)
                ? m_registry.Current.FindResource(groupId, resourceId)?.DefaultVersionId ?? string.Empty : versionId;
            try
            {
                if (m_registry is IWotVersionedRegistryService versioned &&
                    !string.IsNullOrEmpty(selectedVersionId))
                {
                    outcome = await versioned.ValidateVersionAsync(
                            groupId,
                            resourceId,
                            selectedVersionId,
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
            catch (WotRegistryCommitDurabilityUncertainException ex)
            {
                m_logger.ValidationCommittedWithWarning(ex);
                WotResource? committed = ex.CommittedSnapshot.FindResource(groupId, resourceId);
                WotResourceVersion? validated = ex.ValidatedVersionXid is { } validatedXid
                    ? committed?.Versions.FirstOrDefault(version =>
                        WotDependencyGraph.VersionXid(committed, version) == validatedXid)
                    : m_registry is IWotVersionedRegistryService ? committed?.FindVersion(selectedVersionId) : null;
                outcome = validated?.Validation ??
                    throw new InvalidOperationException("Committed validation has no outcome for the addressed Version.", ex);
                status = ServiceResult.Create(StatusCodes.GoodResultsMayBeIncomplete,
                    "The validation outcome was committed, but completion reported a warning.");
            }
            await ReconcileProjectionAsync(CancellationToken.None).ConfigureAwait(false);
            output.Clear();
            output.Add(Variant.FromStructure(outcome));
            return status;
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
            WotRegistryMutationResult result = await m_manager.Coordinator
                .SetEnabledAsync(groupId, resourceId, enabled, OptionalEpoch(input, 1), context.GetOperationContext(), ct)
                .ConfigureAwait(false);
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
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
            await ReconcileProjectionAsync(ct).ConfigureAwait(false);
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
                DetectProjectionFormat = true,
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
                ? ServiceResult.Create(
                    StatusCode.IsBad(result.StatusCode) ? result.StatusCode.Code : StatusCodes.BadInvalidState,
                    result.Message)
                : ServiceResult.Good;
            WotResource? committedResource = ServiceResult.IsGood(serviceResult)
                ? result.Resource ?? m_registry.Current.FindResource(groupId, resourceId)
                : null;
            string? committedVersionId = string.IsNullOrEmpty(versionId)
                ? committedResource?.DesiredVersionId ?? committedResource?.DefaultVersionId
                : versionId;
            WotResourceVersion? committedVersion =
                committedResource?.FindVersion(committedVersionId);
            if (ServiceResult.IsGood(serviceResult))
            {
                await ReconcileProjectionAsync(ct).ConfigureAwait(false);
            }
            return new WotResourceCommitResult(serviceResult, committedVersion);
        }

        private WoTDocumentKindEnum KindForGroup(string groupId)
        {
            WotResourceGroup? group = m_registry.Current.FindGroup(groupId);
            if (group is not null)
            {
                return group.Kind;
            }
            foreach (WotRegistryGroupIdentity identity in m_options.IdentityBindings.Groups)
            {
                if (string.Equals(identity.GroupId, groupId, StringComparison.Ordinal))
                {
                    return identity.Kind;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument, "A new generic group requires explicit kind and catalogue authority.");
        }

        private static string NormalizeId(string id)
        {
            return WotRegistryService.NormalizeSegment(id, nameof(id));
        }

        private static string? GetString(ArrayOf<Variant> input, int index)
        {
            return index < input.Count && input[index].TryGetValue(out string s)
                ? s : null;
        }

        private static bool? GetBoolOrNull(ArrayOf<Variant> input, int index)
        {
            return index < input.Count && input[index].TryGetValue(out bool b)
                ? b : null;
        }

        private static long? OptionalEpoch(ArrayOf<Variant> input, int index)
        {
            if (index >= input.Count)
            {
                return null;
            }
            if (input[index].TryGetValue(out uint unsigned))
            {
                return unsigned == 0 ? null : unsigned;
            }
            if (input[index].TryGetValue(out int signed))
            {
                return signed == 0 ? null : signed;
            }
            return input[index].TryGetValue(out long epoch) && epoch != 0 ? epoch : null;
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
            IXRegistryProjectionEventMetadataProvider,
            IXRegistryProjectionContextualDelete
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

            /// <summary>
            /// Creates the event snapshot of the registry projection.
            /// </summary>
            /// <param name="snapshot">The WoT registry snapshot.</param>
            public XRegistryProjectionEventSnapshot CreateEventSnapshot(
                WotRegistrySnapshot snapshot)
            {
                ImmutableArray<XRegistryProjectionEventGroup> groups = [.. snapshot.Groups.Values
                    .OrderBy(group => group.GroupId, StringComparer.Ordinal)
                    .Select(CreateEventGroup)];
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
                    [.. group.Resources.Values
                        .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
                        .Select(CreateEventResource)])
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
                    [.. resource.Versions.Select(version => CreateEventVersion(resource, version))])
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

            /// <summary>
            /// Configures a group node from its projection group.
            /// </summary>
            /// <param name="node">The group state.</param>
            /// <param name="group">The x registry projection group.</param>
            public void ConfigureGroupNode(GroupState node, IXRegistryProjectionGroup group)
            {
                m_projection.ConfigureTypedGroupNode(node, ((GroupAdapter)group).Group);
            }

            /// <summary>
            /// Configures a resource node from its projection resource.
            /// </summary>
            /// <param name="node">The resource state.</param>
            /// <param name="resource">The x registry projection resource.</param>
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
                        adapter.Version,
                        adapter.IsConcreteVersion);
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

            protected bool SupportsVersions => m_projection.UsesVersionedProjection;

            /// <summary>
            /// Creates a group through the registry coordinator.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<IXRegistryProjectionGroup?> CreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                if (m_projection.m_registry is WotRegistryService stock)
                {
                    WotDocumentGroupResult created = await stock.ProvisionConfiguredGroupAsync(
                        groupId, false, ct).ConfigureAwait(false);
                    return new GroupAdapter(created.Group);
                }
                WotResourceGroup? group = await m_projection.m_registry
                    .TryCreateGroupAsync(groupId, m_projection.KindForGroup(groupId), cancellationToken: ct)
                    .ConfigureAwait(false);
                return group is null ? null : new GroupAdapter(group);
            }

            /// <summary>
            /// Gets a group, creating it when it does not exist.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<(IXRegistryProjectionGroup Group, bool Created)> GetOrCreateGroupAsync(
                string groupId,
                CancellationToken ct)
            {
                if (m_projection.m_registry is WotRegistryService stock)
                {
                    WotDocumentGroupResult result = await stock.ProvisionConfiguredGroupAsync(
                        groupId, true, ct).ConfigureAwait(false);
                    return (new GroupAdapter(result.Group), result.Created);
                }
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

            /// <summary>
            /// Creates a resource and its version through the registry service.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="resourceId">The resource ID.</param>
            /// <param name="versionId">The version ID.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<IXRegistryProjectionResource?> CreateResourceAsync(
                string groupId,
                string resourceId,
                string versionId,
                CancellationToken ct)
            {
                if (m_projection.m_registry is WotRegistryService stock)
                {
                    try
                    {
                        WotDocumentResourceResult result = await stock.ProvisionConfiguredResourceAsync(
                            groupId, resourceId, versionId, false, ct).ConfigureAwait(false);
                        return new ResourceAdapter(result.Resource, result.Version);
                    }
                    catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdExists)
                    {
                        return null;
                    }
                }
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

            /// <summary>
            /// Gets a resource and its version, creating them when they do not exist.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="resourceId">The resource ID.</param>
            /// <param name="versionId">The version ID.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<(IXRegistryProjectionResource Resource, bool Created)>
                GetOrCreateResourceAsync(
                    string groupId,
                    string resourceId,
                    string versionId,
                    CancellationToken ct)
            {
                if (m_projection.m_registry is WotRegistryService stock)
                {
                    WotDocumentResourceResult result = await stock.ProvisionConfiguredResourceAsync(
                        groupId, resourceId, versionId, true, ct).ConfigureAwait(false);
                    return (new ResourceAdapter(result.Resource, result.Version), result.CreatedVersion);
                }
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

            public ValueTask<ServiceResult> DeleteGroupAsync(
                string groupId,
                long? epoch,
                CancellationToken ct)
            {
                return DeleteGroupAsync(groupId, epoch, null, ct);
            }

            /// <summary>
            /// Deletes a group, handing the executing request's context to the generic lifecycle.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="epoch">The expected epoch of the group, if any.</param>
            /// <param name="context">The system context of the executing request, if any.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<ServiceResult> DeleteGroupAsync(
                string groupId,
                long? epoch,
                ISystemContext? context,
                CancellationToken ct)
            {
                WotRegistryMutationResult result = await m_projection.m_registry
                    .DeleteGroupAsync(groupId, epoch, context?.GetOperationContext(), ct).ConfigureAwait(false);
                return ToServiceResult(result);
            }

            /// <summary>
            /// Deletes a logical resource through the delete coordinator.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="resourceId">The resource ID.</param>
            /// <param name="epoch">The expected epoch of the resource, if any.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                CancellationToken ct)
            {
                return DeleteResourceAsync(groupId, resourceId, epoch, null, ct);
            }

            private async ValueTask<ServiceResult> DeleteResourceAsync(
                string groupId,
                string resourceId,
                long? epoch,
                ISystemContext? context,
                CancellationToken ct)
            {
                WotDeleteOutcome result = await m_projection.m_manager.Coordinator.DeleteAsync(new WotDeleteRequest
                {
                    GroupId = groupId, ResourceId = resourceId, ExpectedEpoch = epoch,
                    Policy = m_projection.m_manager.Coordinator.DeletePolicy,
                    CallerContext = context?.GetOperationContext()
                }, ct).ConfigureAwait(false);
                return ToServiceResult(new WotRegistryMutationResult(
                    result.Delete.Outcome, null, result.Delete.Generation, [], result.Delete.Message));
            }

            /// <summary>
            /// Deletes a projected version or, when requested, the whole logical resource.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="resourceId">The resource ID.</param>
            /// <param name="versionId">The version ID.</param>
            /// <param name="deleteLogicalResource">The delete-logical-resource flag supplied by the caller.</param>
            /// <param name="epoch">The expected epoch of the entity, if any.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                CancellationToken ct)
            {
                return DeleteProjectedEntityAsync(
                    groupId, resourceId, versionId, deleteLogicalResource, epoch, null, ct);
            }

            /// <summary>
            /// Deletes a projected version or the logical resource, handing the executing request's
            /// context to the generic lifecycle.
            /// </summary>
            /// <param name="groupId">The group ID.</param>
            /// <param name="resourceId">The resource ID.</param>
            /// <param name="versionId">The version ID.</param>
            /// <param name="deleteLogicalResource">The delete-logical-resource flag supplied by the caller.</param>
            /// <param name="epoch">The expected epoch of the entity, if any.</param>
            /// <param name="context">The system context of the executing request, if any.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public async ValueTask<ServiceResult> DeleteProjectedEntityAsync(
                string groupId,
                string resourceId,
                string versionId,
                bool deleteLogicalResource,
                long? epoch,
                ISystemContext? context,
                CancellationToken ct)
            {
                if (deleteLogicalResource)
                {
                    return await DeleteResourceAsync(groupId, resourceId, epoch, context, ct).ConfigureAwait(false);
                }
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
                        context?.GetOperationContext(),
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
            if (!UsesVersionedProjection)
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
            /// <summary>
            /// Initializes a new <c>ResourceAdapter</c> instance.
            /// </summary>
            /// <param name="resource">The WoT resource used to initialize the instance.</param>
            /// <param name="version">The version used to initialize the instance.</param>
            /// <param name="preparedLease">The prepared lease used to initialize the instance.</param>
            public ResourceAdapter(
                WotResource resource, WotResourceVersion version, IWotRegistryVersionLease? preparedLease = null)
            {
                Resource = resource;
                Version = version;
                PreparedLease = preparedLease;
            }

            public WotResource Resource { get; }
            public WotResourceVersion Version { get; }

            /// <summary>
            /// Gets the prepared lease for <c>ResourceAdapter</c>.
            /// </summary>
            public IWotRegistryVersionLease? PreparedLease { get; }
            public bool IsConcreteVersion => true;
            public string GroupId => Resource.GroupId;
            public string ResourceId => Resource.ResourceId;
            public string Xid => $"{Resource.Xid}/versions/{Version.VersionId}";

            public string Name => string.IsNullOrWhiteSpace(Version.Title)
                ? Resource.SourceId ?? Resource.Name
                : Version.Title!;

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
            IXRegistryPreparedResourceFile,
            IXRegistryAsyncProjectedContentlessResourceFile,
            IXRegistryAsyncProjectedResourceFileHandleForwarder,
            IXRegistryProjectedResourceSessionDiscard
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

            /// <summary>
            /// Opens the file for a write that preserves the existing content.
            /// </summary>
            /// <param name="context">The system context.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<(ServiceResult Status, uint FileHandle)> OpenPreservingWriteAsync(
                ISystemContext context,
                CancellationToken cancellationToken)
            {
                return m_file.OpenPreservingWriteAsync(context, cancellationToken);
            }

            /// <summary>
            /// Opens the file of a specific resource for a write that preserves the existing content.
            /// </summary>
            /// <param name="resource">The x registry projection resource.</param>
            /// <param name="context">The system context.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<(ServiceResult Status, uint FileHandle)> OpenPreservingWriteAsync(
                IXRegistryProjectionResource resource,
                ISystemContext context,
                CancellationToken cancellationToken)
            {
                return m_file.OpenPreservingWriteAsync(
                    context, cancellationToken, (resource as ResourceAdapter)?.PreparedLease);
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

            /// <summary>
            /// Opens the file for a write that starts without the existing content.
            /// </summary>
            /// <param name="context">The system context.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<OpenMethodStateResult> OpenContentlessWriteAsync(
                ISystemContext context,
                CancellationToken cancellationToken)
            {
                return m_file.OpenContentlessWriteAsync(context, cancellationToken);
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

            /// <summary>
            /// Discards the write handles held by a closing session.
            /// </summary>
            /// <param name="sessionId">The session identifier.</param>
            /// <param name="ct">The cancellation token supplied by the caller.</param>
            public ValueTask DiscardSessionAsync(NodeId sessionId, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                m_file.CloseSession(sessionId);
                return default;
            }

            ServiceResult IXRegistryProjectedResourceFileHandleForwarder.ForwardOpen(
                ISystemContext context, MethodState method, NodeId objectId,
                byte mode, ref uint fileHandle)
            {
                return ((IXRegistryProjectedResourceFileHandleForwarder)m_file)
                    .ForwardOpen(context, method, objectId, mode, ref fileHandle);
            }

            ValueTask<OpenMethodStateResult>
                IXRegistryAsyncProjectedResourceFileHandleForwarder.ForwardOpenAsync(
                ISystemContext context, MethodState method, NodeId objectId,
                byte mode, CancellationToken cancellationToken)
            {
                return ((IXRegistryAsyncProjectedResourceFileHandleForwarder)m_file)
                    .ForwardOpenAsync(context, method, objectId, mode, cancellationToken);
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
        private readonly ILogger m_logger;
        private readonly ushort m_modelNs;
        private readonly Strategy m_strategy;
        private readonly XRegistryProjectionEngine m_engine;
        private readonly ConditionalWeakTable<WotRegistrySnapshot, Lazy<ImmutableDictionary<string, ByteString>>>
            m_membershipDigests = new();
    }

    /// <summary>
    /// Contains the source-generated log-event declarations for <c>WotRegistryProjectionLog</c>.
    /// </summary>
    internal static partial class WotRegistryProjectionLog
    {
        /// <summary>
        /// Writes the log event: <c>The exact-Version validation outcome committed with a completion warning</c>.
        /// </summary>
        /// <param name="logger">The logger receiving this event.</param>
        /// <param name="exception">The exception attached to the event.</param>
        [LoggerMessage(EventId = WotConServerEventIds.WotRegistryProjection, Level = LogLevel.Warning,
            Message = "The exact-Version validation outcome committed with a completion warning")]
        public static partial void ValidationCommittedWithWarning(this ILogger logger, Exception exception);
    }
}
