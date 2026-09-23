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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One metadata-selected Resource and exact Version, before any body acquisition.
    /// </summary>
    public sealed class WotSelectedResource
    {
        internal WotSelectedResource(WotResource resource, WotResourceVersion? version, string resultXid)
        {
            Resource = resource;
            Version = version;
            ResultXid = resultXid;
        }

        /// <summary>
        /// Gets the selected Resource with its original lifecycle state.
        /// </summary>
        public WotResource Resource { get; }

        /// <summary>
        /// Gets the exact selected Version, if it exists.
        /// </summary>
        public WotResourceVersion? Version { get; }

        /// <summary>
        /// Gets the selector-resolved identity to retain in results, including an exact Version Xid.
        /// </summary>
        public string ResultXid { get; }
    }

    /// <summary>
    /// A failure to acquire one exact input. It remains a member of its intended closure.
    /// </summary>
    public sealed class WotResourceAcquisitionFailure
    {
        internal WotResourceAcquisitionFailure(
            WotResource resource, WotResourceVersion version, StatusCode statusCode, string message)
        {
            Resource = resource;
            Version = version;
            StatusCode = statusCode;
            Message = message;
        }

        /// <summary>
        /// Gets the input's Resource identity.
        /// </summary>
        public WotResource Resource { get; }

        /// <summary>
        /// Gets the failed exact Version.
        /// </summary>
        public WotResourceVersion Version { get; }

        /// <summary>
        /// Gets the acquisition status.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Gets the reported reason.
        /// </summary>
        public string Message { get; }
    }

    /// <summary>
    /// An immutable refresh input image. Disposing releases its exact-Version retention leases;
    /// it does not rewrite the captured identities, metadata, edges or document bytes.
    /// </summary>
    public sealed class WotMaterializationSnapshot : IDisposable
    {
        internal WotMaterializationSnapshot(
            WotRegistrySnapshot registry,
            ArrayOf<WotSelectedResource> selection,
            bool selectsAll,
            ArrayOf<WotDependencyClosure> closures,
            ImmutableDictionary<string, ByteString> contents,
            ImmutableArray<IWotRegistryVersionLease> leases)
        {
            Registry = registry;
            Selection = selection;
            SelectsAll = selectsAll;
            Closures = closures;
            Contents = contents;
            m_leases = leases;
            Resources = registry.AllResources().OrderBy(resource => resource.Xid, StringComparer.Ordinal).ToArrayOf();
            AcquisitionFailures = closures.ToList()
                .SelectMany(closure => closure.AcquisitionFailures.ToList()).ToArrayOf();
        }

        /// <summary>
        /// Gets only the selected and required resolution inputs, with their Versions pinned.
        /// </summary>
        public WotRegistrySnapshot Registry { get; }

        /// <summary>
        /// Gets the metadata-selected roots, including skipped exact identities.
        /// </summary>
        public ArrayOf<WotSelectedResource> Selection { get; }

        /// <summary>
        /// Gets whether Selection was omitted. An explicit unmatched selection is never all.
        /// </summary>
        public bool SelectsAll { get; }

        /// <summary>
        /// Gets the captured resources, not unrelated registry members.
        /// </summary>
        public ArrayOf<WotResource> Resources { get; }

        /// <summary>
        /// Gets the dependency closures over the captured inputs.
        /// </summary>
        public ArrayOf<WotDependencyClosure> Closures { get; }

        /// <summary>
        /// Gets the individual acquisition failures.
        /// </summary>
        public ArrayOf<WotResourceAcquisitionFailure> AcquisitionFailures { get; }

        internal ImmutableDictionary<string, ByteString> Contents { get; }

        /// <summary>
        /// Reads the captured bytes of one exact input; never falls back to the current store.
        /// </summary>
        public ByteString GetContent(WotResource resource, WotResourceVersion version)
        {
            _ = resource ?? throw new ArgumentNullException(nameof(resource));
            _ = version ?? throw new ArgumentNullException(nameof(version));
            WotResourceVersion? captured = Registry.FindResource(resource.GroupId, resource.ResourceId)?.DefaultVersion;
            if (captured is null || captured.IncarnationId != version.IncarnationId ||
                captured.Epoch != version.Epoch || !WotContentDigest.Equal(captured.Digest, version.Digest) ||
                !Contents.TryGetValue(version.DigestHex, out ByteString content))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound, "The exact Resource/Version input was not acquired in this snapshot.");
            }
            return content;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            foreach (IWotRegistryVersionLease lease in m_leases)
            {
                lease.Dispose();
            }
        }

        private readonly ImmutableArray<IWotRegistryVersionLease> m_leases;
        private int m_disposed;
    }

    public static partial class WotDependencyGraph
    {
        /// <summary>
        /// Resolves selector identity and requested dependents from metadata before acquiring
        /// any document. The caller owns the returned exact-Version leases.
        /// </summary>
        public static ValueTask<WotMaterializationSnapshot> CaptureAsync(
            IWotRegistryService registry,
            ArrayOf<WoTResourceSelectorDataType> selectors,
            bool includeDependents,
            int maxJsonDepth,
            CancellationToken cancellationToken = default)
        {
            return CapturePublicationAsync(registry, selectors, includeDependents, maxJsonDepth, [], cancellationToken);
        }

        internal static async ValueTask<WotMaterializationSnapshot> CapturePublicationAsync(
            IWotRegistryService registry,
            ArrayOf<WoTResourceSelectorDataType> selectors,
            bool includeDependents,
            int maxJsonDepth,
            ArrayOf<ArrayOf<string>> replacementClosures,
            CancellationToken cancellationToken,
            bool committedInputs = false)
        {
            _ = registry ?? throw new ArgumentNullException(nameof(registry));
            WotRegistrySnapshot original = registry.Current;
            var retainedInputs = new Dictionary<string, WotResource>(StringComparer.Ordinal);
            if (committedInputs)
            {
                foreach (WotResource owner in original.AllResources())
                {
                    foreach (WotResource input in owner.CommittedInputs)
                    {
                        WotResourceVersion version = input.Versions[0];
                        if (owner.CommittedVersion?.DependencySnapshot is { } observation)
                        {
                            bool matched = false;
                            foreach (WotDependencyTargetPin target in observation.Targets)
                            {
                                if (observation.Edges[(int)target.EdgeIndex].TargetXid != input.Xid)
                                {
                                    continue;
                                }
                                if (target.VersionXid != VersionXid(input, version) ||
                                    !WotContentDigest.Equal(target.ContentDigest, version.Digest))
                                {
                                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                                        "A retained input contradicts its committed dependency observation.");
                                }
                                matched = true;
                            }
                            if (!matched)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadInvalidState, "A retained input has no committed dependency edge.");
                            }
                        }
                        if (retainedInputs.TryGetValue(input.Xid, out WotResource? previous))
                        {
                            WotResourceVersion retained = previous.Versions[0];
                            WotResourceVersion candidate = input.Versions[0];
                            // Reloaded records have independent process-local incarnation IDs.
                            if (previous.Kind != input.Kind || previous.SourceId != input.SourceId ||
                                previous.MetaCreatedAt != input.MetaCreatedAt ||
                                !SameCommittedInputVersion(retained, candidate))
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadInvalidState,
                                    "Committed publications disagree on a resolution input.");
                            }
                        }
                        retainedInputs[input.Xid] = input;
                    }
                }
                foreach (WotResourceGroup group in original.Groups.Values)
                {
                    ImmutableDictionary<string, WotResource> resources = group.Resources;
                    foreach (WotResource resource in group.Resources.Values)
                    {
                        if (resource.CommittedVersion is not { } version)
                        {
                            continue;
                        }
                        WotResourceVersion? current = resource.FindVersion(version.VersionId);
                        int index = current is null ? -1 : resource.Versions.IndexOf(current);
                        if (index < 0)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState, "A committed input has no retained Version identity.");
                        }
                        WotResource recoveredResource = resource.With(
                            versions: resource.Versions.SetItem(index, version),
                            defaultVersionId: version.VersionId, desiredVersionId: version.VersionId, enabled: true);
                        resources = resources.SetItem(resource.ResourceId, recoveredResource);
                    }
                    original = original.WithGroup(group.WithResources(resources, group.Epoch), original.Generation);
                }
                foreach (WotResource input in retainedInputs.Values)
                {
                    WotResource? current = original.FindResource(input.GroupId, input.ResourceId);
                    if (current is null || current.Kind != input.Kind || current.SourceId != input.SourceId ||
                        current.MetaCreatedAt != input.MetaCreatedAt)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "A committed resolution input has lost its identity.");
                    }
                    if (current.ActiveVersionId is not null)
                    {
                        if (current.CommittedVersion is not { } active ||
                            !SameCommittedInputVersion(active, input.Versions[0]))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState,
                                "A resolution input conflicts with its existing committed activation.");
                        }
                        continue;
                    }
                    WotResourceGroup group = original.FindGroup(input.GroupId)!;
                    original = original.WithGroup(group.WithResources(
                        group.Resources.SetItem(input.ResourceId, input), group.Epoch), original.Generation);
                }
            }
            ArrayOf<WotSelectedResource> selection = SelectResources(original, selectors, includeDependents);
            WotRegistrySnapshot pinned = original;
            foreach (WotSelectedResource selected in selection)
            {
                if (selected.Version is not null)
                {
                    WotResource resource = PinVersion(selected.Resource, selected.Version);
                    WotResourceGroup group = pinned.FindGroup(resource.GroupId)!;
                    pinned = pinned.WithGroup(group.WithResources(
                        group.Resources.SetItem(resource.ResourceId, resource), group.Epoch), pinned.Generation);
                }
            }
            var roots = selection.ToList()
                .Where(selected => selected.Resource.Enabled && selected.Version?.HasContent == true)
                .Select(selected => PinVersion(selected.Resource, selected.Version!)).ToArray();
            var content = ImmutableDictionary.CreateBuilder<string, ByteString>(StringComparer.Ordinal);
            var leases = new List<IWotRegistryVersionLease>();
            var acquired = new HashSet<Guid>();
            bool transferred = false;
            try
            {
                async ValueTask<ByteString> ReadAsync(WotResourceVersion version, CancellationToken token)
                {
                    WotResource owner = pinned.AllResources().Single(resource =>
                        resource.Versions.Any(candidate => candidate.IncarnationId == version.IncarnationId));
                    if (committedInputs && owner.ActiveVersionId is null && !retainedInputs.ContainsKey(owner.Xid))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "A resolution dependency has no retained committed input.");
                    }
                    if (registry is IWotRegistryVersionLeaseProvider provider && acquired.Add(version.IncarnationId))
                    {
                        IWotRegistryVersionLease lease = await provider.AcquireVersionLeaseAsync(
                            owner.GroupId, owner.ResourceId, version, token).ConfigureAwait(false);
                        leases.Add(lease);
                        if (lease.Version.Epoch != version.Epoch ||
                            !WotContentDigest.Equal(lease.Version.Digest, version.Digest))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState, "The exact Version changed before input acquisition.");
                        }
                    }
                    ByteString bytes = await registry.ReadContentAsync(version, token).ConfigureAwait(false);
                    if (bytes.IsNull || bytes.Length != version.ContentLength ||
                        !WotContentDigest.Equal(WotContentDigest.Compute(bytes), version.Digest))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "Acquired bytes do not match the captured exact Version.");
                    }
                    ByteString captured = ByteString.From(bytes.Span.ToArray());
                    content[version.DigestHex] = captured;
                    return captured;
                }

                ImmutableArray<WotDependencyClosure> closures = await BuildClosuresAsync(
                    pinned, roots, maxJsonDepth, ReadAsync, replacementClosures, cancellationToken)
                    .ConfigureAwait(false);
                var groups = ImmutableDictionary.CreateBuilder<string, WotResourceGroup>(StringComparer.Ordinal);
                foreach (WotResource resource in closures.SelectMany(closure => closure.Members))
                {
                    if (!groups.TryGetValue(resource.GroupId, out WotResourceGroup? group))
                    {
                        group = original.FindGroup(resource.GroupId)!.WithResources(
                            ImmutableDictionary<string, WotResource>.Empty,
                            original.FindGroup(resource.GroupId)!.Epoch);
                    }
                    groups[resource.GroupId] = group.WithResources(
                        group.Resources.SetItem(resource.ResourceId, resource), group.Epoch);
                }
                var capturedRegistry = new WotRegistrySnapshot(
                    original.Generation, groups.ToImmutable(), original.Labels);
                var result = new WotMaterializationSnapshot(
                    capturedRegistry, selection, selectors.IsEmpty, closures.ToArrayOf(),
                    content.ToImmutable(), [.. leases]);
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred)
                {
                    foreach (IWotRegistryVersionLease lease in leases)
                    {
                        lease.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Applies Kind and exact entity identity, then the optional reverse dependency closure,
        /// using only the registry's immutable metadata.
        /// </summary>
        public static ArrayOf<WotSelectedResource> SelectResources(
            WotRegistrySnapshot snapshot,
            ArrayOf<WoTResourceSelectorDataType> selectors,
            bool includeDependents = false)
        {
            _ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            var selected = new Dictionary<string, WotSelectedResource>(StringComparer.Ordinal);
            if (selectors.IsEmpty)
            {
                foreach (WotResource resource in snapshot.AllResources())
                {
                    if (resource.Enabled && resource.DefaultVersion?.HasContent == true)
                    {
                        selected.Add(resource.Xid, new WotSelectedResource(
                            resource, resource.DefaultVersion, resource.Xid));
                    }
                }
            }
            else
            {
                foreach (WoTResourceSelectorDataType selector in selectors)
                {
                    if (selector is null || selector.Kind is not (WoTDocumentKindEnum.ThingDescription or
                        WoTDocumentKindEnum.ThingModel or WoTDocumentKindEnum.All))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid selector Kind.");
                    }
                    foreach (WotResource resource in snapshot.AllResources())
                    {
                        if (selector.Kind != WoTDocumentKindEnum.All && selector.Kind != resource.Kind)
                        {
                            continue;
                        }
                        WotResourceVersion? version = resource.DefaultVersion;
                        string resultXid = resource.Xid;
                        if (!string.IsNullOrEmpty(selector.Xid))
                        {
                            if (selector.Xid != resource.Xid &&
                                selector.Xid != snapshot.FindGroup(resource.GroupId)!.Xid)
                            {
                                version = resource.Versions.FirstOrDefault(candidate =>
                                    VersionXid(resource, candidate) == selector.Xid);
                                if (version is null)
                                {
                                    continue;
                                }
                                resultXid = selector.Xid;
                            }
                        }
                        else
                        {
                            if ((!string.IsNullOrEmpty(selector.GroupId) && selector.GroupId != resource.GroupId) ||
                                (!string.IsNullOrEmpty(selector.ResourceId) &&
                                    selector.ResourceId != resource.ResourceId))
                            {
                                continue;
                            }
                            if (!string.IsNullOrEmpty(selector.VersionId))
                            {
                                version = resource.FindVersion(selector.VersionId);
                                if (version is null)
                                {
                                    continue;
                                }
                            }
                        }
                        if (selected.TryGetValue(resource.Xid, out WotSelectedResource? previous) &&
                            previous.Version?.VersionId != version?.VersionId)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidArgument,
                                "Selection names incompatible Versions of one Resource.");
                        }
                        selected[resource.Xid] = new WotSelectedResource(resource, version, resultXid);
                    }
                }
            }
            if (includeDependents && selected.Count != 0)
            {
                foreach (WotResource resource in snapshot.AllResources())
                {
                    if (resource.DefaultVersion is { HasContent: true, Dependencies: null })
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            "IncludeDependents requires an authoritative dependency metadata index.");
                    }
                }
                bool changed;
                do
                {
                    changed = false;
                    foreach (WotResource resource in snapshot.AllResources())
                    {
                        if (selected.ContainsKey(resource.Xid) || !resource.Enabled ||
                            resource.DefaultVersion?.Dependencies is not { } metadata)
                        {
                            continue;
                        }
                        if (metadata.References.Contains(reference =>
                            ResolveReference(snapshot, resource, reference) is { } target &&
                            selected.ContainsKey(target.Xid)))
                        {
                            selected.Add(resource.Xid, new WotSelectedResource(
                                resource, resource.DefaultVersion, resource.Xid));
                            changed = true;
                        }
                    }
                }
                while (changed);
            }
            return selected.Values.OrderBy(selection => selection.Resource.Xid, StringComparer.Ordinal).ToArrayOf();
        }

        internal static string VersionXid(WotResource resource, WotResourceVersion version)
        {
            return resource.Xid + "/versions/" + version.VersionId;
        }

        private static bool SameCommittedInputVersion(WotResourceVersion left, WotResourceVersion right)
        {
            return left.VersionId == right.VersionId && left.CreatedAt == right.CreatedAt &&
                left.Epoch == right.Epoch && left.ContentLength == right.ContentLength &&
                left.Format == right.Format && left.ContentType == right.ContentType &&
                WotContentDigest.Equal(left.Digest, right.Digest);
        }

        private static WotResource PinVersion(WotResource resource, WotResourceVersion version)
        {
            return resource.With(defaultVersionId: version.VersionId, desiredVersionId: version.VersionId);
        }

        private static WotResource? ResolveReference(
            WotRegistrySnapshot snapshot, WotResource source, WotResourceReference reference)
        {
            string raw = TrimFragment(reference.TargetUri);
            WotResource? target = raw.IndexOfAny([':', '/', '\\']) < 0
                ? snapshot.FindResource(source.GroupId, raw)
                : null;
            target ??= Resolve(snapshot, raw);
            target ??= Resolve(snapshot, reference.LookupUri);
            if (target is not null)
            {
                WotResourceVersion? exact = target.Versions.FirstOrDefault(version =>
                    VersionXid(target, version) == raw || VersionXid(target, version) == reference.LookupUri);
                if (exact is not null)
                {
                    return PinVersion(target, exact);
                }
            }
            return target;
        }
    }
}
