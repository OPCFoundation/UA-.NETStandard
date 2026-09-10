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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Produces NuGet release evidence from immutable build inputs and reconciled package archives.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="reconciler">
    /// The service that maps archive payloads to evaluated package and dependency ownership.
    /// </param>
    internal sealed class NugetEvidence(EvidenceFiles files, PackageReconciler reconciler)
    {
        /// <summary>
        /// Generates package inventories, bills of materials, source mappings, and a release-evidence envelope.
        /// </summary>
        public async Task<int> GenerateAsync(
            string repositoryRoot,
            string packages,
            string[] inputs,
            string contextPath,
            string? manifestPath,
            string output,
            CancellationToken cancellationToken)
        {
            EvaluationExpectation context = await files.ReadModelAsync(
                contextPath, EvidenceJsonContext.Default.EvaluationExpectation, cancellationToken)
                .ConfigureAwait(false);
            Versions.ValidateIdentity(context.Source, context.Producer);
            Versions.Parse(context.Release.Version);
            if (context.Release.Group != "nuget" ||
                inputs.Length == 0 ||
                context.Release.Channel is not ("stable" or "preview" or "development"))
            {
                throw new InvalidDataException(
                    "NuGet generation requires explicit NuGet context and frozen input bundles.");
            }
            EvidenceFiles.RejectLinks(output);
            EvidenceFiles.RejectLinks(packages);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                throw new InvalidDataException("Evidence output must be empty.");
            }
            Directory.CreateDirectory(output);
            var bundles = new List<FrozenBundle>();
            var inputDigests = new List<string>();
            var unmet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string input in inputs)
            {
                string path = EvidenceFiles.Confined(input, "build-inputs.json");
                FrozenBundle bundle = await files.ReadModelAsync(
                    path, EvidenceJsonContext.Default.FrozenBundle, cancellationToken).ConfigureAwait(false);
                if (bundle.SchemaVersion != 1 ||
                    bundle.Source != context.Source ||
                    bundle.Producer.System != context.Producer.System ||
                    bundle.Producer.Workflow != context.Producer.Workflow ||
                    bundle.Producer.DefinitionSha != context.Producer.DefinitionSha ||
                    bundle.Producer.RunId != context.Producer.RunId ||
                    bundle.Producer.Attempt != context.Producer.Attempt ||
                    !Versions.Equal(bundle.Version, context.Release.Version))
                {
                    throw new InvalidDataException(
                        "Frozen inputs have a different source/version/build attempt or unsupported schema.");
                }
                foreach (FrozenFile frozen in bundle.Contracts.Concat(bundle.Mappings.Select(m => m.Assets)))
                {
                    string frozenPath = EvidenceFiles.Confined(input, frozen.Path);
                    string frozenDigest = await files.DigestAsync(frozenPath, cancellationToken).ConfigureAwait(false);
                    if (frozenDigest != frozen.Digest ||
                        new FileInfo(frozenPath).Length != frozen.Size)
                    {
                        throw new InvalidDataException("Frozen input bytes changed after capture.");
                    }
                }
                inputDigests.Add(await files.DigestAsync(path, cancellationToken).ConfigureAwait(false));
                bundles.Add(bundle);
                unmet.UnionWith(bundle.UnmetControls);
            }
            ProjectMapping[] mappings = [.. bundles.SelectMany(b => b.Mappings)];
            if (mappings.Select(m => m.Project + "|" + m.Configuration)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                mappings.Length)
            {
                throw new InvalidDataException("Duplicate project/configuration input bundle.");
            }
            PolicyConfiguration policy = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release/policy.json"),
                EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ProfilesConfiguration profiles = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.AssuranceProfiles),
                EvidenceJsonContext.Default.ProfilesConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == "nuget");
            var metapackages = new List<string>();
            foreach (string source in group.Variants!.Where(v => v.Id == "metapackages").SelectMany(v => v.Sources!))
            {
                metapackages.Add(NuspecMetadata.Required(NuspecMetadata.Read(await files.ReadAsync(
                    EvidenceFiles.Confined(repositoryRoot, source), cancellationToken).ConfigureAwait(false)), "id"));
            }
            var inventories = new List<PackageInventory>();
            var archives = new List<ArchiveRecord>();
            string[] paths = [.. Directory.GetFiles(packages).Where(p =>
                p.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];
            if (paths.Length == 0)
            {
                throw new InvalidDataException("No NuGet archives were supplied.");
            }
            foreach (string path in paths)
            {
                PackageInventory inventory = await reconciler.ReconcileAsync(
                    path, context.Release.Version, mappings, [.. metapackages], cancellationToken)
                    .ConfigureAwait(false);
                inventories.Add(inventory);
                unmet.UnionWith(inventory.UnmetControls);
                archives.Add(new ArchiveRecord(
                    inventory.Artifact.Id, inventory.Artifact.Version,
                    inventory.Artifact.Kind == "nuget-symbols" ? "symbols" : "package",
                    Path.GetFileName(path), inventory.Artifact.Digest[7..]));
            }
            if (inventories.Select(i => i.Artifact.Id + "|" + i.Artifact.Kind)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != inventories.Count)
            {
                throw new InvalidDataException("Duplicate archive package identities.");
            }
            CheckMembership(repositoryRoot, group, mappings, inventories, metapackages, unmet);
            var documents = new List<DocumentRecord>();
            var sourceInputs = new SourceInputsRecord(
                1, context.Source, [.. inputDigests.Order(StringComparer.Ordinal)],
                [.. mappings.Select(m => new MappingSummary(
                    m.Project, m.Configuration, m.PackageId, m.PackageVersion, m.IsPackable, m.IncludeSymbols,
                    m.SymbolPackageFormat, m.Payloads.Any(p => p.Origin == "project:" + m.Project &&
                        p.File.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)), m.Assets.Digest))],
                [.. bundles.Select(b => b.Producer)]);
            await files.WriteModelAsync(
                Path.Combine(output, "source-inputs.json"), sourceInputs,
                EvidenceJsonContext.Default.SourceInputsRecord, cancellationToken).ConfigureAwait(false);
            string sourceDigest = await files.DigestAsync(
                Path.Combine(output, "source-inputs.json"), cancellationToken)
                .ConfigureAwait(false);
            var sourceSubject = new SubjectRecord(
                "source", context.Source.Repository + "@" + context.Source.ActualSha, sourceDigest);
            documents.Add(new DocumentRecord(
                "input-manifest", "json", "1", "source-inputs.json", sourceDigest, sourceSubject));
            foreach ((string path, string type) in new[]
            {
                (".azurepipelines/release/policy.json", "policy"),
                (policy.ArtifactCatalog, "artifact-catalog"),
                (policy.AssuranceProfiles, "profile")
            })
            {
                string destination = "contracts/" + Path.GetFileName(path);
                Directory.CreateDirectory(Path.Combine(output, "contracts"));
                byte[] bytes = await files.ReadAsync(EvidenceFiles.Confined(repositoryRoot, path), cancellationToken)
                    .ConfigureAwait(false);
                await File.WriteAllBytesAsync(
                    EvidenceFiles.Confined(output, destination), bytes, cancellationToken).ConfigureAwait(false);
                documents.Add(new DocumentRecord(
                    type, "json", "1.0.0", destination, EvidenceFiles.Digest(bytes), sourceSubject));
            }
            if (manifestPath != null)
            {
                ArchiveManifest manifest = await files.ReadModelAsync(
                    manifestPath, EvidenceJsonContext.Default.ArchiveManifest, cancellationToken)
                    .ConfigureAwait(false);
                if (manifest.SchemaVersion != 1 ||
                    manifest.Repository != context.Source.Repository ||
                    manifest.Commit != context.Source.ActualSha ||
                    manifest.RunId != context.Producer.RunId ||
                    manifest.Workflow != context.Producer.Workflow ||
                    manifest.Ref != context.Source.ActualRef ||
                    manifest.PackageCount != archives.Count(a => a.Type == "package") ||
                    manifest.SymbolPackageCount != archives.Count(a => a.Type == "symbols") ||
                    manifest.DebugPackageCount != inventories.Count(i =>
                        i.Artifact.Kind == "nuget-package" && i.Artifact.Configuration == "Debug") ||
                    !Versions.Equal(manifest.PackageVersion, context.Release.Version) ||
                    !manifest.Archives.OrderBy(a => a.File, StringComparer.Ordinal).SequenceEqual(
                        archives.OrderBy(a => a.File, StringComparer.Ordinal)))
                {
                    throw new InvalidDataException(
                        "Existing v1 manifest does not match source, producer or actual archives.");
                }
                byte[] bytes = await files.ReadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                string path = Path.Combine(output, "release-manifest.json");
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                string digest = EvidenceFiles.Digest(bytes);
                documents.Add(new DocumentRecord(
                    "archive-manifest", "json", "1", "release-manifest.json", digest,
                    new SubjectRecord("artifact-set", "nuget", digest)));
            }
            else
            {
                unmet.Add("ARTIFACT_INTEGRITY");
            }
            foreach (PackageInventory inventory in inventories)
            {
                ArtifactRecord artifact = inventory.Artifact;
                string prefix = "packages/" +
                    artifact.Id +
                    "." +
                    Versions.Parse(artifact.Version).ToNormalizedString() +
                    "." +
                    artifact.Kind;
                string inventoryPath = prefix + ".inventory.json";
                await files.WriteModelAsync(
                    EvidenceFiles.Confined(output, inventoryPath), inventory,
                    EvidenceJsonContext.Default.PackageInventory, cancellationToken).ConfigureAwait(false);
                string bomPath = prefix + ".cdx.json";
                await File.WriteAllTextAsync(
                    EvidenceFiles.Confined(output, bomPath), CycloneDxInventory.Serialize(inventory),
                    cancellationToken)
                    .ConfigureAwait(false);
                var subject = new SubjectRecord(artifact.Kind, artifact.Id, artifact.Digest);
                documents.Add(new DocumentRecord(
                    "inventory", "json", "1", inventoryPath,
                    await files.DigestAsync(EvidenceFiles.Confined(output, inventoryPath), cancellationToken)
                        .ConfigureAwait(false),
                    subject));
                documents.Add(new DocumentRecord(
                    "sbom", "CycloneDX", "1.6", bomPath,
                    await files.DigestAsync(EvidenceFiles.Confined(output, bomPath), cancellationToken)
                        .ConfigureAwait(false),
                    subject));
            }
            JobRecord[] jobs = [.. profiles.Profiles.Where(p => group.Profiles.Contains(p.Id, StringComparer.Ordinal))
                .SelectMany(p => p.Jobs.Select(j => new JobRecord(
                    j.Id, p.Id, j.Project, p.Configuration, p.Host, p.HostTfm, p.LibraryTfm, p.Platform,
                    "all", p.Filter, false, "missing", [])))];
            string[] observedControls = [.. unmet.Order(StringComparer.Ordinal)];
            string[] pendingControls =
            [
                "SOURCE_IDENTITY", "PRODUCER_IDENTITY", "POLICY_IDENTITY", "PROVENANCE_VERIFIED",
                "SIGNATURE_VERIFIED", "ASSURANCE_COMPLETE", "INPUT_IDENTITY", "EVIDENCE_FRESHNESS",
                "PUBLIC_EVIDENCE_SAFE", "PRODUCER_NOT_READY", "PUBLISHER_BOUNDARY"
            ];
            unmet.UnionWith(pendingControls);
            if (context.Release.Channel != "development")
            {
                unmet.Add("RELEASE_INTENT");
                pendingControls = [.. pendingControls, "RELEASE_INTENT"];
            }
            pendingControls = [.. pendingControls.Except(observedControls, StringComparer.Ordinal)];
            var envelope = new EvidenceEnvelope(
                2, context.Source, context.Producer, context.Release,
                new PolicyRecord(policy.Id, policy.Version,
                    await files.DigestAsync(
                        EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release/policy.json"),
                        cancellationToken).ConfigureAwait(false), policy.Stage),
                [.. inventories.Select(i => i.Artifact)], [.. documents],
                new AssuranceRecord(
                    group.Profiles, jobs.Length, 0, 0, 0, jobs.Length, 0, jobs,
                    [.. documents.Where(d => d.Type is "policy" or "profile" or "artifact-catalog" or "input-manifest")
                        .Select(d => new InputRecord(d.Type, d.Type switch
                        {
                            "artifact-catalog" => "catalog",
                            "input-manifest" => "source-manifest",
                            _ => d.Type
                        }, d.Path, d.Digest))]),
                new AssessmentRecord("incomplete", [.. unmet.Order(StringComparer.Ordinal)],
                    pendingControls, observedControls));
            envelope = await ProducerAssessments.AttachAsync(envelope, output, files, cancellationToken)
                .ConfigureAwait(false);
            await files.WriteModelAsync(
                Path.Combine(output, "release-evidence.json"), envelope,
                EvidenceJsonContext.Default.EvidenceEnvelope, cancellationToken).ConfigureAwait(false);
            return Versions.RequiresStableControls(policy, context.Release) ? 1 : 0;
        }

        private static void CheckMembership(
            string root,
            ArtifactGroup group,
            ProjectMapping[] mappings,
            List<PackageInventory> inventories,
            List<string> metapackages,
            HashSet<string> unmet)
        {
            string[] ids = [.. File.ReadAllLines(EvidenceFiles.Confined(root, group.ModernCatalog!))
                .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#'))];
            if (ids.Length == 0 || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            {
                throw new InvalidDataException("Invalid modern package catalog.");
            }
            string[] expectedIds =
            [
                .. ids,
                .. metapackages,
                .. mappings.Where(m =>
                    m.IsPackable &&
                    m.Configuration == "Debug" &&
                    m.PackageId.EndsWith(".Debug", StringComparison.Ordinal) &&
                    ids.Contains(m.PackageId[..^6], StringComparer.OrdinalIgnoreCase)).Select(m => m.PackageId)
            ];
            string[] actualIds =
                [.. inventories.Where(i => i.Artifact.Kind == "nuget-package").Select(i => i.Artifact.Id)];
            if (!expectedIds.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                actualIds.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase) ||
                !mappings.Any(m => m.Configuration == "Debug" &&
                    m.IsPackable &&
                    m.PackageId.EndsWith(".Debug", StringComparison.Ordinal)))
            {
                unmet.Add("ARTIFACT_MEMBERSHIP");
            }
            foreach (ProjectMapping mapping in mappings.Where(m => m.IsPackable))
            {
                bool retained = actualIds.Contains(mapping.PackageId, StringComparer.OrdinalIgnoreCase);
                bool hasPdb = mapping.Payloads.Any(p => p.Origin == "project:" + mapping.Project &&
                    p.File.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
                bool symbols = inventories.Any(i => i.Artifact.Kind == "nuget-symbols" &&
                    string.Equals(i.Artifact.Id, mapping.PackageId, StringComparison.OrdinalIgnoreCase));
                if (retained &&
                    mapping.IncludeSymbols &&
                    mapping.SymbolPackageFormat == "snupkg" &&
                    hasPdb &&
                    !symbols)
                {
                    unmet.Add("ARTIFACT_MEMBERSHIP");
                }
            }
            if (inventories.Any(i => i.Artifact.Kind == "nuget-symbols" &&
                !actualIds.Contains(i.Artifact.Id, StringComparer.OrdinalIgnoreCase)))
            {
                unmet.Add("ARTIFACT_MEMBERSHIP");
            }
        }
    }
}
