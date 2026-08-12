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
using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Server.Registry
{
    /// <summary>
    /// A request to create or update a registry resource version.
    /// </summary>
    public sealed class AasUpsertResourceRequest
    {
        /// <summary>
        /// Gets or sets the containing group source identity.
        /// </summary>
        public string GroupSourceIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource source identity.
        /// </summary>
        public string ResourceSourceIdentity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the group kind.
        /// </summary>
        public AasRegistryEntityKind GroupKind { get; set; } = AasRegistryEntityKind.Shell;

        /// <summary>
        /// Gets or sets the resource kind.
        /// </summary>
        public AasRegistryEntityKind ResourceKind { get; set; } = AasRegistryEntityKind.Submodel;

        /// <summary>
        /// Gets or sets the exact document bytes.
        /// </summary>
        public ByteString Content { get; set; }

        /// <summary>
        /// Gets or sets the content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// Gets or sets the registry format.
        /// </summary>
        public string Format { get; set; } = "aas/3.0+json";

        /// <summary>
        /// Gets or sets the display name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the semantic id for a submodel resource.
        /// </summary>
        public string? SemanticId { get; set; }

        /// <summary>
        /// Gets or sets the template identifier for a submodel resource.
        /// </summary>
        public string? Template { get; set; }

        /// <summary>
        /// Gets or sets the carried AAS administration version label.
        /// </summary>
        public string? AdministrationVersion { get; set; }

        /// <summary>
        /// Gets or sets the carried AAS administration revision label.
        /// </summary>
        public string? AdministrationRevision { get; set; }

        /// <summary>
        /// Gets or sets the disclosure tier.
        /// </summary>
        public AASDisclosureTierDataType DisclosureTier { get; set; } = AASDisclosureTierDataType.Public;

        /// <summary>
        /// Gets or sets advertised authorization configuration.
        /// </summary>
        public ArrayOf<AASAuthorizationOptionDataType> Authorization { get; set; } = [];

        /// <summary>
        /// Gets or sets specific asset ids used by LookupShellsByAssetLink for shell groups.
        /// </summary>
        public ArrayOf<AasRegistryAssetLink> SpecificAssetIds { get; set; } = [];

        /// <summary>
        /// Gets or sets whether unauthorized callers receive Bad_NotFound.
        /// </summary>
        public bool ConcealFromUnauthorized { get; set; }

        /// <summary>
        /// Gets or sets whether the version becomes the default.
        /// </summary>
        public bool SetAsDefault { get; set; } = true;

        /// <summary>
        /// Gets or sets the version timestamp. Defaults to now.
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A specific asset id indexed by the registry discovery method.
    /// </summary>
    public sealed class AasRegistryAssetLink
    {
        /// <summary>
        /// Gets or sets the asset key name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the asset key value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// The result of a registry mutation.
    /// </summary>
    public sealed class AasRegistryMutationResult
    {
        internal AasRegistryMutationResult(
            StatusCode statusCode,
            AasRegistryResource? resource,
            long generation,
            string message)
        {
            StatusCode = statusCode;
            Resource = resource;
            Generation = generation;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// Gets the mutation status.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the affected resource.
        /// </summary>
        public AasRegistryResource? Resource { get; }

        /// <summary>
        /// Gets the generation after mutation.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets a diagnostic message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets whether the mutation changed state.
        /// </summary>
        public bool Changed => !StatusCode.IsBad(StatusCode);
    }

    /// <summary>
    /// Registry change event arguments carrying immutable before and after snapshots.
    /// </summary>
    public sealed class AasRegistryChangedEventArgs : EventArgs
    {
        internal AasRegistryChangedEventArgs(AasRegistrySnapshot previous, AasRegistrySnapshot current)
        {
            Previous = previous;
            Current = current;
        }

        /// <summary>
        /// Gets the previous snapshot.
        /// </summary>
        public AasRegistrySnapshot Previous { get; }

        /// <summary>
        /// Gets the current snapshot.
        /// </summary>
        public AasRegistrySnapshot Current { get; }
    }

    /// <summary>
    /// Result returned by GetSubmodel.
    /// </summary>
    public sealed class AasGetSubmodelResult
    {
        internal AasGetSubmodelResult(StatusCode statusCode, ByteString document, string format, string contentType)
        {
            StatusCode = statusCode;
            Document = document;
            Format = format;
            ContentType = contentType;
        }

        /// <summary>
        /// Gets the status code.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the document bytes only on success.
        /// </summary>
        public ByteString Document { get; }

        /// <summary>
        /// Gets the format only on success.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the content type only on success.
        /// </summary>
        public string ContentType { get; }
    }

    /// <summary>
    /// Observes the structural GetSubmodel resolution and authorization path in tests.
    /// </summary>
    public interface IAasRegistryAccessPathObserver
    {
        /// <summary>
        /// Called after resolution and authorization have both completed, just before the final branch.
        /// </summary>
        void OnResolvedAndAuthorized(string submodelIdentifier, bool exists, bool authorized, bool concealed);
    }

    /// <summary>
    /// Evaluates caller-specific target resource access.
    /// </summary>
    public interface IAasRegistryAuthorizationEvaluator
    {
        /// <summary>
        /// Gets whether the caller is authenticated.
        /// </summary>
        bool IsAuthenticated(ISystemContext? context);

        /// <summary>
        /// Gets whether the caller may read the target file.
        /// </summary>
        bool CanReadSubmodel(ISystemContext? context, AasRegistryResource resource);
    }

    /// <summary>
    /// Default disclosure evaluator: public content is anonymous, controlled content requires authentication.
    /// </summary>
    public sealed class DefaultAasRegistryAuthorizationEvaluator : IAasRegistryAuthorizationEvaluator
    {
        /// <inheritdoc/>
        public bool IsAuthenticated(ISystemContext? context)
        {
            if (context is null)
            {
                return false;
            }
            return context is ISessionSystemContext sessionContext && !sessionContext.SessionId.GetValueOrDefault().IsNull;
        }

        /// <inheritdoc/>
        public bool CanReadSubmodel(ISystemContext? context, AasRegistryResource resource)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            return resource.DisclosureTier == AASDisclosureTierDataType.Public || IsAuthenticated(context);
        }
    }

    /// <summary>
    /// The injectable AAS registry service.
    /// </summary>
    public interface IAasRegistryService
    {
        /// <summary>
        /// Gets the current immutable snapshot.
        /// </summary>
        AasRegistrySnapshot Current { get; }

        /// <summary>
        /// Gets persistence bounds.
        /// </summary>
        AasRegistryPersistenceBounds Bounds { get; }

        /// <summary>
        /// Raised after a committed snapshot switch.
        /// </summary>
        event EventHandler<AasRegistryChangedEventArgs>? Changed;

        /// <summary>
        /// Loads persisted state.
        /// </summary>
        ValueTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets or creates a group by its source identity.
        /// </summary>
        ValueTask<AasRegistryGroup> GetOrCreateGroupAsync(
            string sourceIdentity,
            AasRegistryEntityKind kind,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates or updates a resource version.
        /// </summary>
        ValueTask<AasRegistryMutationResult> UpsertResourceAsync(
            AasUpsertResourceRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads content for a stored version.
        /// </summary>
        ValueTask<ByteString> ReadContentAsync(
            AasRegistryResourceVersion version,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a content chunk by digest.
        /// </summary>
        ValueTask<ByteString> ReadContentChunkAsync(
            string digestHex,
            long offset,
            int count,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves shells by a specific asset id name and value.
        /// </summary>
        ArrayOf<string> LookupShellsByAssetLink(string name, string value, ISystemContext? context = null);

        /// <summary>
        /// Gets a submodel document after target authorization.
        /// </summary>
        ValueTask<AasGetSubmodelResult> GetSubmodelAsync(
            string submodelIdentifier,
            ISystemContext? context = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default immutable-snapshot AAS registry service.
    /// </summary>
    public sealed class AasRegistryService : IAasRegistryService, IDisposable
    {
        /// <summary>
        /// Initializes a registry service.
        /// </summary>
        public AasRegistryService(
            IAasRegistryStore? store = null,
            AasRegistryPersistenceBounds? bounds = null,
            IAasRegistryAuthorizationEvaluator? authorizationEvaluator = null,
            IAasRegistryAccessPathObserver? accessPathObserver = null)
        {
            m_store = store ?? new InMemoryAasRegistryStore();
            m_resourceStore = m_store is IAasRegistryResourceStoreProvider provider
                ? provider.ResourceStore
                : new InMemoryResourceStore();
            Bounds = bounds ?? new AasRegistryPersistenceBounds();
            Bounds.Validate();
            m_authorizationEvaluator = authorizationEvaluator ?? new DefaultAasRegistryAuthorizationEvaluator();
            m_accessPathObserver = accessPathObserver;
            m_snapshot = AasRegistrySnapshot.Empty;
        }

        /// <inheritdoc/>
        public AasRegistrySnapshot Current => Volatile.Read(ref m_snapshot);

        /// <inheritdoc/>
        public AasRegistryPersistenceBounds Bounds { get; }

        /// <inheritdoc/>
        public event EventHandler<AasRegistryChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AasRegistrySnapshot loaded = await m_store.LoadAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_snapshot, loaded);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AasRegistryGroup> GetOrCreateGroupAsync(
            string sourceIdentity,
            AasRegistryEntityKind kind,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceIdentity))
            {
                throw new ArgumentException("A source identity is required.", nameof(sourceIdentity));
            }
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AasRegistrySnapshot snapshot = m_snapshot;
                AasRegistryGroup? existing = snapshot.FindGroupBySourceIdentity(sourceIdentity);
                if (existing is not null)
                {
                    return existing;
                }
                if (snapshot.GroupsById.Count >= Bounds.MaxGroups)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                string groupId = CreateIdentifier(sourceIdentity, snapshot.GroupsById.Keys);
                long generation = snapshot.Generation + 1;
                var group = new AasRegistryGroup(groupId, sourceIdentity, kind, epoch: generation);
                await CommitAndPublishAsync(snapshot, snapshot.WithGroup(group, generation), cancellationToken)
                    .ConfigureAwait(false);
                return group;
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<AasRegistryMutationResult> UpsertResourceAsync(
            AasUpsertResourceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.GroupSourceIdentity))
            {
                throw new ArgumentException("A group source identity is required.", nameof(request));
            }
            if (string.IsNullOrWhiteSpace(request.ResourceSourceIdentity))
            {
                throw new ArgumentException("A resource source identity is required.", nameof(request));
            }
            if (request.Content.IsNull)
            {
                throw new ArgumentException("Document content is required.", nameof(request));
            }
            if (request.Content.Length > Bounds.MaxDocumentBytes)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            ByteString digest = AasRegistryContentDigest.Compute(request.Content);
            string digestHex = AasRegistryContentDigest.ToHex(digest);
            await m_resourceStore.WriteAsync(digestHex, 0, request.Content, cancellationToken)
                .ConfigureAwait(false);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AasRegistrySnapshot snapshot = m_snapshot;
                AasRegistryGroup group = FindOrCreateGroup(snapshot, request.GroupSourceIdentity, request.GroupKind);
                if (request.SpecificAssetIds.Count > 0)
                {
                    ImmutableSortedDictionary<string, string> labels = group.Labels;
                    foreach (AasRegistryAssetLink link in request.SpecificAssetIds)
                    {
                        if (!string.IsNullOrEmpty(link.Name))
                        {
                            labels = labels.SetItem($"asset:{link.Name}", link.Value ?? string.Empty);
                        }
                    }
                    group = group.WithLabels(labels, snapshot.Generation + 1);
                }
                AasRegistryResource? existing = group.FindResourceBySourceIdentity(request.ResourceSourceIdentity);
                string resourceId = existing?.ResourceId
                    ?? CreateIdentifier(request.ResourceSourceIdentity, group.Resources.Keys);
                ImmutableArray<AasRegistryResourceVersion> versions = existing?.Versions ?? [];
                if (existing is null && group.Resources.Count >= Bounds.MaxResourcesPerGroup)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                if (existing is not null && versions.Length >= Bounds.MaxVersionsPerResource)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                DateTime now = request.CreatedAt == default
                    ? DateTime.UtcNow
                    : DateTime.SpecifyKind(request.CreatedAt, DateTimeKind.Utc);
                var version = new AasRegistryResourceVersion(
                    AasRegistryVersionId.Create(now, digest),
                    digest,
                    request.Content.Length,
                    request.ContentType,
                    request.Format,
                    now,
                    now,
                    request.AdministrationVersion,
                    request.AdministrationRevision);
                versions = versions.Add(version);
                long generation = snapshot.Generation + 1;
                AasRegistryResource resource = existing is null
                    ? new AasRegistryResource(
                        group.GroupId,
                        resourceId,
                        request.ResourceSourceIdentity,
                        request.ResourceKind,
                        versions,
                        version.VersionId,
                        request.Name,
                        request.Description,
                        request.SemanticId,
                        request.Template,
                        request.DisclosureTier,
                        request.Authorization,
                        request.ConcealFromUnauthorized,
                        generation)
                    : existing.WithVersions(
                        versions,
                        request.SetAsDefault ? version.VersionId : existing.DefaultVersionId,
                        generation);
                group = group.WithResource(resource, generation);
                AasRegistrySnapshot next = snapshot.WithGroup(group, generation);
                await CommitAndPublishAsync(snapshot, next, cancellationToken).ConfigureAwait(false);
                return new AasRegistryMutationResult(StatusCodes.Good, resource, generation, string.Empty);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadContentAsync(
            AasRegistryResourceVersion version,
            CancellationToken cancellationToken = default)
        {
            if (version is null)
            {
                throw new ArgumentNullException(nameof(version));
            }
            return ReadContentChunkAsync(
                version.DigestHex, 0, checked((int)version.ContentLength), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadContentChunkAsync(
            string digestHex,
            long offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            return m_resourceStore.ReadAsync(digestHex, offset, count, cancellationToken);
        }

        /// <inheritdoc/>
        public ArrayOf<string> LookupShellsByAssetLink(string name, string value, ISystemContext? context = null)
        {
            bool authenticated = m_authorizationEvaluator.IsAuthenticated(context);
            var results = new List<string>();
            foreach (AasRegistryGroup group in Current.GroupsById.Values)
            {
                if (group.Kind != AasRegistryEntityKind.Shell)
                {
                    continue;
                }
                if (!authenticated && group.DisclosureTier == AASDisclosureTierDataType.Controlled &&
                    group.ConcealFromUnauthorized)
                {
                    continue;
                }
                if (group.Labels.TryGetValue($"asset:{name}", out string? actual) &&
                    string.Equals(actual, value, StringComparison.Ordinal))
                {
                    results.Add(group.SourceIdentity);
                }
                if (!authenticated && results.Count >= Bounds.MaxUnauthenticatedCollectionResults)
                {
                    break;
                }
            }
            return new ArrayOf<string>(results.ToArray());
        }

        /// <inheritdoc/>
        public async ValueTask<AasGetSubmodelResult> GetSubmodelAsync(
            string submodelIdentifier,
            ISystemContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(submodelIdentifier))
            {
                return Empty(StatusCodes.BadInvalidArgument);
            }
            AasRegistrySnapshot snapshot = Current;
            AasRegistryResource? resource = snapshot.FindSubmodelBySourceIdentity(submodelIdentifier);
            bool exists = resource is not null;
            bool authorized = exists && m_authorizationEvaluator.CanReadSubmodel(context, resource!);
            bool concealed = exists && resource!.ConcealFromUnauthorized && !authorized;
            m_accessPathObserver?.OnResolvedAndAuthorized(submodelIdentifier, exists, authorized, concealed);
            if (!exists || concealed)
            {
                return Empty(StatusCodes.BadNotFound);
            }
            if (!authorized)
            {
                return Empty(StatusCodes.BadUserAccessDenied);
            }
            AasRegistryResourceVersion? version = resource!.DefaultVersion;
            if (version is null)
            {
                return Empty(StatusCodes.BadNotFound);
            }
            ByteString content = await ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
            if (content.IsNull)
            {
                return Empty(StatusCodes.BadNotFound);
            }
            return new AasGetSubmodelResult(StatusCodes.Good, content, version.Format, version.ContentType);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_mutex.Dispose();
        }
        internal static string CreateIdentifier(string sourceIdentity, IEnumerable<string> siblings)
        {
            return XRegistryIdentifier.FromSourceIdentity(sourceIdentity, siblings);
        }
        private static AasGetSubmodelResult Empty(StatusCode statusCode)
        {
            return new AasGetSubmodelResult(statusCode, ByteString.Empty, string.Empty, string.Empty);
        }
        private AasRegistryGroup FindOrCreateGroup(
            AasRegistrySnapshot snapshot,
            string sourceIdentity,
            AasRegistryEntityKind kind)
        {
            AasRegistryGroup? existing = snapshot.FindGroupBySourceIdentity(sourceIdentity);
            if (existing is not null)
            {
                return existing;
            }
            if (snapshot.GroupsById.Count >= Bounds.MaxGroups)
            {
                throw new ServiceResultException(StatusCodes.BadTooManyOperations);
            }
            string groupId = CreateIdentifier(sourceIdentity, snapshot.GroupsById.Keys);
            long generation = snapshot.Generation + 1;
            return new AasRegistryGroup(groupId, sourceIdentity, kind, epoch: generation);
        }
        private async ValueTask CommitAndPublishAsync(
            AasRegistrySnapshot previous,
            AasRegistrySnapshot next,
            CancellationToken cancellationToken)
        {
            await m_store.CommitAsync(next, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref m_snapshot, next);
            Changed?.Invoke(this, new AasRegistryChangedEventArgs(previous, next));
        }
        private readonly IAasRegistryStore m_store;
        private readonly IXRegistryResourceStore m_resourceStore;
        private readonly IAasRegistryAuthorizationEvaluator m_authorizationEvaluator;
        private readonly IAasRegistryAccessPathObserver? m_accessPathObserver;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private AasRegistrySnapshot m_snapshot;
    }
}
