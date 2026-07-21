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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A durable, file-backed registry store that persists each committed
    /// generation transactionally. Version document bytes are written once into a
    /// content-addressed <c>blobs</c> directory (deduplicated by SHA-256 digest);
    /// all registry metadata (groups, resources, versions, load/validation state,
    /// labels) is captured in a single <c>manifest.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="CommitAsync"/> first stages every referenced blob durably
    /// (write-through to disk, then atomic move into place) and only then switches
    /// the committed generation by atomically replacing <c>manifest.json</c>
    /// (write-to-temp, write-through, then <see cref="File.Replace(string, string, string?)"/>).
    /// Because the manifest is the single pointer to a generation and it is the
    /// last thing written, a crash (or an injected failure) never exposes a
    /// half-written generation: the reader either sees the previous manifest in
    /// full or the new one in full. <see cref="LoadAsync"/> reads only the
    /// committed manifest and its referenced blobs; staged temp files are ignored.
    /// </para>
    /// <para>
    /// Invalid documents are committed with their failure state so a restart
    /// restores exactly the last committed registry contents. Blobs that the newly
    /// committed manifest no longer references are pruned on a best-effort basis
    /// after the manifest switch.
    /// </para>
    /// </remarks>
    public sealed class FileWotRegistryStore : IWotRegistryStore
    {
        /// <summary>
        /// Initializes a new file-backed store rooted at <paramref name="rootFolder"/>.
        /// </summary>
        public FileWotRegistryStore(string rootFolder)
        {
            m_root = rootFolder ?? throw new ArgumentNullException(nameof(rootFolder));
            m_blobsFolder = Path.Combine(m_root, "blobs");
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistrySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            string manifestPath = Path.Combine(m_root, ManifestFile);
            if (!File.Exists(manifestPath))
            {
                return WotRegistrySnapshot.Empty;
            }

            ManifestDto? manifest = await ReadJsonAsync(
                    manifestPath, WotRegistryStoreJson.Default.ManifestDto, cancellationToken)
                .ConfigureAwait(false);
            if (manifest is null)
            {
                return WotRegistrySnapshot.Empty;
            }

            ImmutableSortedDictionary<string, string> registryLabels = ToLabels(manifest.RegistryLabels);
            long generation = manifest.Generation;

            var groups = ImmutableDictionary.CreateBuilder<string, WotResourceGroup>();
            if (manifest.Groups is not null)
            {
                foreach (GroupDto groupDto in manifest.Groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resources = ImmutableDictionary.CreateBuilder<string, WotResource>();
                    if (groupDto.Resources is not null)
                    {
                        foreach (ResourceDto resourceDto in groupDto.Resources)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            WotResource? resource = await LoadResourceAsync(
                                resourceDto, cancellationToken).ConfigureAwait(false);
                            if (resource is not null)
                            {
                                resources[resource.ResourceId] = resource;
                                generation = Math.Max(generation, resource.Epoch);
                            }
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

            return new WotRegistrySnapshot(generation, groups.ToImmutable(), registryLabels);
        }

        /// <inheritdoc/>
        public async ValueTask CommitAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            snapshot ??= WotRegistrySnapshot.Empty;

            Directory.CreateDirectory(m_root);
            Directory.CreateDirectory(m_blobsFolder);

            // 1. Stage every referenced version blob durably before the manifest
            // that points at it is switched in. Blobs are content-addressed, so an
            // unchanged document is written at most once and shared across
            // versions/resources.
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                foreach (WotResource resource in group.Resources.Values)
                {
                    foreach (WotResourceVersion version in resource.Versions)
                    {
                        string digestHex = WotContentDigest.ToHex(version.Digest);
                        if (digestHex.Length == 0)
                        {
                            continue;
                        }
                        if (referenced.Add(digestHex))
                        {
                            string blobPath = BlobPath(digestHex);
                            if (!File.Exists(blobPath))
                            {
                                await DurableWriteAsync(
                                    blobPath, version.Content.ToArray(), cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }
                }
            }

            // 2. Build and durably write the manifest, then switch it in with a
            // single atomic replace. This is the commit point: until it completes,
            // the previously committed generation stays fully intact.
            ManifestDto manifest = ToManifest(snapshot);
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest, WotRegistryStoreJson.Default.ManifestDto);
            await AtomicReplaceAsync(
                Path.Combine(m_root, ManifestFile), manifestBytes, cancellationToken)
                .ConfigureAwait(false);

            // 3. Best-effort prune of blobs the new committed generation no longer
            // references. A failure here never affects correctness of the commit.
            PruneOrphanBlobs(referenced);
        }

        private async ValueTask<WotResource?> LoadResourceAsync(
            ResourceDto dto,
            CancellationToken cancellationToken)
        {
            var versions = ImmutableArray.CreateBuilder<WotResourceVersion>();
            if (dto.Versions is not null)
            {
                foreach (VersionDto v in dto.Versions)
                {
                    if (string.IsNullOrEmpty(v.DigestHex))
                    {
                        continue;
                    }
                    string blobPath = BlobPath(v.DigestHex!);
                    if (!File.Exists(blobPath))
                    {
                        continue;
                    }
                    byte[] content = await ReadAllBytesAsync(blobPath, cancellationToken)
                        .ConfigureAwait(false);
                    versions.Add(new WotResourceVersion(
                        v.VersionId,
                        content,
                        v.ContentType ?? string.Empty,
                        v.Format ?? string.Empty,
                        ParseDate(v.CreatedAt),
                        ParseDate(v.ModifiedAt)));
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
                    ? ImmutableArray<string>.Empty
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
                    Resources = resources.Count == 0 ? null : resources.ToArray()
                });
            }
            return new ManifestDto
            {
                SchemaVersion = CurrentSchemaVersion,
                Generation = snapshot.Generation,
                RegistryLabels = FromLabels(snapshot.Labels),
                Groups = groups.Count == 0 ? null : groups.ToArray()
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
                RootNodeId = resource.RootNodeId?.ToString(),
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
            => Path.Combine(m_blobsFolder, digestHex + ".bin");

        private void PruneOrphanBlobs(HashSet<string> referenced)
        {
            try
            {
                if (!Directory.Exists(m_blobsFolder))
                {
                    return;
                }
                foreach (string existing in Directory.EnumerateFiles(m_blobsFolder, "*.bin"))
                {
                    string name = Path.GetFileNameWithoutExtension(existing);
                    if (!referenced.Contains(name))
                    {
                        TryDelete(existing);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static async ValueTask<T?> ReadJsonAsync<T>(
            string path,
            JsonTypeInfo<T> typeInfo,
            CancellationToken cancellationToken)
            where T : class
        {
            try
            {
                byte[] bytes = await ReadAllBytesAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                return JsonSerializer.Deserialize(bytes, typeInfo);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes <paramref name="bytes"/> to a fresh content-addressed blob:
        /// write-through to a temp file, then move it into place. The temp file is
        /// flushed to disk so the blob is durable before the manifest that
        /// references it is committed.
        /// </summary>
        private static async ValueTask DurableWriteAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await WriteThroughAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
                if (File.Exists(path))
                {
                    // A blob with this digest already exists; the content is
                    // identical, so keep the existing durable copy.
                    TryDelete(tmp);
                    return;
                }
                File.Move(tmp, path);
                tmp = null!;
            }
            finally
            {
                if (tmp is not null)
                {
                    TryDelete(tmp);
                }
            }
        }

        /// <summary>
        /// Atomically replaces <paramref name="path"/> with <paramref name="bytes"/>:
        /// write-through to a temp file, then <see cref="File.Replace(string, string, string?)"/>
        /// (or an initial move). This is the commit point for the manifest pointer.
        /// </summary>
        private static async ValueTask AtomicReplaceAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await WriteThroughAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
                if (File.Exists(path))
                {
                    // Atomic replace-in-place on the same volume.
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        private static async ValueTask WriteThroughAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
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
#if NETSTANDARD2_1_OR_GREATER || NET
            await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
            await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
            => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

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

        private static NodeId? ParseNodeId(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            try
            {
                return NodeId.Parse(value);
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private const string ManifestFile = "manifest.json";
        private const int CurrentSchemaVersion = 2;

        private readonly string m_root;
        private readonly string m_blobsFolder;

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
    internal sealed partial class WotRegistryStoreJson : JsonSerializerContext
    {
    }
}
