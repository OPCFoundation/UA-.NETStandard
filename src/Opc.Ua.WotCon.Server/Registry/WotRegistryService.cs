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
    public sealed class WotRegistryService :
        IWotRegistryService,
        IWotVersionedRegistryService,
        IDisposable
    {
        /// <summary>
        /// Initializes a new registry service over the supplied store.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store = null,
            WotRegistryPersistenceBounds? bounds = null)
        {
            m_store = store ?? new InMemoryWotRegistryStore();
            m_resourceStore = m_store is IWotRegistryResourceStoreProvider provider
                ? provider.ResourceStore
                : new InMemoryResourceStore();
            Bounds = bounds ?? new WotRegistryPersistenceBounds();
            Bounds.Validate();
            m_snapshot = WotRegistrySnapshot.Empty;
        }

        /// <inheritdoc/>
        public WotRegistrySnapshot Current => Volatile.Read(ref m_snapshot);

        /// <inheritdoc/>
        public WotRegistryPersistenceBounds Bounds { get; }

        /// <inheritdoc/>
        public event EventHandler<WotRegistryChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_reloadRequired = true;
                WotRegistrySnapshot loaded = await m_store
                    .LoadAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, loaded);
                m_reloadRequired = false;
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
            groupId = NormalizeSegment(groupId, nameof(groupId));
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? existing = snapshot.FindGroup(groupId);
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
            groupId = NormalizeSegment(groupId, nameof(groupId));
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                if (snapshot.FindGroup(groupId) is not null)
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
            groupId = NormalizeSegment(groupId, nameof(groupId));
            resourceId = NormalizeSegment(resourceId, nameof(resourceId));
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotResource? existing = m_snapshot.FindResource(groupId, resourceId);
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
            groupId = NormalizeSegment(groupId, nameof(groupId));
            resourceId = NormalizeSegment(resourceId, nameof(resourceId));
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
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
            return result.Created ? (result.Resource!, result.Version!) : null;
        }

        private async ValueTask<VersionCreateResult> CreateVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            WoTDocumentKindEnum kind,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            groupId = NormalizeSegment(groupId, nameof(groupId));
            resourceId = NormalizeSegment(resourceId, nameof(resourceId));
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                WotResource? existing = group?.Resources.GetValueOrDefault(resourceId);
                if (getOrCreate &&
                    string.IsNullOrWhiteSpace(versionId) &&
                    existing?.DefaultVersion is { } defaultVersion)
                {
                    return new VersionCreateResult(existing, defaultVersion, false);
                }
                string assignedVersionId = string.IsNullOrWhiteSpace(versionId)
                    ? NextVersionId(existing)
                    : NormalizeSegment(versionId, nameof(versionId));
                if (existing?.FindVersion(assignedVersionId) is { } existingVersion)
                {
                    return getOrCreate
                        ? new VersionCreateResult(existing, existingVersion, false)
                        : default;
                }

                if (group is null)
                {
                    if (snapshot.Groups.Count >= Bounds.MaxGroups)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"The registry already holds the maximum of {Bounds.MaxGroups} groups.");
                    }
                    group = new WotResourceGroup(groupId, kind, epoch: 0);
                }
                if (existing is null &&
                    group.Resources.Count >= Bounds.MaxResourcesPerGroup)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"Group '{groupId}' already holds the maximum of " +
                        $"{Bounds.MaxResourcesPerGroup} resources.");
                }
                if (existing is not null &&
                    existing.Versions.Length >= Bounds.MaxVersionsPerResource)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"Resource '{resourceId}' already holds the maximum of " +
                        $"{Bounds.MaxVersionsPerResource} versions.");
                }

                DateTime now = DateTime.UtcNow;
                WotResourceVersion version =
                    WotResourceVersion.CreatePlaceholder(assignedVersionId, now);
                WotResource resource;
                bool resourceCreated = existing is null;
                if (resourceCreated)
                {
                    resource = new WotResource(
                        groupId,
                        resourceId,
                        kind,
                        [version],
                        defaultVersionId: assignedVersionId,
                        desiredVersionId: assignedVersionId,
                        enabled: true,
                        loadState: WoTLoadStateEnum.Unloaded,
                        epoch: 1,
                        name: resourceId)
                    {
                        MetaCreatedAt = now,
                        MetaModifiedAt = now
                    };
                }
                else
                {
                    long metaEpoch = existing!.MetaEpoch + 1;
                    resource = existing.With(
                            versions: existing.Versions.Add(version),
                            defaultVersionId: assignedVersionId,
                            desiredVersionId: assignedVersionId,
                            epoch: metaEpoch)
                        .WithMeta(metaEpoch, modifiedAt: now);
                }

                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next = ReplaceResource(
                    snapshot,
                    group,
                    resource,
                    generation,
                    bumpGroupEpoch: resourceCreated);
                await CommitAndPublishAsync(
                        snapshot,
                        next,
                        [resource.Xid],
                        projectionOnly: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new VersionCreateResult(resource, version, true);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistryMutationResult> DeleteVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default)
        {
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

                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next;
                WotResource? resultResource;
                if (resource.Versions.Length == 1)
                {
                    WotResourceGroup nextGroup = group.WithResources(
                        group.Resources.Remove(resourceId),
                        group.Epoch + 1);
                    next = snapshot.WithGroup(nextGroup, generation);
                    resultResource = resource;
                }
                else
                {
                    ImmutableArray<WotResourceVersion> versions =
                        resource.Versions.Remove(version);
                    string defaultVersionId = string.Equals(
                        resource.DefaultVersionId,
                        versionId,
                        StringComparison.Ordinal)
                            ? versions[^1].VersionId
                            : resource.DefaultVersionId!;
                    string? desiredVersionId = string.Equals(
                        resource.DesiredVersionId,
                        versionId,
                        StringComparison.Ordinal)
                            ? defaultVersionId
                            : resource.DesiredVersionId;
                    long metaEpoch = resource.MetaEpoch + 1;
                    WotResource updated = resource.With(
                            versions: versions,
                            defaultVersionId: defaultVersionId,
                            desiredVersionId: desiredVersionId,
                            epoch: metaEpoch)
                        .WithMeta(metaEpoch, modifiedAt: DateTime.UtcNow);
                    next = ReplaceResource(
                        snapshot,
                        group,
                        updated,
                        generation,
                        bumpGroupEpoch: false);
                    resultResource = updated;
                }
                await CommitAndPublishAsync(
                        snapshot,
                        next,
                        [resource.Xid],
                        projectionOnly: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, resultResource, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(
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
            ByteString content = await ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
            WoTValidationOutcomeDataType outcome = ValidateContent(content);

            await MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch: null,
                (current, generation) => (
                    current.With(validation: outcome, epoch: current.Epoch),
                    null),
                cancellationToken).ConfigureAwait(false);
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
                    snapshot, next, [resource.Xid], projectionOnly: false, cancellationToken)
                .ConfigureAwait(false);
            return resource;
        }

        private static WoTValidationOutcomeDataType ValidateContent(ByteString content)
        {
            try
            {
                using var document = WotDocument.Parse(content.Span.ToArray());
                _ = document.Id;
                return new WoTValidationOutcomeDataType
                {
                    FormatValidated = true,
                    FormatOutcome = WoTOutcomeEnum.Success,
                    CompatibilityValidated = false,
                    CompatibilityOutcome = WoTOutcomeEnum.Skipped,
                    ValidatedAt = DateTime.UtcNow,
                    VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
                };
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return FailedValidation(ex.Message);
            }
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
                : NormalizeSegment(request.GroupId!, nameof(request.GroupId));

            ByteString content = ByteString.From(request.Content.Span.ToArray());

            // Light parse to derive the kind/id/title and to record a format
            // failure state for a document that cannot even be parsed. Full WoT
            // validation and projection are performed by the coordinator.
            string? thingId = null;
            string? title = null;
            WoTValidationOutcomeDataType? validation = null;
            ImmutableArray<string>.Builder diagnostics = ImmutableArray.CreateBuilder<string>();
            bool parseFailed = false;
            try
            {
                var options = new WotNodeSetConverterOptions
                {
                    MaxJsonDocumentSize = Bounds.MaxDocumentBytes,
                    MaxJsonDepth = Bounds.MaxJsonDepth
                };
                using var document = WotDocument.Parse(content.Span.ToArray(), options);
                thingId = document.Id;
                title = document.Title;
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                parseFailed = true;
                diagnostics.Add($"Document parse failed: {ex.Message}");
                validation = FailedValidation(ex.Message);
            }

            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
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

                string resourceId = DeriveResourceId(request, thingId, title);
                WotResource? existing = group.Resources.TryGetValue(
                    resourceId, out WotResource? found) ? found : null;

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

                ByteString digest = WotContentDigest.Compute(content);
                if (string.IsNullOrWhiteSpace(request.VersionId) &&
                    existing?.DefaultVersion is { HasContent: true } defaultVersion &&
                    WotContentDigest.Equal(defaultVersion.Digest, digest) &&
                    !parseFailed)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Unchanged,
                        existing,
                        snapshot.Generation,
                        [],
                        "Content digest unchanged.");
                }
                string versionId = string.IsNullOrWhiteSpace(request.VersionId)
                    ? NextVersionId(existing)
                    : NormalizeSegment(request.VersionId!, nameof(request.VersionId));
                WotResourceVersion? current = existing?.FindVersion(versionId);
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

                if (current?.HasContent == true &&
                    WotContentDigest.Equal(current.Digest, digest) &&
                    !parseFailed)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Unchanged,
                        existing,
                        snapshot.Generation,
                        [],
                        "Content digest unchanged.");
                }

                string digestHex = WotContentDigest.ToHex(digest);
                await m_resourceStore
                        .WriteAsync(digestHex, 0, content, cancellationToken)
                        .ConfigureAwait(false);

                long generation = snapshot.Generation + 1;
                DateTime now = DateTime.UtcNow;
                WotResourceVersion version = current is null
                    ? new WotResourceVersion(
                        versionId,
                        digest,
                        content.Length,
                        request.ContentType,
                        request.Format,
                        createdAt: now,
                        modifiedAt: now)
                    : current.With(
                        digest: digest,
                        contentLength: content.Length,
                        contentType: request.ContentType,
                        format: request.Format,
                        modifiedAt: now,
                        epoch: current.Epoch + 1,
                        hasContent: true);

                ImmutableArray<WotResourceVersion> versions;
                if (existing is null)
                {
                    versions = [version];
                }
                else if (current is null)
                {
                    versions = Trim(
                        existing.Versions.Add(version),
                        Bounds.MaxVersionsPerResource);
                }
                else
                {
                    versions = existing.Versions.SetItem(
                        existing.Versions.IndexOf(current),
                        version);
                }

                string? defaultVersionId = request.SetAsDefault
                    ? versionId
                    : existing?.DefaultVersionId ?? versionId;
                bool resourceMetaChanged = existing is null ||
                    current is null ||
                    !string.Equals(
                        existing.DefaultVersionId,
                        defaultVersionId,
                        StringComparison.Ordinal);

                WoTLoadStateEnum loadState = parseFailed
                    ? WoTLoadStateEnum.Failed
                    : WoTLoadStateEnum.Unloaded;

                WotResource resource = existing is null
                    ? new WotResource(
                        groupId,
                        resourceId,
                        request.Kind,
                        versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: request.SetAsDefault ? versionId : null,
                        enabled: true,
                        loadState: loadState,
                        validation: validation,
                        diagnostics: diagnostics.ToImmutable(),
                        epoch: 1,
                        name: request.Name ?? title ?? resourceId,
                        description: request.Description,
                        thingId: thingId,
                        title: title)
                    {
                        MetaCreatedAt = now,
                        MetaModifiedAt = now
                    }
                    : existing.With(
                        versions: versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: request.SetAsDefault ? versionId : existing.DesiredVersionId,
                        loadState: loadState,
                        validation: validation,
                        diagnostics: diagnostics.ToImmutable(),
                        epoch: resourceMetaChanged
                            ? existing.MetaEpoch + 1
                            : existing.MetaEpoch,
                        name: request.Name ?? existing.Name,
                        description: request.Description ?? existing.Description,
                        thingId: thingId ?? existing.ThingId,
                        title: title ?? existing.Title,
                        clearValidation: validation is null);
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
                        snapshot, next, [resource.Xid], projectionOnly: false, cancellationToken)
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
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotResourceGroup nextGroup = group.WithResources(
                    group.Resources.Remove(resourceId), group.Epoch + 1);
                WotRegistrySnapshot next = snapshot.WithGroup(nextGroup, generation);
                await CommitAndPublishAsync(
                        snapshot, next, [resource.Xid], projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, resource, generation, []);
            }
            finally
            {
                m_mutex.Release();
            }
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
                    if (resource.FindVersion(versionId) is null)
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
                        epoch: resource.MetaEpoch + 1)
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
                        labels: version.Labels.SetItem(key, value),
                        epoch: version.Epoch + 1,
                        modifiedAt: DateTime.UtcNow);
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
                        labels: version.Labels.Remove(key),
                        epoch: version.Epoch + 1,
                        modifiedAt: DateTime.UtcNow)
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
                long generation = snapshot.Generation + 1;
                var changed = new List<string>();
                WotRegistrySnapshot next = snapshot;
                foreach (WotResourceProjection projection in projections)
                {
                    WotResource? resource = next.FindResource(
                        projection.GroupId, projection.ResourceId);
                    if (resource is null)
                    {
                        continue;
                    }
                    string? activeVersionId = projection.RetainPreviousActiveVersion
                        ? resource.ActiveVersionId
                        : projection.ActiveVersionId;
                    WotResource updated = resource.With(
                        activeVersionId: activeVersionId,
                        loadState: projection.LoadState,
                        validation: projection.Validation,
                        diagnostics: projection.Diagnostics,
                        epoch: resource.Epoch,
                        refreshGeneration: projection.RefreshGeneration,
                        lastRefreshTime: projection.LastRefreshTime,
                        materializedNodeCount: projection.MaterializedNodeCount,
                        rootNodeId: projection.RootNodeId,
                        clearActiveVersion: activeVersionId is null,
                        clearValidation: projection.Validation is null,
                        clearRootNodeId: projection.RootNodeId.IsNull);
                    WotResourceGroup group = next.FindGroup(projection.GroupId)!;
                    next = ReplaceResource(
                        next,
                        group,
                        updated,
                        generation,
                        bumpGroupEpoch: false);
                    changed.Add(updated.Xid);
                }
                if (changed.Count == 0)
                {
                    return;
                }
                await CommitAndPublishAsync(
                        snapshot, next, changed, projectionOnly: true, cancellationToken)
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
            m_mutex.Dispose();
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
                        projectionOnly: false,
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
            CancellationToken cancellationToken)
        {
            try
            {
                await m_store.CommitAsync(intended, cancellationToken).ConfigureAwait(false);
            }
            catch (WotRegistryCommitDurabilityUncertainException exception)
            {
                Volatile.Write(ref m_snapshot, exception.CommittedSnapshot);
                RaiseChanged(
                    previous,
                    exception.CommittedSnapshot,
                    changed,
                    projectionOnly);
                throw;
            }
            catch (WotRegistryCommitNotCommittedException)
            {
                // The store established that the previous generation remains active.
                throw;
            }
            catch (WotRegistryCommitIndeterminateException)
            {
                m_reloadRequired = true;
                throw;
            }

            Volatile.Write(ref m_snapshot, intended);
            RaiseChanged(previous, intended, changed, projectionOnly);
        }

        private void EnsureMutationAllowed()
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
            bool projectionOnly)
        {
            Changed?.Invoke(
                this,
                new WotRegistryChangedEventArgs(previous, current, changed, projectionOnly));
        }

        private static ImmutableArray<WotResourceVersion> Trim(
            ImmutableArray<WotResourceVersion> versions,
            int max)
        {
            if (versions.Length <= max)
            {
                return versions;
            }
            // Drop the oldest versions beyond the retention bound.
            return versions.RemoveRange(0, versions.Length - max);
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
                        next = value + 1;
                    }
                }
            }
            return WotRegistrySnapshot.FormatVersionId(next);
        }

        private static string DeriveResourceId(
            WotUpsertResourceRequest request,
            string? thingId,
            string? title)
        {
            if (!string.IsNullOrWhiteSpace(request.ResourceId))
            {
                return NormalizeSegment(request.ResourceId!, nameof(request.ResourceId));
            }
            string candidate = thingId ?? request.Name ?? title ?? Guid.NewGuid().ToString("N");
            return Slugify(candidate);
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
            string slug = Slugify(value);
            if (slug.Length == 0)
            {
                throw new ArgumentException(
                    $"'{value}' does not contain any identifier-safe characters.", paramName);
            }
            return slug;
        }

        private static string Slugify(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (c is (>= 'a' and <= 'z') or
                    (>= '0' and <= '9') or
                    '-' or '_' or '.')
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
            bool Created);

        private readonly IWotRegistryStore m_store;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private WotRegistrySnapshot m_snapshot;
        private bool m_reloadRequired;
    }
}
