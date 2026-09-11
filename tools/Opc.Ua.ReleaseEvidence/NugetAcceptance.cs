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
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Checks actual NuGet package membership, inventories, licenses, and bills of materials against release evidence.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class NugetAcceptance(EvidenceFiles files)
    {
        /// <summary>
        /// Adds findings for package-set mismatches and incomplete or inconsistent payload, license, and BOM coverage.
        /// </summary>
        public async Task VerifyAsync(
            string repositoryRoot,
            string bundleRoot,
            string artifactsRoot,
            EvidenceEnvelope envelope,
            PolicyConfiguration policy,
            List<Finding> findings,
            CancellationToken cancellationToken)
        {
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == "nuget");
            Dictionary<string, string> archivesByDigest = await files.IndexArchivesAsync(
                artifactsRoot, cancellationToken).ConfigureAwait(false);
            DocumentRecord[] inputDocuments = [.. envelope.Documents.Where(d => d.Type == "input-manifest")];
            if (inputDocuments.Length != 1)
            {
                findings.Add(new Finding("ARTIFACT_MEMBERSHIP", "One final evaluated package mapping is required."));
            }
            else
            {
                string path = EvidenceFiles.Confined(bundleRoot, inputDocuments[0].Path);
                if (File.Exists(path))
                {
                    SourceInputsRecord inputs = await files.ReadModelAsync(
                        path, EvidenceJsonContext.Default.SourceInputsRecord, cancellationToken).ConfigureAwait(false);
                    await CheckMembershipAsync(repositoryRoot, group, envelope, inputs, findings, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            foreach (ArtifactRecord artifact in envelope.Artifacts)
            {
                DocumentRecord[] inventories = [.. envelope.Documents.Where(d => d.Type == "inventory" &&
                    d.Subject.Kind == artifact.Kind && d.Subject.Id == artifact.Id &&
                    d.Subject.Digest == artifact.Digest)];
                DocumentRecord[] boms = [.. envelope.Documents.Where(d => d.Type == "sbom" &&
                    d.Subject.Kind == artifact.Kind && d.Subject.Id == artifact.Id &&
                    d.Subject.Digest == artifact.Digest)];
                if (inventories.Length != 1 || boms.Length != 1)
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "Each package needs one inventory and BOM."));
                    continue;
                }
                if (!File.Exists(EvidenceFiles.Confined(bundleRoot, inventories[0].Path)) ||
                    !File.Exists(EvidenceFiles.Confined(bundleRoot, boms[0].Path)))
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "A referenced inventory or BOM is missing."));
                    continue;
                }
                PackageInventory inventory = await files.ReadModelAsync(
                    EvidenceFiles.Confined(bundleRoot, inventories[0].Path),
                    EvidenceJsonContext.Default.PackageInventory, cancellationToken).ConfigureAwait(false);
                if (inventory.Licenses.Length == 0 ||
                    inventory.Licenses.Any(l => l.Kind is not ("expression" or "file" or "url")) ||
                    inventory.ResolvedGraphs.Any(g => inventory.Payloads.Any(p =>
                        p.Owner == g.Id && p.Version == g.Version) &&
                        (g.Licenses.Length == 0 ||
                            g.Licenses.Any(l => l.Kind is not ("expression" or "file" or "url")))))
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "Shipped component licenses remain unknown."));
                }
                using JsonDocument expectedBom = JsonDocument.Parse(CycloneDxInventory.Serialize(inventory));
                using JsonDocument actualBom = await files.ReadJsonAsync(
                    EvidenceFiles.Confined(bundleRoot, boms[0].Path), cancellationToken).ConfigureAwait(false);
                if (!JsonElement.DeepEquals(expectedBom.RootElement, actualBom.RootElement))
                {
                    findings.Add(new Finding(
                        "INVENTORY_COMPLETE", "The BOM does not cover the complete reconciled payload and graphs."));
                }
                if (!archivesByDigest.TryGetValue(artifact.Digest, out string? archivePath))
                {
                    continue;
                }
                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                PackageReconciler.ValidateEntries(archive);
                ZipArchiveEntry nuspec = archive.Entries.Single(e =>
                    !e.FullName.Contains('/', StringComparison.Ordinal) &&
                    e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                using Stream specification = nuspec.Open();
                using var buffer = new MemoryStream();
                await specification.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                XElement metadata = NuspecMetadata.Read(buffer.ToArray());
                ZipArchiveEntry[] entries = [.. archive.Entries.Where(e => !e.FullName.EndsWith('/'))];
                if (inventory.Payloads.Length != entries.Length ||
                    inventory.Payloads.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                        inventory.Payloads.Length)
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "Inventory must enumerate every entry once."));
                    continue;
                }
                foreach (ZipArchiveEntry entry in entries)
                {
                    InventoryPayload? payload = inventory.Payloads.SingleOrDefault(p => p.Path == entry.FullName);
                    using Stream content = entry.Open();
                    string digest = "sha256:" + Convert.ToHexStringLower(
                        await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false));
                    bool metadataEntry = PackageReconciler.IsMetadata(entry.FullName, metadata);
                    if (payload == null || payload.Digest != digest ||
                        (payload.Classification == "metadata") != metadataEntry ||
                        (!metadataEntry && (payload.Owner == null || payload.Version == null ||
                            !inventory.ResolvedGraphs.Any(g =>
                                g.Id == payload.Owner && g.Version == payload.Version) &&
                                payload.Owner != artifact.Id)))
                    {
                        findings.Add(new Finding(
                            "INVENTORY_COMPLETE", "Actual payload content/ownership is missing or misclassified."));
                    }
                }
            }
        }

        private async Task CheckMembershipAsync(
            string root,
            ArtifactGroup group,
            EvidenceEnvelope envelope,
            SourceInputsRecord inputs,
            List<Finding> findings,
            CancellationToken cancellationToken)
        {
            string[] modern = [.. (await File.ReadAllLinesAsync(
                EvidenceFiles.Confined(root, group.ModernCatalog!), cancellationToken).ConfigureAwait(false))
                .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#'))];
            if (inputs.SchemaVersion != 1 || inputs.Source != envelope.Source ||
                inputs.BuildInputDigests.Length == 0 ||
                inputs.BuildInputDigests.Any(d => !VerificationControls.IsDigest(d)) ||
                inputs.Mappings.Any(m => !VerificationControls.IsDigest(m.GraphDigest) ||
                    !Versions.Equal(m.Version, envelope.Release.Version)) ||
                inputs.Mappings.Select(m => m.Project + "|" + m.Configuration)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Mappings.Length ||
                inputs.CapturedProducers?.Any(p =>
                    p.System != envelope.Producer.System || p.Workflow != envelope.Producer.Workflow ||
                    p.DefinitionSha != envelope.Producer.DefinitionSha ||
                    p.RunId != envelope.Producer.RunId || p.Attempt != envelope.Producer.Attempt) == true)
            {
                findings.Add(new Finding("INPUT_IDENTITY", "Frozen source/configuration/graph mappings are invalid."));
            }
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in modern)
            {
                MappingSummary[] release = [.. inputs.Mappings.Where(m =>
                    m.IsPackable && m.Configuration == "Release" && m.PackageId == id)];
                if (release.Length != 1 ||
                    !inputs.Mappings.Any(m => m.Project == release[0].Project && m.Configuration == "Debug"))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_MEMBERSHIP", "Modern package Release/Debug configuration scope is incomplete."));
                }
                expected.Add("nuget-package|Release|" + id);
            }
            foreach (MappingSummary mapping in inputs.Mappings.Where(m => m.IsPackable))
            {
                bool release = mapping.Configuration == "Release" &&
                    modern.Contains(mapping.PackageId, StringComparer.OrdinalIgnoreCase);
                bool debug = mapping.Configuration == "Debug" &&
                    mapping.PackageId.EndsWith(".Debug", StringComparison.Ordinal) &&
                    modern.Contains(mapping.PackageId[..^6], StringComparer.OrdinalIgnoreCase);
                if (!release && !debug)
                {
                    continue;
                }
                expected.Add("nuget-package|" + mapping.Configuration + "|" + mapping.PackageId);
                if (mapping.IncludeSymbols && mapping.SymbolPackageFormat == "snupkg" && mapping.HasPdb)
                {
                    expected.Add("nuget-symbols|" + mapping.Configuration + "|" + mapping.PackageId);
                }
            }
            foreach (string source in group.Variants!.Where(v => v.Id == "metapackages").SelectMany(v => v.Sources!))
            {
                string id = NuspecMetadata.Required(NuspecMetadata.Read(
                    await files.ReadAsync(EvidenceFiles.Confined(root, source), cancellationToken)
                        .ConfigureAwait(false)), "id");
                expected.Add("nuget-package|Release|" + id);
            }
            string[] actual = [.. envelope.Artifacts.Select(a => a.Kind + "|" + a.Configuration + "|" + a.Id)];
            if (!expected.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                actual.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new Finding("ARTIFACT_MEMBERSHIP",
                    "Package group differs from the final modern/Debug/metapackage/symbol catalog."));
            }
        }
    }
}
