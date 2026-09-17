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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class FileWotRegistryStore
    {
        /// <inheritdoc/>
        public bool SupportsPreparedCommits =>
            Volatile.Read(ref m_disposed) == 0 &&
            ResourceStore is IWotRegistryContentLeaseProvider { SupportsImmutableContentLeases: true };

        /// <inheritdoc/>
        public async ValueTask<IWotRegistryValidatedGeneration> CaptureValidatedGenerationAsync(
            CancellationToken cancellationToken = default)
        {
            EnsurePreparedCapability();
            using StorageLock storage = await AcquireStorageLockAsync(cancellationToken, writeIntent: false)
                .ConfigureAwait(false);
            if (m_expectedManifest is not { } expected || m_expectedGeneration is null)
            {
                throw new InvalidOperationException("LoadAsync must succeed before capturing validated input.");
            }
            if (m_validatedGeneration is { } cached &&
                cached.TryGetTarget(out CapturedGeneration? generation) &&
                generation.OwnerId == m_preparedOwnerId &&
                generation.CaptureRevision == Volatile.Read(ref m_captureRevision) &&
                generation.Stamp.Equals(expected) &&
                generation.TryRetain())
            {
                try
                {
                    await ReadCapturedGenerationAsync(generation, cancellationToken).ConfigureAwait(false);
                    return new GenerationLease(generation);
                }
                catch
                {
                    generation.Dispose();
                    throw;
                }
            }

            LoadedGeneration? loaded = await ReadGenerationAsync(cancellationToken).ConfigureAwait(false);
            ManifestStamp actual = loaded?.Stamp ?? ManifestStamp.Absent;
            if (!actual.Equals(expected))
            {
                throw new InvalidOperationException("The registry changed before validated input was captured.");
            }
            if (loaded is null)
            {
                string[] artifacts = FindRecoveryArtifacts();
                if (artifacts.Length != 0)
                {
                    throw CreateRecoveryArtifactsException(artifacts);
                }
            }
            CapturedGeneration captured = await CaptureGenerationAsync(
                loaded?.Snapshot ?? WotRegistrySnapshot.Empty,
                actual,
                loaded?.ManifestBytes ?? [],
                previous: null,
                cancellationToken).ConfigureAwait(false);
            m_validatedGeneration = new WeakReference<CapturedGeneration>(captured);
            return new GenerationLease(captured);
        }

        /// <inheritdoc/>
        public async ValueTask<IWotRegistryPreparedCommit> PrepareCommitAsync(
            WotRegistrySnapshot intendedSnapshot,
            IWotRegistryValidatedGeneration expectedGeneration,
            WotRegistryCommitScope scope,
            CancellationToken cancellationToken = default)
        {
            _ = intendedSnapshot ?? throw new ArgumentNullException(nameof(intendedSnapshot));
            _ = expectedGeneration ?? throw new ArgumentNullException(nameof(expectedGeneration));
            if (scope is not (WotRegistryCommitScope.Full or WotRegistryCommitScope.ProjectionMetadata))
            {
                throw new ArgumentOutOfRangeException(nameof(scope));
            }
            EnsurePreparedCapability();
            if (expectedGeneration is not GenerationLease lease)
            {
                throw new ArgumentException("Validated input belongs to another store.", nameof(expectedGeneration));
            }
            CapturedGeneration? captured = lease.RetainGeneration();
            try
            {
                ValidateCapturedOwner(captured);
                using StorageLock storage = await AcquireStorageLockAsync(cancellationToken, writeIntent: false)
                    .ConfigureAwait(false);
                await ReadCapturedGenerationAsync(captured, cancellationToken, intendedSnapshot).ConfigureAwait(false);
                if (intendedSnapshot.Generation <= captured.Snapshot.Generation)
                {
                    throw new InvalidOperationException("A prepared commit must advance the captured store generation.");
                }
                if (scope == WotRegistryCommitScope.ProjectionMetadata)
                {
                    ValidateProjectionMetadata(captured.Snapshot, intendedSnapshot);
                }
                ValidatedCommit validated = await ValidateIntendedSnapshotAsync(
                    intendedSnapshot, cancellationToken, captured.Contents).ConfigureAwait(false);
                var prepared = new PreparedFileCommit(
                    intendedSnapshot, captured, validated, scope, CommitPreparedAsync);
                captured = null;
                return prepared;
            }
            finally
            {
                captured?.Dispose();
            }
        }

        internal static void ValidateProjectionMetadata(
            WotRegistrySnapshot expected,
            WotRegistrySnapshot intended)
        {
            ManifestDto original = ToManifest(expected);
            ManifestDto normalized = ToManifest(intended);
            normalized.Generation = original.Generation;
            normalized.RefreshGeneration = original.RefreshGeneration;
            normalized.CanonicalViewGraphState = original.CanonicalViewGraphState;
            GroupDto[] oldGroups = original.Groups ?? [];
            GroupDto[] newGroups = normalized.Groups ?? [];
            if (oldGroups.Length != newGroups.Length)
            {
                throw new InvalidDataException("Projection metadata cannot add or remove resource groups.");
            }
            for (int groupIndex = 0; groupIndex < oldGroups.Length; groupIndex++)
            {
                GroupDto oldGroup = oldGroups[groupIndex];
                GroupDto newGroup = newGroups[groupIndex];
                ResourceDto[] oldResources = oldGroup.Resources ?? [];
                ResourceDto[] newResources = newGroup.Resources ?? [];
                if (!string.Equals(oldGroup.GroupId, newGroup.GroupId, StringComparison.Ordinal) ||
                    oldResources.Length != newResources.Length)
                {
                    throw new InvalidDataException("Projection metadata cannot change resource membership.");
                }
                for (int resourceIndex = 0; resourceIndex < oldResources.Length; resourceIndex++)
                {
                    ResourceDto oldResource = oldResources[resourceIndex];
                    ResourceDto newResource = newResources[resourceIndex];
                    VersionDto[] oldVersions = oldResource.Versions ?? [];
                    VersionDto[] newVersions = newResource.Versions ?? [];
                    if (!string.Equals(
                            oldResource.ResourceId, newResource.ResourceId, StringComparison.Ordinal) ||
                        oldVersions.Length != newVersions.Length)
                    {
                        throw new InvalidDataException("Projection metadata cannot change Version membership.");
                    }
                    for (int versionIndex = 0; versionIndex < oldVersions.Length; versionIndex++)
                    {
                        if (!string.Equals(
                            oldVersions[versionIndex].VersionId,
                            newVersions[versionIndex].VersionId,
                            StringComparison.Ordinal))
                        {
                            throw new InvalidDataException("Projection metadata cannot change Version identities.");
                        }
                        newVersions[versionIndex].Validation = oldVersions[versionIndex].Validation;
                    }
                    newResource.ActiveVersionId = oldResource.ActiveVersionId;
                    newResource.LoadState = oldResource.LoadState;
                    newResource.RefreshGeneration = oldResource.RefreshGeneration;
                    newResource.LastRefreshTime = oldResource.LastRefreshTime;
                    newResource.MaterializedNodeCount = oldResource.MaterializedNodeCount;
                    newResource.RootNodeId = oldResource.RootNodeId;
                    newResource.Diagnostics = oldResource.Diagnostics;
                    newResource.Validation = oldResource.Validation;
                }
            }
            byte[] originalBytes = JsonSerializer.SerializeToUtf8Bytes(
                original, WotRegistryStoreJson.Default.ManifestDto);
            byte[] normalizedBytes = JsonSerializer.SerializeToUtf8Bytes(
                normalized, WotRegistryStoreJson.Default.ManifestDto);
            if (!originalBytes.AsSpan().SequenceEqual(normalizedBytes))
            {
                throw new InvalidDataException(
                    "Projection metadata cannot change authoritative identities, content, labels, or entity epochs.");
            }
        }

        private void EnsurePreparedCapability()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(FileWotRegistryStore));
            }
            if (!SupportsPreparedCommits)
            {
                throw new NotSupportedException(
                    "The resource provider cannot establish authoritative immutable content leases.");
            }
        }

        private void ValidateCapturedOwner(CapturedGeneration captured)
        {
            EnsurePreparedCapability();
            if (captured.OwnerId != m_preparedOwnerId)
            {
                throw new ArgumentException("Validated input belongs to another store.");
            }
            if (captured.CaptureRevision != Volatile.Read(ref m_captureRevision))
            {
                throw new InvalidOperationException("Validated input was invalidated by a store reload.");
            }
        }

        private async ValueTask<LoadedGeneration?> ReadCapturedGenerationAsync(
            CapturedGeneration captured,
            CancellationToken cancellationToken,
            WotRegistrySnapshot? intendedSnapshot = null)
        {
            ValidateCapturedOwner(captured);
            if (m_expectedManifest is not { } expected ||
                !expected.Equals(captured.Stamp) ||
                m_expectedGeneration != captured.Snapshot.Generation)
            {
                throw new InvalidOperationException("The captured store generation is stale.");
            }
            byte[] bytes;
            try
            {
                bytes = await ReadAllBytesAsync(
                    Path.Combine(m_root, ManifestFile), cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException) when (!captured.Stamp.Exists)
            {
                string[] artifacts = FindRecoveryArtifacts();
                if (artifacts.Length != 0)
                {
                    InvalidDataException failure = CreateRecoveryArtifactsException(artifacts);
                    if (intendedSnapshot is not null)
                    {
                        m_expectedManifest = null;
                        m_expectedGeneration = null;
                        InvalidatePreparedInput();
                        throw new WotRegistryCommitIndeterminateException(
                            intendedSnapshot,
                            new InvalidOperationException(
                                "The commit was not attempted because the expected absent primary state is indeterminate."),
                            failure);
                    }
                    throw failure;
                }
                return null;
            }
            if (!captured.Stamp.Exists ||
                !bytes.AsSpan().SequenceEqual(captured.ManifestBytes))
            {
                throw new InvalidOperationException(
                    "The on-disk WoT registry changed after this store loaded it. " +
                    $"Expected {captured.Stamp}; found manifest SHA-256 " +
                    $"'{WotContentDigest.ToHex(WotContentDigest.Compute(bytes))}'. Reload before retrying.");
            }
            return new LoadedGeneration(captured.Snapshot, captured.Stamp, bytes);
        }

        private async ValueTask<CapturedGeneration> CaptureGenerationAsync(
            WotRegistrySnapshot snapshot,
            ManifestStamp stamp,
            byte[] manifestBytes,
            CapturedGeneration? previous,
            CancellationToken cancellationToken)
        {
            EnsurePreparedCapability();
            var provider = (IWotRegistryContentLeaseProvider)ResourceStore;
            Dictionary<string, ContentEvidence>? contents = new(StringComparer.Ordinal);
            try
            {
                foreach (WotResource resource in snapshot.AllResources())
                {
                    foreach (WotResourceVersion version in resource.Versions)
                    {
                        if (!version.HasContent)
                        {
                            continue;
                        }
                        string key = version.DigestHex;
                        if (contents.TryGetValue(key, out ContentEvidence? existing))
                        {
                            if (existing.ContentLength != version.ContentLength)
                            {
                                throw new InvalidDataException("A digest identifies conflicting content lengths.");
                            }
                            continue;
                        }
                        if (previous is not null &&
                            previous.Contents.TryGetValue(key, out ContentEvidence? retained) &&
                            retained.ContentLength == version.ContentLength &&
                            retained.TryRetain())
                        {
                            contents.Add(key, retained);
                            continue;
                        }
                        IWotRegistryContentLease? lease = await provider.AcquireContentLeaseAsync(
                            key, cancellationToken).ConfigureAwait(false);
                        try
                        {
                            if (!string.Equals(lease.ResourceKey, key, StringComparison.Ordinal) ||
                                lease.ContentLength != version.ContentLength)
                            {
                                throw new InvalidDataException("The content lease does not match its manifest entry.");
                            }
                            await VerifyBlobContentsAsync(
                                key, "captured immutable generation", lease.ContentLength,
                                lease.ReadAsync, cancellationToken).ConfigureAwait(false);
                            contents.Add(key, new ContentEvidence(lease));
                            lease = null;
                        }
                        finally
                        {
                            lease?.Dispose();
                        }
                    }
                }
                var captured = new CapturedGeneration(
                    m_preparedOwnerId, Volatile.Read(ref m_captureRevision),
                    snapshot, stamp, manifestBytes, contents);
                contents = null;
                return captured;
            }
            finally
            {
                if (contents is not null)
                {
                    foreach (ContentEvidence content in contents.Values)
                    {
                        content.Dispose();
                    }
                }
            }
        }

        private ValueTask CommitPreparedAsync(
            PreparedFileCommit prepared,
            CancellationToken cancellationToken)
        {
            ValidateCapturedOwner(prepared.Expected);
            return CommitCoreAsync(prepared.IntendedSnapshot, cancellationToken, prepared);
        }

        private void RetainCommittedGeneration(
            PreparedFileCommit? prepared,
            CapturedGeneration? generation)
        {
            if (prepared is not null && generation is not null)
            {
                prepared.CommittedGeneration = generation;
                m_validatedGeneration = new WeakReference<CapturedGeneration>(generation);
            }
        }

        private void InvalidatePreparedInput()
        {
            Interlocked.Increment(ref m_captureRevision);
            m_validatedGeneration = null;
        }

        private sealed class ContentEvidence(IWotRegistryContentLease lease) : IDisposable
        {
            public long ContentLength => lease.ContentLength;

            public bool TryRetain()
            {
                int count = Volatile.Read(ref m_references);
                while (count > 0)
                {
                    int observed = Interlocked.CompareExchange(ref m_references, checked(count + 1), count);
                    if (observed == count)
                    {
                        return true;
                    }
                    count = observed;
                }
                return false;
            }

            public void Dispose()
            {
                if (Interlocked.Decrement(ref m_references) == 0)
                {
                    lease.Dispose();
                }
            }

            private int m_references = 1;
        }

        private sealed class CapturedGeneration(
            Guid ownerId,
            long captureRevision,
            WotRegistrySnapshot snapshot,
            ManifestStamp stamp,
            byte[] manifestBytes,
            Dictionary<string, ContentEvidence> contents) : IDisposable
        {
            public Guid OwnerId { get; } = ownerId;
            public long CaptureRevision { get; } = captureRevision;
            public WotRegistrySnapshot Snapshot { get; } = snapshot;
            public ManifestStamp Stamp { get; } = stamp;
            public byte[] ManifestBytes { get; } = manifestBytes;
            public IReadOnlyDictionary<string, ContentEvidence> Contents { get; } = contents;

            public bool TryRetain()
            {
                int count = Volatile.Read(ref m_references);
                while (count > 0)
                {
                    int observed = Interlocked.CompareExchange(ref m_references, checked(count + 1), count);
                    if (observed == count)
                    {
                        return true;
                    }
                    count = observed;
                }
                return false;
            }

            public void Dispose()
            {
                if (Interlocked.Decrement(ref m_references) == 0)
                {
                    foreach (ContentEvidence content in Contents.Values)
                    {
                        content.Dispose();
                    }
                }
            }

            private int m_references = 1;
        }

        private sealed class GenerationLease(CapturedGeneration generation) : IWotRegistryValidatedGeneration
        {
            public WotRegistrySnapshot Snapshot => Volatile.Read(ref m_disposed) == 0
                ? generation.Snapshot
                : throw new ObjectDisposedException(nameof(GenerationLease));

            public CapturedGeneration RetainGeneration()
            {
                if (Volatile.Read(ref m_disposed) != 0 || !generation.TryRetain())
                {
                    throw new ObjectDisposedException(nameof(GenerationLease));
                }
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    generation.Dispose();
                    throw new ObjectDisposedException(nameof(GenerationLease));
                }
                return generation;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    generation.Dispose();
                }
            }

            private int m_disposed;
        }

        private sealed class PreparedFileCommit(
            WotRegistrySnapshot intendedSnapshot,
            CapturedGeneration expected,
            ValidatedCommit validated,
            WotRegistryCommitScope scope,
            Func<PreparedFileCommit, CancellationToken, ValueTask> commit) : IWotRegistryPreparedCommit
        {
            public WotRegistrySnapshot IntendedSnapshot { get; } = intendedSnapshot;
            public CapturedGeneration Expected { get; } = expected;
            public ValidatedCommit Validated { get; } = validated;
            public WotRegistryCommitScope Scope { get; } = scope;
            public CapturedGeneration? CommittedGeneration { get; set; }

            public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
            {
                if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The prepared store commit has already been consumed.");
                }
                try
                {
                    await commit(this, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref m_state, 2);
                    m_finished.TrySetResult(true);
                }
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref m_state, 3, 0) == 1)
                {
                    await m_finished.Task.ConfigureAwait(false);
                }
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    Expected.Dispose();
                    CommittedGeneration?.Dispose();
                }
            }

            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_state;
            private int m_disposed;
        }

        private readonly Guid m_preparedOwnerId = Guid.NewGuid();
        private WeakReference<CapturedGeneration>? m_validatedGeneration;
        private long m_captureRevision;
        private int m_disposed;
    }
}
