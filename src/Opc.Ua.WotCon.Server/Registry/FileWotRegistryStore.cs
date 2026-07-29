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
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A durable, file-backed registry store. A storage-root lock serializes loads
    /// and commits across store instances and processes. Every load validates the
    /// primary manifest and all content-addressed blobs; any failure other than an
    /// absent primary manifest with no store-owned recovery artifacts fails closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="CommitAsync"/> revalidates the expected on-disk generation and
    /// requires a strictly newer snapshot under the lock. It flushes new blobs and
    /// synchronizes the blobs and root directory entries before atomically switching
    /// <c>manifest.json</c>, then synchronizes the storage root again. A stale store
    /// instance is rejected before it can stage blobs or replace the primary manifest.
    /// A confirmed pre-switch failure on a proven-pristine store rolls back only the
    /// artifacts created by that attempt and durably synchronizes their removal.
    /// Every lock acquisition also resynchronizes the root path component parents so
    /// a partially completed multi-level root creation is retry-safe.
    /// A reported manifest-replacement failure is classified only after validating
    /// the primary, staged manifest, recovery backup, and all referenced blobs.
    /// </para>
    /// <para>
    /// A <c>manifest.json.bak</c> left by an operator or older implementation is
    /// never loaded or overwritten automatically. Atomic replacement uses a unique
    /// recovery backup and preserves staged artifacts whenever replacement reports
    /// failure. Blobs are not pruned automatically, so operator recovery evidence
    /// cannot lose referenced content.
    /// </para>
    /// </remarks>
    public sealed class FileWotRegistryStore : IWotRegistryStore
    {
        /// <summary>
        /// Initializes a new file-backed store rooted at <paramref name="rootFolder"/>.
        /// </summary>
        public FileWotRegistryStore(string rootFolder)
            : this(
                rootFolder,
                directorySyncFailureInjector: null,
                manifestReplace: null)
        {
        }

        /// <summary>
        /// Initializes a new file-backed store that keeps the manifest itself but delegates the
        /// document bytes to <paramref name="resourceStore"/>.
        /// <para>
        /// Substituting the byte layer is what lets a WoT registry run in a high-availability or
        /// distributed deployment: the documents then live in a store every node can reach. The
        /// manifest is still written and switched atomically by this class, which is safe because
        /// blobs are content-addressed and therefore immutable — a document is always written
        /// before the manifest that references it, so an interrupted commit can leave an orphaned
        /// document but never a dangling reference.
        /// </para>
        /// <para>
        /// When a store is supplied it owns the durability of the document bytes; the directory
        /// fsync this class performs for its own <c>blobs</c> folder does not apply to it.
        /// </para>
        /// </summary>
        /// <param name="rootFolder">The registry folder that holds the manifest.</param>
        /// <param name="resourceStore">The store that holds the document bytes.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resourceStore"/> is <c>null</c>.</exception>
        public FileWotRegistryStore(string rootFolder, IXRegistryResourceStore resourceStore)
            : this(
                rootFolder,
                directorySyncFailureInjector: null,
                manifestReplace: null,
                resourceStore: resourceStore ?? throw new ArgumentNullException(nameof(resourceStore)))
        {
        }

        internal FileWotRegistryStore(
            string rootFolder,
            Action<DirectorySyncPhase>? directorySyncFailureInjector,
            Action<string, string, string>? manifestReplace = null,
            IXRegistryResourceStore? resourceStore = null)
        {
            m_root = Path.GetFullPath(
                rootFolder ?? throw new ArgumentNullException(nameof(rootFolder)));
            m_blobsFolder = Path.Combine(m_root, "blobs");
            m_lockPath = Path.Combine(m_root, LockFile);
            m_resourceStore = resourceStore;
            m_directorySyncFailureInjector = directorySyncFailureInjector;
            m_manifestReplace = manifestReplace;
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistrySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            m_expectedManifest = null;
            m_expectedGeneration = null;
            using StorageLock storageLock = await AcquireStorageLockAsync(cancellationToken)
                .ConfigureAwait(false);
            LoadedGeneration? loaded = await ReadGenerationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (loaded is null)
            {
                string[] recoveryArtifacts = FindRecoveryArtifacts();
                if (recoveryArtifacts.Length != 0)
                {
                    throw CreateRecoveryArtifactsException(recoveryArtifacts);
                }
                m_expectedManifest = ManifestStamp.Absent;
                m_expectedGeneration = WotRegistrySnapshot.Empty.Generation;
                return WotRegistrySnapshot.Empty;
            }

            m_expectedManifest = loaded.Stamp;
            m_expectedGeneration = loaded.Snapshot.Generation;
            return loaded.Snapshot;
        }

        private string[] FindRecoveryArtifacts()
        {
            try
            {
                return Directory.GetFileSystemEntries(m_root)
                    .Where(path =>
                    {
                        string name = Path.GetFileName(path);
                        return string.Equals(name, "blobs", StringComparison.Ordinal) ||
                            string.Equals(name, ManifestFile, StringComparison.Ordinal) ||
                            name.StartsWith(ManifestFile + ".", StringComparison.Ordinal);
                    })
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Access to WoT registry root '{m_root}' was denied while checking " +
                    "for recovery artifacts.",
                    ex);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Unable to inspect WoT registry root '{m_root}' for recovery artifacts.",
                    ex);
            }
        }

        private static InvalidDataException CreateRecoveryArtifactsException(
            string[] recoveryArtifacts)
        {
            return new InvalidDataException(
                "The WoT registry primary manifest is absent, but store-owned " +
                "recovery artifacts indicate prior or staged state: " +
                $"{string.Join(", ", recoveryArtifacts)}. Operator recovery " +
                "is required; the registry cannot be treated as empty.");
        }

        /// <inheritdoc/>
        public async ValueTask CommitAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            snapshot ??= WotRegistrySnapshot.Empty;
            ValidatedCommit intended = await ValidateIntendedSnapshotAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            using StorageLock storageLock = await AcquireStorageLockAsync(cancellationToken)
                .ConfigureAwait(false);
            if (m_expectedManifest is not { } expected ||
                m_expectedGeneration is not { } expectedGeneration)
            {
                throw new InvalidOperationException(
                    "LoadAsync must complete successfully before this file-backed " +
                    "registry store can commit.");
            }

            PristineCommitArtifacts? pristineArtifacts = null;
            LoadedGeneration? current = await ReadGenerationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current is null && !expected.Exists)
            {
                string[] recoveryArtifacts = FindRecoveryArtifacts();
                if (recoveryArtifacts.Length != 0)
                {
                    m_expectedManifest = null;
                    m_expectedGeneration = null;
                    InvalidDataException validationFailure =
                        CreateRecoveryArtifactsException(recoveryArtifacts);
                    throw new WotRegistryCommitIndeterminateException(
                        snapshot,
                        new InvalidOperationException(
                            "The commit was not attempted because the expected absent " +
                            "primary manifest state is indeterminate."),
                        validationFailure);
                }
                pristineArtifacts = new PristineCommitArtifacts();
            }
            ManifestStamp actual = current?.Stamp ?? ManifestStamp.Absent;
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException(
                    $"The on-disk WoT registry changed after this store loaded it. " +
                    $"Expected {expected}; found {actual}. Reload before retrying.");
            }
            if (snapshot.Generation <= expectedGeneration)
            {
                throw new InvalidOperationException(
                    $"WoT registry snapshot generation {snapshot.Generation} must be " +
                    $"strictly greater than the loaded generation {expectedGeneration}.");
            }
            try
            {
                pristineArtifacts?.ClaimBlobsDirectory();
                Directory.CreateDirectory(m_blobsFolder);

                // 1. Stage every referenced version blob durably before the manifest
                // that points at it is switched in. Blobs are content-addressed, so an
                // unchanged document is written at most once and shared across
                // versions/resources.
                foreach (KeyValuePair<string, byte[]> blob in intended.Blobs)
                {
                    if (m_resourceStore is not null)
                    {
                        // An injected store owns the durability of the bytes it holds, so the
                        // directory fsync below does not apply to it. Content addressing makes
                        // matching blobs immutable, so never rewrite one that already verifies.
                        if (!await ResourceStoreBlobMatchesAsync(
                                m_resourceStore, blob.Key, blob.Value, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            await m_resourceStore
                                .WriteAsync(blob.Key, 0, ByteString.From(blob.Value), cancellationToken)
                                .ConfigureAwait(false);
                        }
                        continue;
                    }
                    await EnsureBlobAsync(
                            BlobPath(blob.Key),
                            blob.Value,
                            blob.Key,
                            pristineArtifacts,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                SyncDirectory(m_blobsFolder, DirectorySyncPhase.BlobsBeforeManifest);
                SyncDirectory(m_root, DirectorySyncPhase.RootBeforeManifest);
            }
            catch (Exception failure)
                when (IsConfirmedPreSwitchFailure(failure))
            {
                await RollbackPristinePreSwitchFailureAsync(
                        snapshot,
                        pristineArtifacts,
                        failure)
                    .ConfigureAwait(false);
                throw;
            }

            // 2. Durably switch the prevalidated manifest only after blob directory
            // entries are on stable storage.
            string? replaceBackupPath = await AtomicReplaceManifestAsync(
                    snapshot,
                    intended.ManifestBytes,
                    intended.Stamp,
                    expected,
                    expectedGeneration,
                    pristineArtifacts,
                    cancellationToken)
                .ConfigureAwait(false);

            try
            {
                SyncDirectory(m_root, DirectorySyncPhase.RootAfterManifest);
            }
            catch (IOException durabilityFailure)
            {
                await ResolvePostSwitchFailureAsync(
                        snapshot,
                        intended.ManifestBytes,
                        intended.Stamp,
                        durabilityFailure)
                    .ConfigureAwait(false);
            }

            m_expectedManifest = intended.Stamp;
            m_expectedGeneration = snapshot.Generation;
            if (replaceBackupPath is not null)
            {
                TryDelete(replaceBackupPath);
            }
        }

        private async ValueTask<ValidatedCommit> ValidateIntendedSnapshotAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (snapshot.Generation < 0)
            {
                throw new InvalidDataException(
                    $"WoT registry snapshot generation {snapshot.Generation} cannot be negative.");
            }

            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, WotResourceGroup> groupEntry in snapshot.Groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotResourceGroup group = groupEntry.Value;
                if (!string.Equals(
                    groupEntry.Key,
                    group.GroupId,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"WoT registry snapshot group key '{groupEntry.Key}' does not " +
                        $"match group id '{group.GroupId}'.");
                }

                foreach (KeyValuePair<string, WotResource> resourceEntry in group.Resources)
                {
                    WotResource resource = resourceEntry.Value;
                    if (!string.Equals(
                        resourceEntry.Key,
                        resource.ResourceId,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"WoT registry snapshot resource key '{resourceEntry.Key}' " +
                            $"does not match resource id '{resource.ResourceId}'.");
                    }
                    if (!string.Equals(
                        resource.GroupId,
                        group.GroupId,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"WoT registry snapshot resource " +
                            $"'{resource.GroupId}/{resource.ResourceId}' does not belong " +
                            $"to group '{group.GroupId}'.");
                    }

                    foreach (WotResourceVersion version in resource.Versions)
                    {
                        byte[] content = version.Content.ToArray();
                        string digestHex = WotContentDigest.ToHex(version.Digest);
                        string actualDigest = WotContentDigest.ToHex(
                            WotContentDigest.Compute(content));
                        if (!string.Equals(
                            digestHex,
                            actualDigest,
                            StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                $"Registry snapshot version '{version.VersionId}' has " +
                                $"digest '{digestHex}', but its content hashes to " +
                                $"'{actualDigest}'.");
                        }
                        if (blobs.TryGetValue(digestHex, out byte[]? existing))
                        {
                            if (!existing.AsSpan().SequenceEqual(content))
                            {
                                throw new InvalidDataException(
                                    $"Registry snapshot digest '{digestHex}' identifies " +
                                    "different content.");
                            }
                        }
                        else
                        {
                            blobs.Add(digestHex, content);
                        }
                    }
                }
            }

            ManifestDto manifest = ToManifest(snapshot);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                WotRegistryStoreJson.Default.ManifestDto);
            ManifestDto roundTripped = JsonSerializer.Deserialize(
                    manifestBytes,
                    WotRegistryStoreJson.Default.ManifestDto) ??
                throw new InvalidDataException(
                    "The serialized WoT registry snapshot manifest was null.");
            if (roundTripped.SchemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"WoT registry snapshot manifest uses schema " +
                    $"{roundTripped.SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            WotRegistrySnapshot validated = await LoadSnapshotAsync(
                    roundTripped,
                    "intended snapshot manifest",
                    blobs,
                    cancellationToken)
                .ConfigureAwait(false);
            if (validated.Generation != snapshot.Generation)
            {
                throw new InvalidDataException(
                    $"WoT registry snapshot generation {snapshot.Generation} does not " +
                    $"cover contained epoch {validated.Generation}.");
            }

            return new ValidatedCommit(
                manifestBytes,
                new ManifestStamp(
                    exists: true,
                    roundTripped.Generation,
                    WotContentDigest.ToHex(WotContentDigest.Compute(manifestBytes))),
                blobs);
        }

        private async ValueTask ResolvePostSwitchFailureAsync(
            WotRegistrySnapshot intendedSnapshot,
            byte[] intendedManifestBytes,
            ManifestStamp intendedStamp,
            IOException durabilityFailure)
        {
            m_expectedManifest = null;
            m_expectedGeneration = null;

            LoadedGeneration actual;
            try
            {
                actual = await ReadGenerationAsync(CancellationToken.None)
                    .ConfigureAwait(false) ??
                    throw new InvalidDataException(
                        "The primary manifest is missing after its atomic switch.");
                if (actual.Snapshot.Generation != intendedSnapshot.Generation ||
                    !actual.Stamp.Equals(intendedStamp) ||
                    !actual.ManifestBytes.AsSpan().SequenceEqual(intendedManifestBytes))
                {
                    throw new InvalidDataException(
                        "The primary manifest does not exactly match the intended " +
                        "committed generation.");
                }
            }
            catch (Exception validationFailure)
                when (IsCommitValidationFailure(validationFailure))
            {
                throw new WotRegistryCommitIndeterminateException(
                    intendedSnapshot,
                    durabilityFailure,
                    validationFailure);
            }

            m_expectedManifest = actual.Stamp;
            m_expectedGeneration = actual.Snapshot.Generation;
            throw new WotRegistryCommitDurabilityUncertainException(
                actual.Snapshot,
                durabilityFailure);
        }

        private static bool IsCommitValidationFailure(Exception exception)
        {
            return exception is InvalidDataException or IOException or
                NotSupportedException or ArgumentException or InvalidOperationException or
                ServiceResultException;
        }

        private ValueTask<LoadedGeneration?> ReadGenerationAsync(
            CancellationToken cancellationToken)
        {
            return ReadGenerationAsync(
                Path.Combine(m_root, ManifestFile),
                "primary manifest",
                cancellationToken);
        }

        private async ValueTask<LoadedGeneration?> ReadGenerationAsync(
            string manifestPath,
            string manifestRole,
            CancellationToken cancellationToken)
        {
            byte[] manifestBytes;
            try
            {
                manifestBytes = await ReadAllBytesAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Unable to read WoT registry {manifestRole} '{manifestPath}'. " +
                    "The registry was left unchanged.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Access to WoT registry {manifestRole} '{manifestPath}' was denied. " +
                    "The registry was left unchanged.",
                    ex);
            }

            ManifestDto manifest;
            try
            {
                manifest = JsonSerializer.Deserialize(
                        manifestBytes, WotRegistryStoreJson.Default.ManifestDto) ??
                    throw new JsonException("The manifest root is null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"WoT registry {manifestRole} '{manifestPath}' is corrupt. " +
                    "The registry was left unchanged.",
                    ex);
            }
            if (manifest.SchemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"WoT registry {manifestRole} '{manifestPath}' uses schema " +
                    $"{manifest.SchemaVersion}; expected {CurrentSchemaVersion}. " +
                    "The registry was left unchanged.");
            }

            WotRegistrySnapshot loaded = await LoadSnapshotAsync(
                    manifest,
                    manifestRole,
                    suppliedBlobs: null,
                    cancellationToken)
                .ConfigureAwait(false);
            return new LoadedGeneration(
                loaded,
                new ManifestStamp(
                    exists: true,
                    manifest.Generation,
                    WotContentDigest.ToHex(WotContentDigest.Compute(manifestBytes))),
                manifestBytes);
        }

        private async ValueTask<WotRegistrySnapshot> LoadSnapshotAsync(
            ManifestDto manifest,
            string manifestRole,
            IReadOnlyDictionary<string, byte[]>? suppliedBlobs,
            CancellationToken cancellationToken)
        {
            ImmutableSortedDictionary<string, string> registryLabels =
                ToLabels(manifest.RegistryLabels);
            long generation = manifest.Generation;
            var loadedBlobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            ImmutableDictionary<string, WotResourceGroup>.Builder groups =
                ImmutableDictionary.CreateBuilder<string, WotResourceGroup>();
            var identities = new HashSet<string>(StringComparer.Ordinal)
            {
                RegistryXid,
                RegistryNodeIdPath
            };
            if (manifest.Groups is not null)
            {
                foreach (GroupDto groupDto in manifest.Groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(groupDto.GroupId))
                    {
                        throw new InvalidDataException(
                            "WoT registry manifest contains an empty group id.");
                    }
                    ValidateSegment(groupDto.GroupId, "group id");
                    string groupXid = $"/groups/{groupDto.GroupId}";
                    string groupNodeIdPath =
                        $"{RegistryNodeIdPath}/groups/{groupDto.GroupId}";
                    if (!identities.Add(groupXid) ||
                        !identities.Add(groupNodeIdPath))
                    {
                        throw new InvalidDataException(
                            $"WoT registry manifest contains duplicate group id or " +
                            $"identity '{groupDto.GroupId}'.");
                    }
                    ImmutableDictionary<string, WotResource>.Builder resources =
                        ImmutableDictionary.CreateBuilder<string, WotResource>();
                    if (groupDto.Resources is not null)
                    {
                        foreach (ResourceDto resourceDto in groupDto.Resources)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!string.Equals(
                                resourceDto.GroupId,
                                groupDto.GroupId,
                                StringComparison.Ordinal))
                            {
                                throw new InvalidDataException(
                                    $"WoT registry manifest resource " +
                                    $"'{resourceDto.GroupId}/{resourceDto.ResourceId}' " +
                                    $"does not belong to containing group " +
                                    $"'{groupDto.GroupId}'.");
                            }
                            if (string.IsNullOrEmpty(resourceDto.ResourceId))
                            {
                                throw new InvalidDataException(
                                    $"WoT registry manifest group '{groupDto.GroupId}' " +
                                    "contains an empty resource id.");
                            }
                            ValidateSegment(resourceDto.ResourceId, "resource id");
                            string resourceXid =
                                $"/groups/{resourceDto.GroupId}/resources/" +
                                resourceDto.ResourceId;
                            string resourceNodeIdPath =
                                $"{RegistryNodeIdPath}/groups/{resourceDto.GroupId}/" +
                                $"resources/{resourceDto.ResourceId}";
                            if (!identities.Add(resourceXid) ||
                                !identities.Add(resourceNodeIdPath))
                            {
                                throw new InvalidDataException(
                                    $"WoT registry manifest contains duplicate resource id " +
                                    $"or identity '{resourceXid}'.");
                            }
                            WotResource resource = await LoadResourceAsync(
                                    resourceDto,
                                    loadedBlobs,
                                    manifestRole,
                                    suppliedBlobs,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            resources[resource.ResourceId] = resource;
                            generation = Math.Max(generation, resource.Epoch);
                        }
                    }

                    var group = new WotResourceGroup(
                        groupDto.GroupId,
                        (WoTDocumentKindEnum)groupDto.Kind,
                        resources.ToImmutable(),
                        groupDto.Name,
                        groupDto.Description,
                        groupDto.Epoch,
                        ToLabels(groupDto.Labels));
                    groups[group.GroupId] = group;
                    generation = Math.Max(generation, groupDto.Epoch);
                }
            }

            return new WotRegistrySnapshot(
                generation, groups.ToImmutable(), registryLabels);
        }

        private async ValueTask<WotResource> LoadResourceAsync(
            ResourceDto dto,
            Dictionary<string, byte[]> loadedBlobs,
            string manifestRole,
            IReadOnlyDictionary<string, byte[]>? suppliedBlobs,
            CancellationToken cancellationToken)
        {
            ImmutableArray<WotResourceVersion>.Builder versions =
                ImmutableArray.CreateBuilder<WotResourceVersion>();
            var versionIds = new HashSet<string>(StringComparer.Ordinal);
            if (dto.Versions is not null)
            {
                foreach (VersionDto version in dto.Versions)
                {
                    if (string.IsNullOrEmpty(version.VersionId) ||
                        !versionIds.Add(version.VersionId))
                    {
                        throw new InvalidDataException(
                            $"Registry resource '{dto.GroupId}/{dto.ResourceId}' " +
                            $"contains a duplicate or empty version id " +
                            $"'{version.VersionId}'.");
                    }
                    ValidateSegment(version.VersionId, "version id");
                    if (!IsSha256Hex(version.DigestHex))
                    {
                        throw new InvalidDataException(
                            $"Registry resource '{dto.GroupId}/{dto.ResourceId}' version " +
                            $"'{version.VersionId}' has an invalid SHA-256 DigestHex.");
                    }
                    string digestHex = version.DigestHex!.ToLowerInvariant();
                    if (!loadedBlobs.TryGetValue(digestHex, out byte[]? content))
                    {
                        if (suppliedBlobs is not null)
                        {
                            if (!suppliedBlobs.TryGetValue(digestHex, out content))
                            {
                                throw new InvalidDataException(
                                    $"WoT registry blob '{digestHex}' referenced by the " +
                                    $"{manifestRole} is missing.");
                            }
                            string suppliedDigest = WotContentDigest.ToHex(
                                WotContentDigest.Compute(content));
                            if (!string.Equals(
                                digestHex,
                                suppliedDigest,
                                StringComparison.Ordinal))
                            {
                                throw new InvalidDataException(
                                    $"WoT registry blob supplied for the {manifestRole} " +
                                    $"has SHA-256 '{suppliedDigest}', not '{digestHex}'.");
                            }
                        }
                        else
                        {
                            content = m_resourceStore is not null
                                ? await ReadFromResourceStoreAsync(
                                        digestHex, manifestRole, cancellationToken)
                                    .ConfigureAwait(false)
                                : await ReadBlobAsync(
                                        BlobPath(digestHex),
                                        digestHex,
                                        manifestRole,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                        }
                        loadedBlobs.Add(digestHex, content);
                    }
                    versions.Add(new WotResourceVersion(
                        version.VersionId,
                        content,
                        version.ContentType ?? string.Empty,
                        version.Format ?? string.Empty,
                        ParseDate(version.CreatedAt),
                        ParseDate(version.ModifiedAt)));
                }
            }

            return new WotResource(
                dto.GroupId,
                dto.ResourceId,
                (WoTDocumentKindEnum)dto.Kind,
                versions.ToImmutable(),
                defaultVersionId: dto.DefaultVersionId,
                desiredVersionId: dto.DesiredVersionId,
                activeVersionId: dto.ActiveVersionId,
                enabled: dto.Enabled,
                loadState: (WoTLoadStateEnum)dto.LoadState,
                validation: FromDto(dto.Validation),
                diagnostics: dto.Diagnostics is null
                    ? []
                    : ImmutableArray.Create(dto.Diagnostics),
                epoch: dto.Epoch,
                refreshGeneration: dto.RefreshGeneration,
                lastRefreshTime: ParseDate(dto.LastRefreshTime),
                materializedNodeCount: dto.MaterializedNodeCount,
                rootNodeId: ParseNodeId(dto.RootNodeId),
                name: dto.Name,
                description: dto.Description,
                thingId: dto.ThingId,
                title: dto.Title,
                labels: ToLabels(dto.Labels));
        }

        private static ManifestDto ToManifest(WotRegistrySnapshot snapshot)
        {
            var groups = new List<GroupDto>(snapshot.Groups.Count);
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                var resources = new List<ResourceDto>(group.Resources.Count);
                foreach (WotResource resource in group.Resources.Values)
                {
                    resources.Add(ToDto(resource));
                }
                groups.Add(new GroupDto
                {
                    GroupId = group.GroupId,
                    Kind = (int)group.Kind,
                    Name = group.Name,
                    Description = group.Description,
                    Epoch = group.Epoch,
                    Labels = FromLabels(group.Labels),
                    Resources = resources.Count == 0 ? null : [.. resources]
                });
            }
            return new ManifestDto
            {
                SchemaVersion = CurrentSchemaVersion,
                Generation = snapshot.Generation,
                RegistryLabels = FromLabels(snapshot.Labels),
                Groups = groups.Count == 0 ? null : [.. groups]
            };
        }

        private static ResourceDto ToDto(WotResource resource)
        {
            var versions = new VersionDto[resource.Versions.Length];
            for (int i = 0; i < resource.Versions.Length; i++)
            {
                WotResourceVersion v = resource.Versions[i];
                versions[i] = new VersionDto
                {
                    VersionId = v.VersionId,
                    ContentType = v.ContentType,
                    Format = v.Format,
                    CreatedAt = FormatDate(v.CreatedAt),
                    ModifiedAt = FormatDate(v.ModifiedAt),
                    DigestHex = v.DigestHex
                };
            }
            return new ResourceDto
            {
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                Kind = (int)resource.Kind,
                Name = resource.Name,
                Description = resource.Description,
                DefaultVersionId = resource.DefaultVersionId,
                DesiredVersionId = resource.DesiredVersionId,
                ActiveVersionId = resource.ActiveVersionId,
                Enabled = resource.Enabled,
                LoadState = (int)resource.LoadState,
                Epoch = resource.Epoch,
                RefreshGeneration = resource.RefreshGeneration,
                LastRefreshTime = FormatDate(resource.LastRefreshTime),
                MaterializedNodeCount = resource.MaterializedNodeCount,
                RootNodeId = resource.RootNodeId.IsNull ? null : resource.RootNodeId.ToString(),
                ThingId = resource.ThingId,
                Title = resource.Title,
                Diagnostics = resource.Diagnostics.IsDefaultOrEmpty
                    ? null
                    : System.Linq.Enumerable.ToArray(resource.Diagnostics),
                Validation = ToDto(resource.Validation),
                Versions = versions.Length == 0 ? null : versions,
                Labels = FromLabels(resource.Labels)
            };
        }

        /// <summary>
        /// Converts a possibly-null DTO dictionary into the ordinally-ordered
        /// immutable label dictionary, defaulting to <see cref="WotLabels.Empty"/>.
        /// </summary>
        private static ImmutableSortedDictionary<string, string> ToLabels(
            Dictionary<string, string>? labels)
        {
            if (labels is null || labels.Count == 0)
            {
                return WotLabels.Empty;
            }
            return ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, labels);
        }

        /// <summary>
        /// Converts the immutable label dictionary into a plain
        /// <see cref="Dictionary{TKey, TValue}"/> for JSON serialization, or
        /// <c>null</c> when empty (kept out of the persisted document).
        /// </summary>
        private static Dictionary<string, string>? FromLabels(
            ImmutableSortedDictionary<string, string> labels)
        {
            return labels.Count == 0 ? null : new Dictionary<string, string>(labels);
        }

        private static ValidationDto? ToDto(WoTValidationOutcomeDataType? validation)
        {
            if (validation is null)
            {
                return null;
            }
            return new ValidationDto
            {
                FormatValidated = validation.FormatValidated,
                FormatOutcome = (int)validation.FormatOutcome,
                FormatReason = validation.FormatReason,
                CompatibilityValidated = validation.CompatibilityValidated,
                CompatibilityOutcome = (int)validation.CompatibilityOutcome,
                CompatibilityReason = validation.CompatibilityReason,
                CompatibilityPolicy = validation.CompatibilityPolicy,
                ValidatedAt = FormatDate(validation.ValidatedAt.ToDateTime()),
                VocabularyVersion = validation.VocabularyVersion
            };
        }

        private static WoTValidationOutcomeDataType? FromDto(ValidationDto? dto)
        {
            if (dto is null)
            {
                return null;
            }
            return new WoTValidationOutcomeDataType
            {
                FormatValidated = dto.FormatValidated,
                FormatOutcome = (WoTOutcomeEnum)dto.FormatOutcome,
                FormatReason = dto.FormatReason,
                CompatibilityValidated = dto.CompatibilityValidated,
                CompatibilityOutcome = (WoTOutcomeEnum)dto.CompatibilityOutcome,
                CompatibilityReason = dto.CompatibilityReason,
                CompatibilityPolicy = dto.CompatibilityPolicy,
                ValidatedAt = ParseDate(dto.ValidatedAt),
                VocabularyVersion = dto.VocabularyVersion
            };
        }

        private string BlobPath(string digestHex)
        {
            return Path.Combine(m_blobsFolder, digestHex + ".bin");
        }

        private async ValueTask<StorageLock> AcquireStorageLockAsync(
            CancellationToken cancellationToken)
        {
            EnsureRootDirectoryDurable();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    FileStream? lockStream = null;
                    try
                    {
                        lockStream = new FileStream(
                            m_lockPath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.None);
                        var storageLock = new StorageLock(lockStream);
                        lockStream = null;
                        return storageLock;
                    }
                    finally
                    {
                        lockStream?.Dispose();
                    }
                }
                catch (IOException ex) when (IsLockContention(ex))
                {
                }
                await Task.Delay(s_lockRetryDelay, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private void EnsureRootDirectoryDurable()
        {
            string[] durabilityPath = m_rootDurabilityPath ??=
                BuildRootDurabilityPath();
            foreach (string path in durabilityPath)
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                DirectoryInfo? parent = Directory.GetParent(path);
                if (parent is null)
                {
                    continue;
                }
                SyncDirectory(
                    parent.FullName,
                    DirectorySyncPhase.RootComponentParent);
            }
        }

        private string[] BuildRootDurabilityPath()
        {
            var components = new Stack<string>();
            DirectoryInfo? current = new DirectoryInfo(m_root);
            while (current is not null && !current.Exists)
            {
                components.Push(current.FullName);
                current = current.Parent;
            }
            if (current is null)
            {
                throw new IOException(
                    $"No existing ancestor was found for registry root '{m_root}'.");
            }

            var durabilityPath = new List<string>(components.Count + 1)
            {
                current.FullName
            };
            while (components.Count > 0)
            {
                durabilityPath.Add(components.Pop());
            }
            return [.. durabilityPath];
        }

        private static bool IsLockContention(IOException exception)
        {
            int error = exception.HResult & 0xffff;
            // Unix contention may surface as errno or the mapped Win32 sharing code.
            return error is 4 or 11 or 13 or 16 or 32 or 33 or 35;
        }

        private async ValueTask<byte[]> ReadFromResourceStoreAsync(
            string expectedDigest,
            string manifestRole,
            CancellationToken cancellationToken)
        {
            long length = await m_resourceStore!.GetLengthAsync(expectedDigest, cancellationToken)
                .ConfigureAwait(false);
            if (length < 0)
            {
                throw new InvalidDataException(
                    $"WoT registry document '{expectedDigest}' referenced by the {manifestRole} " +
                    "is missing from the resource store. The registry was left unchanged.");
            }

            ByteString document = await m_resourceStore
                .ReadAsync(expectedDigest, 0, checked((int)length), cancellationToken)
                .ConfigureAwait(false);
            if (document.IsNull)
            {
                // A null ByteString is how the contract reports an unknown key, which can only
                // happen here if the document was removed between the two calls.
                throw new InvalidDataException(
                    $"WoT registry document '{expectedDigest}' referenced by the {manifestRole} " +
                    "is missing from the resource store. The registry was left unchanged.");
            }

            byte[] content = document.Span.ToArray();
            string actualDigest = WotContentDigest.ToHex(WotContentDigest.Compute(content));
            if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"WoT registry document '{expectedDigest}' has SHA-256 '{actualDigest}', " +
                    $"but the {manifestRole} requires '{expectedDigest}'. " +
                    "The registry was left unchanged.");
            }
            return content;
        }

        private async ValueTask<byte[]> ReadBlobAsync(
            string path,
            string expectedDigest,
            string manifestRole,
            CancellationToken cancellationToken)
        {
            byte[] content;
            try
            {
                content = await ReadAllBytesAsync(path, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidDataException(
                    $"WoT registry blob '{path}' referenced by the {manifestRole} " +
                    "is missing. The registry was left unchanged.",
                    ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new InvalidDataException(
                    $"WoT registry blob directory for '{path}' referenced by the " +
                    $"{manifestRole} is missing. " +
                    "The registry was left unchanged.",
                    ex);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Unable to read WoT registry blob '{path}'. " +
                    "The registry was left unchanged.",
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Access to WoT registry blob '{path}' was denied. " +
                    "The registry was left unchanged.",
                    ex);
            }

            string actualDigest = WotContentDigest.ToHex(WotContentDigest.Compute(content));
            if (!string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"WoT registry blob '{path}' has SHA-256 '{actualDigest}', " +
                    $"but the {manifestRole} requires '{expectedDigest}'. " +
                    "The registry was left unchanged.");
            }
            return content;
        }

        private static bool IsSha256Hex(string? value)
        {
            if (value?.Length != Sha256HexLength)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9') ||
                    (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ValidateSegment(string value, string description)
        {
            string normalized;
            try
            {
                normalized = WotRegistryService.NormalizeSegment(value, description);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException(
                    $"WoT registry {description} '{value}' is not segment-safe.",
                    ex);
            }
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"WoT registry {description} '{value}' is not segment-safe.");
            }
        }

        private async ValueTask RollbackPristinePreSwitchFailureAsync(
            WotRegistrySnapshot intendedSnapshot,
            PristineCommitArtifacts? artifacts,
            Exception persistenceFailure)
        {
            if (artifacts is null)
            {
                return;
            }

            try
            {
                await RollbackPristinePreSwitchArtifactsAsync(artifacts)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
                when (IsConfirmedPreSwitchFailure(cleanupFailure))
            {
                throw CreatePristineRollbackIndeterminate(
                    intendedSnapshot,
                    persistenceFailure,
                    cleanupFailure);
            }
        }

        private async ValueTask RollbackPristinePreSwitchArtifactsAsync(
            PristineCommitArtifacts artifacts)
        {
            if (!ValidatePristinePreSwitchArtifacts(artifacts))
            {
                return;
            }

            string rollbackMarker = Path.Combine(
                m_root,
                ManifestFile + ".rollback-" + Guid.NewGuid().ToString("N"));
            byte[] markerBytes = Array.Empty<byte>();
            await WriteThroughAsync(
                    rollbackMarker,
                    markerBytes,
                    CancellationToken.None,
                    artifacts.TrackFile)
                .ConfigureAwait(false);
            SyncDirectory(
                m_root,
                DirectorySyncPhase.RootAfterPristineRollbackMarker);

            foreach (string path in artifacts.Files
                .Where(path => !PathsEqual(path, rollbackMarker))
                .OrderBy(path => path, s_fileSystemPathComparer))
            {
                if (Directory.Exists(path))
                {
                    throw new InvalidDataException(
                        $"Pristine WoT registry rollback artifact '{path}' changed " +
                        "from a file to a directory.");
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (Directory.Exists(m_blobsFolder))
            {
                string[] remaining = Directory.GetFileSystemEntries(m_blobsFolder);
                if (remaining.Length != 0)
                {
                    throw new InvalidDataException(
                        "The pristine WoT registry blob directory acquired unknown " +
                        $"artifacts during rollback: {string.Join(", ", remaining)}.");
                }
                SyncDirectory(
                    m_blobsFolder,
                    DirectorySyncPhase.BlobsAfterPristineRollback);
                Directory.Delete(m_blobsFolder);
            }
            SyncDirectory(
                m_root,
                DirectorySyncPhase.RootAfterPristineRollback);

            await RemovePristineRollbackMarkerAsync(rollbackMarker, markerBytes)
                .ConfigureAwait(false);
        }

        private bool ValidatePristinePreSwitchArtifacts(
            PristineCommitArtifacts artifacts)
        {
            bool foundArtifacts = false;
            foreach (string path in FindRecoveryArtifacts())
            {
                if (PathsEqual(path, m_blobsFolder))
                {
                    if (!artifacts.OwnsBlobsDirectory || !Directory.Exists(path))
                    {
                        throw new InvalidDataException(
                            $"Unexpected WoT registry artifact '{path}' prevents " +
                            "a proven pristine rollback.");
                    }
                    foundArtifacts = true;
                    continue;
                }
                if (!artifacts.OwnsFile(path))
                {
                    throw new InvalidDataException(
                        $"Unknown WoT registry artifact '{path}' prevents a proven " +
                        "pristine rollback.");
                }
                ValidateOwnedRollbackFile(path);
                foundArtifacts = true;
            }

            if (Directory.Exists(m_blobsFolder))
            {
                foreach (string path in Directory.GetFileSystemEntries(m_blobsFolder))
                {
                    if (!artifacts.OwnsFile(path))
                    {
                        throw new InvalidDataException(
                            $"Unknown WoT registry blob artifact '{path}' prevents a " +
                            "proven pristine rollback.");
                    }
                    ValidateOwnedRollbackFile(path);
                }
            }
            return foundArtifacts;
        }

        private static void ValidateOwnedRollbackFile(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidDataException(
                    $"Pristine WoT registry rollback artifact '{path}' is not a " +
                    "regular file.");
            }
        }

        private async ValueTask RemovePristineRollbackMarkerAsync(
            string rollbackMarker,
            byte[] markerBytes)
        {
            try
            {
                File.Delete(rollbackMarker);
                SyncDirectory(
                    m_root,
                    DirectorySyncPhase.RootAfterPristineRollbackMarkerRemoval);
            }
            catch (Exception cleanupFailure)
                when (IsConfirmedPreSwitchFailure(cleanupFailure))
            {
                throw await PreservePristineRollbackMarkerAsync(
                        rollbackMarker,
                        markerBytes,
                        cleanupFailure)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask<IOException> PreservePristineRollbackMarkerAsync(
            string rollbackMarker,
            byte[] markerBytes,
            Exception cleanupFailure)
        {
            try
            {
                if (!File.Exists(rollbackMarker))
                {
                    await WriteThroughAsync(
                            rollbackMarker,
                            markerBytes,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                SyncDirectory(
                    m_root,
                    DirectorySyncPhase.RootAfterPristineRollbackMarker);
                return new IOException(
                    "The pristine WoT registry rollback marker could not be " +
                    "durably removed and was retained.",
                    cleanupFailure);
            }
            catch (Exception retentionFailure)
                when (IsConfirmedPreSwitchFailure(retentionFailure))
            {
                return CreateRollbackMarkerRetentionFailure(
                    cleanupFailure,
                    retentionFailure);
            }
        }

        private static IOException CreateRollbackMarkerRetentionFailure(
            Exception cleanupFailure,
            Exception retentionFailure)
        {
            return new IOException(
                "The pristine WoT registry rollback marker could neither be " +
                "durably removed nor retained.",
                new AggregateException(cleanupFailure, retentionFailure));
        }

        private WotRegistryCommitIndeterminateException
            CreatePristineRollbackIndeterminate(
                WotRegistrySnapshot intendedSnapshot,
                Exception persistenceFailure,
                Exception cleanupFailure)
        {
            m_expectedManifest = null;
            m_expectedGeneration = null;
            return new WotRegistryCommitIndeterminateException(
                intendedSnapshot,
                persistenceFailure,
                new IOException(
                    "The confirmed pre-switch failure occurred on a pristine WoT " +
                    "registry, but rollback could not be proven durable. Any remaining " +
                    "store-owned artifacts were retained for fail-closed recovery. " +
                    $"Cleanup failure: {cleanupFailure.Message}",
                    cleanupFailure));
        }

        private static bool PathsEqual(string left, string right)
        {
            return s_fileSystemPathComparer.Equals(left, right);
        }

        private static bool IsConfirmedPreSwitchFailure(Exception exception)
        {
            return exception is OperationCanceledException or
                UnauthorizedAccessException or InvalidDataException or IOException;
        }

        private static async ValueTask EnsureBlobAsync(
            string path,
            byte[] content,
            string expectedDigest,
            PristineCommitArtifacts? pristineArtifacts,
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] existing = await ReadAllBytesAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                string existingDigest = WotContentDigest.ToHex(
                    WotContentDigest.Compute(existing));
                if (!string.Equals(
                    expectedDigest, existingDigest, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Existing WoT registry blob '{path}' has SHA-256 " +
                        $"'{existingDigest}', not '{expectedDigest}'.");
                }
                return;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    $"Access to existing WoT registry blob '{path}' was denied.",
                    ex);
            }
            catch (IOException ex)
            {
                throw new IOException(
                    $"Unable to validate existing WoT registry blob '{path}'.",
                    ex);
            }

            await DurableWriteAsync(
                    path,
                    content,
                    pristineArtifacts,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static async ValueTask<bool> ResourceStoreBlobMatchesAsync(
            IXRegistryResourceStore resourceStore,
            string resourceKey,
            byte[] expected,
            CancellationToken cancellationToken)
        {
            long length = await resourceStore.GetLengthAsync(resourceKey, cancellationToken)
                .ConfigureAwait(false);
            if (length != expected.Length || length < 0 || length > int.MaxValue)
            {
                return false;
            }

            ByteString existing = await resourceStore
                .ReadAsync(resourceKey, 0, (int)length, cancellationToken)
                .ConfigureAwait(false);
            if (existing.IsNull || existing.Length != expected.Length)
            {
                return false;
            }

            return string.Equals(
                resourceKey,
                WotContentDigest.ToHex(WotContentDigest.Compute(existing.Span.ToArray())),
                StringComparison.Ordinal);
        }

        private static async ValueTask DurableWriteAsync(
            string path,
            byte[] bytes,
            PristineCommitArtifacts? pristineArtifacts,
            CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            bool moved = false;
            try
            {
                await WriteThroughAsync(
                        tmp,
                        bytes,
                        cancellationToken,
                        pristineArtifacts is null
                            ? null
                            : pristineArtifacts.TrackFile)
                    .ConfigureAwait(false);
                File.Move(tmp, path);
                pristineArtifacts?.TrackFile(path);
                moved = true;
            }
            finally
            {
                if (!moved)
                {
                    TryDelete(tmp);
                }
            }
        }

        private async ValueTask<string?> AtomicReplaceManifestAsync(
            WotRegistrySnapshot intendedSnapshot,
            byte[] bytes,
            ManifestStamp intendedStamp,
            ManifestStamp expectedStamp,
            long expectedGeneration,
            PristineCommitArtifacts? pristineArtifacts,
            CancellationToken cancellationToken)
        {
            string path = Path.Combine(m_root, ManifestFile);
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            string? replaceBackupPath = null;
            bool preserveTemporary = false;
            try
            {
                try
                {
                    await WriteThroughAsync(
                            tmp,
                            bytes,
                            cancellationToken,
                            pristineArtifacts is null
                                ? null
                                : pristineArtifacts.TrackFile)
                        .ConfigureAwait(false);
                    SyncDirectory(
                        m_root,
                        DirectorySyncPhase.RootAfterManifestStaging);
                }
                catch (Exception failure)
                    when (pristineArtifacts is not null &&
                        IsConfirmedPreSwitchFailure(failure))
                {
                    preserveTemporary = true;
                    await RollbackPristinePreSwitchFailureAsync(
                            intendedSnapshot,
                            pristineArtifacts,
                            failure)
                        .ConfigureAwait(false);
                    throw;
                }
                if (expectedStamp.Exists)
                {
                    replaceBackupPath =
                        path + ".replace-backup-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        if (m_manifestReplace is null)
                        {
                            File.Replace(tmp, path, replaceBackupPath);
                        }
                        else
                        {
                            m_manifestReplace(tmp, path, replaceBackupPath);
                        }
                    }
                    catch (Exception replaceFailure)
                    {
                        // ReplaceFile can report failure after moving either source
                        // or destination. Preserve every artifact and classify only
                        // from validated disk state while the root lock is held.
                        preserveTemporary = true;
                        await ResolveReplaceFailureAsync(
                                intendedSnapshot,
                                bytes,
                                intendedStamp,
                                expectedStamp,
                                expectedGeneration,
                                tmp,
                                replaceBackupPath,
                                replaceFailure)
                            .ConfigureAwait(false);
                        throw;
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
                preserveTemporary = true;
                return replaceBackupPath;
            }
            finally
            {
                if (!preserveTemporary)
                {
                    TryDelete(tmp);
                }
            }
        }

        private async ValueTask ResolveReplaceFailureAsync(
            WotRegistrySnapshot intendedSnapshot,
            byte[] intendedManifestBytes,
            ManifestStamp intendedStamp,
            ManifestStamp expectedStamp,
            long expectedGeneration,
            string temporaryManifestPath,
            string replaceBackupPath,
            Exception replaceFailure)
        {
            ManifestCandidate primary = await InspectManifestCandidateAsync(
                    Path.Combine(m_root, ManifestFile),
                    "primary manifest")
                .ConfigureAwait(false);
            ManifestCandidate temporary = await InspectManifestCandidateAsync(
                    temporaryManifestPath,
                    "staged manifest")
                .ConfigureAwait(false);
            ManifestCandidate backup = await InspectManifestCandidateAsync(
                    replaceBackupPath,
                    "replace backup manifest")
                .ConfigureAwait(false);

            if (MatchesIntended(
                primary,
                intendedSnapshot.Generation,
                intendedStamp,
                intendedManifestBytes))
            {
                LoadedGeneration committed = primary.Generation!;
                m_expectedManifest = committed.Stamp;
                m_expectedGeneration = committed.Snapshot.Generation;
                throw new WotRegistryCommitDurabilityUncertainException(
                    committed.Snapshot,
                    replaceFailure);
            }

            if (MatchesGeneration(primary, expectedGeneration, expectedStamp) &&
                MatchesIntended(
                    temporary,
                    intendedSnapshot.Generation,
                    intendedStamp,
                    intendedManifestBytes))
            {
                m_expectedManifest = expectedStamp;
                m_expectedGeneration = expectedGeneration;
                throw new WotRegistryCommitNotCommittedException(
                    intendedSnapshot,
                    replaceFailure,
                    temporaryManifestPath);
            }

            m_expectedManifest = null;
            m_expectedGeneration = null;
            throw new WotRegistryCommitIndeterminateException(
                intendedSnapshot,
                replaceFailure,
                new InvalidDataException(
                    "Unable to establish the result of the manifest replacement. " +
                    $"{primary.Describe()}; {temporary.Describe()}; {backup.Describe()}."));
        }

        private async ValueTask<ManifestCandidate> InspectManifestCandidateAsync(
            string path,
            string role)
        {
            try
            {
                LoadedGeneration? generation = await ReadGenerationAsync(
                        path,
                        role,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return generation is null
                    ? ManifestCandidate.Missing(path, role)
                    : ManifestCandidate.Valid(path, role, generation);
            }
            catch (Exception validationFailure)
            {
                return ManifestCandidate.Invalid(path, role, validationFailure);
            }
        }

        private static bool MatchesGeneration(
            ManifestCandidate candidate,
            long generation,
            ManifestStamp stamp)
        {
            return candidate.Generation is { } loaded &&
                loaded.Snapshot.Generation == generation &&
                loaded.Stamp.Equals(stamp);
        }

        private static bool MatchesIntended(
            ManifestCandidate candidate,
            long generation,
            ManifestStamp stamp,
            byte[] manifestBytes)
        {
            return MatchesGeneration(candidate, generation, stamp) &&
                candidate.Generation!.ManifestBytes.AsSpan().SequenceEqual(manifestBytes);
        }

        private static async ValueTask WriteThroughAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken,
            Action<string>? fileCreated = null)
        {
            // FileOptions.WriteThrough bypasses the OS write cache so the bytes
            // reach stable storage before the handle closes; this preserves the
            // "blobs durable before manifest switch" ordering the commit relies on.
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous);
            fileCreated?.Invoke(path);
#if NETSTANDARD2_1_OR_GREATER || NET
            await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        private void SyncDirectory(string path, DirectorySyncPhase phase)
        {
            m_directorySyncFailureInjector?.Invoke(phase);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using SafeFileHandle handle = CreateFileW(
                    path,
                    GenericWrite,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Unable to open WoT registry directory '{path}' for a " +
                        $"durability flush.",
                        new Win32Exception(error));
                }
                if (!FlushFileBuffers(handle))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Unable to durably synchronize WoT registry directory '{path}'.",
                        new Win32Exception(error));
                }
                return;
            }

            byte[] utf8Path = System.Text.Encoding.UTF8.GetBytes(path + "\0");
            using SafeUnixDirectoryHandle directory =
                OpenUnixDirectory(utf8Path, OpenReadOnly);
            if (directory.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Unable to open WoT registry directory '{path}' for a " +
                    $"durability flush.",
                    new Win32Exception(error));
            }
            if (Fsync(directory) != 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Unable to durably synchronize WoT registry directory '{path}'.",
                    new Win32Exception(error));
            }
        }

        private static async ValueTask<byte[]> ReadAllBytesAsync(
            string path,
            CancellationToken cancellationToken)
        {
#if NETSTANDARD2_1_OR_GREATER || NET
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
#else
            await Task.CompletedTask.ConfigureAwait(false);
            return File.ReadAllBytes(path);
#endif
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string FormatDate(DateTime value)
        {
            return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseDate(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return DateTime.MinValue;
            }
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
        }

        private static NodeId ParseNodeId(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return NodeId.Null;
            }
            try
            {
                return NodeId.Parse(value);
            }
            catch (ServiceResultException)
            {
                return NodeId.Null;
            }
        }

        [DllImport(
            "kernel32.dll",
            EntryPoint = "CreateFileW",
            ExactSpelling = true,
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "FlushFileBuffers",
            ExactSpelling = true,
            SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);

        [DllImport(
            "libc",
            EntryPoint = "open",
            ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl,
            SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern SafeUnixDirectoryHandle OpenUnixDirectory(
            [In] byte[] path,
            int flags);

        [DllImport(
            "libc",
            EntryPoint = "fsync",
            CallingConvention = CallingConvention.Cdecl,
            SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int Fsync(SafeUnixDirectoryHandle file);

        [DllImport(
            "libc",
            EntryPoint = "close",
            CallingConvention = CallingConvention.Cdecl,
            SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int CloseUnix(IntPtr file);

        internal enum DirectorySyncPhase
        {
            RootComponentParent,
            BlobsBeforeManifest,
            RootBeforeManifest,
            RootAfterManifestStaging,
            RootAfterManifest,
            RootAfterPristineRollbackMarker,
            BlobsAfterPristineRollback,
            RootAfterPristineRollback,
            RootAfterPristineRollbackMarkerRemoval
        }

        private sealed class PristineCommitArtifacts
        {
            public PristineCommitArtifacts()
            {
                Files = new HashSet<string>(s_fileSystemPathComparer);
            }

            public bool OwnsBlobsDirectory { get; private set; }

            public HashSet<string> Files { get; }

            public void ClaimBlobsDirectory()
            {
                OwnsBlobsDirectory = true;
            }

            public bool OwnsFile(string path)
            {
                return Files.Contains(path);
            }

            public void TrackFile(string path)
            {
                Files.Add(path);
            }
        }

        private sealed class StorageLock : IDisposable
        {
            public StorageLock(FileStream stream)
            {
                m_stream = stream;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref m_stream, null)?.Dispose();
            }
            private FileStream? m_stream;
        }

        private sealed class SafeUnixDirectoryHandle : SafeHandleMinusOneIsInvalid
        {
            private SafeUnixDirectoryHandle()
                : base(ownsHandle: true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return CloseUnix(handle) == 0;
            }
        }

        private sealed class LoadedGeneration
        {
            public LoadedGeneration(
                WotRegistrySnapshot snapshot,
                ManifestStamp stamp,
                byte[] manifestBytes)
            {
                Snapshot = snapshot;
                Stamp = stamp;
                ManifestBytes = manifestBytes;
            }

            public WotRegistrySnapshot Snapshot { get; }

            public ManifestStamp Stamp { get; }

            public byte[] ManifestBytes { get; }
        }

        private sealed class ValidatedCommit
        {
            public ValidatedCommit(
                byte[] manifestBytes,
                ManifestStamp stamp,
                Dictionary<string, byte[]> blobs)
            {
                ManifestBytes = manifestBytes;
                Stamp = stamp;
                Blobs = blobs;
            }

            public byte[] ManifestBytes { get; }

            public ManifestStamp Stamp { get; }

            public Dictionary<string, byte[]> Blobs { get; }
        }

        private sealed class ManifestCandidate
        {
            private ManifestCandidate(
                string path,
                string role,
                LoadedGeneration? generation,
                Exception? failure)
            {
                Path = path;
                Role = role;
                Generation = generation;
                Failure = failure;
            }

            public string Path { get; }

            public string Role { get; }

            public LoadedGeneration? Generation { get; }

            public Exception? Failure { get; }

            public static ManifestCandidate Missing(string path, string role)
            {
                return new ManifestCandidate(path, role, generation: null, failure: null);
            }

            public static ManifestCandidate Valid(
                string path,
                string role,
                LoadedGeneration generation)
            {
                return new ManifestCandidate(path, role, generation, failure: null);
            }

            public static ManifestCandidate Invalid(
                string path,
                string role,
                Exception failure)
            {
                return new ManifestCandidate(path, role, generation: null, failure);
            }

            public string Describe()
            {
                if (Generation is { } generation)
                {
                    return $"{Role} '{Path}' is valid ({generation.Stamp})";
                }
                if (Failure is { } failure)
                {
                    return $"{Role} '{Path}' is invalid ({failure.Message})";
                }
                return $"{Role} '{Path}' is absent";
            }
        }

        private readonly struct ManifestStamp : IEquatable<ManifestStamp>
        {
            public ManifestStamp(bool exists, long generation, string digestHex)
            {
                Exists = exists;
                Generation = generation;
                DigestHex = digestHex;
            }

            public static ManifestStamp Absent => new(
                exists: false,
                generation: 0,
                digestHex: string.Empty);

            public bool Exists { get; }

            public long Generation { get; }

            public string DigestHex { get; }

            public bool Equals(ManifestStamp other)
            {
                return Exists == other.Exists &&
                    Generation == other.Generation &&
                    string.Equals(DigestHex, other.DigestHex, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is ManifestStamp stamp && Equals(stamp);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Exists ? 1 : 0;
                    hash = (hash * 397) ^ Generation.GetHashCode();
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DigestHex);
                    return hash;
                }
            }

            public override string ToString()
            {
                return Exists
                    ? $"generation {Generation}, manifest SHA-256 {DigestHex}"
                    : "an absent primary manifest";
            }
        }

        private const string ManifestFile = "manifest.json";
        private const string LockFile = ".wot-registry.lock";
        private const string RegistryXid = "/";
        private const string RegistryNodeIdPath = "WoTRegistry";
        private const int CurrentSchemaVersion = 2;
        private const int Sha256HexLength = 64;
        private const int OpenReadOnly = 0;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private static readonly TimeSpan s_lockRetryDelay = TimeSpan.FromMilliseconds(25);
        private static readonly StringComparer s_fileSystemPathComparer =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private readonly string m_root;
        private readonly string m_blobsFolder;
        private readonly IXRegistryResourceStore? m_resourceStore;
        private readonly string m_lockPath;
        private readonly Action<DirectorySyncPhase>? m_directorySyncFailureInjector;
        private readonly Action<string, string, string>? m_manifestReplace;
        private string[]? m_rootDurabilityPath;
        private ManifestStamp? m_expectedManifest;
        private long? m_expectedGeneration;

        internal sealed class ManifestDto
        {
            public int SchemaVersion { get; set; }
            public long Generation { get; set; }
            public Dictionary<string, string>? RegistryLabels { get; set; }
            public GroupDto[]? Groups { get; set; }
        }

        internal sealed class GroupDto
        {
            public string GroupId { get; set; } = string.Empty;
            public int Kind { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public long Epoch { get; set; }
            public Dictionary<string, string>? Labels { get; set; }
            public ResourceDto[]? Resources { get; set; }
        }

        internal sealed class VersionDto
        {
            public string VersionId { get; set; } = string.Empty;
            public string? ContentType { get; set; }
            public string? Format { get; set; }
            public string? CreatedAt { get; set; }
            public string? ModifiedAt { get; set; }
            public string? DigestHex { get; set; }
        }

        internal sealed class ValidationDto
        {
            public bool FormatValidated { get; set; }
            public int FormatOutcome { get; set; }
            public string? FormatReason { get; set; }
            public bool CompatibilityValidated { get; set; }
            public int CompatibilityOutcome { get; set; }
            public string? CompatibilityReason { get; set; }
            public string? CompatibilityPolicy { get; set; }
            public string? ValidatedAt { get; set; }
            public string? VocabularyVersion { get; set; }
        }

        internal sealed class ResourceDto
        {
            public string GroupId { get; set; } = string.Empty;
            public string ResourceId { get; set; } = string.Empty;
            public int Kind { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string? DefaultVersionId { get; set; }
            public string? DesiredVersionId { get; set; }
            public string? ActiveVersionId { get; set; }
            public bool Enabled { get; set; }
            public int LoadState { get; set; }
            public long Epoch { get; set; }
            public uint RefreshGeneration { get; set; }
            public string? LastRefreshTime { get; set; }
            public int MaterializedNodeCount { get; set; }
            public string? RootNodeId { get; set; }
            public string? ThingId { get; set; }
            public string? Title { get; set; }
            public string[]? Diagnostics { get; set; }
            public ValidationDto? Validation { get; set; }
            public VersionDto[]? Versions { get; set; }
            public Dictionary<string, string>? Labels { get; set; }
        }
    }

    /// <summary>
    /// Source-generated JSON metadata serialization for the file-backed store,
    /// keeping the store trimming/AOT-safe (no reflection-based serialization).
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(FileWotRegistryStore.ManifestDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.GroupDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.ResourceDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.VersionDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.ValidationDto))]
    internal sealed partial class WotRegistryStoreJson : JsonSerializerContext;
}
