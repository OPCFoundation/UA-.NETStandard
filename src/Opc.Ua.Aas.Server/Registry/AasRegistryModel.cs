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
using System.Globalization;
using System.Linq;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.Aas.Server.Registry
{
    /// <summary>
    /// The AAS registry entity kind used to select the generated group and resource types.
    /// </summary>
    public enum AasRegistryEntityKind
    {
        /// <summary>
        /// A shell group whose source identity is AasIdentifier.
        /// </summary>
        Shell,

        /// <summary>
        /// A submodel document whose source identity is SubmodelIdentifier.
        /// </summary>
        Submodel,

        /// <summary>
        /// A submodel template group whose source identity is TemplateNamespace.
        /// </summary>
        SubmodelTemplate,

        /// <summary>
        /// A concept dictionary group whose source identity is DictionaryIdentifier.
        /// </summary>
        ConceptDictionary,

        /// <summary>
        /// A concept description document whose source identity is ConceptIdentifier.
        /// </summary>
        ConceptDescription,

        /// <summary>
        /// A package store group whose source identity is StoreIdentifier.
        /// </summary>
        PackageStore,

        /// <summary>
        /// A package document whose source identity is PackageIdentifier.
        /// </summary>
        Package,

        /// <summary>
        /// An environment document placeholder.
        /// </summary>
        Environment
    }

    /// <summary>
    /// Computes the content digest used for version byte identity.
    /// </summary>
    public static class AasRegistryContentDigest
    {
        /// <summary>
        /// Computes the SHA-256 digest of content bytes.
        /// </summary>
        public static ByteString Compute(ReadOnlySpan<byte> content)
        {
            return AasDigest.Compute(content, AasDigest.Sha256Name);
        }

        /// <summary>
        /// Computes the SHA-256 digest of content bytes.
        /// </summary>
        public static ByteString Compute(ByteString content)
        {
            return Compute(content.IsNull ? default : content.Span);
        }

        /// <summary>
        /// Formats a digest as lowercase hexadecimal.
        /// </summary>
        public static string ToHex(ByteString digest)
        {
            return AasDigest.ToHex(digest);
        }

        /// <summary>
        /// Formats bytes as lowercase hexadecimal.
        /// </summary>
        public static string ToHex(ReadOnlySpan<byte> bytes)
        {
            return AasDigest.ToHex(bytes);
        }
    }

    /// <summary>
    /// Helpers for deterministic xRegistry label dictionaries.
    /// </summary>
    public static class AasRegistryLabels
    {
        /// <summary>
        /// Gets an empty ordinal label dictionary.
        /// </summary>
        public static ImmutableSortedDictionary<string, string> Empty { get; } =
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// A single immutable resource version.
    /// </summary>
    public sealed class AasRegistryResourceVersion
    {
        /// <summary>
        /// Initializes a resource version.
        /// </summary>
        public AasRegistryResourceVersion(
            string versionId,
            ByteString digest,
            long contentLength,
            string contentType,
            string format,
            DateTime createdAt,
            DateTime modifiedAt,
            string? administrationVersion = null,
            string? administrationRevision = null)
        {
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
            Digest = digest.IsNull ? throw new ArgumentException("A digest is required.", nameof(digest)) : digest;
            ContentLength = contentLength >= 0
                ? contentLength
                : throw new ArgumentOutOfRangeException(nameof(contentLength));
            ContentType = contentType ?? string.Empty;
            Format = format ?? string.Empty;
            CreatedAt = createdAt;
            ModifiedAt = modifiedAt;
            AdministrationVersion = administrationVersion ?? string.Empty;
            AdministrationRevision = administrationRevision ?? string.Empty;
        }

        /// <summary>
        /// Gets the registry version identifier.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// Gets the SHA-256 digest over the exact document bytes.
        /// </summary>
        public ByteString Digest { get; }

        /// <summary>
        /// Gets the stored document length.
        /// </summary>
        public long ContentLength { get; }

        /// <summary>
        /// Gets the content type.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Gets the format tag.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the UTC creation time.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Gets the UTC modification time.
        /// </summary>
        public DateTime ModifiedAt { get; }

        /// <summary>
        /// Gets the AAS administration version label carried unchanged from the document.
        /// </summary>
        public string AdministrationVersion { get; }

        /// <summary>
        /// Gets the AAS administration revision label carried unchanged from the document.
        /// </summary>
        public string AdministrationRevision { get; }

        /// <summary>
        /// Gets the content digest as lowercase hexadecimal.
        /// </summary>
        public string DigestHex => AasRegistryContentDigest.ToHex(Digest);
    }

    /// <summary>
    /// Immutable AAS registry resource metadata.
    /// </summary>
    public sealed class AasRegistryResource
    {
        /// <summary>
        /// Initializes a resource snapshot.
        /// </summary>
        public AasRegistryResource(
            string groupId,
            string resourceId,
            string sourceIdentity,
            AasRegistryEntityKind kind,
            ImmutableArray<AasRegistryResourceVersion> versions,
            string? defaultVersionId = null,
            string? name = null,
            string? description = null,
            string? semanticId = null,
            string? template = null,
            AASDisclosureTierDataType disclosureTier = AASDisclosureTierDataType.Public,
            ArrayOf<AASAuthorizationOptionDataType>? authorization = null,
            bool concealFromUnauthorized = false,
            long epoch = 0,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            SourceIdentity = sourceIdentity ?? throw new ArgumentNullException(nameof(sourceIdentity));
            Kind = kind;
            Versions = versions.IsDefault ? [] : versions.Sort(VersionComparer.Instance);
            DefaultVersionId = defaultVersionId ?? Versions.LastOrDefault()?.VersionId;
            Name = name ?? sourceIdentity;
            Description = description ?? string.Empty;
            SemanticId = semanticId ?? string.Empty;
            Template = template ?? string.Empty;
            DisclosureTier = disclosureTier;
            Authorization = CopyAuthorization(authorization);
            ConcealFromUnauthorized = concealFromUnauthorized;
            Epoch = epoch;
            Labels = labels ?? AasRegistryLabels.Empty;
        }

        /// <summary>
        /// Gets the owning group id.
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Gets the xRegistry resource id.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Gets the verbatim source identity.
        /// </summary>
        public string SourceIdentity { get; }

        /// <summary>
        /// Gets the resource kind.
        /// </summary>
        public AasRegistryEntityKind Kind { get; }

        /// <summary>
        /// Gets retained resource versions, ordered by time.
        /// </summary>
        public ImmutableArray<AasRegistryResourceVersion> Versions { get; }

        /// <summary>
        /// Gets the default version identifier.
        /// </summary>
        public string? DefaultVersionId { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the semantic id for submodel resources.
        /// </summary>
        public string SemanticId { get; }

        /// <summary>
        /// Gets the template identifier for submodel resources.
        /// </summary>
        public string Template { get; }

        /// <summary>
        /// Gets whether this document is public or controlled.
        /// </summary>
        public AASDisclosureTierDataType DisclosureTier { get; }

        /// <summary>
        /// Gets authorization configuration. It never carries credentials, keys or tokens.
        /// </summary>
        public ArrayOf<AASAuthorizationOptionDataType> Authorization { get; }

        /// <summary>
        /// Gets whether unauthorized callers receive Bad_NotFound instead of Bad_UserAccessDenied.
        /// </summary>
        public bool ConcealFromUnauthorized { get; }

        /// <summary>
        /// Gets the entity epoch.
        /// </summary>
        public long Epoch { get; }

        /// <summary>
        /// Gets xRegistry labels.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>
        /// Gets the resource xid.
        /// </summary>
        public string Xid => $"/groups/{GroupId}/resources/{ResourceId}";

        /// <summary>
        /// Gets the newest/default version.
        /// </summary>
        public AasRegistryResourceVersion? DefaultVersion => FindVersion(DefaultVersionId) ?? Versions.LastOrDefault();

        /// <summary>
        /// Gets a resource copy with a different version list.
        /// </summary>
        public AasRegistryResource WithVersions(
            ImmutableArray<AasRegistryResourceVersion> versions,
            string? defaultVersionId,
            long epoch)
        {
            return new AasRegistryResource(
                GroupId,
                ResourceId,
                SourceIdentity,
                Kind,
                versions,
                defaultVersionId,
                Name,
                Description,
                SemanticId,
                Template,
                DisclosureTier,
                Authorization,
                ConcealFromUnauthorized,
                epoch,
                Labels);
        }

        /// <summary>
        /// Finds the newest version not later than the specified moment.
        /// </summary>
        public AasRegistryResourceVersion? FindVersionAsOf(DateTime asOfUtc)
        {
            return Versions
                .Where(version => version.CreatedAt <= asOfUtc)
                .OrderBy(version => version.CreatedAt)
                .LastOrDefault();
        }

        /// <summary>
        /// Finds a version by identifier.
        /// </summary>
        public AasRegistryResourceVersion? FindVersion(string? versionId)
        {
            return versionId is null
                ? null
                : Versions.FirstOrDefault(version => string.Equals(version.VersionId, versionId, StringComparison.Ordinal));
        }
        internal static ArrayOf<AASAuthorizationOptionDataType> CopyAuthorization(
            ArrayOf<AASAuthorizationOptionDataType>? authorization)
        {
            if (authorization is null)
            {
                return [];
            }
            var copy = new List<AASAuthorizationOptionDataType>();
            foreach (AASAuthorizationOptionDataType option in authorization)
            {
                copy.Add(new AASAuthorizationOptionDataType
                {
                    Type = option.Type ?? string.Empty,
                    Mechanism = option.Mechanism ?? string.Empty,
                    ResourceUri = option.ResourceUri ?? string.Empty,
                    AuthorityUri = option.AuthorityUri ?? string.Empty
                });
            }
            return new ArrayOf<AASAuthorizationOptionDataType>(copy.ToArray());
        }
        private sealed class VersionComparer : IComparer<AasRegistryResourceVersion>
        {
            public static VersionComparer Instance { get; } = new();
            public int Compare(AasRegistryResourceVersion? x, AasRegistryResourceVersion? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }
                if (x is null)
                {
                    return -1;
                }
                if (y is null)
                {
                    return 1;
                }
                int at = x.CreatedAt.CompareTo(y.CreatedAt);
                return at != 0 ? at : string.CompareOrdinal(x.VersionId, y.VersionId);
            }
        }
    }

    /// <summary>
    /// Immutable AAS registry group metadata.
    /// </summary>
    public sealed class AasRegistryGroup
    {
        /// <summary>
        /// Initializes a group snapshot.
        /// </summary>
        public AasRegistryGroup(
            string groupId,
            string sourceIdentity,
            AasRegistryEntityKind kind,
            ImmutableSortedDictionary<string, AasRegistryResource>? resources = null,
            string? name = null,
            string? description = null,
            AASDisclosureTierDataType disclosureTier = AASDisclosureTierDataType.Public,
            ArrayOf<AASAuthorizationOptionDataType>? authorization = null,
            bool concealFromUnauthorized = false,
            long epoch = 0,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            SourceIdentity = sourceIdentity ?? throw new ArgumentNullException(nameof(sourceIdentity));
            Kind = kind;
            Resources = resources ?? ImmutableSortedDictionary.Create<string, AasRegistryResource>(StringComparer.Ordinal);
            Name = name ?? sourceIdentity;
            Description = description ?? string.Empty;
            DisclosureTier = disclosureTier;
            Authorization = AasRegistryResource.CopyAuthorization(authorization);
            ConcealFromUnauthorized = concealFromUnauthorized;
            Epoch = epoch;
            Labels = labels ?? AasRegistryLabels.Empty;
        }

        /// <summary>
        /// Gets the xRegistry group id.
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Gets the verbatim source identity.
        /// </summary>
        public string SourceIdentity { get; }

        /// <summary>
        /// Gets the group kind.
        /// </summary>
        public AasRegistryEntityKind Kind { get; }

        /// <summary>
        /// Gets the resources owned by this group.
        /// </summary>
        public ImmutableSortedDictionary<string, AasRegistryResource> Resources { get; }

        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the disclosure tier.
        /// </summary>
        public AASDisclosureTierDataType DisclosureTier { get; }

        /// <summary>
        /// Gets authorization configuration.
        /// </summary>
        public ArrayOf<AASAuthorizationOptionDataType> Authorization { get; }

        /// <summary>
        /// Gets whether unauthorized callers should not see this group.
        /// </summary>
        public bool ConcealFromUnauthorized { get; }

        /// <summary>
        /// Gets the entity epoch.
        /// </summary>
        public long Epoch { get; }

        /// <summary>
        /// Gets labels.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>
        /// Gets the group xid.
        /// </summary>
        public string Xid => $"/groups/{GroupId}";

        /// <summary>
        /// Finds a resource by source identity.
        /// </summary>
        /// <remarks>
        /// A repeated write has to reach the resource it already allocated, so
        /// it is matched on source identity rather than on a re-derived
        /// identifier, which would disambiguate against itself.
        /// </remarks>
        public AasRegistryResource? FindResourceBySourceIdentity(string sourceIdentity)
        {
            foreach (AasRegistryResource resource in Resources.Values)
            {
                if (string.Equals(resource.SourceIdentity, sourceIdentity, StringComparison.Ordinal))
                {
                    return resource;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a copy with a resource added or replaced.
        /// </summary>
        public AasRegistryGroup WithResource(AasRegistryResource resource, long epoch)
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            return new AasRegistryGroup(
                GroupId,
                SourceIdentity,
                Kind,
                Resources.SetItem(resource.ResourceId, resource),
                Name,
                Description,
                DisclosureTier,
                Authorization,
                ConcealFromUnauthorized,
                epoch,
                Labels);
        }

        /// <summary>
        /// Returns a copy with labels added or replaced.
        /// </summary>
        public AasRegistryGroup WithLabels(ImmutableSortedDictionary<string, string> labels, long epoch)
        {
            return new AasRegistryGroup(
                GroupId,
                SourceIdentity,
                Kind,
                Resources,
                Name,
                Description,
                DisclosureTier,
                Authorization,
                ConcealFromUnauthorized,
                epoch,
                labels);
        }
    }

    /// <summary>
    /// Immutable AAS registry snapshot.
    /// </summary>
    public sealed class AasRegistrySnapshot : IXRegistryProjectionSnapshot
    {
        /// <summary>
        /// Initializes a snapshot.
        /// </summary>
        public AasRegistrySnapshot(
            long generation,
            ImmutableSortedDictionary<string, AasRegistryGroup>? groups = null,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            Generation = generation;
            GroupsById = groups ?? ImmutableSortedDictionary.Create<string, AasRegistryGroup>(StringComparer.Ordinal);
            Labels = labels ?? AasRegistryLabels.Empty;
        }

        /// <summary>
        /// Gets an empty snapshot.
        /// </summary>
        public static AasRegistrySnapshot Empty { get; } = new(0);

        /// <summary>
        /// Gets the strictly increasing generation.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets groups keyed by group id.
        /// </summary>
        public ImmutableSortedDictionary<string, AasRegistryGroup> GroupsById { get; }

        /// <inheritdoc/>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <inheritdoc/>
        IEnumerable<IXRegistryProjectionGroup> IXRegistryProjectionSnapshot.Groups =>
            GroupsById.Values.Select(group => new AasProjectionGroupAdapter(group));

        /// <summary>
        /// Finds a group.
        /// </summary>
        public AasRegistryGroup? FindGroup(string groupId)
        {
            return GroupsById.TryGetValue(groupId, out AasRegistryGroup? group) ? group : null;
        }

        /// <summary>
        /// Finds a group by source identity.
        /// </summary>
        /// <remarks>
        /// An entity keeps the identifier it was allocated, so a repeated write
        /// has to be matched on the source identity it carries. Re-deriving the
        /// identifier would disambiguate it against itself and fork a second
        /// group for the same shell.
        /// </remarks>
        public AasRegistryGroup? FindGroupBySourceIdentity(string sourceIdentity)
        {
            foreach (AasRegistryGroup group in GroupsById.Values)
            {
                if (string.Equals(group.SourceIdentity, sourceIdentity, StringComparison.Ordinal))
                {
                    return group;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a resource.
        /// </summary>
        public AasRegistryResource? FindResource(string groupId, string resourceId)
        {
            return FindGroup(groupId)?.Resources.TryGetValue(resourceId, out AasRegistryResource? resource) == true
                ? resource
                : null;
        }

        /// <summary>
        /// Finds a submodel resource by source identity.
        /// </summary>
        public AasRegistryResource? FindSubmodelBySourceIdentity(string submodelIdentifier)
        {
            foreach (AasRegistryGroup group in GroupsById.Values)
            {
                foreach (AasRegistryResource resource in group.Resources.Values)
                {
                    if (resource.Kind == AasRegistryEntityKind.Submodel &&
                        string.Equals(resource.SourceIdentity, submodelIdentifier, StringComparison.Ordinal))
                    {
                        return resource;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a copy with a group added or replaced.
        /// </summary>
        public AasRegistrySnapshot WithGroup(AasRegistryGroup group, long generation)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            return new AasRegistrySnapshot(generation, GroupsById.SetItem(group.GroupId, group), Labels);
        }
    }
    internal sealed class AasProjectionGroupAdapter : IXRegistryProjectionGroup
    {
        public AasProjectionGroupAdapter(AasRegistryGroup group)
        {
            Group = group;
        }
        public AasRegistryGroup Group { get; }
        public string GroupId => Group.GroupId;
        public string Xid => Group.Xid;
        public string Name => Group.Name;
        public string Description => Group.Description;
        public long Epoch => Group.Epoch;
        public ImmutableSortedDictionary<string, string> Labels => Group.Labels;

        /// <summary>
        /// Enumerates the resources this group projects into the AddressSpace.
        /// </summary>
        /// <remarks>
        /// A resource that conceals itself from the unauthorized is left out
        /// entirely. Clause 6.5.7 requires a Server that must not reveal even
        /// the existence of controlled content to omit those entries rather
        /// than mark them, and the disclosure decision otherwise lived only
        /// inside GetSubmodel: the Method answered BadNotFound while an
        /// anonymous Browse of the registry folder still showed the node, its
        /// identifier, its semanticId and its content digest, so the
        /// concealment did not hold end to end.
        /// </remarks>
        public IEnumerable<IXRegistryProjectionResource> Resources =>
            Group.Resources.Values
                .Where(resource => !resource.ConcealFromUnauthorized)
                .Select(resource => new AasProjectionResourceAdapter(resource));
    }
    internal sealed class AasProjectionResourceAdapter : IXRegistryProjectionResource
    {
        public AasProjectionResourceAdapter(AasRegistryResource resource)
        {
            Resource = resource;
        }
        public AasRegistryResource Resource { get; }
        public string GroupId => Resource.GroupId;
        public string ResourceId => Resource.ResourceId;
        public string Xid => Resource.Xid;
        public string Name => Resource.Name;
        public string Description => Resource.Description;
        public string VersionId => Resource.DefaultVersionId ?? string.Empty;
        public string Format => Resource.DefaultVersion?.Format ?? string.Empty;
        public string ContentType => Resource.DefaultVersion?.ContentType ?? string.Empty;
        public long Epoch => Resource.Epoch;
        public DateTime CreatedAt => Resource.DefaultVersion?.CreatedAt ?? default;
        public DateTime ModifiedAt => Resource.DefaultVersion?.ModifiedAt ?? DateTime.UtcNow;
        public ImmutableSortedDictionary<string, string> Labels => Resource.Labels;
    }
    internal static class AasRegistryVersionId
    {
        public static string Create(DateTime createdAt, ByteString digest)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "v{0:yyyyMMddHHmmssfffffffZ}.{1}",
                createdAt,
                AasRegistryContentDigest.ToHex(digest).Substring(0, 12));
        }
    }
}
