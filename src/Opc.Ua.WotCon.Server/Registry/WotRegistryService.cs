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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// The default <see cref="IWotRegistryService"/>. Owns the current
    /// immutable <see cref="WotRegistrySnapshot"/>, serialises every mutation on
    /// a single lock, enforces the configured <see cref="WotRegistryPersistenceBounds"/>,
    /// and persists through the injected <see cref="IWotRegistryStore"/>. Every
    /// mutation is persisted by <see cref="IWotRegistryStore.CommitAsync"/> before
    /// the new snapshot is published. A pre-switch failure leaves
    /// <see cref="Current"/> unchanged. If the store validates that the manifest
    /// switched but final durability is uncertain, the service publishes the
    /// committed snapshot and then surfaces the dedicated exception. A confirmed
    /// not-committed outcome leaves the previous snapshot published and remains
    /// retryable. An indeterminate commit blocks mutation until
    /// <see cref="InitializeAsync"/> reloads a known generation.
    /// </summary>
    public sealed partial class WotRegistryService :
        IWotRegistryService,
        IWotDeletePolicyRegistryService,
        IWotVersionedRegistryService,
        IWotTypedRegistryService,
        IWotRegistryVersionLeaseProvider,
        IWotRegistryDependencySnapshotProvider,
        IWotInvocationRegistryPublicationService,
        IDisposable
    {
        /// <summary>
        /// Initializes a new registry service over the supplied store.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store = null,
            WotRegistryPersistenceBounds? bounds = null)
            : this(store, bounds, new WotRegistryIdentityBindings(), WotProjectionCompatibilityMode.None)
        {
        }

        /// <summary>
        /// Initializes a registry with explicit authority bindings for generic provisioning.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store,
            WotRegistryPersistenceBounds? bounds,
            WotRegistryIdentityBindings identityBindings)
            : this(store, bounds, identityBindings, WotProjectionCompatibilityMode.None)
        {
        }

        /// <summary>
        /// Initializes a registry with an explicitly selected projection-plan compatibility mode.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store,
            WotRegistryPersistenceBounds? bounds,
            WotProjectionCompatibilityMode projectionCompatibilityMode)
            : this(store, bounds, new WotRegistryIdentityBindings(), projectionCompatibilityMode)
        {
        }

        /// <summary>
        /// Initializes a registry with explicit authority bindings and projection-plan compatibility.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store,
            WotRegistryPersistenceBounds? bounds,
            WotRegistryIdentityBindings identityBindings,
            WotProjectionCompatibilityMode projectionCompatibilityMode)
        {
            if (projectionCompatibilityMode is not (
                WotProjectionCompatibilityMode.None or WotProjectionCompatibilityMode.DraftProjection11))
            {
                throw new ArgumentOutOfRangeException(nameof(projectionCompatibilityMode));
            }
            m_projectionCompatibilityMode = projectionCompatibilityMode;
            m_store = store ?? new InMemoryWotRegistryStore();
            m_resourceStore = m_store is IWotRegistryResourceStoreProvider provider
                ? provider.ResourceStore
                : new InMemoryResourceStore();
            Bounds = bounds ?? new WotRegistryPersistenceBounds();
            Bounds.Validate();
            (m_groupIdentities, m_resourceIdentities) = ValidateIdentityBindings(identityBindings);
            m_snapshot = WotRegistrySnapshot.Empty;
        }

        /// <inheritdoc/>
        public WotRegistrySnapshot Current => Volatile.Read(ref m_snapshot);

        /// <inheritdoc/>
        public WotRegistryPersistenceBounds Bounds { get; }

        /// <inheritdoc/>
        public bool SupportsDependencySnapshots => true;

        /// <inheritdoc/>
        public event EventHandler<WotRegistryChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotResourceGroup> GetOrCreateGroupAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            EnsureDocumentKind(kind);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                groupId = ResolveAssignedGroupId(groupId);
                WotResourceGroup? existing = snapshot.FindGroup(groupId);
                EnsureGroupKind(existing, kind);
                if (existing is not null)
                {
                    return existing;
                }
                if (snapshot.Groups.Count >= Bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {Bounds.MaxGroups} groups.");
                }
                long generation = snapshot.Generation + 1;
                var group = new WotResourceGroup(
                    groupId, kind, name: name, epoch: generation);
                WotRegistrySnapshot next = snapshot.WithGroup(group, generation);
                await CommitAndPublishAsync(
                        snapshot, next, [group.Xid], projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
                return group;
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotResourceGroup?> TryCreateGroupAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            EnsureDocumentKind(kind);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                groupId = ResolveAssignedGroupId(groupId);
                WotResourceGroup? existing = snapshot.FindGroup(groupId);
                EnsureGroupKind(existing, kind);
                if (existing is not null)
                {
                    return null;
                }
                if (snapshot.Groups.Count >= Bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {Bounds.MaxGroups} groups.");
                }
                long generation = snapshot.Generation + 1;
                var group = new WotResourceGroup(groupId, kind, name: name, epoch: generation);
                WotRegistrySnapshot next = snapshot.WithGroup(group, generation);
                await CommitAndPublishAsync(
                        snapshot, next, [group.Xid], projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
                return group;
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> DeleteGroupAsync(
            string groupId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                return await coordinator.DeleteGroupAsync(groupId, expectedEpoch, cancellationToken).ConfigureAwait(false);
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                if (group is null)
                {
                    return Failed(snapshot.Generation, "Group not found.");
                }
                if (expectedEpoch is { } epoch && epoch != group.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next = snapshot.WithoutGroup(groupId, generation);
                var changed = new List<string> { group.Xid };
                foreach (WotResource resource in group.Resources.Values)
                {
                    changed.Add(resource.Xid);
                }
                await CommitAndPublishAsync(
                        snapshot, next, changed, projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(WotResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            CancellationToken cancellationToken = default)
        {
            EnsureDocumentKind(kind);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                groupId = ResolveAssignedGroupId(groupId);
                resourceId = ResolveAssignedResourceId(groupId, resourceId);
                EnsureGroupKind(m_snapshot.FindGroup(groupId), kind);
                WotResource? existing = m_snapshot.FindResource(groupId, resourceId);
                EnsureResourceKind(existing, kind);
                if (existing is not null)
                {
                    return (existing, false);
                }
                WotResource created = await CreatePlaceholderLockedAsync(
                    groupId, resourceId, kind, cancellationToken).ConfigureAwait(false);
                return (created, true);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotResource?> TryCreateResourceAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            CancellationToken cancellationToken = default)
        {
            EnsureDocumentKind(kind);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                groupId = ResolveAssignedGroupId(groupId);
                resourceId = ResolveAssignedResourceId(groupId, resourceId);
                EnsureGroupKind(m_snapshot.FindGroup(groupId), kind);
                EnsureResourceKind(m_snapshot.FindResource(groupId, resourceId), kind);
                if (m_snapshot.FindResource(groupId, resourceId) is not null)
                {
                    return null;
                }
                return await CreatePlaceholderLockedAsync(
                    groupId, resourceId, kind, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<(WotResource Resource, WotResourceVersion Version, bool Created)>
            GetOrCreateVersionAsync(
                string groupId,
                string resourceId,
                string versionId,
                WoTDocumentKindEnum kind,
                CancellationToken cancellationToken = default)
        {
            VersionCreateResult result = await CreateVersionAsync(
                    groupId,
                    resourceId,
                    versionId,
                    kind,
                    getOrCreate: true,
                    cancellationToken)
                .ConfigureAwait(false);
            return (result.Resource!, result.Version!, result.Created);
        }

        /// <inheritdoc/>
        public async ValueTask<(WotResource Resource, WotResourceVersion Version)?>
            TryCreateVersionAsync(
                string groupId,
                string resourceId,
                string versionId,
                WoTDocumentKindEnum kind,
                CancellationToken cancellationToken = default)
        {
            VersionCreateResult result = await CreateVersionAsync(
                    groupId,
                    resourceId,
                    versionId,
                    kind,
                    getOrCreate: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Version is null ? null : (result.Resource!, result.Version!);
        }

        private async ValueTask<VersionCreateResult> CreateVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            WoTDocumentKindEnum kind,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            EnsureDocumentKind(kind);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                groupId = ResolveAssignedGroupId(groupId);
                resourceId = ResolveAssignedResourceId(groupId, resourceId);
                return await CreateVersionLockedAsync(
                    groupId, resourceId, versionId, kind, getOrCreate,
                    useTypedSemantics: false, sourceId: null, beforeCommit: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<VersionCreateResult> CreateVersionLockedAsync(
            string groupId,
            string resourceId,
            string versionId,
            WoTDocumentKindEnum kind,
            bool getOrCreate,
            bool useTypedSemantics,
            string? sourceId,
            Func<WotResource, WotResourceVersion, IWotRegistryVersionLease, CancellationToken, ValueTask>? beforeCommit,
            CancellationToken cancellationToken)
        {
            WotRegistrySnapshot snapshot = m_snapshot;
            WotResourceGroup? group = snapshot.FindGroup(groupId);
            EnsureGroupKind(group, kind);
            WotResource? existing = group?.Resources.GetValueOrDefault(resourceId);
            EnsureResourceKind(existing, kind);
            sourceId ??= existing?.SourceId;
            if (group?.CatalogUri is not null && sourceId is null)
            {
                throw InvalidAuthority("Creation in an authoritative group requires an exact source identity.");
            }
            string? explicitVersionId = string.IsNullOrEmpty(versionId) ? null : versionId;
            WotResourceVersion? pendingVersion = existing?.Versions.FirstOrDefault(version => !version.HasContent);
            WotResourceVersion? resolved = !useTypedSemantics && explicitVersionId is null && pendingVersion is not null
                ? pendingVersion
                : getOrCreate && explicitVersionId is null
                    ? existing?.FindVersion(existing.DefaultVersionId)
                    : existing?.FindVersion(explicitVersionId);
            if (resolved is not null)
            {
                if (!getOrCreate && explicitVersionId is not null)
                {
                    return default;
                }
                if (beforeCommit is not null)
                {
                    await PrepareVersionLeaseAsync(existing!, resolved, beforeCommit, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return new VersionCreateResult(existing, resolved, false);
            }
            string assignedVersionId = explicitVersionId is null
                ? NextVersionId(existing)
                : ValidateExplicitVersionId(explicitVersionId, nameof(versionId));
            if (existing?.Versions.Any(version => string.Equals(
                    version.VersionId, assignedVersionId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdExists,
                    $"Version '{assignedVersionId}' differs only by case from an existing sibling Version.");
            }
            if (group is null)
            {
                if (snapshot.Groups.Count >= Bounds.MaxGroups)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The group limit is reached.");
                }
                group = new WotResourceGroup(groupId, kind, epoch: 0);
            }
            if (existing is null && group.Resources.Count >= Bounds.MaxResourcesPerGroup)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The resource limit is reached.");
            }
            if (existing is not null)
            {
                if (!useTypedSemantics && pendingVersion is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        $"Resource '{resourceId}' already has a pending contentless Version.");
                }
                if ((useTypedSemantics &&
                    existing.Versions.Count(version => !version.HasContent) >= Bounds.MaxVersionsPerResource) ||
                    !CanRetainIncomingCommittedVersion(existing, Bounds.MaxVersionsPerResource, assignedVersionId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        "The Version limit cannot preserve the existing and incoming Versions.");
                }
            }
            DateTime now = DateTime.UtcNow;
            WotResourceVersion version = WotResourceVersion.CreatePlaceholder(assignedVersionId, now)
                .WithDocumentMetadata(sourceId, null, null, null);
            bool resourceCreated = existing is null;
            WotResource resource;
            if (resourceCreated)
            {
                resource = new WotResource(
                    groupId, resourceId, kind, [version],
                    defaultVersionId: assignedVersionId,
                    desiredVersionId: assignedVersionId,
                    epoch: 1, name: sourceId ?? resourceId, thingId: sourceId)
                {
                    MetaCreatedAt = now,
                    MetaModifiedAt = now
                };
            }
            else
            {
                long metaEpoch = existing!.MetaEpoch + 1;
                string defaultVersionId = existing.DefaultVersionId ?? assignedVersionId;
                resource = existing.With(
                    versions: existing.Versions.Add(version),
                    defaultVersionId: defaultVersionId,
                    desiredVersionId: existing.DesiredVersionId ?? defaultVersionId,
                    epoch: metaEpoch).WithMeta(metaEpoch, modifiedAt: now);
            }
            if (beforeCommit is not null)
            {
                await PrepareVersionLeaseAsync(resource, version, beforeCommit, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            long generation = snapshot.Generation + 1;
            WotRegistrySnapshot next = ReplaceResource(snapshot, group, resource, generation, resourceCreated);
            await CommitAndPublishAsync(snapshot, next, [resource.Xid], projectionOnly: true, cancellationToken)
                .ConfigureAwait(false);
            return new VersionCreateResult(resource, version, true, resourceCreated);
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> DeleteVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                return await coordinator.DeleteVersionAsync(
                    groupId, resourceId, versionId, expectedEpoch, cancellationToken).ConfigureAwait(false);
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                WotResource? resource = group?.Resources.GetValueOrDefault(resourceId);
                WotResourceVersion? version = resource?.FindVersion(versionId);
                if (group is null || resource is null || version is null)
                {
                    return Failed(snapshot.Generation, "Version not found.");
                }
                if (expectedEpoch is { } epoch && epoch != version.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                return await DeleteVersionLockedAsync(
                        snapshot,
                        group,
                        resource,
                        version,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> DeleteProjectedEntityAsync(
            string groupId,
            string resourceId,
            string versionId,
            bool deleteLogicalResource,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                return deleteLogicalResource
                    ? await DeleteThroughLifecycleAsync(
                        coordinator, groupId, resourceId, expectedEpoch, cancellationToken).ConfigureAwait(false)
                    : await coordinator.DeleteVersionAsync(
                        groupId, resourceId, versionId, expectedEpoch, cancellationToken).ConfigureAwait(false);
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                WotResource? resource = group?.Resources.GetValueOrDefault(resourceId);
                if (group is null || resource is null)
                {
                    return Failed(snapshot.Generation, "Resource not found.");
                }

                // In the distinct Resource/Versions/Version hierarchy, the
                // role is determined by the node's structural position and
                // cannot become stale — deleteLogicalResource is authoritative.
                if (deleteLogicalResource)
                {
                    if (expectedEpoch is { } resourceEpoch &&
                        resourceEpoch != resource.MetaEpoch)
                    {
                        return Rejected(snapshot.Generation, "Epoch mismatch.");
                    }
                    WotDeleteResult delete = await DeleteResourceWithPolicyLockedAsync(
                        snapshot,
                        resource,
                        WoTDeletePolicyEnum.Reject,
                        cancellationToken).ConfigureAwait(false);
                    return ToMutationResult(delete, resource);
                }

                WotResourceVersion? version = resource.FindVersion(versionId);
                if (version is null)
                {
                    return Failed(snapshot.Generation, "Version not found.");
                }
                if (expectedEpoch is { } versionEpoch && versionEpoch != version.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                return await DeleteVersionLockedAsync(
                        snapshot,
                        group,
                        resource,
                        version,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            WotResource? resource = m_snapshot.FindResource(groupId, resourceId) ??
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "Resource not found.");
            WotResourceVersion? version = resource.DefaultVersion ??
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The resource has no default version to validate.");
            return ValidateVersionAsync(
                groupId,
                resourceId,
                version.VersionId,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<WoTValidationOutcomeDataType> ValidateVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            CancellationToken cancellationToken = default)
        {
            WotResource? resource = m_snapshot.FindResource(groupId, resourceId) ??
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "Resource not found.");
            WotResourceVersion? version = resource.FindVersion(versionId) ??
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "Version not found.");
            if (!version.HasContent)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The Version has no document content to validate.");
            }

            ByteString content = await ReadContentAsync(version, cancellationToken)
                .ConfigureAwait(false);
            WoTValidationOutcomeDataType outcome = ValidateContent(content, resource.Kind, version);
            await StoreValidationAsync(
                    groupId,
                    resourceId,
                    versionId,
                    version,
                    resource.Kind,
                    outcome,
                    cancellationToken)
                .ConfigureAwait(false);
            return outcome;
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadContentAsync(
            WotResourceVersion version,
            CancellationToken cancellationToken = default)
        {
            if (version is null)
            {
                throw new ArgumentNullException(nameof(version));
            }
            if (!version.HasContent)
            {
                return ByteString.Empty;
            }
            if (version.ContentLength > int.MaxValue)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfMemory,
                    "The document exceeds the maximum readable size.");
            }

            ByteString content = await m_resourceStore
                .ReadAsync(version.DigestHex, 0, checked((int)version.ContentLength), cancellationToken)
                .ConfigureAwait(false);
            if (content.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The document content is missing from the resource store.");
            }
            if (content.Length != version.ContentLength ||
                !WotContentDigest.Equal(WotContentDigest.Compute(content), version.Digest))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataEncodingInvalid,
                    "The document content does not match the registry metadata.");
            }
            return content;
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReadContentChunkAsync(
            string digestHex,
            long offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(digestHex))
            {
                return ByteString.Empty;
            }
            return await m_resourceStore
                .ReadAsync(digestHex, offset, count, cancellationToken)
                .ConfigureAwait(false);
        }

        private async ValueTask<WotResource> CreatePlaceholderLockedAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            CancellationToken cancellationToken)
        {
            WotRegistrySnapshot snapshot = m_snapshot;
            WotResourceGroup? group = snapshot.FindGroup(groupId);
            EnsureGroupKind(group, kind);
            if (group?.CatalogUri is not null)
            {
                throw InvalidAuthority("Use typed provisioning to create a resource with its exact source identity.");
            }
            if (group is null)
            {
                // Implicit group creation must enforce MaxGroups identically to the
                // explicit GetOrCreateGroupAsync / TryCreateGroupAsync paths.
                if (snapshot.Groups.Count >= Bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {Bounds.MaxGroups} groups.");
                }
                group = new WotResourceGroup(groupId, kind, epoch: 0);
            }
            if (group.Resources.Count >= Bounds.MaxResourcesPerGroup)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations,
                    $"Group '{groupId}' already holds the maximum of " +
                    $"{Bounds.MaxResourcesPerGroup} resources.");
            }
            long generation = snapshot.Generation + 1;
            DateTime now = DateTime.UtcNow;
            var resource = new WotResource(
                groupId,
                resourceId,
                kind,
                [],
                enabled: true,
                loadState: WoTLoadStateEnum.Unloaded,
                epoch: 1,
                name: resourceId)
            {
                MetaCreatedAt = now,
                MetaModifiedAt = now
            };
            WotRegistrySnapshot next = ReplaceResource(
                snapshot,
                group,
                resource,
                generation,
                bumpGroupEpoch: true);
            await CommitAndPublishAsync(
                    snapshot, next, [resource.Xid], projectionOnly: true, cancellationToken)
                .ConfigureAwait(false);
            return resource;
        }

        private WoTValidationOutcomeDataType ValidateContent(
            ByteString content, WoTDocumentKindEnum kind, WotResourceVersion version)
        {
            try
            {
                using var document = WotDocument.Parse(content.Span.ToArray(), CreateDocumentOptions());
                string? projectionError = WotProjectionAdmission.GetError(
                    document, kind, version.Format, version.ContentType, m_projectionCompatibilityMode);
                if (projectionError is not null)
                {
                    return FailedValidation(projectionError);
                }
                return new WoTValidationOutcomeDataType
                {
                    FormatValidated = false,
                    FormatOutcome = WoTOutcomeEnum.Skipped,
                    FormatReason = "Syntax and document admission were checked; full format validation was not performed.",
                    CompatibilityValidated = false,
                    CompatibilityOutcome = WoTOutcomeEnum.Skipped,
                    CompatibilityReason = "No compatibility validation was performed.",
                    ValidatedAt = DateTime.UtcNow,
                    VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
                };
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return FailedValidation(ex.Message);
            }
        }

        private WotNodeSetConverterOptions CreateDocumentOptions()
        {
            return new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = Bounds.MaxDocumentBytes,
                MaxJsonDepth = Bounds.MaxJsonDepth,
                ProjectionCompatibilityMode = m_projectionCompatibilityMode
            };
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> UpsertResourceAsync(
            WotUpsertResourceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!WotDocumentKinds.IsDocument(request.Kind))
            {
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Rejected,
                    null,
                    m_snapshot.Generation,
                    ["A stored document kind must be ThingDescription or ThingModel, not a selector."],
                    "Invalid document kind.");
            }
            if (request.Content.IsNull || request.Content.Length == 0)
            {
                return Failed(m_snapshot.Generation, "The document is empty.");
            }
            if (request.Content.Length > Bounds.MaxDocumentBytes)
            {
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Rejected,
                    null,
                    m_snapshot.Generation,
                    [$"The document exceeds the maximum size of {Bounds.MaxDocumentBytes} bytes."],
                    "Document too large.");
            }

            string groupId = string.IsNullOrWhiteSpace(request.GroupId)
                ? DefaultGroupFor(request.Kind)
                : request.GroupId!;
            string? explicitVersionId = string.IsNullOrEmpty(request.VersionId)
                ? null
                : request.VersionId;
            string contentType = request.ContentType ?? string.Empty;
            string format = request.Format ?? string.Empty;

            var content = ByteString.From(request.Content.Span.ToArray());
            WotResourceDependencies dependencies = WotDependencyGraph.ReadMetadata(content, Bounds.MaxJsonDepth);

            // Light parse to derive the kind/id/title and to record a format
            // failure state for a document that cannot even be parsed. Full WoT
            // validation and projection are performed by the coordinator.
            string? documentId = null;
            string? title = null;
            string? baseUri = null;
            string? modelVersion = null;
            WoTValidationOutcomeDataType? validation = null;
            ImmutableArray<string>.Builder diagnostics = ImmutableArray.CreateBuilder<string>();
            bool parseFailed = false;
            bool ambiguousIdentity = false;
            WotDocumentKind parsedKind = WotDocumentKind.Unknown;
            bool projectionPlan = false;
            try
            {
                using var document = WotDocument.Parse(content.Span.ToArray(), CreateDocumentOptions());
                projectionPlan = WotProjection.IsProjection(document);
                if (request.DetectProjectionFormat && projectionPlan)
                {
                    format = WotProjection.Format;
                    contentType = WotProjection.ContentType;
                }
                string? projectionError = WotProjectionAdmission.GetError(
                    document, request.Kind, format, contentType, m_projectionCompatibilityMode);
                if (projectionError is not null)
                {
                    return Rejected(m_snapshot.Generation, projectionError);
                }
                documentId = WotRegistryIdentity.ReadSourceId(document.RootElement);
                // Admission checked the plan's declared result kind; its root is not an ordinary TD/TM.
                parsedKind = projectionPlan
                    ? request.Kind == WoTDocumentKindEnum.ThingModel
                        ? WotDocumentKind.ThingModel
                        : WotDocumentKind.ThingDescription
                    : document.Kind;
                title = document.Title;
                baseUri = ReadString(document.RootElement, "base");
                if (document.RootElement.TryGetProperty(
                        "version",
                        out JsonElement versionElement) &&
                    versionElement.ValueKind == JsonValueKind.Object)
                {
                    modelVersion = ReadString(versionElement, "model");
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                parseFailed = true;
                ambiguousIdentity = ex is FormatException;
                diagnostics.Add($"Document parse failed: {ex.Message}");
                validation = FailedValidation(ex.Message);
            }

            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                groupId = ResolveAssignedGroupId(groupId);
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                if (group is not null && group.Kind != request.Kind)
                {
                    return RejectedAuthority(snapshot.Generation, "The document kind contradicts its owning group.");
                }
                if (group is null)
                {
                    // Implicit group creation on upsert must enforce MaxGroups the
                    // same way this method already enforces MaxResourcesPerGroup:
                    // reject the request rather than silently exceeding the bound.
                    if (snapshot.Groups.Count >= Bounds.MaxGroups)
                    {
                        return new WotRegistryMutationResult(
                            WoTOutcomeEnum.Rejected,
                            null,
                            snapshot.Generation,
                            [$"The registry already holds the maximum of {Bounds.MaxGroups} groups."],
                            "Too many groups.");
                    }
                    group = new WotResourceGroup(groupId, request.Kind, epoch: 0);
                }

                if (ambiguousIdentity ||
                    (parsedKind == WotDocumentKind.ThingModel && request.Kind != WoTDocumentKindEnum.ThingModel) ||
                    (parsedKind == WotDocumentKind.ThingDescription &&
                        request.Kind != WoTDocumentKindEnum.ThingDescription) ||
                    (group.CatalogUri is not null &&
                        (!WotRegistryIdentity.IsAbsoluteUri(documentId) ||
                            parseFailed ||
                            (parsedKind == WotDocumentKind.Unknown &&
                                request.Kind == WoTDocumentKindEnum.ThingModel))) ||
                    (string.IsNullOrEmpty(request.ResourceId) && !WotRegistryIdentity.IsAbsoluteUri(documentId)))
                {
                    return RejectedAuthority(
                        snapshot.Generation,
                        "The document has missing, invalid, ambiguous or contradictory authority.");
                }
                string resourceId = DeriveResourceId(group, request, documentId);
                WotResource? existing = group.Resources.TryGetValue(
                    resourceId, out WotResource? found) ? found : null;

                if (projectionPlan &&
                    (group.Kind != request.Kind ||
                        (existing is not null && existing.Kind != request.Kind)))
                {
                    return Rejected(
                        snapshot.Generation,
                        "A projection result kind must match its existing resource and group kinds.");
                }

                if (existing is null &&
                    group.Resources.Count >= Bounds.MaxResourcesPerGroup)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected,
                        null,
                        snapshot.Generation,
                        [$"Group '{groupId}' already holds the maximum of " +
                            $"{Bounds.MaxResourcesPerGroup} resources."],
                        "Too many resources.");
                }

                string? establishedDocumentId = existing?.Versions
                    .Select(version => version.DocumentId)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)) ??
                    existing?.ThingId;
                if ((existing is not null && existing.Kind != request.Kind) ||
                    (!string.IsNullOrWhiteSpace(establishedDocumentId) &&
                        (parseFailed || !string.Equals(establishedDocumentId, documentId, StringComparison.Ordinal))))
                {
                    return RejectedAuthority(
                        snapshot.Generation,
                        $"Document identity '{documentId}' does not match the Resource identity " +
                        $"'{establishedDocumentId}'.");
                }

                ByteString digest = WotContentDigest.Compute(content);
                WotResourceVersion? current;
                string versionId;
                if (explicitVersionId is null)
                {
                    WotResourceVersion? defaultVersion = existing?.FindVersion(
                        existing.DefaultVersionId);
                    current = defaultVersion?.HasContent == true &&
                        WotContentDigest.Equal(defaultVersion.Digest, digest)
                            ? defaultVersion
                            : null;
                    versionId = current?.VersionId ?? NextVersionId(existing);
                }
                else
                {
                    versionId = explicitVersionId;
                    current = existing?.FindVersion(versionId);
                    if (current is null)
                    {
                        ValidateExplicitVersionId(versionId, nameof(request.VersionId));
                        if (existing?.Versions.Any(version => string.Equals(
                                version.VersionId,
                                versionId,
                                StringComparison.OrdinalIgnoreCase)) == true)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdExists,
                                $"Version '{versionId}' differs only by case from an " +
                                "existing sibling Version.");
                        }
                    }
                }
                if (request.ExpectedVersionIncarnation is { } expectedIncarnation &&
                    current?.IncarnationId != expectedIncarnation)
                {
                    return Rejected(
                        snapshot.Generation,
                        "The Version incarnation changed while the writer was open.");
                }
                if (request.ExpectedVersionDigestHex is not null &&
                    !string.Equals(
                        current?.DigestHex ?? string.Empty,
                        request.ExpectedVersionDigestHex,
                        StringComparison.Ordinal))
                {
                    return Rejected(
                        snapshot.Generation,
                        "The committed Version changed while the writer was open.");
                }

                string defaultVersionId = request.SetAsDefault
                    ? versionId
                    : existing?.DefaultVersionId ?? versionId;
                string desiredVersionId = request.SetAsDefault
                    ? versionId
                    : existing?.DesiredVersionId ?? defaultVersionId;
                string selectedVersionId = desiredVersionId;
                string? previousSelectedVersionId =
                    existing?.DesiredVersionId ?? existing?.DefaultVersionId;
                bool updatesLogicalDefault = string.Equals(
                    defaultVersionId,
                    versionId,
                    StringComparison.Ordinal);
                bool updatesSelectedVersion = string.Equals(
                    selectedVersionId,
                    versionId,
                    StringComparison.Ordinal);
                string resourceName = existing is null
                    ? request.Name ?? title ?? documentId ?? resourceId
                    : updatesLogicalDefault
                        ? request.Name ?? existing.Name
                        : existing.Name;
                string resourceDescription = existing is null
                    ? request.Description ?? string.Empty
                    : updatesLogicalDefault
                        ? request.Description ?? existing.Description
                        : existing.Description;
                string? selectedDocumentId = existing?.SourceId ?? documentId;
                string? selectedTitle = updatesLogicalDefault
                    ? title
                    : existing?.Title;
                if (group.CatalogUri is not null && updatesLogicalDefault)
                {
                    resourceName = string.IsNullOrWhiteSpace(title) ? selectedDocumentId! : title!;
                }
                bool contentChanged = current is null ||
                    !current.HasContent ||
                    current.ContentLength != content.Length ||
                    !WotContentDigest.Equal(current.Digest, digest);
                bool admissionChanged = contentChanged ||
                    !string.Equals(
                        current?.ContentType,
                        contentType,
                        StringComparison.Ordinal) ||
                    !string.Equals(current?.Format, format, StringComparison.Ordinal);
                bool versionChanged = current is null ||
                    admissionChanged ||
                    !string.Equals(current.DocumentId, documentId, StringComparison.Ordinal) ||
                    !string.Equals(current.Title, title, StringComparison.Ordinal) ||
                    !string.Equals(current.BaseUri, baseUri, StringComparison.Ordinal) ||
                    !string.Equals(current.ModelVersion, modelVersion, StringComparison.Ordinal);
                bool defaultChanged = existing is null ||
                    !string.Equals(
                        existing.DefaultVersionId,
                        defaultVersionId,
                        StringComparison.Ordinal);
                bool desiredChanged = existing is null ||
                    !string.Equals(
                        existing.DesiredVersionId,
                        desiredVersionId,
                        StringComparison.Ordinal);
                bool resourceMetadataChanged = existing is null ||
                    defaultChanged ||
                    desiredChanged ||
                    !string.Equals(existing.Name, resourceName, StringComparison.Ordinal) ||
                    !string.Equals(
                        existing.Description,
                        resourceDescription,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        existing.ThingId,
                        selectedDocumentId,
                        StringComparison.Ordinal) ||
                    !string.Equals(existing.Title, selectedTitle, StringComparison.Ordinal);
                if (existing is not null && !versionChanged && !resourceMetadataChanged)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Unchanged,
                        existing,
                        snapshot.Generation,
                        [],
                        "Content digest unchanged.");
                }

                long generation = snapshot.Generation + 1;
                DateTime now = DateTime.UtcNow;
                WotResourceVersion version;
                if (current is null)
                {
                    version = new WotResourceVersion(
                        versionId,
                        digest,
                        content.Length,
                        contentType,
                        format,
                        createdAt: now,
                        modifiedAt: now)
                    {
                        Validation = validation,
                        DocumentId = documentId,
                        Title = title,
                        BaseUri = baseUri,
                        ModelVersion = modelVersion,
                        Dependencies = dependencies
                    };
                }
                else if (versionChanged)
                {
                    version = current.With(
                        digest: digest,
                        contentLength: content.Length,
                        contentType: contentType,
                        format: format,
                        modifiedAt: now,
                        epoch: current.Epoch + 1,
                        validation: admissionChanged ? validation : null,
                        clearValidation: admissionChanged && validation is null)
                        .WithDocumentMetadata(
                            documentId,
                            title,
                            baseUri,
                            modelVersion,
                            dependencies);
                }
                else
                {
                    version = current;
                }

                ImmutableArray<WotResourceVersion> candidateVersions;
                if (existing is null)
                {
                    candidateVersions = [version];
                }
                else if (current is null)
                {
                    candidateVersions = existing.Versions.Add(version);
                }
                else
                {
                    candidateVersions = existing.Versions.SetItem(
                        existing.Versions.IndexOf(current),
                        version);
                }
                ImmutableArray<WotResourceVersion> versions = candidateVersions;
                if (existing is not null &&
                    (current is null || !current.HasContent) &&
                    !TryTrim(
                        candidateVersions,
                        Bounds.MaxVersionsPerResource,
                        [
                            existing.ActiveVersionId,
                            defaultVersionId,
                            desiredVersionId,
                            versionId
                        ],
                        out versions))
                {
                    return Rejected(
                        snapshot.Generation,
                        $"The retention limit of {Bounds.MaxVersionsPerResource} committed " +
                        "Versions cannot preserve the active, default, desired, and incoming " +
                        "Versions.");
                }
                bool retentionEvicted = versions.Length < candidateVersions.Length;
                bool resourceMetaChanged = resourceMetadataChanged ||
                    current is null ||
                    retentionEvicted;

                if (contentChanged)
                {
                    string digestHex = WotContentDigest.ToHex(digest);
                    await m_resourceStore
                            .WriteAsync(digestHex, 0, content, cancellationToken)
                            .ConfigureAwait(false);
                }

                bool selectedVersionChanged = existing is null ||
                    !string.Equals(
                        previousSelectedVersionId,
                        selectedVersionId,
                        StringComparison.Ordinal);
                bool materializationChanged = selectedVersionChanged ||
                    (updatesSelectedVersion && admissionChanged);
                WoTLoadStateEnum loadState = materializationChanged
                    ? (parseFailed
                        ? WoTLoadStateEnum.Failed
                        : WoTLoadStateEnum.Unloaded)
                    : existing?.LoadState ?? WoTLoadStateEnum.Unloaded;
                WoTValidationOutcomeDataType? resourceValidation =
                    materializationChanged
                        ? version.Validation
                        : existing?.Validation;
                ImmutableArray<string> resourceDiagnostics =
                    materializationChanged
                        ? diagnostics.ToImmutable()
                        : existing?.Diagnostics ?? [];

                WotResource resource = existing is null
                    ? new WotResource(
                        groupId,
                        resourceId,
                        request.Kind,
                        versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: desiredVersionId,
                        enabled: true,
                        loadState: loadState,
                        validation: resourceValidation,
                        diagnostics: resourceDiagnostics,
                        epoch: 1,
                        name: resourceName,
                        description: resourceDescription,
                        thingId: selectedDocumentId,
                        title: selectedTitle)
                    {
                        MetaCreatedAt = now,
                        MetaModifiedAt = now
                    }
                    : existing.With(
                        versions: versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: desiredVersionId,
                        loadState: loadState,
                        validation: resourceValidation,
                        diagnostics: resourceDiagnostics,
                        epoch: resourceMetaChanged
                            ? existing.MetaEpoch + 1
                            : existing.MetaEpoch,
                        name: resourceName,
                        description: resourceDescription,
                        clearValidation: materializationChanged &&
                            resourceValidation is null);
                if (existing is not null && updatesLogicalDefault)
                {
                    resource = resource.WithSelectedVersionMetadata(
                        selectedDocumentId,
                        selectedTitle);
                }
                if (existing is not null && resourceMetaChanged)
                {
                    resource = resource.WithMeta(resource.MetaEpoch, modifiedAt: now);
                }

                WotRegistrySnapshot next = ReplaceResource(
                    snapshot,
                    group,
                    resource,
                    generation,
                    bumpGroupEpoch: existing is null);
                await CommitAndPublishAsync(
                        snapshot,
                        next,
                        [resource.Xid],
                        projectionOnly: !materializationChanged,
                        cancellationToken)
                    .ConfigureAwait(false);

                WoTOutcomeEnum outcome = parseFailed
                    ? WoTOutcomeEnum.Warning
                    : WoTOutcomeEnum.Success;
                return new WotRegistryMutationResult(
                    outcome, resource, generation, diagnostics.ToImmutable());
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> DeleteResourceAsync(
            string groupId,
            string resourceId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                return await DeleteThroughLifecycleAsync(
                    coordinator, groupId, resourceId, expectedEpoch, cancellationToken).ConfigureAwait(false);
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                WotResource? resource = group?.Resources.GetValueOrDefault(resourceId);
                if (group is null || resource is null)
                {
                    return Failed(snapshot.Generation, "Resource not found.");
                }
                if (expectedEpoch is { } epoch && epoch != resource.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                return await DeleteResourceLockedAsync(
                        snapshot,
                        group,
                        resource,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotDeleteResult> DeleteResourceAsync(
            string groupId,
            string resourceId,
            WoTDeletePolicyEnum policy,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                WotDeleteOutcome result = await coordinator.DeleteAsync(new WotDeleteRequest
                {
                    GroupId = groupId, ResourceId = resourceId, Policy = policy, ExpectedEpoch = expectedEpoch
                }, cancellationToken).ConfigureAwait(false);
                return result.Delete;
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResource? resource = snapshot.FindResource(groupId, resourceId);
                if (resource is null)
                {
                    return DeleteRefused(
                        WoTOutcomeEnum.Failed, policy, snapshot.Generation, "Resource not found.");
                }
                if (expectedEpoch is { } epoch && epoch != resource.Epoch)
                {
                    return DeleteRefused(
                        WoTOutcomeEnum.Rejected, policy, snapshot.Generation, "Epoch mismatch.");
                }

                return await DeleteResourceWithPolicyLockedAsync(
                    snapshot,
                    resource,
                    policy,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotDeleteResult> DeleteResourceWithPolicyLockedAsync(
            WotRegistrySnapshot snapshot,
            WotResource resource,
            WoTDeletePolicyEnum policy,
            CancellationToken cancellationToken)
        {
            WotRegistryLifecyclePlan plan = await PlanResourceLifecycleLockedAsync(
                snapshot, resource, delete: true, policy, cancellationToken).ConfigureAwait(false);
            if (plan.Mutation is { } mutation)
            {
                await CommitAndPublishAsync(snapshot,
                    mutation.Desired.WithPublicationState(plan.Result.Generation, snapshot.RefreshGeneration),
                    mutation.ChangedResourceXids.ToList(), projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            return plan.Result;
        }

        internal async ValueTask<WotRegistryLifecyclePlan> PlanResourceLifecycleAsync(
            string groupId,
            string resourceId,
            bool delete,
            WoTDeletePolicyEnum policy,
            long? expectedEpoch,
            CancellationToken cancellationToken)
        {
            groupId = ResolveAssignedGroupId(groupId);
            resourceId = ResolveAssignedResourceId(groupId, resourceId);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResource? resource = snapshot.FindResource(groupId, resourceId);
                if (resource is null)
                {
                    return new WotRegistryLifecyclePlan(null, null, DeleteRefused(
                        WoTOutcomeEnum.Failed, policy, snapshot.Generation, "Resource not found."));
                }
                if (expectedEpoch is { } epoch && epoch != resource.MetaEpoch)
                {
                    return new WotRegistryLifecyclePlan(null, resource, DeleteRefused(
                        WoTOutcomeEnum.Rejected, policy, snapshot.Generation, "Epoch mismatch."));
                }
                if (!delete && !resource.Enabled && resource.ActiveVersionId is null)
                {
                    return new WotRegistryLifecyclePlan(null, resource, DeleteRefused(
                        WoTOutcomeEnum.Unchanged, policy, snapshot.Generation, "The Resource is already unloaded."));
                }
                return await PlanResourceLifecycleLockedAsync(
                    snapshot, resource, delete, policy, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryLifecyclePlan> PlanResourceLifecycleLockedAsync(
            WotRegistrySnapshot snapshot,
            WotResource resource,
            bool delete,
            WoTDeletePolicyEnum policy,
            CancellationToken cancellationToken,
            ArrayOf<string> deletionCohort = default)
        {
            if (policy is not (WoTDeletePolicyEnum.Reject or WoTDeletePolicyEnum.Retire or
                WoTDeletePolicyEnum.Cascade or WoTDeletePolicyEnum.Force))
            {
                throw new ArgumentOutOfRangeException(nameof(policy));
            }
            WotDependentSet found =
                await WotDependencyGraph.FindDependentsWithFaultsAsync(
                    snapshot,
                    resource,
                    Bounds.MaxJsonDepth,
                    ReadContentAsync,
                    cancellationToken).ConfigureAwait(false);
            ImmutableArray<WotDependent> dependents = found.Dependents
                .Where(dependent => !deletionCohort.Contains(dependent.Xid)).ToImmutableArray();

            // The target's own blob being unreadable says nothing about
            // whether anything depends on it, and it is being removed
            // anyway. Every other unreadable document might be a dependent,
            // and no policy may treat "not checked" as "checked and clear".
            ImmutableArray<string> unknown = Except(found.Unreadable, resource.Xid)
                .Where(xid => !deletionCohort.Contains(xid)).ToImmutableArray();
            ImmutableArray<string>.Builder xids = ImmutableArray.CreateBuilder<string>();
            foreach (WotDependent dependent in dependents)
            {
                xids.Add(dependent.Xid);
            }
            ImmutableArray<string> dependentXids = xids.ToImmutable();

            if (delete && policy == WoTDeletePolicyEnum.Retire && !resource.Enabled &&
                resource.LoadState == WoTLoadStateEnum.Retired && resource.ActiveVersionId is null &&
                resource.RootNodeId.IsNull && resource.MaterializedNodeCount == 0)
            {
                return new WotRegistryLifecyclePlan(null, resource, new WotDeleteResult(
                    WoTOutcomeEnum.Unchanged, policy, snapshot.Generation, deleted: false, retired: true,
                    dependentXids, [], [], unknown, "The Resource is already retired; its document is retained."));
            }

            if (policy == WoTDeletePolicyEnum.Reject &&
                (dependents.Any(dependent => delete || dependent.Resource.ActiveVersionId is not null) ||
                 unknown.Length != 0))
            {
                // Nothing is written: a rejected delete has to leave every
                // piece of state exactly as the caller found it, or the
                // difference between Reject and Force is only a message.
                return new WotRegistryLifecyclePlan(null, resource, new WotDeleteResult(
                    WoTOutcomeEnum.Rejected,
                    policy,
                    snapshot.Generation,
                    deleted: false,
                    retired: false,
                    dependentXids,
                    [],
                    [],
                    unknown,
                    dependents.Length != 0
                        ? $"'{resource.Xid}' still has " +
                            dependentXids.Length.ToString(CultureInfo.InvariantCulture) +
                            " dependent document(s), and the delete policy is Reject."
                        : $"'{resource.Xid}' cannot be shown to be unreferenced: " +
                            unknown.Length.ToString(CultureInfo.InvariantCulture) +
                            " document(s) could not be read, and the delete policy is " +
                            "Reject."));
            }

            long generation = snapshot.Generation + 1;
            DateTime modifiedAt = DateTime.UtcNow;
            var changed = new List<string> { resource.Xid };
            WotRegistrySnapshot next = snapshot;
            bool deleted;
            bool retired;
            ImmutableArray<string> unloaded = [];
            ImmutableArray<string> failed = [];
            if (!delete || policy == WoTDeletePolicyEnum.Retire)
            {
                // The document stays stored and therefore stays
                // resolvable; only its projection comes down. Nothing that
                // might depend on it loses a reference, so an unreadable
                // document is not this policy's problem.
                long metaEpoch = resource.MetaEpoch + 1;
                WotResource retiredResource = resource.With(
                        enabled: false,
                        loadState: policy == WoTDeletePolicyEnum.Retire
                            ? WoTLoadStateEnum.Retired : WoTLoadStateEnum.Unloaded,
                        epoch: metaEpoch,
                        materializedNodeCount: 0,
                        clearActiveVersion: true,
                        clearRootNodeId: true)
                    .WithMeta(metaEpoch, modifiedAt: modifiedAt);
                next = WithResource(next, retiredResource, generation);
                deleted = false;
                retired = true;
                if (policy is WoTDeletePolicyEnum.Cascade or WoTDeletePolicyEnum.Force)
                {
                    (next, unloaded, failed) = ApplyPolicyToDependents(
                        next, policy, dependents, unknown, generation, modifiedAt, changed);
                }
            }
            else
            {
                next = WithoutResource(next, resource, generation);
                deleted = true;
                retired = true;
                (next, unloaded, failed) = ApplyPolicyToDependents(
                    next,
                    policy,
                    dependents,
                    unknown,
                    generation,
                    modifiedAt,
                    changed);
            }

            var mutation = new WotRegistryMutationImage(
                snapshot, next.WithPublicationState(snapshot.Generation, snapshot.RefreshGeneration), changed.ToArrayOf());
            return new WotRegistryLifecyclePlan(mutation, resource, new WotDeleteResult(
                WoTOutcomeEnum.Success,
                policy,
                generation,
                deleted,
                retired,
                dependentXids,
                unloaded,
                failed,
                unknown,
                DescribeDelete(policy, resource.Xid, unloaded, failed, unknown)));
        }

        /// <summary>
        /// Applies a delete policy to the documents that depended on the
        /// deleted one, and to the documents that might have.
        /// </summary>
        /// <remarks>
        /// <c>Cascade</c> unloads only the dependents that lost a reference:
        /// one whose references are all still answered by some other stored
        /// document was never in danger, and taking its projection down would
        /// remove something the delete did not break. A document that could not
        /// be read is not proof of anything, so <c>Cascade</c> leaves it alone
        /// and reports it - unloading it would take down a projection on a
        /// guess. <c>Force</c> marks every dependent <c>Failed</c> instead,
        /// because it deleted the target while they were still resolving
        /// through it, and marks the unreadable ones <c>Failed</c> too: it
        /// cannot say they were unaffected, and its contract is to say what it
        /// broke.
        /// </remarks>
        private static (
            WotRegistrySnapshot Snapshot,
            ImmutableArray<string> Unloaded,
            ImmutableArray<string> Failed) ApplyPolicyToDependents(
            WotRegistrySnapshot snapshot,
            WoTDeletePolicyEnum policy,
            ImmutableArray<WotDependent> dependents,
            ImmutableArray<string> unknown,
            long generation,
            DateTime modifiedAt,
            List<string> changed)
        {
            ImmutableArray<string>.Builder unloaded = ImmutableArray.CreateBuilder<string>();
            ImmutableArray<string>.Builder failed = ImmutableArray.CreateBuilder<string>();
            foreach (WotDependent dependent in dependents)
            {
                WoTLoadStateEnum state;
                if (policy == WoTDeletePolicyEnum.Cascade)
                {
                    if (!dependent.ResolvesOnlyThroughTarget)
                    {
                        continue;
                    }
                    state = WoTLoadStateEnum.Unloaded;
                    unloaded.Add(dependent.Xid);
                }
                else if (policy == WoTDeletePolicyEnum.Force)
                {
                    state = WoTLoadStateEnum.Failed;
                    failed.Add(dependent.Xid);
                }
                else
                {
                    continue;
                }
                long metaEpoch = dependent.Resource.MetaEpoch + 1;
                WotResource updated = dependent.Resource.With(
                        enabled: false,
                        loadState: state,
                        diagnostics: [
                            state == WoTLoadStateEnum.Failed
                                ? "A document this projection resolves through was force-deleted."
                                : "The only document this projection resolved through was deleted."
                        ],
                        epoch: metaEpoch,
                        materializedNodeCount: 0,
                        clearActiveVersion: true,
                        clearRootNodeId: true)
                    .WithMeta(metaEpoch, modifiedAt: modifiedAt);
                snapshot = WithResource(snapshot, updated, generation);
                changed.Add(dependent.Xid);
            }

            if (policy != WoTDeletePolicyEnum.Force)
            {
                return (snapshot, unloaded.ToImmutable(), failed.ToImmutable());
            }
            foreach (string xid in unknown)
            {
                // Every xid here is still in the snapshot and is not one of the
                // dependents above: 'unknown' excludes the target, nothing else
                // is removed, and a document whose content could not be read
                // states no edge, so it can never have been proven a dependent.
                WotResource unreadable = snapshot.FindResourceByXid(xid)!;
                failed.Add(xid);
                long metaEpoch = unreadable.MetaEpoch + 1;
                WotResource updated = unreadable.With(
                        enabled: false,
                        loadState: WoTLoadStateEnum.Failed,
                        diagnostics: [
                            "This document could not be read, so whether it resolved " +
                            "through the force-deleted document is unknown."
                        ],
                        epoch: metaEpoch,
                        materializedNodeCount: 0,
                        clearActiveVersion: true,
                        clearRootNodeId: true)
                    .WithMeta(metaEpoch, modifiedAt: modifiedAt);
                snapshot = WithResource(snapshot, updated, generation);
                changed.Add(xid);
            }
            return (snapshot, unloaded.ToImmutable(), failed.ToImmutable());
        }

        /// <summary>
        /// Removes one xid from a list, which is how the target's own
        /// unreadable blob is kept out of the "might depend on it" set.
        /// </summary>
        private static ImmutableArray<string> Except(
            ImmutableArray<string> values, string excluded)
        {
            if (values.IsDefaultOrEmpty)
            {
                return [];
            }
            ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();
            foreach (string value in values)
            {
                if (!string.Equals(value, excluded, StringComparison.Ordinal))
                {
                    builder.Add(value);
                }
            }
            return builder.ToImmutable();
        }

        private static string DescribeDelete(
            WoTDeletePolicyEnum policy,
            string xid,
            ImmutableArray<string> unloaded,
            ImmutableArray<string> failed,
            ImmutableArray<string> unknown)
        {
            string unreadable = unknown.IsDefaultOrEmpty
                ? string.Empty
                : " " +
                    unknown.Length.ToString(CultureInfo.InvariantCulture) +
                    " document(s) could not be read, so whether they depended on it is " +
                    "unknown.";
            return policy switch
            {
                WoTDeletePolicyEnum.Retire =>
                    $"'{xid}' was retired; the document remains stored and resolvable.",
                WoTDeletePolicyEnum.Cascade =>
                    $"'{xid}' was deleted and " +
                    unloaded.Length.ToString(CultureInfo.InvariantCulture) +
                    " dependent projection(s) were unloaded." +
                    unreadable,
                WoTDeletePolicyEnum.Force =>
                    $"'{xid}' was force-deleted; " +
                    failed.Length.ToString(CultureInfo.InvariantCulture) +
                    " remaining dependent(s) were marked Failed." +
                    unreadable,
                _ => $"'{xid}' was deleted."
            };
        }

        private static WotDeleteResult DeleteRefused(
            WoTOutcomeEnum outcome,
            WoTDeletePolicyEnum policy,
            long generation,
            string message)
        {
            return new WotDeleteResult(
                outcome, policy, generation, false, false, [], [], [], [], message);
        }

        private static WotRegistryMutationResult ToMutationResult(
            WotDeleteResult result,
            WotResource deletedResource)
        {
            return result.Outcome switch
            {
                WoTOutcomeEnum.Success => new WotRegistryMutationResult(
                    result.Outcome,
                    deletedResource,
                    result.Generation,
                    [],
                    result.Message),
                WoTOutcomeEnum.Rejected => Rejected(result.Generation, result.Message),
                _ => Failed(result.Generation, result.Message)
            };
        }

        private static WotRegistrySnapshot WithResource(
            WotRegistrySnapshot snapshot, WotResource resource, long generation)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            return snapshot.WithGroup(
                group.WithResources(
                    group.Resources.SetItem(resource.ResourceId, resource), group.Epoch),
                generation);
        }

        private static WotRegistrySnapshot WithoutResource(
            WotRegistrySnapshot snapshot, WotResource resource, long generation)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            return snapshot.WithGroup(
                group.WithResources(
                    group.Resources.Remove(resource.ResourceId), group.Epoch + 1),
                generation);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> SetDefaultVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            return MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch,
                (resource, generation) =>
                {
                    WotResourceVersion? selected = resource.FindVersion(versionId);
                    if (selected is null)
                    {
                        return (null, Rejected(generation - 1, $"Version '{versionId}' not found."));
                    }
                    if (string.Equals(
                            resource.DefaultVersionId,
                            versionId,
                            StringComparison.Ordinal))
                    {
                        return (resource, null);
                    }
                    WotResource updated = resource.With(
                        defaultVersionId: versionId,
                        desiredVersionId: versionId,
                        validation: selected.Validation,
                        epoch: resource.MetaEpoch + 1,
                        clearValidation: selected.Validation is null)
                        .WithSelectedVersionMetadata(
                            selected.DocumentId,
                            selected.Title)
                        .WithMeta(resource.MetaEpoch + 1, modifiedAt: DateTime.UtcNow);
                    return (updated, null);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> SetEnabledAsync(
            string groupId,
            string resourceId,
            bool enabled,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (!enabled && Volatile.Read(ref m_lifecycleCoordinator) is { } coordinator)
            {
                return coordinator.SetEnabledAsync(groupId, resourceId, false, expectedEpoch, cancellationToken);
            }
            return MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch,
                (resource, generation) =>
                {
                    if (resource.Enabled == enabled)
                    {
                        return (resource, null);
                    }
                    WotResource updated = resource.With(
                            enabled: enabled,
                            epoch: resource.MetaEpoch + 1)
                        .WithMeta(resource.MetaEpoch + 1, modifiedAt: DateTime.UtcNow);
                    return (updated, null);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> AddRegistryLabelAsync(
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            WotLabelValidator.Validate(key, value, Bounds);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                if (expectedEpoch is { } epoch && epoch != snapshot.Generation)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                if (!snapshot.Labels.ContainsKey(key) &&
                    snapshot.Labels.Count >= Bounds.MaxLabelsPerEntity)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        "The registry already holds the maximum of " +
                        $"{Bounds.MaxLabelsPerEntity} labels.");
                }
                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next = snapshot.WithLabels(
                    snapshot.Labels.SetItem(key, value), generation);
                await CommitAndPublishAsync(
                        snapshot, next, [], projectionOnly: true, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> RemoveRegistryLabelAsync(
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The Key argument is required.");
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                if (expectedEpoch is { } epoch && epoch != snapshot.Generation)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                if (!snapshot.Labels.ContainsKey(key))
                {
                    return Failed(snapshot.Generation, $"Label '{key}' not found.");
                }
                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next = snapshot.WithLabels(
                    snapshot.Labels.Remove(key), generation);
                await CommitAndPublishAsync(
                        snapshot, next, [], projectionOnly: true, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> AddGroupLabelAsync(
            string groupId,
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            WotLabelValidator.Validate(key, value, Bounds);
            return MutateGroupAsync(
                groupId,
                expectedEpoch,
                (group, generation) =>
                {
                    if (!group.Labels.ContainsKey(key) &&
                        group.Labels.Count >= Bounds.MaxLabelsPerEntity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"Group '{groupId}' already holds the maximum of " +
                            $"{Bounds.MaxLabelsPerEntity} labels.");
                    }
                    WotResourceGroup updated = group.WithLabels(
                        group.Labels.SetItem(key, value), generation);
                    return (updated, null);
                },
                cancellationToken,
                projectionOnly: true);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> RemoveGroupLabelAsync(
            string groupId,
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The Key argument is required.");
            }
            return MutateGroupAsync(
                groupId,
                expectedEpoch,
                (group, generation) =>
                {
                    if (!group.Labels.ContainsKey(key))
                    {
                        return (null, Failed(generation - 1, $"Label '{key}' not found."));
                    }
                    WotResourceGroup updated = group.WithLabels(
                        group.Labels.Remove(key), generation);
                    return (updated, null);
                },
                cancellationToken,
                projectionOnly: true);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> AddResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            WotLabelValidator.Validate(key, value, Bounds);
            return MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch,
                (resource, generation) =>
                {
                    if (!resource.Labels.ContainsKey(key) &&
                        resource.Labels.Count >= Bounds.MaxLabelsPerEntity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"Resource '{resourceId}' already holds the maximum of " +
                            $"{Bounds.MaxLabelsPerEntity} labels.");
                    }
                    WotResource updated = resource.WithMeta(
                        resource.MetaEpoch + 1,
                        resource.MetaLabels.SetItem(key, value),
                        DateTime.UtcNow);
                    return (updated, null);
                },
                cancellationToken,
                projectionOnly: true);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> RemoveResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The Key argument is required.");
            }
            return MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch,
                (resource, generation) =>
                {
                    if (!resource.Labels.ContainsKey(key))
                    {
                        return (null, Failed(generation - 1, $"Label '{key}' not found."));
                    }
                    WotResource updated = resource.WithMeta(
                        resource.MetaEpoch + 1,
                        resource.MetaLabels.Remove(key),
                        DateTime.UtcNow);
                    return (updated, null);
                },
                cancellationToken,
                projectionOnly: true);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> AddVersionLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            WotLabelValidator.Validate(key, value, Bounds);
            return MutateVersionAsync(
                groupId,
                resourceId,
                versionId,
                expectedEpoch,
                version =>
                {
                    if (!version.Labels.ContainsKey(key) &&
                        version.Labels.Count >= Bounds.MaxLabelsPerEntity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"Version '{versionId}' already holds the maximum of " +
                            $"{Bounds.MaxLabelsPerEntity} labels.");
                    }
                    return version.With(
                        modifiedAt: DateTime.UtcNow,
                        epoch: version.Epoch + 1,
                        labels: version.Labels.SetItem(key, value));
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotRegistryMutationResult> RemoveVersionLabelAsync(
            string groupId,
            string resourceId,
            string versionId,
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "The Key argument is required.");
            }
            return MutateVersionAsync(
                groupId,
                resourceId,
                versionId,
                expectedEpoch,
                version => version.Labels.ContainsKey(key)
                    ? version.With(
                        modifiedAt: DateTime.UtcNow,
                        epoch: version.Epoch + 1,
                        labels: version.Labels.Remove(key))
                    : null,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask ApplyProjectionResultsAsync(
            IReadOnlyList<WotResourceProjection> projections,
            CancellationToken cancellationToken = default)
        {
            if (projections is null || projections.Count == 0)
            {
                return;
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                (WotRegistrySnapshot next, ArrayOf<string> changed) =
                    BuildProjectionSnapshot(snapshot, projections);
                if (changed.Count == 0)
                {
                    return;
                }
                await CommitAndPublishAsync(
                        snapshot, next, changed.ToList(), projectionOnly: true, cancellationToken,
                        WotRegistryCommitScope.ProjectionMetadata)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Interlocked.Exchange(ref m_validatedStoreGeneration, null)?.Dispose();
            m_mutex.Dispose();
        }

        private async ValueTask StoreValidationAsync(
            string groupId,
            string resourceId,
            string versionId,
            WotResourceVersion expectedVersion,
            WoTDocumentKindEnum expectedKind,
            WoTValidationOutcomeDataType outcome,
            CancellationToken cancellationToken)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResource? resource = snapshot.FindResource(groupId, resourceId);
                WotResourceVersion? version = resource?.FindVersion(versionId);
                if (resource is null || version is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown,
                        "The Version was removed while validation was running.");
                }
                if (version.IncarnationId != expectedVersion.IncarnationId ||
                    resource.Kind != expectedKind ||
                    !string.Equals(version.Format, expectedVersion.Format, StringComparison.Ordinal) ||
                    !string.Equals(version.ContentType, expectedVersion.ContentType, StringComparison.Ordinal) ||
                    !string.Equals(
                        version.DigestHex,
                        expectedVersion.DigestHex,
                        StringComparison.Ordinal))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "The Version changed while validation was running.");
                }

                WotResourceVersion updatedVersion = version.With(validation: outcome);
                int index = resource.Versions.IndexOf(version);
                bool isDefault = string.Equals(
                    resource.DefaultVersionId,
                    versionId,
                    StringComparison.Ordinal);
                WotResource updated = resource.With(
                    versions: resource.Versions.SetItem(index, updatedVersion),
                    validation: isDefault ? outcome : resource.Validation,
                    epoch: resource.MetaEpoch);
                long generation = snapshot.Generation + 1;
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotRegistrySnapshot next = ReplaceResource(
                    snapshot,
                    group,
                    updated,
                    generation,
                    bumpGroupEpoch: false);
                await CommitAndPublishAsync(
                        snapshot,
                        next,
                        [updated.Xid],
                        projectionOnly: true,
                        cancellationToken,
                        validation: new WotValidationChange(updated.Xid, versionId))
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryMutationResult> MutateResourceAsync(
            string groupId,
            string resourceId,
            long? expectedEpoch,
            Func<WotResource, long, (WotResource? Updated, WotRegistryMutationResult? Rejection)> mutate,
            CancellationToken cancellationToken,
            bool projectionOnly = false)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResource? resource = snapshot.FindResource(groupId, resourceId);
                if (resource is null)
                {
                    return Failed(snapshot.Generation, "Resource not found.");
                }
                if (expectedEpoch is { } epoch && epoch != resource.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                long generation = snapshot.Generation + 1;
                (WotResource? updated, WotRegistryMutationResult? rejection) = mutate(resource, generation);
                if (rejection is not null)
                {
                    return rejection;
                }
                if (updated is null)
                {
                    return Failed(snapshot.Generation, "Mutation produced no result.");
                }
                if (ReferenceEquals(updated, resource))
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Unchanged,
                        resource,
                        snapshot.Generation,
                        []);
                }
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotRegistrySnapshot next = ReplaceResource(
                    snapshot,
                    group,
                    updated,
                    generation,
                    bumpGroupEpoch: false);
                await CommitAndPublishAsync(
                        snapshot, next, [updated.Xid], projectionOnly, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, updated, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryMutationResult> MutateVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            long? expectedEpoch,
            Func<WotResourceVersion, WotResourceVersion?> mutate,
            CancellationToken cancellationToken)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResource? resource = snapshot.FindResource(groupId, resourceId);
                WotResourceVersion? version = resource?.FindVersion(versionId);
                if (resource is null || version is null)
                {
                    return Failed(snapshot.Generation, "Version not found.");
                }
                if (expectedEpoch is { } epoch && epoch != version.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                WotResourceVersion? updatedVersion = mutate(version);
                if (updatedVersion is null)
                {
                    return Failed(snapshot.Generation, "Version mutation produced no result.");
                }
                int index = resource.Versions.IndexOf(version);
                WotResource updated = resource.With(
                    versions: resource.Versions.SetItem(index, updatedVersion),
                    epoch: resource.MetaEpoch);
                long generation = snapshot.Generation + 1;
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotRegistrySnapshot next = ReplaceResource(
                    snapshot,
                    group,
                    updated,
                    generation,
                    bumpGroupEpoch: false);
                await CommitAndPublishAsync(
                        snapshot,
                        next,
                        [updated.Xid],
                        projectionOnly: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, updated, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryMutationResult> MutateGroupAsync(
            string groupId,
            long? expectedEpoch,
            Func<WotResourceGroup, long, (WotResourceGroup? Updated, WotRegistryMutationResult? Rejection)> mutate,
            CancellationToken cancellationToken,
            bool projectionOnly = false)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                if (group is null)
                {
                    return Failed(snapshot.Generation, "Group not found.");
                }
                if (expectedEpoch is { } epoch && epoch != group.Epoch)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                long generation = snapshot.Generation + 1;
                (WotResourceGroup? updated, WotRegistryMutationResult? rejection) = mutate(group, generation);
                if (rejection is not null)
                {
                    return rejection;
                }
                if (updated is null)
                {
                    return Failed(snapshot.Generation, "Mutation produced no result.");
                }
                WotRegistrySnapshot next = snapshot.WithGroup(updated, generation);
                await CommitAndPublishAsync(
                        snapshot, next, [updated.Xid], projectionOnly, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotRegistryMutationResult> DeleteResourceLockedAsync(
            WotRegistrySnapshot snapshot,
            WotResourceGroup group,
            WotResource resource,
            CancellationToken cancellationToken)
        {
            long generation = snapshot.Generation + 1;
            WotResourceGroup nextGroup = group.WithResources(
                group.Resources.Remove(resource.ResourceId),
                group.Epoch + 1);
            WotRegistrySnapshot next = snapshot.WithGroup(nextGroup, generation);
            await CommitAndPublishAsync(
                    snapshot,
                    next,
                    [resource.Xid],
                    projectionOnly: false,
                    cancellationToken)
                .ConfigureAwait(false);
            return new WotRegistryMutationResult(
                WoTOutcomeEnum.Success,
                resource,
                generation,
                []);
        }

        private async ValueTask<WotRegistryMutationResult> DeleteVersionLockedAsync(
            WotRegistrySnapshot snapshot,
            WotResourceGroup group,
            WotResource resource,
            WotResourceVersion version,
            CancellationToken cancellationToken)
        {
            (WotRegistrySnapshot next, WotRegistryMutationResult result) =
                PlanVersionDeletion(snapshot, group, resource, version);
            if (result.Changed)
            {
                await CommitAndPublishAsync(
                    snapshot, next, [resource.Xid], projectionOnly: false, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        private static (WotRegistrySnapshot Snapshot, WotRegistryMutationResult Result) PlanVersionDeletion(
            WotRegistrySnapshot snapshot,
            WotResourceGroup group,
            WotResource resource,
            WotResourceVersion version)
        {
            long generation = snapshot.Generation + 1;
            WotRegistrySnapshot next;
            WotResource resultResource;
            if (resource.Versions.Length == 1)
            {
                WotResourceGroup nextGroup = group.WithResources(
                    group.Resources.Remove(resource.ResourceId),
                    group.Epoch + 1);
                next = snapshot.WithGroup(nextGroup, generation);
                resultResource = resource;
            }
            else
            {
                ImmutableArray<WotResourceVersion> versions =
                    resource.Versions.Remove(version);
                var committedVersions = versions
                    .Where(candidate => candidate.HasContent)
                    .ToImmutableArray();
                if (committedVersions.IsEmpty)
                {
                    return (snapshot, Rejected(
                        snapshot.Generation,
                        "Deleting the last committed Version would leave only pending Versions."));
                }
                WotResourceVersion? currentDefault = versions.FirstOrDefault(candidate =>
                    candidate.HasContent &&
                    string.Equals(
                        candidate.VersionId,
                        resource.DefaultVersionId,
                        StringComparison.Ordinal));
                WotResourceVersion selectedDefault =
                    currentDefault ?? committedVersions[^1];
                string defaultVersionId = selectedDefault.VersionId;
                WotResourceVersion? survivingDesired = versions.FirstOrDefault(candidate =>
                    candidate.HasContent &&
                    string.Equals(
                        candidate.VersionId,
                        resource.DesiredVersionId,
                        StringComparison.Ordinal));
                string desiredVersionId =
                    survivingDesired?.VersionId ?? defaultVersionId;
                WotResourceVersion selectedVersion =
                    survivingDesired ?? selectedDefault;
                string? previousSelectedVersionId =
                    resource.DesiredVersionId ?? resource.DefaultVersionId;
                bool selectedVersionChanged = !string.Equals(
                    previousSelectedVersionId,
                    selectedVersion.VersionId,
                    StringComparison.Ordinal);
                bool activeVersionDeleted = string.Equals(
                    resource.ActiveVersionId,
                    version.VersionId,
                    StringComparison.Ordinal);
                long metaEpoch = resource.MetaEpoch + 1;
                resultResource = resource.With(
                        versions: versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: desiredVersionId,
                        loadState: selectedVersionChanged || activeVersionDeleted
                            ? WoTLoadStateEnum.Unloaded
                            : resource.LoadState,
                        validation: selectedVersionChanged
                            ? selectedVersion.Validation
                            : resource.Validation,
                        diagnostics: selectedVersionChanged ? [] : resource.Diagnostics,
                        epoch: metaEpoch,
                        materializedNodeCount: activeVersionDeleted
                            ? 0
                            : resource.MaterializedNodeCount,
                        clearActiveVersion: activeVersionDeleted,
                        clearValidation: selectedVersionChanged &&
                            selectedVersion.Validation is null,
                        clearRootNodeId: activeVersionDeleted);
                if (selectedVersionChanged)
                {
                    resultResource = resultResource.WithSelectedVersionMetadata(
                        selectedVersion.DocumentId,
                        selectedVersion.Title);
                }
                resultResource = resultResource
                    .WithMeta(metaEpoch, modifiedAt: DateTime.UtcNow);
                next = ReplaceResource(
                    snapshot,
                    group,
                    resultResource,
                    generation,
                    bumpGroupEpoch: false);
            }
            return (next, new WotRegistryMutationResult(
                WoTOutcomeEnum.Success,
                resultResource,
                generation,
                []));
        }

        private static WotRegistrySnapshot ReplaceResource(
            WotRegistrySnapshot snapshot,
            WotResourceGroup group,
            WotResource resource,
            long generation,
            bool bumpGroupEpoch)
        {
            WotResourceGroup nextGroup = group.WithResources(
                group.Resources.SetItem(resource.ResourceId, resource),
                bumpGroupEpoch ? group.Epoch + 1 : group.Epoch);
            return snapshot.WithGroup(nextGroup, generation);
        }

        private async ValueTask CommitAndPublishAsync(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot intended,
            IReadOnlyList<string> changed,
            bool projectionOnly,
            CancellationToken cancellationToken,
            WotRegistryCommitScope commitScope = WotRegistryCommitScope.Full,
            WotValidationChange? validation = null)
        {
            IWotRegistryPreparedCommit? prepared = null;
            try
            {
                try
                {
                    if (m_store is IWotRegistryPreparedStore { SupportsPreparedCommits: true } preparedStore)
                    {
                        if (m_validatedStoreGeneration is null)
                        {
                            await RefreshValidatedStoreGenerationAsync(cancellationToken).ConfigureAwait(false);
                        }
                        IWotRegistryValidatedGeneration captured = m_validatedStoreGeneration ??
                            throw new InvalidOperationException("No validated store generation is available.");
                        if (captured.Snapshot.Generation != previous.Generation)
                        {
                            throw new InvalidOperationException("The registry and captured store generations differ.");
                        }
                        prepared = await preparedStore.PrepareCommitAsync(
                            intended, captured, commitScope, cancellationToken).ConfigureAwait(false);
                        await prepared.CommitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await m_store.CommitAsync(intended, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (WotRegistryCommitDurabilityUncertainException exception)
                {
                    exception.CommittedSnapshot = RestoreVersionIncarnations(exception.CommittedSnapshot, intended);
                    Volatile.Write(ref m_snapshot, exception.CommittedSnapshot);
                    await CompleteCommittedChangeAsync(previous, exception.CommittedSnapshot, changed,
                        projectionOnly, validation, exception.PersistenceFailure).ConfigureAwait(false);
                    throw;
                }
                catch (WotRegistryCommitNotCommittedException)
                {
                    throw;
                }
                catch (WotRegistryCommitIndeterminateException)
                {
                    m_recoverySnapshot = intended;
                    m_reloadRequired = true;
                    if (validation is not null)
                    {
                        m_pendingValidation = new PendingValidationNotification(previous, intended, validation);
                    }
                    throw;
                }

                Volatile.Write(ref m_snapshot, intended);
                await CompleteCommittedChangeAsync(previous, intended, changed, projectionOnly, validation)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (prepared is not null)
                {
                    await prepared.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask CompleteCommittedChangeAsync(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot committed,
            IReadOnlyList<string> changed,
            bool projectionOnly,
            WotValidationChange? validation,
            Exception? priorFailure = null)
        {
            if (validation is not null)
            {
                m_pendingValidation = new PendingValidationNotification(previous, committed, validation);
            }
            await RefreshValidatedStoreGenerationAfterCommitAsync(committed, priorFailure).ConfigureAwait(false);
            if (validation is not null)
            {
                m_pendingValidation = null;
            }
            RaiseChanged(previous, committed, changed, projectionOnly, priorFailure, validation: validation);
        }

        private async ValueTask RefreshValidatedStoreGenerationAsync(CancellationToken cancellationToken)
        {
            if (m_store is not IWotRegistryPreparedStore { SupportsPreparedCommits: true } preparedStore)
            {
                return;
            }
            IWotRegistryValidatedGeneration next = await preparedStore
                .CaptureValidatedGenerationAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref m_validatedStoreGeneration, next)?.Dispose();
        }

        private async ValueTask RefreshValidatedStoreGenerationAfterCommitAsync(
            WotRegistrySnapshot committed,
            Exception? priorFailure = null)
        {
            try
            {
                await RefreshValidatedStoreGenerationAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                m_reloadRequired = true;
                m_recoverySnapshot = committed;
                throw new WotRegistryCommitDurabilityUncertainException(
                    committed,
                    priorFailure is null ? failure : new AggregateException(priorFailure, failure));
            }
        }

        private async ValueTask InitializeCoreAsync(CancellationToken cancellationToken)
        {
            if (m_recoverySynchronizationActive)
            {
                throw new InvalidOperationException("A recovered projection must finish before the registry is reloaded.");
            }
            m_reloadRequired = true;
            Interlocked.Exchange(ref m_validatedStoreGeneration, null)?.Dispose();
            WotRegistrySnapshot loaded = await m_store.LoadAsync(cancellationToken).ConfigureAwait(false);
            WotRegistryIdentity.ValidateSnapshot(loaded);
            loaded = RestoreVersionIncarnations(
                loaded,
                m_recoverySnapshot?.Generation == loaded.Generation ? m_recoverySnapshot : m_snapshot);
            PendingValidationNotification? pendingValidation = m_pendingValidation;
            bool validationCommitted = false;
            if (pendingValidation is not null)
            {
                WotRegistrySnapshot expected;
                if (loaded.Generation == pendingValidation.Intended.Generation)
                {
                    validationCommitted = true;
                    expected = pendingValidation.Intended;
                }
                else if (loaded.Generation == pendingValidation.Previous.Generation)
                {
                    expected = pendingValidation.Previous;
                }
                else
                {
                    throw new InvalidOperationException(
                        "The pending validation decision does not match the authoritative registry generation.");
                }
                FileWotRegistryStore.ValidateRecoveryMetadata(expected, loaded);
            }
            WotRegistrySnapshot hydrated = await HydrateDependencyMetadataAsync(loaded, cancellationToken)
                .ConfigureAwait(false);
            await RefreshValidatedStoreGenerationAsync(cancellationToken).ConfigureAwait(false);
            bool migrate = !ReferenceEquals(hydrated, loaded) &&
                m_store is IWotRegistryPreparedStore { SupportsPreparedCommits: true };
            Volatile.Write(ref m_snapshot, migrate ? loaded : hydrated);
            m_recoverySnapshot = null;
            m_reloadRequired = false;
            if (migrate)
            {
                WotRegistrySnapshot migrated = hydrated.WithPublicationState(
                    checked(loaded.Generation + 1), hydrated.RefreshGeneration);
                try
                {
                    await CommitAndPublishAsync(
                        loaded, migrated, [], projectionOnly: true, cancellationToken,
                        WotRegistryCommitScope.Full).ConfigureAwait(false);
                }
                catch (WotRegistryCommitDurabilityUncertainException)
                {
                    throw;
                }
                catch
                {
                    m_reloadRequired = true;
                    throw;
                }
            }
            if (pendingValidation is not null)
            {
                m_pendingValidation = null;
                if (validationCommitted)
                {
                    RaiseChanged(pendingValidation.Previous, m_snapshot, [pendingValidation.Change.ResourceXid],
                        projectionOnly: true, validation: pendingValidation.Change);
                }
            }
        }

        private void EnsureMutationAllowed()
        {
            EnsureReadableGeneration();
            if (m_runtimeRecoveryRequired)
            {
                throw new InvalidOperationException(
                    "The WoT registry requires runtime publication recovery before further mutation.");
            }
        }

        private void EnsureReadableGeneration()
        {
            if (m_reloadRequired)
            {
                throw new InvalidOperationException(
                    "The WoT registry requires a successful InitializeAsync reload " +
                    "before further mutation.");
            }
        }

        private void RaiseChanged(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot current,
            IReadOnlyList<string> changed,
            bool projectionOnly,
            Exception? priorFailure = null,
            bool materializationHandled = false,
            WotValidationChange? validation = null)
        {
            EventHandler<WotRegistryChangedEventArgs>? observers = Changed;
            if (observers is null)
            {
                return;
            }
            var change = new WotRegistryChangedEventArgs(
                previous, current, changed, projectionOnly, materializationHandled, validation);
            List<Exception>? failures = null;
            foreach (EventHandler<WotRegistryChangedEventArgs> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(this, change);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures is null)
            {
                return;
            }
            if (priorFailure is not null)
            {
                failures.Insert(0, priorFailure);
            }
            throw new WotRegistryCommitDurabilityUncertainException(
                current, failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }

        private bool TryTrim(
            ImmutableArray<WotResourceVersion> versions,
            int max,
            IEnumerable<string?> protectedVersionIds,
            out ImmutableArray<WotResourceVersion> trimmed)
        {
            int committedCount = versions.Count(version => version.HasContent);
            if (committedCount <= max)
            {
                trimmed = versions;
                return true;
            }
            var protectedIds = new HashSet<string>(
                protectedVersionIds
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!),
                StringComparer.Ordinal);
            protectedIds.IntersectWith(
                versions
                    .Where(version => version.HasContent)
                    .Select(version => version.VersionId));
            foreach (WotResourceVersion version in versions)
            {
                if (version.HasContent && IsVersionLeased(version))
                {
                    protectedIds.Add(version.VersionId);
                }
            }
            if (protectedIds.Count > max)
            {
                trimmed = default;
                return false;
            }

            var retained = versions.ToBuilder();
            while (committedCount > max)
            {
                int removeAt = 0;
                while (removeAt < retained.Count &&
                    (!retained[removeAt].HasContent ||
                        protectedIds.Contains(retained[removeAt].VersionId)))
                {
                    removeAt++;
                }
                if (removeAt >= retained.Count)
                {
                    trimmed = default;
                    return false;
                }
                retained.RemoveAt(removeAt);
                committedCount--;
            }
            trimmed = retained.ToImmutable();
            return true;
        }

        private bool CanRetainIncomingCommittedVersion(
            WotResource resource,
            int max,
            string incomingVersionId)
        {
            string?[] protectedVersionIds =
            [
                resource.ActiveVersionId,
                resource.DefaultVersionId,
                resource.DesiredVersionId,
                incomingVersionId
            ];
            var protectedIds = new HashSet<string>(
                protectedVersionIds
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!),
                StringComparer.Ordinal);
            int protectedCommittedCount = resource.Versions.Count(version =>
                version.HasContent && (protectedIds.Contains(version.VersionId) || IsVersionLeased(version)));
            return protectedCommittedCount + 1 <= max;
        }

        private static string NextVersionId(WotResource? existing)
        {
            long next = 1;
            if (existing is not null)
            {
                foreach (WotResourceVersion version in existing.Versions)
                {
                    if (long.TryParse(
                            version.VersionId,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out long value) &&
                        value >= next)
                    {
                        if (value == long.MaxValue)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadOutOfRange,
                                "The server-assigned Version sequence is exhausted.");
                        }
                        next = value + 1;
                    }
                }
            }
            return WotRegistrySnapshot.FormatVersionId(next);
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }

        private static string DeriveResourceId(
            WotResourceGroup group,
            WotUpsertResourceRequest request,
            string? sourceId)
        {
            if (!string.IsNullOrWhiteSpace(request.ResourceId))
            {
                if (group.Resources.ContainsKey(request.ResourceId!))
                {
                    return request.ResourceId!;
                }
                if (group.CatalogUri is not null)
                {
                    WotRegistryIdentity.RequireUri(sourceId);
                    WotResource? mappedAuthority = group.Resources.Values.FirstOrDefault(
                        resource => string.Equals(resource.SourceId, sourceId, StringComparison.Ordinal));
                    return mappedAuthority?.ResourceId ??
                        AllocateIdentity(sourceId!, group.Resources.Keys, string.Empty);
                }
                string supplied = NormalizeSegment(request.ResourceId!, nameof(request.ResourceId));
                if (group.Resources.ContainsKey(supplied))
                {
                    return supplied;
                }
                if (group.Resources.Keys.Any(id => string.Equals(id, supplied, StringComparison.OrdinalIgnoreCase)))
                {
                    throw InvalidAuthority("The supplied resource identifier collides with an existing assignment.");
                }
                WotResource? mapped = sourceId is null ? null : group.Resources.Values.FirstOrDefault(
                    resource => string.Equals(resource.SourceId, sourceId, StringComparison.Ordinal));
                return mapped?.ResourceId ?? supplied;
            }
            WotRegistryIdentity.RequireUri(sourceId);
            WotResource? existing = group.Resources.Values.FirstOrDefault(
                resource => string.Equals(resource.SourceId, sourceId, StringComparison.Ordinal));
            return existing?.ResourceId ?? AllocateIdentity(sourceId!, group.Resources.Keys, string.Empty);
        }

        private static void EnsureDocumentKind(WoTDocumentKindEnum kind)
        {
            if (!WotDocumentKinds.IsDocument(kind))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "A creation document kind must be ThingDescription or ThingModel, not a selector.");
            }
        }

        private static string DefaultGroupFor(WoTDocumentKindEnum kind)
        {
            return kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels
                        : WotRegistryGroups.ThingDescriptions;
        }

        internal static string NormalizeSegment(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty identifier is required.", paramName);
            }
            return Slugify(value);
        }

        /// <summary>
        /// Only legacy, unbound programmatic identifiers use the historical normalizer.
        /// Existing assigned identifiers, including case-sensitive typed allocations, win first.
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns></returns>
        private string ResolveAssignedGroupId(string groupId)
        {
            if (m_snapshot.Groups.ContainsKey(groupId))
            {
                return groupId;
            }
            string normalized = NormalizeSegment(groupId, nameof(groupId));
            foreach (WotResourceGroup group in m_snapshot.Groups.Values)
            {
                if (string.Equals(group.GroupId, normalized, StringComparison.OrdinalIgnoreCase) &&
                    (group.CatalogUri is not null ||
                        !string.Equals(group.GroupId, normalized, StringComparison.Ordinal)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdExists,
                        "The supplied group identifier collides with an existing assignment; " +
                        "use its exact assigned ID.");
                }
            }
            return normalized;
        }

        private string ResolveAssignedResourceId(string groupId, string resourceId)
        {
            return m_snapshot.FindResource(groupId, resourceId) is not null
                ? resourceId : NormalizeSegment(resourceId, nameof(resourceId));
        }

        internal static bool IsValidExplicitVersionId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128)
            {
                return false;
            }
            if (!IsAsciiAlphaNumeric(value[0]) && value[0] != '_')
            {
                return false;
            }
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (!IsAsciiAlphaNumeric(c) &&
                    c is not ('-' or '.' or '_' or '~' or ':' or '@'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ValidateExplicitVersionId(string value, string paramName)
        {
            if (!IsValidExplicitVersionId(value))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    $"'{paramName}' must be 1-128 characters, start with an ASCII " +
                    "letter, digit, or '_', and contain only ASCII letters, digits, " +
                    "'-', '.', '_', '~', ':', or '@'.");
            }
            return value;
        }

        private static bool IsAsciiAlphaNumeric(char value)
        {
            return value is (>= 'A' and <= 'Z') or
                (>= 'a' and <= 'z') or
                (>= '0' and <= '9');
        }

        private static string Slugify(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.')
                {
                    builder.Append(c);
                }
                else if (c is >= 'A' and <= 'Z')
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (c is ' ' or ':' or '/' or '#')
                {
                    builder.Append('-');
                }
            }
            string slug = builder.ToString().Trim('-', '.');
            return slug.Length == 0 ? Guid.NewGuid().ToString("N") : slug;
        }

        private static WoTValidationOutcomeDataType FailedValidation(string reason)
        {
            return new WoTValidationOutcomeDataType
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Failed,
                FormatReason = reason,
                CompatibilityValidated = false,
                CompatibilityOutcome = WoTOutcomeEnum.Skipped,
                ValidatedAt = DateTime.UtcNow,
                VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
            };
        }

        private static WotRegistryMutationResult Failed(long generation, string message)
        {
            return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Failed, null, generation,
                        [message], message);
        }

        private static WotRegistryMutationResult Rejected(long generation, string message)
        {
            return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected, null, generation,
                        [message], message);
        }

        private readonly record struct VersionCreateResult(
            WotResource? Resource,
            WotResourceVersion? Version,
            bool Created,
            bool CreatedResource = false);

        private readonly IWotRegistryStore m_store;
        private readonly WotProjectionCompatibilityMode m_projectionCompatibilityMode;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private WotRegistrySnapshot m_snapshot;
        private WotRegistrySnapshot? m_recoverySnapshot;
        private IWotRegistryValidatedGeneration? m_validatedStoreGeneration;
        private bool m_reloadRequired;
        private PendingValidationNotification? m_pendingValidation;
        private bool m_runtimeRecoveryRequired;

        private sealed record PendingValidationNotification(
            WotRegistrySnapshot Previous, WotRegistrySnapshot Intended, WotValidationChange Change);
    }
}
