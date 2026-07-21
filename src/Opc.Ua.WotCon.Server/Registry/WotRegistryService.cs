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
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// The default <see cref="IWotRegistryService"/>. Owns the current
    /// immutable <see cref="WotRegistrySnapshot"/>, serialises every mutation on
    /// a single lock, enforces the configured <see cref="WotRegistryPersistenceBounds"/>,
    /// and persists through the injected <see cref="IWotRegistryStore"/>. Every
    /// mutation is made durable by an atomic <see cref="IWotRegistryStore.CommitAsync"/>
    /// <em>before</em> the new snapshot is published to <see cref="Current"/> or
    /// the <see cref="Changed"/> notification is raised, so a persistence failure
    /// leaves <see cref="Current"/> unchanged, raises no event, and a retry
    /// re-attempts the same commit.
    /// </summary>
    public sealed class WotRegistryService : IWotRegistryService, IDisposable
    {
        /// <summary>
        /// Initializes a new registry service over the supplied store.
        /// </summary>
        public WotRegistryService(
            IWotRegistryStore? store = null,
            WotRegistryPersistenceBounds? bounds = null)
        {
            m_store = store ?? new InMemoryWotRegistryStore();
            m_bounds = bounds ?? new WotRegistryPersistenceBounds();
            m_bounds.Validate();
            m_snapshot = WotRegistrySnapshot.Empty;
        }

        /// <inheritdoc/>
        public WotRegistrySnapshot Current => Volatile.Read(ref m_snapshot);

        /// <inheritdoc/>
        public WotRegistryPersistenceBounds Bounds => m_bounds;

        /// <inheritdoc/>
        public event EventHandler<WotRegistryChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                WotRegistrySnapshot loaded = await m_store
                    .LoadAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, loaded);
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
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? existing = snapshot.FindGroup(groupId);
                if (existing is not null)
                {
                    return existing;
                }
                if (snapshot.Groups.Count >= m_bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {m_bounds.MaxGroups} groups.");
                }
                long generation = snapshot.Generation + 1;
                var group = new WotResourceGroup(
                    groupId, kind, name: name, epoch: generation);
                WotRegistrySnapshot next = snapshot.WithGroup(group, generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { group.Xid }, projectionOnly: false);
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
                WotRegistrySnapshot snapshot = m_snapshot;
                if (snapshot.FindGroup(groupId) is not null)
                {
                    return null;
                }
                if (snapshot.Groups.Count >= m_bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {m_bounds.MaxGroups} groups.");
                }
                long generation = snapshot.Generation + 1;
                var group = new WotResourceGroup(groupId, kind, name: name, epoch: generation);
                WotRegistrySnapshot next = snapshot.WithGroup(group, generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { group.Xid }, projectionOnly: false);
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
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, changed, projectionOnly: false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, ImmutableArray<string>.Empty);
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
        public async ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            WotResource? resource = m_snapshot.FindResource(groupId, resourceId);
            if (resource is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "Resource not found.");
            }
            WotResourceVersion? version = resource.DefaultVersion;
            if (version is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The resource has no default version to validate.");
            }
            WoTValidationOutcomeDataType outcome = ValidateContent(version.Content);

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
                if (snapshot.Groups.Count >= m_bounds.MaxGroups)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of {m_bounds.MaxGroups} groups.");
                }
                group = new WotResourceGroup(groupId, kind, epoch: snapshot.Generation + 1);
            }
            if (group.Resources.Count >= m_bounds.MaxResourcesPerGroup)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations,
                    $"Group '{groupId}' already holds the maximum of " +
                    $"{m_bounds.MaxResourcesPerGroup} resources.");
            }
            long generation = snapshot.Generation + 1;
            var resource = new WotResource(
                groupId,
                resourceId,
                kind,
                ImmutableArray<WotResourceVersion>.Empty,
                enabled: true,
                loadState: WoTLoadStateEnum.Unloaded,
                epoch: generation,
                name: resourceId);
            WotRegistrySnapshot next = ReplaceResource(snapshot, group, resource, generation);
            await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref m_snapshot, next);
            RaiseChanged(snapshot, next, new[] { resource.Xid }, projectionOnly: false);
            return resource;
        }

        private static WoTValidationOutcomeDataType ValidateContent(ReadOnlyMemory<byte> content)
        {
            try
            {
                using WotDocument document = WotDocument.Parse(content);
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
            if (request.Content.Length == 0)
            {
                return Failed(m_snapshot.Generation, "The document is empty.");
            }
            if (request.Content.Length > m_bounds.MaxDocumentBytes)
            {
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Rejected,
                    null,
                    m_snapshot.Generation,
                    ImmutableArray.Create(
                        $"The document exceeds the maximum size of {m_bounds.MaxDocumentBytes} bytes."),
                    "Document too large.");
            }

            string groupId = string.IsNullOrWhiteSpace(request.GroupId)
                ? DefaultGroupFor(request.Kind)
                : NormalizeSegment(request.GroupId!, nameof(request.GroupId));

            // Copy the caller's buffer: the immutable snapshot owns the bytes.
            byte[] content = request.Content.ToArray();

            // Light parse to derive the kind/id/title and to record a format
            // failure state for a document that cannot even be parsed. Full WoT
            // validation and projection are performed by the coordinator.
            string? thingId = null;
            string? title = null;
            WoTValidationOutcomeDataType? validation = null;
            var diagnostics = ImmutableArray.CreateBuilder<string>();
            bool parseFailed = false;
            try
            {
                var options = new WotNodeSetConverterOptions
                {
                    MaxJsonDocumentSize = m_bounds.MaxDocumentBytes,
                    MaxJsonDepth = m_bounds.MaxJsonDepth
                };
                using WotDocument document = WotDocument.Parse(content, options);
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
                WotRegistrySnapshot snapshot = m_snapshot;
                WotResourceGroup? group = snapshot.FindGroup(groupId);
                if (group is null)
                {
                    // Implicit group creation on upsert must enforce MaxGroups the
                    // same way this method already enforces MaxResourcesPerGroup:
                    // reject the request rather than silently exceeding the bound.
                    if (snapshot.Groups.Count >= m_bounds.MaxGroups)
                    {
                        return new WotRegistryMutationResult(
                            WoTOutcomeEnum.Rejected,
                            null,
                            snapshot.Generation,
                            ImmutableArray.Create(
                                $"The registry already holds the maximum of {m_bounds.MaxGroups} groups."),
                            "Too many groups.");
                    }
                    group = new WotResourceGroup(groupId, request.Kind, epoch: snapshot.Generation + 1);
                }

                string resourceId = DeriveResourceId(request, thingId, title);
                WotResource? existing = group.Resources.TryGetValue(
                    resourceId, out WotResource? found) ? found : null;

                if (existing is null &&
                    group.Resources.Count >= m_bounds.MaxResourcesPerGroup)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Rejected,
                        null,
                        snapshot.Generation,
                        ImmutableArray.Create(
                            $"Group '{groupId}' already holds the maximum of " +
                            $"{m_bounds.MaxResourcesPerGroup} resources."),
                        "Too many resources.");
                }

                byte[] digest = WotContentDigest.Compute(content);

                // Idempotency: an unchanged default document returns Unchanged
                // and produces no new version and no model change.
                if (existing?.DefaultVersion is { } current &&
                    WotContentDigest.Equal(current.Digest, digest) &&
                    !parseFailed)
                {
                    return new WotRegistryMutationResult(
                        WoTOutcomeEnum.Unchanged,
                        existing,
                        snapshot.Generation,
                        ImmutableArray<string>.Empty,
                        "Content digest unchanged.");
                }

                long generation = snapshot.Generation + 1;
                DateTime now = DateTime.UtcNow;
                string versionId = NextVersionId(existing);
                var version = new WotResourceVersion(
                    versionId,
                    content,
                    request.ContentType,
                    request.Format,
                    createdAt: now,
                    modifiedAt: now,
                    digest: digest);

                ImmutableArray<WotResourceVersion> versions = existing is null
                    ? ImmutableArray.Create(version)
                    : Trim(existing.Versions.Add(version), m_bounds.MaxVersionsPerResource);

                string? defaultVersionId = request.SetAsDefault
                    ? versionId
                    : existing?.DefaultVersionId ?? versionId;

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
                        epoch: generation,
                        name: request.Name ?? title ?? resourceId,
                        description: request.Description,
                        thingId: thingId,
                        title: title)
                    : existing.With(
                        versions: versions,
                        defaultVersionId: defaultVersionId,
                        desiredVersionId: request.SetAsDefault ? versionId : existing.DesiredVersionId,
                        loadState: loadState,
                        validation: validation,
                        clearValidation: validation is null,
                        diagnostics: diagnostics.ToImmutable(),
                        epoch: generation,
                        name: request.Name ?? existing.Name,
                        description: request.Description ?? existing.Description,
                        thingId: thingId ?? existing.ThingId,
                        title: title ?? existing.Title);

                WotRegistrySnapshot next = ReplaceResource(snapshot, group, resource, generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { resource.Xid }, projectionOnly: false);

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
                    group.Resources.Remove(resourceId), generation);
                WotRegistrySnapshot next = snapshot.WithGroup(nextGroup, generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { resource.Xid }, projectionOnly: false);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, resource, generation, ImmutableArray<string>.Empty);
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
                    WotResource updated = resource.With(
                        defaultVersionId: versionId,
                        desiredVersionId: versionId,
                        epoch: generation);
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
                        return (resource.With(epoch: generation), null);
                    }
                    WotResource updated = resource.With(enabled: enabled, epoch: generation);
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
            WotLabelValidator.Validate(key, value, m_bounds);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                WotRegistrySnapshot snapshot = m_snapshot;
                if (expectedEpoch is { } epoch && epoch != snapshot.Generation)
                {
                    return Rejected(snapshot.Generation, "Epoch mismatch.");
                }
                if (!snapshot.Labels.ContainsKey(key) &&
                    snapshot.Labels.Count >= m_bounds.MaxLabelsPerEntity)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations,
                        $"The registry already holds the maximum of " +
                        $"{m_bounds.MaxLabelsPerEntity} labels.");
                }
                long generation = snapshot.Generation + 1;
                WotRegistrySnapshot next = snapshot.WithLabels(
                    snapshot.Labels.SetItem(key, value), generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, Array.Empty<string>(), projectionOnly: true);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, ImmutableArray<string>.Empty);
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
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, Array.Empty<string>(), projectionOnly: true);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, ImmutableArray<string>.Empty);
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
            WotLabelValidator.Validate(key, value, m_bounds);
            return MutateGroupAsync(
                groupId,
                expectedEpoch,
                (group, generation) =>
                {
                    if (!group.Labels.ContainsKey(key) &&
                        group.Labels.Count >= m_bounds.MaxLabelsPerEntity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"Group '{groupId}' already holds the maximum of " +
                            $"{m_bounds.MaxLabelsPerEntity} labels.");
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
            WotLabelValidator.Validate(key, value, m_bounds);
            return MutateResourceAsync(
                groupId,
                resourceId,
                expectedEpoch,
                (resource, generation) =>
                {
                    if (!resource.Labels.ContainsKey(key) &&
                        resource.Labels.Count >= m_bounds.MaxLabelsPerEntity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyOperations,
                            $"Resource '{resourceId}' already holds the maximum of " +
                            $"{m_bounds.MaxLabelsPerEntity} labels.");
                    }
                    WotResource updated = resource.With(
                        labels: resource.Labels.SetItem(key, value), epoch: generation);
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
                    WotResource updated = resource.With(
                        labels: resource.Labels.Remove(key), epoch: generation);
                    return (updated, null);
                },
                cancellationToken,
                projectionOnly: true);
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
                        clearActiveVersion: activeVersionId is null,
                        loadState: projection.LoadState,
                        refreshGeneration: projection.RefreshGeneration,
                        materializedNodeCount: projection.MaterializedNodeCount,
                        rootNodeId: projection.RootNodeId,
                        clearRootNodeId: projection.RootNodeId is null,
                        validation: projection.Validation,
                        clearValidation: projection.Validation is null,
                        diagnostics: projection.Diagnostics,
                        lastRefreshTime: projection.LastRefreshTime,
                        epoch: resource.Epoch);
                    WotResourceGroup group = next.FindGroup(projection.GroupId)!;
                    next = ReplaceResource(next, group, updated, generation);
                    changed.Add(updated.Xid);
                }
                if (changed.Count == 0)
                {
                    return;
                }
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, changed, projectionOnly: true);
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
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotRegistrySnapshot next = ReplaceResource(snapshot, group, updated, generation);
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { updated.Xid }, projectionOnly);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, updated, generation, ImmutableArray<string>.Empty);
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
                await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, next);
                RaiseChanged(snapshot, next, new[] { updated.Xid }, projectionOnly);
                return new WotRegistryMutationResult(
                    WoTOutcomeEnum.Success, null, generation, ImmutableArray<string>.Empty);
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
            long generation)
        {
            WotResourceGroup nextGroup = group.WithResources(
                group.Resources.SetItem(resource.ResourceId, resource), generation);
            return snapshot.WithGroup(nextGroup, generation);
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
            => kind == WoTDocumentKindEnum.ThingModel
                ? WotRegistryGroups.ThingModels
                : WotRegistryGroups.ThingDescriptions;

        private static string NormalizeSegment(string value, string paramName)
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
                if ((c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.')
                {
                    builder.Append(c);
                }
                else if (c >= 'A' && c <= 'Z')
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
            => new WotRegistryMutationResult(
                WoTOutcomeEnum.Failed, null, generation,
                ImmutableArray.Create(message), message);

        private static WotRegistryMutationResult Rejected(long generation, string message)
            => new WotRegistryMutationResult(
                WoTOutcomeEnum.Rejected, null, generation,
                ImmutableArray.Create(message), message);

        private readonly IWotRegistryStore m_store;
        private readonly WotRegistryPersistenceBounds m_bounds;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private WotRegistrySnapshot m_snapshot;
    }
}
