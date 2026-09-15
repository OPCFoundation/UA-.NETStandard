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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService
    {
        /// <inheritdoc/>
        public ValueTask<WotDocumentGroupResult> CreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default)
        {
            return ProvisionDocumentGroupAsync(kind, catalogUri, false, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotDocumentGroupResult> GetOrCreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default)
        {
            return ProvisionDocumentGroupAsync(kind, catalogUri, true, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotDocumentResourceResult> CreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default)
        {
            return ProvisionDocumentResourceAsync(
                groupId, kind, sourceId, versionId, false, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<WotDocumentResourceResult> GetOrCreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default)
        {
            return ProvisionDocumentResourceAsync(
                groupId, kind, sourceId, versionId, true, null, cancellationToken);
        }

        internal async ValueTask<WotDocumentGroupResult> ProvisionConfiguredGroupAsync(
            string suppliedId,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotResourceGroup? existing = m_snapshot.FindGroup(suppliedId);
                WotRegistryGroupIdentity? binding = m_groupIdentities.FirstOrDefault(
                    identity => string.Equals(identity.GroupId, suppliedId, StringComparison.Ordinal));
                if (existing?.CatalogUri is { } catalogUri)
                {
                    if (binding is not null &&
                        (binding.Kind != existing.Kind ||
                            !string.Equals(binding.CatalogUri, catalogUri, StringComparison.Ordinal)))
                    {
                        throw InvalidAuthority("The configured group binding contradicts its persisted authority.");
                    }
                    return await ProvisionGroupLockedAsync(
                        existing.Kind, catalogUri, getOrCreate, cancellationToken).ConfigureAwait(false);
                }
                if (binding is null)
                {
                    throw InvalidAuthority("An unprovisioned generic group requires one explicit authority binding.");
                }
                return await ProvisionGroupLockedAsync(
                    binding.Kind, binding.CatalogUri, getOrCreate, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        internal async ValueTask<WotDocumentResourceResult> ProvisionConfiguredResourceAsync(
            string groupId,
            string suppliedId,
            string versionId,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                WotResourceGroup group = RequireAuthoritativeGroup(groupId);
                group.Resources.TryGetValue(suppliedId, out WotResource? existing);
                string? sourceId = existing?.SourceId;
                foreach (WotRegistryResourceIdentity binding in ResourceBindingsFor(group))
                {
                    if (!string.Equals(binding.ResourceId, suppliedId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (sourceId is not null &&
                        !string.Equals(sourceId, binding.SourceId, StringComparison.Ordinal))
                    {
                        throw InvalidAuthority("The resource binding contradicts its persisted source identity.");
                    }
                    sourceId = binding.SourceId;
                }
                if (sourceId is null)
                {
                    throw InvalidAuthority("An unprovisioned generic resource requires one explicit source binding.");
                }
                return await ProvisionResourceLockedAsync(
                    group, group.Kind, sourceId, versionId, getOrCreate,
                    useTypedSemantics: false, beforeCommit: null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        internal async ValueTask<WotDocumentResourceResult> ProvisionDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId,
            bool getOrCreate,
            Func<WotResource, WotResourceVersion, IWotRegistryVersionLease, CancellationToken, ValueTask>? beforeCommit,
            CancellationToken cancellationToken)
        {
            EnsureDocumentKind(kind);
            WotRegistryIdentity.RequireUri(sourceId);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                return await ProvisionResourceLockedAsync(
                    RequireAuthoritativeGroup(groupId), kind, sourceId, versionId, getOrCreate,
                    useTypedSemantics: true, beforeCommit, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotDocumentGroupResult> ProvisionDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            EnsureDocumentKind(kind);
            WotRegistryIdentity.RequireUri(catalogUri);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureMutationAllowed();
                return await ProvisionGroupLockedAsync(kind, catalogUri, getOrCreate, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private async ValueTask<WotDocumentGroupResult> ProvisionGroupLockedAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            bool getOrCreate,
            CancellationToken cancellationToken)
        {
            WotRegistrySnapshot snapshot = m_snapshot;
            WotResourceGroup? mapped = snapshot.Groups.Values.SingleOrDefault(
                group => group.Kind == kind &&
                    string.Equals(group.CatalogUri, catalogUri, StringComparison.Ordinal));
            WotResourceGroup? established = mapped;
            foreach (WotRegistryGroupIdentity binding in m_groupIdentities)
            {
                if (binding.Kind != kind ||
                    !string.Equals(binding.CatalogUri, catalogUri, StringComparison.Ordinal))
                {
                    continue;
                }
                WotResourceGroup? candidate = snapshot.FindGroup(binding.GroupId);
                if (candidate is null)
                {
                    continue;
                }
                EnsureGroupKind(candidate, kind);
                if ((candidate.CatalogUri is not null &&
                    !string.Equals(candidate.CatalogUri, catalogUri, StringComparison.Ordinal)) ||
                    (established is not null && !ReferenceEquals(candidate, established)))
                {
                    throw InvalidAuthority("The configured catalogue has contradictory existing allocations.");
                }
                established = candidate;
            }
            if (established is not null && !getOrCreate)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdExists, "The catalogue already exists.");
            }
            if (mapped is not null)
            {
                return new WotDocumentGroupResult(mapped, false);
            }
            bool created = established is null;
            if (created && snapshot.Groups.Count >= Bounds.MaxGroups)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The group limit is reached.");
            }
            string assignedId = established?.GroupId ??
                AllocateIdentity(
                    catalogUri, snapshot.Groups.Keys, kind == WoTDocumentKindEnum.ThingDescription ? "td." : "tm.");
            var group = new WotResourceGroup(
                assignedId, kind, established?.Resources, description: established?.Description,
                epoch: established is null ? 1 : established.Epoch + 1,
                labels: established?.Labels, catalogUri: catalogUri);
            WotRegistrySnapshot next = snapshot.WithGroup(group, snapshot.Generation + 1);
            WotRegistryIdentity.ValidateSnapshot(next);
            await CommitAndPublishAsync(snapshot, next, [group.Xid], projectionOnly: true, cancellationToken)
                .ConfigureAwait(false);
            return new WotDocumentGroupResult(group, created);
        }

        private async ValueTask<WotDocumentResourceResult> ProvisionResourceLockedAsync(
            WotResourceGroup group,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId,
            bool getOrCreate,
            bool useTypedSemantics,
            Func<WotResource, WotResourceVersion, IWotRegistryVersionLease, CancellationToken, ValueTask>? beforeCommit,
            CancellationToken cancellationToken)
        {
            EnsureGroupKind(group, kind);
            WotRegistryIdentity.RequireUri(sourceId);
            if (versionId is null)
            {
                throw InvalidAuthority("VersionId must be a String; empty requests server selection.");
            }
            WotResource? existing = group.Resources.Values.SingleOrDefault(
                resource => string.Equals(resource.SourceId, sourceId, StringComparison.Ordinal));
            foreach (WotRegistryResourceIdentity binding in ResourceBindingsFor(group))
            {
                if (!string.Equals(binding.SourceId, sourceId, StringComparison.Ordinal) ||
                    !group.Resources.TryGetValue(binding.ResourceId, out WotResource? configured))
                {
                    continue;
                }
                if (!string.Equals(configured.SourceId, sourceId, StringComparison.Ordinal) ||
                    (existing is not null && !ReferenceEquals(configured, existing)))
                {
                    throw InvalidAuthority("The configured source has contradictory existing allocations.");
                }
                existing = configured;
            }
            string resourceId = existing?.ResourceId ?? AllocateIdentity(sourceId, group.Resources.Keys, string.Empty);
            VersionCreateResult result = await CreateVersionLockedAsync(
                group.GroupId, resourceId, versionId, kind, getOrCreate, useTypedSemantics,
                sourceId, beforeCommit, cancellationToken).ConfigureAwait(false);
            if (result.Version is null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdExists, "The exact Version already exists.");
            }
            return new WotDocumentResourceResult(
                result.Resource!, result.Version, result.CreatedResource, result.Created);
        }

        private WotResourceGroup RequireAuthoritativeGroup(string groupId)
        {
            WotResourceGroup? group = m_snapshot.FindGroup(groupId);
            if (group is null || !WotRegistryIdentity.IsAbsoluteUri(group.CatalogUri))
            {
                throw InvalidAuthority("The owning group must have an established exact catalogue authority.");
            }
            return group;
        }

        private IEnumerable<WotRegistryResourceIdentity> ResourceBindingsFor(WotResourceGroup group)
        {
            foreach (WotRegistryResourceIdentity binding in m_resourceIdentities)
            {
                if (string.Equals(binding.GroupId, group.GroupId, StringComparison.Ordinal) ||
                    m_groupIdentities.Any(alias =>
                        string.Equals(alias.GroupId, binding.GroupId, StringComparison.Ordinal) &&
                        alias.Kind == group.Kind &&
                        string.Equals(alias.CatalogUri, group.CatalogUri, StringComparison.Ordinal)))
                {
                    yield return binding;
                }
            }
        }

        private static string AllocateIdentity(string sourceId, IEnumerable<string> occupied, string prefix)
        {
            try
            {
                return XRegistryIdentifier.FromSourceIdentity(sourceId, occupied.ToArrayOf(), prefix);
            }
            catch (InvalidOperationException ex)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange, ex.Message, ex);
            }
        }

        private static void EnsureGroupKind(WotResourceGroup? group, WoTDocumentKindEnum kind)
        {
            if (group is not null && group.Kind != kind)
            {
                throw InvalidAuthority("The document kind contradicts the owning group's established kind.");
            }
        }

        private static void EnsureResourceKind(WotResource? resource, WoTDocumentKindEnum kind)
        {
            if (resource is not null && resource.Kind != kind)
            {
                throw InvalidAuthority("The document kind contradicts the Resource's established kind.");
            }
        }

        private static ServiceResultException InvalidAuthority(string message)
        {
            return new ServiceResultException(StatusCodes.BadInvalidArgument, message);
        }

        private static WotRegistryMutationResult RejectedAuthority(long generation, string message)
        {
            return new WotRegistryMutationResult(WoTOutcomeEnum.Rejected, null, generation, [message], message)
            {
                StatusCode = StatusCodes.BadInvalidArgument
            };
        }

        private static (WotRegistryGroupIdentity[], WotRegistryResourceIdentity[])
            ValidateIdentityBindings(WotRegistryIdentityBindings bindings)
        {
            if (bindings is null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WotRegistryGroupIdentity group in bindings.Groups)
            {
                if (group is null ||
                    !WotRegistryIdentity.IsIdentifier(group.GroupId) ||
                    !groups.Add(group.GroupId))
                {
                    throw InvalidAuthority(
                        "Generic group bindings contain a missing, invalid or duplicate identifier.");
                }
                EnsureDocumentKind(group.Kind);
                WotRegistryIdentity.RequireUri(group.CatalogUri);
            }
            var resources = new HashSet<(string, string)>();
            foreach (WotRegistryResourceIdentity resource in bindings.Resources)
            {
                if (resource is null ||
                    !WotRegistryIdentity.IsIdentifier(resource.GroupId) ||
                    !WotRegistryIdentity.IsIdentifier(resource.ResourceId) ||
                    !resources.Add((resource.GroupId, resource.ResourceId)))
                {
                    throw InvalidAuthority("Generic resource bindings contain a missing, invalid or duplicate key.");
                }
                WotRegistryIdentity.RequireUri(resource.SourceId);
            }
            return (bindings.Groups.ToArray() ?? [], bindings.Resources.ToArray() ?? []);
        }

        private readonly WotRegistryGroupIdentity[] m_groupIdentities;
        private readonly WotRegistryResourceIdentity[] m_resourceIdentities;
    }
}
