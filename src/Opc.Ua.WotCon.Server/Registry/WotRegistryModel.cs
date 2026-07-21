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
using System.Security.Cryptography;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Computes the content digest used to detect unchanged registry documents.
    /// </summary>
    /// <remarks>
    /// The digest is a SHA-256 over the raw source bytes. It backs the
    /// <c>ContentDigest</c> surfaced on the generated <c>WoTDocumentType</c>
    /// and the idempotency check that lets an unchanged refresh return the
    /// <see cref="WoTOutcomeEnum.Unchanged"/> outcome without re-projecting.
    /// </remarks>
    public static class WotContentDigest
    {
        /// <summary>
        /// Computes the SHA-256 digest of the supplied document bytes.
        /// </summary>
        public static byte[] Compute(ReadOnlyMemory<byte> content)
        {
            using var sha = SHA256.Create();
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(
                    content, out ArraySegment<byte> segment) &&
                segment.Array is not null)
            {
                return sha.ComputeHash(segment.Array, segment.Offset, segment.Count);
            }
            return sha.ComputeHash(content.ToArray());
        }

        /// <summary>
        /// Formats a digest as a lowercase hexadecimal string, or the empty
        /// string when <paramref name="digest"/> is <c>null</c> or empty.
        /// </summary>
        public static string ToHex(byte[]? digest)
        {
            if (digest is null || digest.Length == 0)
            {
                return string.Empty;
            }
            var chars = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++)
            {
                byte b = digest[i];
                chars[i * 2] = GetHexChar(b >> 4);
                chars[(i * 2) + 1] = GetHexChar(b & 0xF);
            }
            return new string(chars);
        }

        private static char GetHexChar(int nibble)
            => (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));

        /// <summary>
        /// Determines whether two digests are byte-for-byte equal.
        /// </summary>
        public static bool Equal(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left is null || right is null)
            {
                return false;
            }
            return left.AsSpan().SequenceEqual(right);
        }
    }

    /// <summary>
    /// An immutable snapshot of a single stored document version (an xRegistry
    /// Version under a Resource). The raw <see cref="Content"/> bytes are the
    /// authoritative source and are never mutated.
    /// </summary>
    public sealed class WotResourceVersion
    {
        /// <summary>
        /// Initializes a new immutable version snapshot.
        /// </summary>
        public WotResourceVersion(
            string versionId,
            ReadOnlyMemory<byte> content,
            string contentType,
            string format,
            DateTime createdAt,
            DateTime modifiedAt,
            byte[]? digest = null)
        {
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
            Content = content;
            ContentType = contentType ?? string.Empty;
            Format = format ?? string.Empty;
            CreatedAt = createdAt;
            ModifiedAt = modifiedAt;
            Digest = digest ?? WotContentDigest.Compute(content);
        }

        /// <summary>Gets the xRegistry versionid.</summary>
        public string VersionId { get; }

        /// <summary>Gets the raw, authoritative document source bytes.</summary>
        public ReadOnlyMemory<byte> Content { get; }

        /// <summary>Gets the media type of the document (for example application/td+json).</summary>
        public string ContentType { get; }

        /// <summary>Gets the document format tag (for example WoT-TD/1.1).</summary>
        public string Format { get; }

        /// <summary>Gets the UTC creation time.</summary>
        public DateTime CreatedAt { get; }

        /// <summary>Gets the UTC modification time.</summary>
        public DateTime ModifiedAt { get; }

        /// <summary>Gets the SHA-256 content digest of <see cref="Content"/>.</summary>
        public byte[] Digest { get; }

        /// <summary>Gets the content digest as a lowercase hexadecimal string.</summary>
        public string DigestHex => WotContentDigest.ToHex(Digest);
    }

    /// <summary>
    /// Helpers for the xRegistry label/attribute dictionaries carried by
    /// <see cref="WotResourceGroup"/>, <see cref="WotResource"/> and
    /// <see cref="WotRegistrySnapshot"/>. Ordinal key ordering keeps
    /// enumeration (and therefore NodeManager materialization order)
    /// deterministic regardless of insertion order.
    /// </summary>
    public static class WotLabels
    {
        /// <summary>Gets the empty, ordinally-ordered label dictionary.</summary>
        public static ImmutableSortedDictionary<string, string> Empty { get; } =
            ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// An immutable snapshot of a single registered document resource, carrying
    /// its versions, desired/active version pointers, and load/validation state.
    /// </summary>
    public sealed class WotResource
    {
        /// <summary>
        /// Initializes a new immutable resource snapshot.
        /// </summary>
        public WotResource(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            ImmutableArray<WotResourceVersion> versions,
            string? defaultVersionId = null,
            string? desiredVersionId = null,
            string? activeVersionId = null,
            bool enabled = true,
            WoTLoadStateEnum loadState = WoTLoadStateEnum.Unloaded,
            WoTValidationOutcomeDataType? validation = null,
            ImmutableArray<string> diagnostics = default,
            long epoch = 0,
            uint refreshGeneration = 0,
            DateTime lastRefreshTime = default,
            int materializedNodeCount = 0,
            NodeId? rootNodeId = null,
            string? name = null,
            string? description = null,
            string? thingId = null,
            string? title = null,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            Kind = kind;
            Versions = versions.IsDefault ? ImmutableArray<WotResourceVersion>.Empty : versions;
            DefaultVersionId = defaultVersionId;
            DesiredVersionId = desiredVersionId ?? defaultVersionId;
            ActiveVersionId = activeVersionId;
            Enabled = enabled;
            LoadState = loadState;
            Validation = validation;
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<string>.Empty : diagnostics;
            Epoch = epoch;
            RefreshGeneration = refreshGeneration;
            LastRefreshTime = lastRefreshTime;
            MaterializedNodeCount = materializedNodeCount;
            RootNodeId = rootNodeId;
            Name = name ?? resourceId;
            Description = description ?? string.Empty;
            ThingId = thingId;
            Title = title;
            Labels = labels ?? WotLabels.Empty;
        }

        /// <summary>Gets the owning group id.</summary>
        public string GroupId { get; }

        /// <summary>Gets the xRegistry resourceid.</summary>
        public string ResourceId { get; }

        /// <summary>Gets the xRegistry xid (<c>/groups/{group}/resources/{resource}</c>).</summary>
        public string Xid => $"/groups/{GroupId}/resources/{ResourceId}";

        /// <summary>Gets whether the document is a Thing Description or Thing Model.</summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>Gets the immutable set of versions, oldest first.</summary>
        public ImmutableArray<WotResourceVersion> Versions { get; }

        /// <summary>Gets the versionid marked as default (the projected version).</summary>
        public string? DefaultVersionId { get; }

        /// <summary>Gets the versionid the operator wants active.</summary>
        public string? DesiredVersionId { get; }

        /// <summary>Gets the versionid currently projected into the AddressSpace.</summary>
        public string? ActiveVersionId { get; }

        /// <summary>Gets whether the resource is enabled for projection.</summary>
        public bool Enabled { get; }

        /// <summary>Gets the current load/projection state.</summary>
        public WoTLoadStateEnum LoadState { get; }

        /// <summary>Gets the last validation outcome, if any.</summary>
        public WoTValidationOutcomeDataType? Validation { get; }

        /// <summary>Gets the human-readable diagnostics for the last operation.</summary>
        public ImmutableArray<string> Diagnostics { get; }

        /// <summary>Gets the resource epoch (bumped on every mutation).</summary>
        public long Epoch { get; }

        /// <summary>Gets the refresh generation of the active projection.</summary>
        public uint RefreshGeneration { get; }

        /// <summary>Gets the UTC time of the last refresh.</summary>
        public DateTime LastRefreshTime { get; }

        /// <summary>Gets the number of AddressSpace nodes materialized for the active projection.</summary>
        public int MaterializedNodeCount { get; }

        /// <summary>Gets the root node of the active projection, if any.</summary>
        public NodeId? RootNodeId { get; }

        /// <summary>Gets the resource display name.</summary>
        public string Name { get; }

        /// <summary>Gets the resource description.</summary>
        public string Description { get; }

        /// <summary>Gets the WoT Thing id parsed from the default document (TD only).</summary>
        public string? ThingId { get; }

        /// <summary>Gets the WoT title parsed from the default document.</summary>
        public string? Title { get; }

        /// <summary>
        /// Gets the resource's extensible xRegistry labels/attributes,
        /// ordinally ordered by key. Materialized as the resource's
        /// browseable <c>Labels</c> (AttributesType) container.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>Gets the default (or desired) version snapshot, if present.</summary>
        public WotResourceVersion? DefaultVersion
            => FindVersion(DesiredVersionId ?? DefaultVersionId);

        /// <summary>Gets the active version snapshot, if present.</summary>
        public WotResourceVersion? ActiveVersion => FindVersion(ActiveVersionId);

        /// <summary>Finds a version by id.</summary>
        public WotResourceVersion? FindVersion(string? versionId)
        {
            if (string.IsNullOrEmpty(versionId))
            {
                return null;
            }
            foreach (WotResourceVersion version in Versions)
            {
                if (string.Equals(version.VersionId, versionId, StringComparison.Ordinal))
                {
                    return version;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a copy of this resource with selected fields replaced.
        /// </summary>
        public WotResource With(
            ImmutableArray<WotResourceVersion>? versions = null,
            string? defaultVersionId = null,
            string? desiredVersionId = null,
            string? activeVersionId = null,
            bool? enabled = null,
            WoTLoadStateEnum? loadState = null,
            WoTValidationOutcomeDataType? validation = null,
            ImmutableArray<string>? diagnostics = null,
            long? epoch = null,
            uint? refreshGeneration = null,
            DateTime? lastRefreshTime = null,
            int? materializedNodeCount = null,
            NodeId? rootNodeId = null,
            string? name = null,
            string? description = null,
            string? thingId = null,
            string? title = null,
            ImmutableSortedDictionary<string, string>? labels = null,
            bool clearActiveVersion = false,
            bool clearValidation = false,
            bool clearRootNodeId = false)
        {
            return new WotResource(
                GroupId,
                ResourceId,
                Kind,
                versions ?? Versions,
                defaultVersionId ?? DefaultVersionId,
                desiredVersionId ?? DesiredVersionId,
                clearActiveVersion ? null : (activeVersionId ?? ActiveVersionId),
                enabled ?? Enabled,
                loadState ?? LoadState,
                clearValidation ? null : (validation ?? Validation),
                diagnostics ?? Diagnostics,
                epoch ?? Epoch,
                refreshGeneration ?? RefreshGeneration,
                lastRefreshTime ?? LastRefreshTime,
                materializedNodeCount ?? MaterializedNodeCount,
                clearRootNodeId ? null : (rootNodeId ?? RootNodeId),
                name ?? Name,
                description ?? Description,
                thingId ?? ThingId,
                title ?? Title,
                labels ?? Labels);
        }
    }

    /// <summary>
    /// An immutable snapshot of a document group (an xRegistry Group). A group
    /// is homogeneous in <see cref="Kind"/>: the well-known
    /// <c>ThingDescriptions</c> and <c>ThingModels</c> groups map to
    /// <see cref="WoTDocumentKindEnum.ThingDescription"/> and
    /// <see cref="WoTDocumentKindEnum.ThingModel"/>.
    /// </summary>
    public sealed class WotResourceGroup
    {
        /// <summary>
        /// Initializes a new immutable group snapshot.
        /// </summary>
        public WotResourceGroup(
            string groupId,
            WoTDocumentKindEnum kind,
            ImmutableDictionary<string, WotResource>? resources = null,
            string? name = null,
            string? description = null,
            long epoch = 0,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
            Kind = kind;
            Resources = resources ?? ImmutableDictionary<string, WotResource>.Empty;
            Name = name ?? groupId;
            Description = description ?? string.Empty;
            Epoch = epoch;
            Labels = labels ?? WotLabels.Empty;
        }

        /// <summary>Gets the xRegistry groupid.</summary>
        public string GroupId { get; }

        /// <summary>Gets the xRegistry xid (<c>/groups/{group}</c>).</summary>
        public string Xid => $"/groups/{GroupId}";

        /// <summary>Gets the document kind shared by all resources in this group.</summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>Gets the resources keyed by resourceid.</summary>
        public ImmutableDictionary<string, WotResource> Resources { get; }

        /// <summary>Gets the group display name.</summary>
        public string Name { get; }

        /// <summary>Gets the group description.</summary>
        public string Description { get; }

        /// <summary>Gets the group epoch.</summary>
        public long Epoch { get; }

        /// <summary>
        /// Gets the group's extensible xRegistry labels/attributes, ordinally
        /// ordered by key. Materialized as the group's browseable
        /// <c>Labels</c> (AttributesType) container.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>Returns a copy of this group with the resource set replaced.</summary>
        public WotResourceGroup WithResources(
            ImmutableDictionary<string, WotResource> resources,
            long epoch)
        {
            return new WotResourceGroup(GroupId, Kind, resources, Name, Description, epoch, Labels);
        }

        /// <summary>Returns a copy of this group with the label set replaced.</summary>
        public WotResourceGroup WithLabels(
            ImmutableSortedDictionary<string, string> labels,
            long epoch)
        {
            return new WotResourceGroup(GroupId, Kind, Resources, Name, Description, epoch, labels);
        }
    }

    /// <summary>
    /// An immutable, point-in-time snapshot of the entire WoT registry. Each
    /// mutation of the registry produces a new snapshot with a strictly greater
    /// <see cref="Generation"/>; readers hold a snapshot reference and never see
    /// a partially-applied change.
    /// </summary>
    public sealed class WotRegistrySnapshot
    {
        /// <summary>Gets the empty snapshot (generation 0, no groups).</summary>
        public static WotRegistrySnapshot Empty { get; } =
            new WotRegistrySnapshot(0, ImmutableDictionary<string, WotResourceGroup>.Empty);

        /// <summary>
        /// Initializes a new immutable registry snapshot.
        /// </summary>
        public WotRegistrySnapshot(
            long generation,
            ImmutableDictionary<string, WotResourceGroup> groups,
            ImmutableSortedDictionary<string, string>? labels = null)
        {
            Generation = generation;
            Groups = groups ?? ImmutableDictionary<string, WotResourceGroup>.Empty;
            Labels = labels ?? WotLabels.Empty;
        }

        /// <summary>
        /// Gets the monotonically increasing snapshot generation (registry epoch).
        /// </summary>
        public long Generation { get; }

        /// <summary>Gets the groups keyed by groupid.</summary>
        public ImmutableDictionary<string, WotResourceGroup> Groups { get; }

        /// <summary>
        /// Gets the registry-level extensible xRegistry labels/attributes,
        /// ordinally ordered by key. Materialized as the well-known
        /// <c>WoTRegistry</c> object's browseable <c>Labels</c>
        /// (AttributesType) container. Optimistic-concurrency checks against
        /// these labels compare against <see cref="Generation"/>, since the
        /// singleton registry object has no separate per-entity epoch.
        /// </summary>
        public ImmutableSortedDictionary<string, string> Labels { get; }

        /// <summary>Enumerates every resource across all groups.</summary>
        public IEnumerable<WotResource> AllResources()
        {
            foreach (WotResourceGroup group in Groups.Values)
            {
                foreach (WotResource resource in group.Resources.Values)
                {
                    yield return resource;
                }
            }
        }

        /// <summary>Enumerates every resource of the requested kind.</summary>
        public IEnumerable<WotResource> ResourcesOfKind(WoTDocumentKindEnum kind)
            => AllResources().Where(r => r.Kind == kind);

        /// <summary>Finds a group by id, or <c>null</c>.</summary>
        public WotResourceGroup? FindGroup(string groupId)
            => Groups.TryGetValue(groupId, out WotResourceGroup? group) ? group : null;

        /// <summary>Finds a resource by group and resource id, or <c>null</c>.</summary>
        public WotResource? FindResource(string groupId, string resourceId)
        {
            if (Groups.TryGetValue(groupId, out WotResourceGroup? group) &&
                group.Resources.TryGetValue(resourceId, out WotResource? resource))
            {
                return resource;
            }
            return null;
        }

        /// <summary>Finds a resource by its xRegistry xid, or <c>null</c>.</summary>
        public WotResource? FindResourceByXid(string xid)
            => AllResources().FirstOrDefault(
                r => string.Equals(r.Xid, xid, StringComparison.Ordinal));

        /// <summary>
        /// Produces a new snapshot with <paramref name="group"/> upserted and the
        /// generation advanced to <paramref name="generation"/>.
        /// </summary>
        public WotRegistrySnapshot WithGroup(WotResourceGroup group, long generation)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            return new WotRegistrySnapshot(generation, Groups.SetItem(group.GroupId, group), Labels);
        }

        /// <summary>Produces a new snapshot with a group removed.</summary>
        public WotRegistrySnapshot WithoutGroup(string groupId, long generation)
            => new WotRegistrySnapshot(generation, Groups.Remove(groupId), Labels);

        /// <summary>Produces a new snapshot with the registry-level label set replaced.</summary>
        public WotRegistrySnapshot WithLabels(
            ImmutableSortedDictionary<string, string> labels, long generation)
            => new WotRegistrySnapshot(generation, Groups, labels);

        /// <summary>
        /// Formats a monotonic version id from the sequence number, using the
        /// zero-padded, lexicographically sortable form used by the file store.
        /// </summary>
        public static string FormatVersionId(long sequence)
            => sequence.ToString("D19", CultureInfo.InvariantCulture);
    }
}
