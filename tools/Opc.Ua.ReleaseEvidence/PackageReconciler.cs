// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NuGet.Versioning;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class PackageReconciler(EvidenceFiles files)
    {
        public async Task<PackageInventory> ReconcileAsync(
            string archivePath,
            string releaseVersion,
            ProjectMapping[] allMappings,
            string[] metapackageIds,
            CancellationToken cancellationToken)
        {
            EvidenceFiles.RejectLinks(archivePath);
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ValidateEntries(archive);
            ZipArchiveEntry[] nuspecs = [.. archive.Entries.Where(e =>
                !e.FullName.Contains('/', StringComparison.Ordinal) &&
                e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))];
            if (nuspecs.Length != 1)
            {
                throw new InvalidDataException("A package must contain exactly one root nuspec.");
            }
            XElement metadata = NuspecMetadata.Read(
                await ReadEntryAsync(nuspecs[0], cancellationToken).ConfigureAwait(false));
            string id = NuspecMetadata.Required(metadata, "id");
            string version = NuspecMetadata.Required(metadata, "version");
            if (id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            {
                throw new InvalidDataException("Invalid embedded NuGet package ID.");
            }
            if (!Versions.Equal(version, releaseVersion))
            {
                throw new InvalidDataException("Archive nuspec version differs from the release context.");
            }
            ProjectMapping[] matching = [.. allMappings.Where(m => m.IsPackable &&
                string.Equals(m.PackageId, id, StringComparison.OrdinalIgnoreCase))];
            if (matching.Length > 1)
            {
                throw new InvalidDataException("Archive has multiple evaluated project/configuration owners.");
            }
            bool metapackage = metapackageIds.Contains(id, StringComparer.OrdinalIgnoreCase);
            if (matching.Length == 0 && !metapackage)
            {
                throw new InvalidDataException($"No evaluated package mapping for {id}.");
            }
            string configuration = metapackage ? "Release" : matching[0].Configuration;
            string kind = archivePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
                ? "nuget-symbols" : "nuget-package";
            ProjectMapping[] closure = Closure(matching, allMappings);
            PayloadCandidate[] candidates = [.. closure.SelectMany(m => m.Payloads)];
            var payloads = new List<InventoryPayload>();
            var unmet = new HashSet<string>(StringComparer.Ordinal);
            var tfms = new HashSet<string>(StringComparer.Ordinal);
            var rids = new HashSet<string>(StringComparer.Ordinal);
            var roslyn = new HashSet<string>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }
                using Stream stream = entry.Open();
                string digest = "sha256:" +
                    Convert.ToHexStringLower(
                        await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                PayloadCandidate[] owners = [.. candidates.Where(c => c.Digest == digest &&
                    string.Equals(Path.GetFileName(c.File), Path.GetFileName(entry.FullName),
                        StringComparison.OrdinalIgnoreCase))];
                string[] identities =
                [
                    .. owners.Select(o => o.Origin + "|" + o.Owner + "|" + o.Version).Distinct(StringComparer.Ordinal)
                ];
                bool metadataOnly = IsMetadata(entry.FullName, metadata);
                bool native = IsNative(entry.FullName);
                string classification = metadataOnly ? "metadata"
                    : identities.Length != 1 ? native ? "native-unowned" : "unowned"
                    : entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ? "symbols"
                    : native ? "native" : "shipped";
                if (!metadataOnly && identities.Length != 1)
                {
                    unmet.Add("INVENTORY_COMPLETE");
                }
                string[] targets =
                    [.. owners.Select(o => o.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                string[] bands =
                [
                    .. entry.FullName.Split('/').Where(p => p.StartsWith("roslyn", StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                ];
                if (!metadataOnly)
                {
                    foreach (string target in targets)
                    {
                        tfms.Add(target.Split('/')[0]);
                    }
                    foreach (string band in bands)
                    {
                        roslyn.Add(band);
                    }
                    string[] segments = entry.FullName.Split('/');
                    if (segments.Length > 2 && segments[0] == "runtimes")
                    {
                        rids.Add(segments[1]);
                    }
                }
                payloads.Add(new InventoryPayload(
                    entry.FullName, digest, classification, targets, bands,
                    identities.Length == 1 ? owners[0].Owner : null,
                    identities.Length == 1 ? owners[0].Version : null));
            }
            LicenseRecord[] licenses = NuspecMetadata.Licenses(metadata);
            if (kind == "nuget-symbols" &&
                (matching.Length == 0 ||
                    !matching[0].IncludeSymbols ||
                    payloads.Any(p => p.Classification != "metadata" &&
                        !p.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))))
            {
                unmet.Add("ARTIFACT_MEMBERSHIP");
            }
            foreach (ResolvedComponent component in closure.SelectMany(m => m.Components))
            {
                if (payloads.Any(p => p.Owner == component.Id && p.Version == component.Version) &&
                    (component.Licenses.Length == 0 ||
                        component.Licenses.Any(l =>
                            l.Kind == "unknown" || (l.Kind == "file" && l.Digest == null))))
                {
                    unmet.Add("INVENTORY_COMPLETE");
                }
            }
            for (int i = 0; i < licenses.Length; i++)
            {
                if (licenses[i].Kind == "file")
                {
                    EvidenceFiles.ValidateRelative(licenses[i].Value);
                    InventoryPayload? license = payloads.SingleOrDefault(p => p.Path == licenses[i].Value);
                    licenses[i] = licenses[i] with { Digest = license?.Digest };
                    if (license == null)
                    {
                        unmet.Add("INVENTORY_COMPLETE");
                    }
                }
                if (licenses[i].Kind == "unknown")
                {
                    unmet.Add("INVENTORY_COMPLETE");
                }
            }
            var consumers = new List<ConsumerDependency>();
            foreach (XElement dependency in metadata.Descendants().Where(e => e.Name.LocalName == "dependency"))
            {
                string dependencyId = dependency.Attribute("id")?.Value ??
                    throw new InvalidDataException("A declared dependency has no ID.");
                string range = dependency.Attribute("version")?.Value ?? string.Empty;
                if (!VersionRange.TryParse(range, out _))
                {
                    throw new InvalidDataException("A declared consumer dependency has an invalid version range.");
                }
                consumers.Add(new ConsumerDependency(
                    dependencyId, range, dependency.Parent?.Attribute("targetFramework")?.Value ?? "any"));
            }
            string[] prerequisites = [.. metadata.Descendants()
                .Where(e => e.Name.LocalName is "frameworkAssembly" or "frameworkReference")
                .Select(e => e.Attribute("assemblyName")?.Value ?? e.Attribute("name")?.Value ?? "unknown")
                .Order(StringComparer.Ordinal)];
            var artifact = new ArtifactRecord(
                kind, id, version, configuration,
                await files.DigestAsync(archivePath, cancellationToken).ConfigureAwait(false),
                new ScopeRecord(
                    [.. tfms.Order(StringComparer.Ordinal)], [.. rids.Order(StringComparer.Ordinal)],
                    [.. roslyn.Order(StringComparer.Ordinal)], []), new FileInfo(archivePath).Length);
            return new PackageInventory(
                1, artifact, licenses,
                [
                    .. consumers.OrderBy(c => c.Target, StringComparer.Ordinal)
                        .ThenBy(c => c.Id, StringComparer.Ordinal)
                ],
                prerequisites,
                [.. closure.SelectMany(m => m.Components).OrderBy(c => c.Project, StringComparer.Ordinal)
                    .ThenBy(c => c.Target, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal)],
                [.. payloads], [.. unmet.Order(StringComparer.Ordinal)]);
        }

        private static ProjectMapping[] Closure(ProjectMapping[] roots, ProjectMapping[] mappings)
        {
            var found = new List<ProjectMapping>();
            var queue = new Queue<ProjectMapping>(roots);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.TryDequeue(out ProjectMapping? mapping))
            {
                if (!seen.Add(mapping.Project + "|" + mapping.Configuration))
                {
                    continue;
                }
                found.Add(mapping);
                foreach (string reference in mapping.References)
                {
                    ProjectMapping? child = mappings.SingleOrDefault(m =>
                        m.Project == reference &&
                        m.Configuration == mapping
                            .Configuration) ??
                        throw new InvalidDataException("A contributing project is missing from the frozen inputs.");
                    queue.Enqueue(child);
                }
            }
            return [.. found];
        }

        internal static void ValidateEntries(ZipArchive archive)
        {
            if (archive.Entries.Count is 0 or > 100000)
            {
                throw new InvalidDataException("Empty archive or excessive archive entry count.");
            }
            long total = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                EvidenceFiles.ValidateRelative(entry.FullName.TrimEnd('/'));
                if (!names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                {
                    throw new InvalidDataException("Duplicate archive path or symbolic link.");
                }
                total = checked(total + entry.Length);
                if (entry.Length > 256L * 1024 * 1024 ||
                    total > 2L * 1024 * 1024 * 1024 ||
                    (entry.Length > 1024 * 1024 && entry.Length / Math.Max(entry.CompressedLength, 1) > 1000))
                {
                    throw new InvalidDataException("Archive exceeds safe size or compression-ratio limits.");
                }
            }
        }

        private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
        {
            if (entry.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Nuspec exceeds the document-size limit.");
            }
            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        internal static bool IsMetadata(string path, XElement metadata)
        {
            if (path.StartsWith("lib/", StringComparison.Ordinal) ||
                path.StartsWith("runtimes/", StringComparison.Ordinal) ||
                path.StartsWith("analyzers/", StringComparison.Ordinal))
            {
                return false;
            }
            return path is "[Content_Types].xml" or ".signature.p7s" ||
                path == "_rels/.rels" ||
                (path.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) &&
                    path.EndsWith(".psmdcp", StringComparison.Ordinal)) ||
                (!path.Contains('/', StringComparison.Ordinal) &&
                    path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) ||
                metadata.Elements().Any(e =>
                    (e.Name.LocalName is "readme" or "icon" ||
                        e.Name.LocalName == "license" && e.Attribute("type")?.Value == "file") &&
                    e.Value == path);
        }

        private static bool IsNative(string path)
        {
            return path.Contains("/native/", StringComparison.Ordinal) ||
                new[] { ".so", ".dylib", ".a", ".lib" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }
    }
}
