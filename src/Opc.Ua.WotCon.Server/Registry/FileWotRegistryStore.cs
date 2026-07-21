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
using System.Security.Cryptography;
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
    /// A durable, file-backed registry store. Each group and resource is stored
    /// under a content-addressed directory; metadata is written with a bounded
    /// atomic replace (write-to-temp then <see cref="File.Replace(string, string, string?)"/>)
    /// so a crash never leaves a half-written record. Invalid documents are
    /// persisted with their failure state so a restart restores exactly the last
    /// observed registry contents.
    /// </summary>
    public sealed class FileWotRegistryStore : IWotRegistryStore
    {
        /// <summary>
        /// Initializes a new file-backed store rooted at <paramref name="rootFolder"/>.
        /// </summary>
        public FileWotRegistryStore(string rootFolder)
        {
            m_root = rootFolder ?? throw new ArgumentNullException(nameof(rootFolder));
            m_groupsFolder = Path.Combine(m_root, "groups");
        }

        /// <inheritdoc/>
        public async ValueTask<WotRegistrySnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            RegistryDto? registryDto = null;
            string registryMetaPath = Path.Combine(m_root, RegistryMetaFile);
            if (File.Exists(registryMetaPath))
            {
                registryDto = await ReadJsonAsync(
                        registryMetaPath, WotRegistryStoreJson.Default.RegistryDto, cancellationToken)
                    .ConfigureAwait(false);
            }
            ImmutableSortedDictionary<string, string> registryLabels = ToLabels(registryDto?.Labels);
            long generation = registryDto?.Epoch ?? 0;

            if (!Directory.Exists(m_groupsFolder))
            {
                return new WotRegistrySnapshot(
                    generation, ImmutableDictionary<string, WotResourceGroup>.Empty, registryLabels);
            }

            var groups = ImmutableDictionary.CreateBuilder<string, WotResourceGroup>();

            foreach (string groupDir in Directory.EnumerateDirectories(m_groupsFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string groupMetaPath = Path.Combine(groupDir, GroupMetaFile);
                if (!File.Exists(groupMetaPath))
                {
                    continue;
                }
                GroupDto? groupDto = await ReadJsonAsync(
                        groupMetaPath, WotRegistryStoreJson.Default.GroupDto, cancellationToken)
                    .ConfigureAwait(false);
                if (groupDto is null)
                {
                    continue;
                }

                var resources = ImmutableDictionary.CreateBuilder<string, WotResource>();
                string resourcesDir = Path.Combine(groupDir, "resources");
                if (Directory.Exists(resourcesDir))
                {
                    foreach (string resourceDir in Directory.EnumerateDirectories(resourcesDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        WotResource? resource = await LoadResourceAsync(
                            resourceDir, cancellationToken).ConfigureAwait(false);
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

            return new WotRegistrySnapshot(generation, groups.ToImmutable(), registryLabels);
        }

        /// <inheritdoc/>
        public async ValueTask UpsertGroupAsync(
            WotResourceGroup group,
            CancellationToken cancellationToken = default)
        {
            string groupDir = GroupDirectory(group.GroupId);
            Directory.CreateDirectory(groupDir);
            var dto = new GroupDto
            {
                GroupId = group.GroupId,
                Kind = (int)group.Kind,
                Name = group.Name,
                Description = group.Description,
                Epoch = group.Epoch,
                Labels = FromLabels(group.Labels)
            };
            await WriteJsonAsync(
                    Path.Combine(groupDir, GroupMetaFile),
                    dto,
                    WotRegistryStoreJson.Default.GroupDto,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask UpsertRegistryLabelsAsync(
            ImmutableSortedDictionary<string, string> labels,
            long epoch,
            CancellationToken cancellationToken = default)
        {
            var dto = new RegistryDto
            {
                Epoch = epoch,
                Labels = FromLabels(labels)
            };
            await WriteJsonAsync(
                    Path.Combine(m_root, RegistryMetaFile),
                    dto,
                    WotRegistryStoreJson.Default.RegistryDto,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask UpsertResourceAsync(
            WotResource resource,
            CancellationToken cancellationToken = default)
        {
            string resourceDir = ResourceDirectory(resource.GroupId, resource.ResourceId);
            string versionsDir = Path.Combine(resourceDir, "versions");
            Directory.CreateDirectory(versionsDir);

            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotResourceVersion version in resource.Versions)
            {
                string binPath = Path.Combine(versionsDir, Hash(version.VersionId) + ".bin");
                keep.Add(Path.GetFileName(binPath));
                if (!File.Exists(binPath))
                {
                    await AtomicWriteAsync(
                        binPath, version.Content.ToArray(), cancellationToken).ConfigureAwait(false);
                }
            }
            // Remove version blobs no longer retained (retention trimming).
            foreach (string existing in Directory.EnumerateFiles(versionsDir, "*.bin"))
            {
                if (!keep.Contains(Path.GetFileName(existing)))
                {
                    TryDelete(existing);
                }
            }

            var dto = ToDto(resource);
            await WriteJsonAsync(
                    Path.Combine(resourceDir, ResourceMetaFile),
                    dto,
                    WotRegistryStoreJson.Default.ResourceDto,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask RemoveResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            string resourceDir = ResourceDirectory(groupId, resourceId);
            TryDeleteDirectory(resourceDir);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask RemoveGroupAsync(
            string groupId,
            CancellationToken cancellationToken = default)
        {
            TryDeleteDirectory(GroupDirectory(groupId));
            return default;
        }

        private async ValueTask<WotResource?> LoadResourceAsync(
            string resourceDir,
            CancellationToken cancellationToken)
        {
            string metaPath = Path.Combine(resourceDir, ResourceMetaFile);
            if (!File.Exists(metaPath))
            {
                return null;
            }
            ResourceDto? dto = await ReadJsonAsync(
                    metaPath, WotRegistryStoreJson.Default.ResourceDto, cancellationToken)
                .ConfigureAwait(false);
            if (dto is null)
            {
                return null;
            }

            string versionsDir = Path.Combine(resourceDir, "versions");
            var versions = ImmutableArray.CreateBuilder<WotResourceVersion>();
            if (dto.Versions is not null)
            {
                foreach (VersionDto v in dto.Versions)
                {
                    string binPath = Path.Combine(versionsDir, Hash(v.VersionId) + ".bin");
                    if (!File.Exists(binPath))
                    {
                        continue;
                    }
                    byte[] content = await ReadAllBytesAsync(binPath, cancellationToken)
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
                Versions = versions,
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

        private string GroupDirectory(string groupId)
            => Path.Combine(m_groupsFolder, Hash(groupId));

        private string ResourceDirectory(string groupId, string resourceId)
            => Path.Combine(GroupDirectory(groupId), "resources", Hash(resourceId));

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

        private static async ValueTask WriteJsonAsync<T>(
            string path,
            T value,
            JsonTypeInfo<T> typeInfo,
            CancellationToken cancellationToken)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
            await AtomicWriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask AtomicWriteAsync(
            string path,
            byte[] bytes,
            CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
#if NETSTANDARD2_1_OR_GREATER || NET
                await File.WriteAllBytesAsync(tmp, bytes, cancellationToken).ConfigureAwait(false);
#else
                File.WriteAllBytes(tmp, bytes);
                await Task.CompletedTask.ConfigureAwait(false);
#endif
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

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(40);
            for (int i = 0; i < 20 && i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
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

        private const string GroupMetaFile = "group.json";
        private const string ResourceMetaFile = "resource.json";
        private const string RegistryMetaFile = "registry.json";

        private readonly string m_root;
        private readonly string m_groupsFolder;

        internal sealed class GroupDto
        {
            public string GroupId { get; set; } = string.Empty;
            public int Kind { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public long Epoch { get; set; }
            public Dictionary<string, string>? Labels { get; set; }
        }

        internal sealed class RegistryDto
        {
            public long Epoch { get; set; }
            public Dictionary<string, string>? Labels { get; set; }
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
    [JsonSerializable(typeof(FileWotRegistryStore.GroupDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.ResourceDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.VersionDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.ValidationDto))]
    [JsonSerializable(typeof(FileWotRegistryStore.RegistryDto))]
    internal sealed partial class WotRegistryStoreJson : JsonSerializerContext
    {
    }
}
