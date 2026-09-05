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
                group = new WotResourceGroup(groupId, kind, epoch: snapshot.Generation + 1);
            }
            if (group.Resources.Count >= Bounds.MaxResourcesPerGroup)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations,
                    $"Group '{groupId}' already holds the maximum of " +
                    $"{Bounds.MaxResourcesPerGroup} resources.");
            }
            long generation = snapshot.Generation + 1;
            var resource = new WotResource(
                groupId,
                resourceId,
                kind,
                [],
                enabled: true,
                loadState: WoTLoadStateEnum.Unloaded,
                epoch: generation,
                name: resourceId);
            WotRegistrySnapshot next = ReplaceResource(snapshot, group, resource, generation);
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
                    group = new WotResourceGroup(groupId, request.Kind, epoch: snapshot.Generation + 1);
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
                        [],
                        "Content digest unchanged.");
                }

                string digestHex = WotContentDigest.ToHex(digest);
                await m_resourceStore
                        .WriteAsync(digestHex, 0, content, cancellationToken)
                        .ConfigureAwait(false);

                long generation = snapshot.Generation + 1;
                DateTime now = DateTime.UtcNow;
                string versionId = NextVersionId(existing);
                var version = new WotResourceVersion(
                        versionId,
                        digest,
                        content.Length,
                        request.ContentType,
                        request.Format,
                        createdAt: now,
                        modifiedAt: now);

                ImmutableArray<WotResourceVersion> versions = existing is null
                    ? [version]
                    : Trim(existing.Versions.Add(version), Bounds.MaxVersionsPerResource);

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
                        diagnostics: diagnostics.ToImmutable(),
                        epoch: generation,
                        name: request.Name ?? existing.Name,
                        description: request.Description ?? existing.Description,
                        thingId: thingId ?? existing.ThingId,
                        title: title ?? existing.Title,
                        clearValidation: validation is null);

                WotRegistrySnapshot next = ReplaceResource(snapshot, group, resource, generation);
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
                    group.Resources.Remove(resourceId), generation);
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
        public async ValueTask<WotDeleteResult> DeleteResourceAsync(
            string groupId,
            string resourceId,
            WoTDeletePolicyEnum policy,
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
                    return DeleteRefused(
                        WoTOutcomeEnum.Failed, policy, snapshot.Generation, "Resource not found.");
                }
                if (expectedEpoch is { } epoch && epoch != resource.Epoch)
                {
                    return DeleteRefused(
                        WoTOutcomeEnum.Rejected, policy, snapshot.Generation, "Epoch mismatch.");
                }

                WotDependentSet found =
                    await WotDependencyGraph.FindDependentsWithFaultsAsync(
                        snapshot,
                        resource,
                        Bounds.MaxJsonDepth,
                        ReadContentAsync,
                        cancellationToken).ConfigureAwait(false);
                ImmutableArray<WotDependent> dependents = found.Dependents;

                // The target's own blob being unreadable says nothing about
                // whether anything depends on it, and it is being removed
                // anyway. Every other unreadable document might be a dependent,
                // and no policy may treat "not checked" as "checked and clear".
                ImmutableArray<string> unknown = Except(found.Unreadable, resource.Xid);
                ImmutableArray<string>.Builder xids = ImmutableArray.CreateBuilder<string>();
                foreach (WotDependent dependent in dependents)
                {
                    xids.Add(dependent.Xid);
                }
                ImmutableArray<string> dependentXids = xids.ToImmutable();

                if (policy == WoTDeletePolicyEnum.Reject &&
                    (dependents.Length != 0 || unknown.Length != 0))
                {
                    // Nothing is written: a rejected delete has to leave every
                    // piece of state exactly as the caller found it, or the
                    // difference between Reject and Force is only a message.
                    return new WotDeleteResult(
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
                                "Reject.");
                }

                long generation = snapshot.Generation + 1;
                var changed = new List<string> { resource.Xid };
                WotRegistrySnapshot next = snapshot;
                bool deleted;
                bool retired;
                ImmutableArray<string> unloaded = [];
                ImmutableArray<string> failed = [];
                if (policy == WoTDeletePolicyEnum.Retire)
                {
                    // The document stays stored and therefore stays
                    // resolvable; only its projection comes down. Nothing that
                    // might depend on it loses a reference, so an unreadable
                    // document is not this policy's problem.
                    next = WithResource(
                        next,
                        resource.With(
                            enabled: false,
                            loadState: WoTLoadStateEnum.Retired,
                            epoch: resource.Epoch + 1,
                            clearActiveVersion: true,
                            clearRootNodeId: true,
                            materializedNodeCount: 0),
                        generation);
                    deleted = false;
                    retired = true;
                }
                else
                {
                    next = WithoutResource(next, resource, generation);
                    deleted = true;
                    retired = true;
                    (next, unloaded, failed) = ApplyPolicyToDependents(
                        next, policy, dependents, unknown, generation, changed);
                }

                await CommitAndPublishAsync(
                        snapshot, next, changed, projectionOnly: false, cancellationToken)
                    .ConfigureAwait(false);
                return new WotDeleteResult(
                    WoTOutcomeEnum.Success,
                    policy,
                    generation,
                    deleted,
                    retired,
                    dependentXids,
                    unloaded,
                    failed,
                    unknown,
                    DescribeDelete(policy, resource.Xid, unloaded, failed, unknown));
            }
            finally
            {
                m_mutex.Release();
            }
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
                snapshot = WithResource(
                    snapshot,
                    dependent.Resource.With(
                        enabled: false,
                        loadState: state,
                        epoch: dependent.Resource.Epoch + 1,
                        clearActiveVersion: true,
                        clearRootNodeId: true,
                        materializedNodeCount: 0,
                        diagnostics: [
                            state == WoTLoadStateEnum.Failed
                                ? "A document this projection resolves through was force-deleted."
                                : "The only document this projection resolved through was deleted."
                        ]),
                    generation);
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
                snapshot = WithResource(
                    snapshot,
                    unreadable.With(
                        enabled: false,
                        loadState: WoTLoadStateEnum.Failed,
                        epoch: unreadable.Epoch + 1,
                        clearActiveVersion: true,
                        clearRootNodeId: true,
                        materializedNodeCount: 0,
                        diagnostics: [
                            "This document could not be read, so whether it resolved " +
                            "through the force-deleted document is unknown."
                        ]),
                    generation);
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
                : " " + unknown.Length.ToString(CultureInfo.InvariantCulture) +
                    " document(s) could not be read, so whether they depended on it is " +
                    "unknown.";
            return policy switch
            {
                WoTDeletePolicyEnum.Retire =>
                    $"'{xid}' was retired; the document remains stored and resolvable.",
                WoTDeletePolicyEnum.Cascade =>
                    $"'{xid}' was deleted and " +
                    unloaded.Length.ToString(CultureInfo.InvariantCulture) +
                    " dependent projection(s) were unloaded." + unreadable,
                WoTDeletePolicyEnum.Force =>
                    $"'{xid}' was force-deleted; " +
                    failed.Length.ToString(CultureInfo.InvariantCulture) +
                    " remaining dependent(s) were marked Failed." + unreadable,
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

        private static WotRegistrySnapshot WithResource(
            WotRegistrySnapshot snapshot, WotResource resource, long generation)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            return snapshot.WithGroup(
                group.WithResources(
                    group.Resources.SetItem(resource.ResourceId, resource), generation),
                generation);
        }

        private static WotRegistrySnapshot WithoutResource(
            WotRegistrySnapshot snapshot, WotResource resource, long generation)
        {
            WotResourceGroup group = snapshot.FindGroup(resource.GroupId)!;
            return snapshot.WithGroup(
                group.WithResources(
                    group.Resources.Remove(resource.ResourceId), generation),
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
                    WotResource updated = resource.With(
epoch: generation, labels: resource.Labels.SetItem(key, value));
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
epoch: generation, labels: resource.Labels.Remove(key));
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
                    next = ReplaceResource(next, group, updated, generation);
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
                WotResourceGroup group = snapshot.FindGroup(groupId)!;
                WotRegistrySnapshot next = ReplaceResource(snapshot, group, updated, generation);
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
            long generation)
        {
            WotResourceGroup nextGroup = group.WithResources(
                group.Resources.SetItem(resource.ResourceId, resource), generation);
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

        private readonly IWotRegistryStore m_store;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private WotRegistrySnapshot m_snapshot;
        private bool m_reloadRequired;
    }
}
