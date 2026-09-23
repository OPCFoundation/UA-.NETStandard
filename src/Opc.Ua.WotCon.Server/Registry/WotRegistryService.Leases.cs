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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService
    {
        /// <inheritdoc/>
        public async ValueTask<IWotRegistryVersionLease> AcquireVersionLeaseAsync(
            string groupId,
            string resourceId,
            WotResourceVersion version,
            CancellationToken cancellationToken = default)
        {
            _ = version ?? throw new ArgumentNullException(nameof(version));
            groupId = ResolveAssignedGroupId(groupId);
            resourceId = ResolveAssignedResourceId(groupId, resourceId);
            await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureReadableGeneration();
                WotResource? resource = m_snapshot.FindResource(groupId, resourceId);
                WotResourceVersion? current = resource?.FindVersion(version.VersionId);
                if ((current is null || current.IncarnationId != version.IncarnationId || current.Epoch != version.Epoch ||
                    !WotContentDigest.Equal(current.Digest, version.Digest)) &&
                    resource?.CommittedVersion is { } committed &&
                    committed.VersionId == version.VersionId &&
                    committed.IncarnationId == version.IncarnationId &&
                    committed.Epoch == version.Epoch &&
                    WotContentDigest.Equal(committed.Digest, version.Digest))
                {
                    current = committed;
                }
                if (current is null || current.IncarnationId != version.IncarnationId ||
                    current.Epoch != version.Epoch || !WotContentDigest.Equal(current.Digest, version.Digest))
                {
                    WotResourceVersion? retained = FindCommittedInput(groupId, resourceId, version);
                    if (retained is not null)
                    {
                        current = retained;
                    }
                }
                if (current is null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "The Version no longer exists.");
                }
                if (current.IncarnationId != version.IncarnationId)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The Version incarnation changed.");
                }
                return AcquireVersionLease(current);
            }
            finally
            {
                m_mutex.Release();
            }
        }

        private WotResourceVersion? FindCommittedInput(
            string groupId, string resourceId, WotResourceVersion expected)
        {
            foreach (WotResource owner in m_snapshot.AllResources())
            {
                foreach (WotResource input in owner.CommittedInputs)
                {
                    WotResourceVersion version = input.Versions[0];
                    if (input.GroupId == groupId && input.ResourceId == resourceId &&
                        version.VersionId == expected.VersionId && version.IncarnationId == expected.IncarnationId &&
                        version.Epoch == expected.Epoch && WotContentDigest.Equal(version.Digest, expected.Digest))
                    {
                        return version;
                    }
                }
            }
            return null;
        }

        private async ValueTask PrepareVersionLeaseAsync(
            WotResource resource,
            WotResourceVersion version,
            Func<WotResource, WotResourceVersion, IWotRegistryVersionLease, CancellationToken, ValueTask> prepare,
            CancellationToken cancellationToken)
        {
            VersionLease? lease = AcquireVersionLease(version);
            try
            {
                await prepare(resource, version, lease, cancellationToken).ConfigureAwait(false);
                lease = null;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        private VersionLease AcquireVersionLease(WotResourceVersion version)
        {
            while (true)
            {
                if (m_versionLeases.TryGetValue(version.IncarnationId, out VersionLeaseCount? existing))
                {
                    if (existing.TryAcquire())
                    {
                        return new VersionLease(this, version, existing);
                    }
                    RemoveReleasedLease(version.IncarnationId, existing);
                }
                var created = new VersionLeaseCount();
                if (m_versionLeases.TryAdd(version.IncarnationId, created))
                {
                    return new VersionLease(this, version, created);
                }
            }
        }

        private bool IsVersionLeased(WotResourceVersion version)
        {
            return m_versionLeases.TryGetValue(version.IncarnationId, out VersionLeaseCount? leases) &&
                leases.Count > 0;
        }

        private static WotRegistrySnapshot RestoreVersionIncarnations(
            WotRegistrySnapshot loaded,
            WotRegistrySnapshot known)
        {
            if (ReferenceEquals(loaded, known))
            {
                return loaded;
            }
            foreach (WotResourceGroup group in loaded.Groups.Values)
            {
                ImmutableDictionary<string, WotResource> resources = group.Resources;
                foreach (WotResource resource in group.Resources.Values)
                {
                    WotResource? previous = known.FindResource(group.GroupId, resource.ResourceId);
                    if (previous is null ||
                        previous.Kind != resource.Kind ||
                        previous.MetaCreatedAt != resource.MetaCreatedAt ||
                        !string.Equals(previous.SourceId, resource.SourceId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    ImmutableArray<WotResourceVersion>.Builder? versions = null;
                    for (int i = 0; i < resource.Versions.Length; i++)
                    {
                        WotResourceVersion version = resource.Versions[i];
                        WotResourceVersion? original = previous.FindVersion(version.VersionId);
                        if (original is not null &&
                            original.CreatedAt == version.CreatedAt &&
                            original.IncarnationId != version.IncarnationId)
                        {
                            // Restore only identities from the owner's known lifecycle.
                            // Metadata and content updates do not create a new incarnation.
                            versions ??= resource.Versions.ToBuilder();
                            versions[i] = version.With(incarnationId: original.IncarnationId);
                        }
                    }
                    if (versions is not null)
                    {
                        resources = resources.SetItem(
                            resource.ResourceId,
                            resource.With(versions: versions.ToImmutable()));
                    }
                }
                if (!ReferenceEquals(resources, group.Resources))
                {
                    loaded = loaded.WithGroup(group.WithResources(resources, group.Epoch), loaded.Generation);
                }
            }
            return loaded;
        }

        private void RemoveReleasedLease(Guid incarnation, VersionLeaseCount leases)
        {
            ((ICollection<KeyValuePair<Guid, VersionLeaseCount>>)m_versionLeases)
                .Remove(new KeyValuePair<Guid, VersionLeaseCount>(incarnation, leases));
        }

        private sealed class VersionLease(
            WotRegistryService owner,
            WotResourceVersion version,
            VersionLeaseCount leases) : IWotRegistryVersionLease
        {
            public WotResourceVersion Version { get; } = version;

            public void Dispose()
            {
                VersionLeaseCount? released = Interlocked.Exchange(ref m_leases, null);
                if (released is not null && released.Release() == 0)
                {
                    owner.RemoveReleasedLease(Version.IncarnationId, released);
                }
            }

            private VersionLeaseCount? m_leases = leases;
        }

        private sealed class VersionLeaseCount
        {
            public int Count => Volatile.Read(ref m_count);

            public bool TryAcquire()
            {
                int count = Count;
                while (count > 0)
                {
                    int observed = Interlocked.CompareExchange(ref m_count, checked(count + 1), count);
                    if (observed == count)
                    {
                        return true;
                    }
                    count = observed;
                }
                return false;
            }

            public int Release()
            {
                return Interlocked.Decrement(ref m_count);
            }

            private int m_count = 1;
        }

        private readonly ConcurrentDictionary<Guid, VersionLeaseCount> m_versionLeases = new();
    }
}
