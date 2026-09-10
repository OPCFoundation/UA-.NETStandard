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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Collects reconciled OCI artifacts, attestations, native documents, and observed or pending control gaps.
    /// </summary>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    /// <param name="Attestations">The native OCI attestations discovered during reconciliation.</param>
    /// <param name="Documents">The evidence documents covered by this record.</param>
    /// <param name="Findings">The findings describing failed checks or unmet controls.</param>
    /// <param name="PendingControls">
    /// The unmet controls that still require independent verification rather than describing observed failures.
    /// </param>
    internal sealed record OciAnalysis(
        ArtifactRecord[] Artifacts,
        OciAttestation[] Attestations,
        DocumentRecord[] Documents,
        Finding[] Findings,
        string[]? PendingControls = null);

    /// <summary>
    /// Reconciles downloaded OCI descriptors, filesystem contents, and native attestations against an artifact group.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class OciReconciler(EvidenceFiles files)
    {
        /// <summary>
        /// Writes an offline OCI reconciliation report without treating descriptor checks as independent release
        /// approval.
        /// </summary>
        public async Task<int> ReconcileAsync(
            string repositoryRoot,
            string requestPath,
            string contextPath,
            string output,
            CancellationToken cancellationToken)
        {
            OciRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.OciRequest, cancellationToken).ConfigureAwait(false);
            EvaluationExpectation context = await files.ReadModelAsync(
                contextPath, EvidenceJsonContext.Default.EvaluationExpectation, cancellationToken)
                .ConfigureAwait(false);
            Versions.ValidateIdentity(context.Source, context.Producer);
            Versions.Parse(context.Release.Version);
            if (context.Release.Group is not ("containers" or "pump") ||
                context.Release.Channel is not ("stable" or "preview" or "development") ||
                request.Images.Length == 0 ||
                request.Images.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != request.Images.Length)
            {
                throw new InvalidDataException("OCI reconciliation requires a nonempty, unique approved image group.");
            }
            PolicyConfiguration policy = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release/policy.json"),
                EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == context.Release.Group);
            OciAnalysis analysis = await AnalyzeAsync(
                request, Path.GetDirectoryName(Path.GetFullPath(requestPath))!, context, group, null, cancellationToken)
                .ConfigureAwait(false);
            var findings = new List<Finding>(analysis.Findings);
            foreach (string control in new[]
            {
                "SOURCE_IDENTITY", "PRODUCER_IDENTITY", "POLICY_IDENTITY", "INVENTORY_COMPLETE", "PROVENANCE_VERIFIED",
                "SIGNATURE_VERIFIED", "ASSURANCE_COMPLETE", "RELEASE_INTENT", "INPUT_IDENTITY", "EVIDENCE_FRESHNESS",
                "PUBLIC_EVIDENCE_SAFE", "PRODUCER_NOT_READY", "PUBLISHER_BOUNDARY"
            })
            {
                findings.Add(new Finding(
                    control, "Descriptor reconciliation is not independent release/attestation verification."));
            }
            var report = new OciReport(
                1, "incomplete", group.Id, analysis.Artifacts, analysis.Attestations,
                [.. findings.Select(f => f.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                [.. findings]);
            await files.WriteModelAsync(output, report, EvidenceJsonContext.Default.OciReport, cancellationToken)
                .ConfigureAwait(false);
            return Versions.RequiresStableControls(policy, context.Release) ? 1 : 0;
        }

        /// <summary>
        /// Analyzes OCI group membership, image bytes, and native attestations using optional authenticated build
        /// expectations.
        /// </summary>
        internal async Task<OciAnalysis> AnalyzeAsync(
            OciRequest request,
            string root,
            EvaluationExpectation context,
            ArtifactGroup group,
            Dictionary<string, OciBuildExpectation>? expectedBuilds,
            CancellationToken cancellationToken)
        {
            Versions.ValidateIdentity(context.Source, context.Producer);
            if (group.Id != context.Release.Group || group.Kind != "oci" || request.Images.Length is 0 or > 64 ||
                request.Images.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != request.Images.Length)
            {
                throw new InvalidDataException("OCI adapter requires a unique, nonempty matching artifact group.");
            }
            var findings = new List<Finding>();
            var pendingFindings = new List<Finding>();
            var artifacts = new List<ArtifactRecord>();
            var attestations = new List<OciAttestation>();
            var documents = new List<DocumentRecord>();
            var filesystems = new Dictionary<string, Dictionary<string, OciFileEvidence>>(StringComparer.Ordinal);
            var nativeDocuments = new Dictionary<string, string>(StringComparer.Ordinal);
            if (expectedBuilds != null &&
                expectedBuilds.Values.Any(build => !OciNativeValidation.MatchesBuildContext(build, context)))
            {
                findings.Add(new Finding(
                    "PROVENANCE_VERIFIED", "Native source/tool expectations differ from the authenticated producer."));
            }
            foreach (OciImageInput image in request.Images)
            {
                string layout = EvidenceFiles.Confined(root, image.Layout);
                if (!group.Images!.Any(i => image.Id == group.UpstreamRepositoryPrefix + "/" + i.Id))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_MEMBERSHIP", "An image repository is outside the approved group."));
                }
                await TraverseAsync(
                    image, layout, image.RootDigest, null, null, null, null, context,
                    artifacts, attestations, findings, filesystems, nativeDocuments,
                    new HashSet<string>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
            }
            foreach (ImageConfiguration image in group.Images!)
            {
                string id = group.UpstreamRepositoryPrefix + "/" + image.Id;
                foreach (string platform in group.Platforms!)
                {
                    if (artifacts.Count(a => a.Kind == "oci-manifest" &&
                        a.Id == id &&
                        a.Scopes.Platforms.SequenceEqual([platform], StringComparer.Ordinal)) != 1)
                    {
                        findings.Add(new Finding(
                            "ARTIFACT_MEMBERSHIP", $"Missing or duplicate runnable subject: {id}/{platform}."));
                    }
                }
            }
            if (artifacts.Any(a => a.Kind == "oci-manifest" &&
                (a.Scopes.Platforms.Length != 1 ||
                    !group.Platforms!.Contains(a.Scopes.Platforms[0], StringComparer.Ordinal))))
            {
                findings.Add(new Finding("ARTIFACT_MEMBERSHIP", "Unexpected runnable platform."));
            }
            foreach (OciAttestation attestation in attestations)
            {
                string subjectKey = attestation.Image + "@" + attestation.SubjectDigest;
                OciBuildExpectation? expected = null;
                expectedBuilds?.TryGetValue(subjectKey, out expected);
                if (expected != null && !group.Images!.Any(image =>
                    group.UpstreamRepositoryPrefix + "/" + image.Id == attestation.Image &&
                    image.Dockerfile == expected.Dockerfile))
                {
                    findings.Add(new Finding("PROVENANCE_VERIFIED",
                        "Authenticated native Dockerfile differs from the evaluated group catalog."));
                }
                if (expected != null && expected.SourceSha != context.Source.ActualSha)
                {
                    findings.Add(new Finding(
                        "SOURCE_IDENTITY", "Authenticated OCI build source differs from the group."));
                    expected = null;
                }
                filesystems.TryGetValue(subjectKey, out Dictionary<string, OciFileEvidence>? filesystem);
                string path = nativeDocuments[attestation.LayerDigest];
                using JsonDocument document = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                ArtifactRecord[] subjects = artifacts.Where(a => a.Kind == "oci-manifest" &&
                    a.Id == attestation.Image && a.Digest == attestation.SubjectDigest).ToArray();
                if (expectedBuilds != null && (subjects.Length != 1 ||
                    document.RootElement.GetProperty("subject").EnumerateArray().Any(s =>
                        !MatchesSubjectName(s.GetProperty("name").GetString(), attestation.Image) ||
                        !MatchesPlatformQualifier(s.GetProperty("name").GetString()!,
                            subjects[0].Scopes.Platforms[0]))))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_INTEGRITY", "Native platform qualifier differs from its runnable subject."));
                }
                JsonElement predicate = document.RootElement.GetProperty("predicate");
                if (attestation.PredicateType == "https://spdx.dev/Document")
                {
                    await OciNativeValidation.CheckSpdxAsync(
                        files, predicate, filesystem, expected, findings, pendingFindings, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    OciNativeValidation.CheckProvenance(
                        predicate, attestation.PredicateType, expected, findings, pendingFindings);
                }
                documents.Add(new DocumentRecord(
                    attestation.PredicateType == "https://spdx.dev/Document" ? "sbom" : "provenance",
                    attestation.PredicateType == "https://spdx.dev/Document" ? "SPDX" : "in-toto",
                    attestation.FormatVersion, Path.GetRelativePath(root, path).Replace('\\', '/'),
                    attestation.LayerDigest,
                    new SubjectRecord("oci-manifest", attestation.Image, attestation.SubjectDigest)));
            }
            foreach (ArtifactRecord subject in artifacts.Where(a => a.Kind == "oci-manifest"))
            {
                foreach (string type in new[] { "sbom", "provenance" })
                {
                    if (documents.Count(d => d.Type == type && d.Subject.Id == subject.Id &&
                        d.Subject.Digest == subject.Digest) != 1)
                    {
                        findings.Add(new Finding(type == "sbom" ? "INVENTORY_COMPLETE" : "PROVENANCE_VERIFIED",
                            "A runnable subject is missing its unique native evidence document."));
                    }
                }
            }
            if (expectedBuilds != null)
            {
                string[] actualSubjects = artifacts.Where(a => a.Kind == "oci-manifest")
                    .Select(a => a.Id + "@" + a.Digest).Order(StringComparer.Ordinal).ToArray();
                if (!actualSubjects.SequenceEqual(
                    expectedBuilds.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                    !artifacts.Select(ArtifactIdentity).Order(StringComparer.Ordinal).SequenceEqual(
                        context.Artifacts.Select(ArtifactIdentity).Order(StringComparer.Ordinal),
                        StringComparer.Ordinal))
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY",
                        "OCI root/platform manifest set differs from independently authenticated expected subjects."));
                }
            }
            return new OciAnalysis([.. artifacts], [.. attestations], [.. documents], [.. findings],
                [.. pendingFindings.Select(f => f.Code)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }

        private async Task TraverseAsync(
            OciImageInput image,
            string layout,
            string digest,
            string? mediaType,
            long? size,
            string? platform,
            string? attestationSubject,
            EvaluationExpectation context,
            List<ArtifactRecord> artifacts,
            List<OciAttestation> attestations,
            List<Finding> findings,
            Dictionary<string, Dictionary<string, OciFileEvidence>> filesystems,
            Dictionary<string, string> nativeDocuments,
            HashSet<string> visited,
            CancellationToken cancellationToken)
        {
            if (!visited.Add(digest) || visited.Count > 1024)
            {
                findings.Add(new Finding(
                    "ARTIFACT_MEMBERSHIP", "Duplicate/cyclic or excessive OCI descriptor graph."));
                return;
            }
            string path = BlobPath(layout, digest);
            if (!await VerifyBlobAsync(path, digest, size, findings, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            using JsonDocument document = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
            JsonElement value = document.RootElement;
            string actualMediaType = value.GetProperty("mediaType").GetString()!;
            if (value.GetProperty("schemaVersion").GetInt32() != 2 ||
                (mediaType != null && mediaType != actualMediaType))
            {
                throw new InvalidDataException("OCI media type/schema differs from the descriptor.");
            }
            if (attestationSubject != null)
            {
                if (actualMediaType is not ("application/vnd.oci.image.manifest.v1+json" or
                    "application/vnd.docker.distribution.manifest.v2+json"))
                {
                    throw new InvalidDataException("An attestation descriptor must reference a manifest.");
                }
                await CheckAttestationAsync(
                    image, layout, digest, attestationSubject, value, attestations, findings,
                    nativeDocuments, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            if (actualMediaType is "application/vnd.oci.image.index.v1+json" or
                "application/vnd.docker.distribution.manifest.list.v2+json")
            {
                artifacts.Add(new ArtifactRecord(
                    "oci-index", image.Id, context.Release.Version, "not-applicable", digest,
                    new ScopeRecord([], [], [], []), new FileInfo(path).Length));
                JsonElement children = value.GetProperty("manifests");
                if (children.GetArrayLength() == 0)
                {
                    findings.Add(new Finding("ARTIFACT_MEMBERSHIP", "An OCI index has no children."));
                }
                foreach (JsonElement child in children.EnumerateArray())
                {
                    string? childPlatform = child.TryGetProperty("platform", out JsonElement platformValue)
                        ? Platform(platformValue) : null;
                    bool isAttestation = child.TryGetProperty("annotations", out JsonElement annotations) &&
                        annotations.TryGetProperty("vnd.docker.reference.type", out JsonElement referenceType) &&
                        referenceType.GetString() == "attestation-manifest";
                    string? reference = null;
                    if (isAttestation)
                    {
                        reference = annotations.TryGetProperty("vnd.docker.reference.digest", out JsonElement subject)
                            ? subject.GetString() ?? string.Empty : string.Empty;
                        bool sibling = children.EnumerateArray().Count(c =>
                            c.GetProperty("digest").GetString() == reference &&
                            c.GetProperty("mediaType").GetString() is
                                "application/vnd.oci.image.manifest.v1+json" or
                                "application/vnd.docker.distribution.manifest.v2+json" &&
                            c.TryGetProperty("platform", out JsonElement p) &&
                            Platform(p) != "unknown/unknown" &&
                            (!c.TryGetProperty("annotations", out JsonElement a) ||
                                !a.TryGetProperty("vnd.docker.reference.type", out _))) == 1;
                        if (childPlatform != "unknown/unknown" || !sibling)
                        {
                            findings.Add(new Finding("ARTIFACT_INTEGRITY",
                                "Attestation reference does not name a runnable sibling with the expected platform."));
                            reference = string.Empty;
                        }
                    }
                    await TraverseAsync(
                        image, layout, child.GetProperty("digest").GetString()!,
                        child.GetProperty("mediaType").GetString(), child.GetProperty("size").GetInt64(),
                        childPlatform, reference, context, artifacts, attestations, findings,
                        filesystems, nativeDocuments, visited,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
                return;
            }
            if (actualMediaType is not ("application/vnd.oci.image.manifest.v1+json" or
                "application/vnd.docker.distribution.manifest.v2+json"))
            {
                throw new InvalidDataException("Unsupported OCI subject media type.");
            }
            JsonElement configDescriptor = value.GetProperty("config");
            ValidateConfigMediaType(configDescriptor);
            string configDigest = configDescriptor.GetProperty("digest").GetString()!;
            string configPath = BlobPath(layout, configDigest);
            if (!await VerifyBlobAsync(configPath, configDigest, configDescriptor.GetProperty("size").GetInt64(),
                findings, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            using JsonDocument config = await files.ReadJsonAsync(configPath, cancellationToken).ConfigureAwait(false);
            string actualPlatform = Platform(config.RootElement);
            if (actualPlatform == "linux/arm64" && platform == "linux/arm64/v8")
            {
                actualPlatform = platform;
            }
            if (platform != null && platform != actualPlatform)
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "OCI platform descriptor disagrees with image config."));
            }
            if (!config.RootElement.TryGetProperty("config", out JsonElement runtime) ||
                !runtime.TryGetProperty("Labels", out JsonElement labels) ||
                !labels.TryGetProperty("org.opencontainers.image.revision", out JsonElement revision) ||
                revision.GetString() != context.Source.ActualSha ||
                !labels.TryGetProperty("org.opencontainers.image.version", out JsonElement version) ||
                !Versions.Equal(version.GetString()!, context.Release.Version))
            {
                findings.Add(new Finding(
                    "SOURCE_IDENTITY", "Image config has missing or mismatched source/version labels."));
            }
            artifacts.Add(new ArtifactRecord(
                "oci-manifest", image.Id, context.Release.Version, "not-applicable", digest,
                new ScopeRecord([], [], [], [actualPlatform]), new FileInfo(path).Length));
            JsonElement imageLayers = value.GetProperty("layers");
            if (imageLayers.GetArrayLength() > 256)
            {
                findings.Add(new Finding("ARTIFACT_INTEGRITY", "OCI runnable layer count exceeds the bound."));
                return;
            }
            foreach (JsonElement layer in imageLayers.EnumerateArray())
            {
                string layerDigest = layer.GetProperty("digest").GetString()!;
                await VerifyBlobAsync(BlobPath(layout, layerDigest), layerDigest, layer.GetProperty("size").GetInt64(),
                    findings, cancellationToken).ConfigureAwait(false);
            }
            if (config.RootElement.TryGetProperty("rootfs", out _))
            {
                try
                {
                    filesystems.Add(image.Id + "@" + digest, await new OciLayerInventory(files).ReadAsync(
                        layout, value, config.RootElement, cancellationToken).ConfigureAwait(false));
                }
                catch (InvalidDataException)
                {
                    findings.Add(new Finding(
                        "INVENTORY_COMPLETE", "Final-image layer evidence is invalid or unsupported."));
                }
                catch (EndOfStreamException)
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "Final-image layer evidence is truncated."));
                }
            }
            else
            {
                findings.Add(new Finding(
                    "INVENTORY_COMPLETE", "Image configuration does not bind uncompressed layers."));
            }
        }

        private async Task CheckAttestationAsync(
            OciImageInput image,
            string layout,
            string manifestDigest,
            string subjectDigest,
            JsonElement manifest,
            List<OciAttestation> attestations,
            List<Finding> findings,
            Dictionary<string, string> nativeDocuments,
            CancellationToken cancellationToken)
        {
            int initialFindings = findings.Count;
            JsonElement config = manifest.GetProperty("config");
            ValidateConfigMediaType(config);
            string configDigest = config.GetProperty("digest").GetString()!;
            await VerifyBlobAsync(
                BlobPath(layout, configDigest), configDigest, config.GetProperty("size").GetInt64(),
                findings, cancellationToken).ConfigureAwait(false);
            if (manifest.TryGetProperty("subject", out JsonElement subject))
            {
                string digest = subject.GetProperty("digest").GetString()!;
                if (digest != subjectDigest)
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY",
                        "Attestation manifest subject differs from the index reference."));
                }
                await VerifyBlobAsync(
                    BlobPath(layout, digest), digest, subject.GetProperty("size").GetInt64(),
                    findings, cancellationToken).ConfigureAwait(false);
            }
            JsonElement layers = manifest.GetProperty("layers");
            if (layers.GetArrayLength() is 0 or > 128)
            {
                findings.Add(new Finding("INVENTORY_COMPLETE", "Attestation predicate layer count is unsupported."));
                return;
            }
            foreach (JsonElement layer in layers.EnumerateArray())
            {
                int previousFindings = findings.Count;
                string digest = layer.GetProperty("digest").GetString()!;
                string path = BlobPath(layout, digest);
                if (!await VerifyBlobAsync(
                    path, digest, layer.GetProperty("size").GetInt64(), findings, cancellationToken)
                    .ConfigureAwait(false))
                {
                    continue;
                }
                if (layer.GetProperty("mediaType").GetString() != "application/vnd.in-toto+json")
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", "Unsupported attestation layer media type."));
                    continue;
                }
                using JsonDocument document = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                JsonElement statement = document.RootElement;
                string type = statement.GetProperty("_type").GetString()!;
                if (type is not ("https://in-toto.io/Statement/v0.1" or "https://in-toto.io/Statement/v1"))
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY", "Unsupported in-toto statement version."));
                }
                JsonElement subjects = statement.GetProperty("subject");
                if (subjects.GetArrayLength() == 0 ||
                    subjects.EnumerateArray().Any(s =>
                        string.IsNullOrWhiteSpace(s.GetProperty("name").GetString()) ||
                        !s.GetProperty("digest").TryGetProperty("sha256", out JsonElement hash) ||
                        "sha256:" + hash.GetString() != subjectDigest))
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY",
                        "Statement subject does not match the runnable manifest."));
                }
                string predicateType = statement.GetProperty("predicateType").GetString()!;
                if (!layer.TryGetProperty("annotations", out JsonElement annotations) ||
                    !annotations.TryGetProperty("in-toto.io/predicate-type", out JsonElement advertisedType) ||
                    advertisedType.GetString() != predicateType)
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY", "Predicate type differs from its descriptor."));
                }
                string formatVersion;
                if (predicateType == "https://spdx.dev/Document")
                {
                    formatVersion = statement.GetProperty("predicate").GetProperty("spdxVersion").GetString()!;
                    OciNativeValidation.CheckDescribedSubject(statement.GetProperty("predicate"), findings);
                    if (formatVersion != "SPDX-2.3")
                    {
                        findings.Add(new Finding("INVENTORY_COMPLETE", "Only native SPDX 2.3 is supported."));
                    }
                }
                else if (predicateType is "https://slsa.dev/provenance/v0.2" or "https://slsa.dev/provenance/v1")
                {
                    formatVersion = predicateType;
                }
                else
                {
                    findings.Add(new Finding("PROVENANCE_VERIFIED", "Unsupported native provenance predicate."));
                    continue;
                }
                if (initialFindings == findings.Count && previousFindings == findings.Count)
                {
                    attestations.Add(new OciAttestation(
                        image.Id, manifestDigest, subjectDigest, digest, predicateType, formatVersion));
                    nativeDocuments[digest] = path;
                }
            }
        }

        private static void ValidateConfigMediaType(JsonElement descriptor)
        {
            if (descriptor.GetProperty("mediaType").GetString() is not (
                "application/vnd.oci.image.config.v1+json" or "application/vnd.docker.container.image.v1+json"))
            {
                throw new InvalidDataException("Unsupported OCI config media type.");
            }
        }

        private static bool MatchesSubjectName(string? name, string image)
        {
            if (name == image)
            {
                return true;
            }
            if (name == null || !name.StartsWith("pkg:docker/" + image + "@", StringComparison.Ordinal))
            {
                return false;
            }
            string tagAndQualifiers = name[("pkg:docker/" + image + "@").Length..];
            return tagAndQualifiers.Length > 0 && !tagAndQualifiers.Contains('#', StringComparison.Ordinal);
        }

        private static string ArtifactIdentity(ArtifactRecord artifact)
        {
            return string.Join('|', artifact.Kind, artifact.Id, artifact.Digest,
                string.Join(',', artifact.Scopes.Platforms));
        }

        private static bool MatchesPlatformQualifier(string name, string platform)
        {
            if (!name.StartsWith("pkg:docker/", StringComparison.Ordinal))
            {
                return true;
            }
            int query = name.IndexOf('?', StringComparison.Ordinal);
            if (query < 0)
            {
                return false;
            }
            string[] qualifiers = name[(query + 1)..].Split('&');
            string[] platforms = qualifiers.Where(p => p.StartsWith("platform=", StringComparison.Ordinal)).ToArray();
            if (platforms.Length != 1)
            {
                return false;
            }
            string actual = Uri.UnescapeDataString(platforms[0]["platform=".Length..]);
            return actual == platform || (actual == "linux/arm64" && platform == "linux/arm64/v8");
        }

        private async Task<bool> VerifyBlobAsync(
            string path, string digest, long? size, List<Finding> findings, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                findings.Add(new Finding("ARTIFACT_INTEGRITY", "Missing or altered OCI blob."));
                return false;
            }
            long length = new FileInfo(path).Length;
            if (length > 4L * 1024 * 1024 * 1024 || (size != null && (size < 0 || length != size)))
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "OCI descriptor size differs or exceeds the byte limit."));
                return false;
            }
            if (await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != digest)
            {
                findings.Add(new Finding("ARTIFACT_INTEGRITY", "Missing or altered OCI blob."));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves a canonical lowercase SHA-256 blob identifier to its confined OCI layout path.
        /// </summary>
        internal static string BlobPath(string layout, string digest)
        {
            if (digest == null ||
                digest.Length != 71 ||
                !digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                digest[7..].Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            {
                throw new InvalidDataException("OCI digests must be lowercase SHA-256.");
            }
            return EvidenceFiles.Confined(layout, "blobs/sha256/" + digest[7..]);
        }

        private static string Platform(JsonElement platform)
        {
            string result = platform.GetProperty("os").GetString() +
                "/" +
                platform.GetProperty("architecture").GetString();
            return platform.TryGetProperty("variant", out JsonElement variant) &&
                !string.IsNullOrWhiteSpace(variant.GetString())
                ? result + "/" + variant.GetString()
                : result;
        }
    }
}
