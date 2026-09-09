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
    /// Binds one OCI image subject and platform to its root digest and expected native build context.
    /// </summary>
    /// <param name="Image">The OCI image repository identifier.</param>
    /// <param name="RootDigest">The immutable digest of the requested OCI image root.</param>
    /// <param name="SubjectDigest">The digest of the OCI subject described by the attestation or referrer.</param>
    /// <param name="Platform">The operating-system and architecture scope associated with the artifact or job.</param>
    /// <param name="Build">
    /// The independently expected source, tool, and material identities for this image subject.
    /// </param>
    internal sealed record OciSubjectBuildRecord(
        string Image, string RootDigest, string SubjectDigest, string Platform, OciBuildExpectation Build);

    /// <summary>
    /// Collects native build expectations for the subjects of a related OCI image set.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Kind">The oci-build-context discriminator identifying the native build record.</param>
    /// <param name="Relationship">The declared same-source relationship between the image subjects.</param>
    /// <param name="Subjects">The image subjects and independently expected native build contexts.</param>
    internal sealed record OciBuildContext(
        int SchemaVersion, string Kind, string Relationship, OciSubjectBuildRecord[] Subjects);

    /// <summary>
    /// Assembles and verifies OCI evidence using native image attestations and authenticated build contexts.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    internal sealed class OciArtifactAdapter(EvidenceFiles files)
    {
        /// <summary>
        /// Assembles an OCI evidence envelope and copies native documents, contracts, and optional assurance inputs.
        /// </summary>
        public async Task<EvidenceEnvelope> AssembleAsync(
            string repositoryRoot,
            string requestPath,
            EvaluationExpectation context,
            AssuranceRecord assurance,
            string outputDirectory,
            CancellationToken cancellationToken,
            string? assuranceRoot = null,
            string? referrerContextPath = null)
        {
            OciRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.OciRequest, cancellationToken).ConfigureAwait(false);
            string policyPath = EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json");
            PolicyConfiguration policy = await files.ReadModelAsync(
                policyPath, EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
            string policyDigest = await files.DigestAsync(policyPath, cancellationToken).ConfigureAwait(false);
            if (policyDigest != context.PolicyDigest)
            {
                throw new InvalidDataException("OCI assembly context does not match its policy bytes.");
            }
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == context.Release.Group);
            string root = Path.GetDirectoryName(Path.GetFullPath(requestPath))!;
            OciAnalysis analysis = await new OciReconciler(files).AnalyzeAsync(
                request, root, context, group, null, cancellationToken).ConfigureAwait(false);
            var documents = new List<DocumentRecord>();
            foreach (DocumentRecord document in analysis.Documents)
            {
                string destination = "native/" + document.Digest[7..] + ".json";
                await CopyImmutableAsync(
                    EvidenceFiles.Confined(root, document.Path),
                    EvidenceFiles.Confined(outputDirectory, destination), document.Digest, cancellationToken)
                    .ConfigureAwait(false);
                documents.Add(document with { Path = destination });
            }
            string sourcePath = EvidenceFiles.Confined(outputDirectory, "source-record.json");
            await files.WriteModelAsync(
                sourcePath, context.Source, EvidenceJsonContext.Default.SourceRecord, cancellationToken)
                .ConfigureAwait(false);
            string sourceDigest = await files.DigestAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var source = new SubjectRecord("source", context.Source.Repository, sourceDigest);
            documents.Add(new DocumentRecord("source-record", "json", "1", "source-record.json", sourceDigest, source));
            if (referrerContextPath != null)
            {
                using JsonDocument referrerContext = await files.ReadJsonAsync(referrerContextPath, cancellationToken)
                    .ConfigureAwait(false);
                OciReferrerBinding[] referrers = OciClosureReader.ParseReferrers(referrerContext.RootElement);
                await new OciClosureReader(files).ReadAsync(
                    requestPath, context with { Artifacts = analysis.Artifacts }, referrers, cancellationToken)
                    .ConfigureAwait(false);
                string digest = await files.DigestAsync(referrerContextPath, cancellationToken).ConfigureAwait(false);
                string destination = "native/" + digest[7..] + ".referrers.json";
                await CopyImmutableAsync(referrerContextPath, EvidenceFiles.Confined(outputDirectory, destination),
                    digest, cancellationToken).ConfigureAwait(false);
                documents.Add(new DocumentRecord("producer-record", "json", "1", destination, digest, source));
            }
            if (assuranceRoot != null)
            {
                foreach (InputRecord input in assurance.InputIdentities)
                {
                    await CopyImmutableAsync(EvidenceFiles.Confined(assuranceRoot, input.Path),
                        EvidenceFiles.Confined(outputDirectory, input.Path), input.Digest, cancellationToken)
                        .ConfigureAwait(false);
                }
                foreach (JobRecord job in assurance.Jobs.Where(j => j.ResultDocument != null))
                {
                    string input = EvidenceFiles.Confined(assuranceRoot, job.ResultDocument!);
                    string digest = await files.DigestAsync(input, cancellationToken).ConfigureAwait(false);
                    await CopyImmutableAsync(input, EvidenceFiles.Confined(outputDirectory, job.ResultDocument!),
                        digest, cancellationToken).ConfigureAwait(false);
                    documents.Add(new DocumentRecord(
                        "assurance-summary", "json", "1", job.ResultDocument!, digest, source));
                }
            }
            foreach ((string path, string type) in new[]
            {
                (".azurepipelines/release-policy.json", "policy"),
                (policy.ArtifactCatalog, "artifact-catalog"),
                (policy.AssuranceProfiles, "profile")
            })
            {
                string input = EvidenceFiles.Confined(repositoryRoot, path);
                string digest = await files.DigestAsync(input, cancellationToken).ConfigureAwait(false);
                string destination = "contracts/" + Path.GetFileName(path);
                await CopyImmutableAsync(input, EvidenceFiles.Confined(outputDirectory, destination),
                    digest, cancellationToken).ConfigureAwait(false);
                documents.Add(new DocumentRecord(type, "json", "1", destination, digest, source));
            }
            string[] pending =
            [
                "SOURCE_IDENTITY", "PRODUCER_IDENTITY", "POLICY_IDENTITY", "RELEASE_INTENT",
                "INVENTORY_COMPLETE", "PROVENANCE_VERIFIED", "SIGNATURE_VERIFIED", "ASSURANCE_COMPLETE",
                "INPUT_IDENTITY", "EVIDENCE_FRESHNESS", "PUBLIC_EVIDENCE_SAFE",
                "PRODUCER_NOT_READY", "PUBLISHER_BOUNDARY"
            ];
            string[] observed =
                [.. analysis.Findings.Select(f => f.Code)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            pending = [.. pending.Concat(analysis.PendingControls ?? [])
                .Except(observed, StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            var envelope = new EvidenceEnvelope(
                2, context.Source, context.Producer, context.Release,
                new PolicyRecord(policy.Id, policy.Version, policyDigest, policy.Stage),
                analysis.Artifacts, [.. documents], assurance,
                new AssessmentRecord("incomplete",
                    [.. pending.Concat(observed).Order(StringComparer.Ordinal)], pending, observed));
            return await ProducerAssessments.AttachAsync(envelope, outputDirectory, files, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reconciles OCI artifacts and native attestations against independently authenticated build expectations.
        /// </summary>
        public Task<OciAnalysis> VerifyNativeAsync(
            OciRequest request,
            string root,
            EvaluationExpectation expected,
            ArtifactGroup group,
            Dictionary<string, OciBuildExpectation> authenticatedBuilds,
            CancellationToken cancellationToken)
        {
            // The shared verifier supplies this map only after authenticating its receipt and exact subject set.
            return new OciReconciler(files).AnalyzeAsync(
                request, root, expected, group, authenticatedBuilds, cancellationToken);
        }

        /// <summary>
        /// Validates signed producer-document bindings and uses their OCI build contexts for native reconciliation.
        /// </summary>
        public async Task<OciAnalysis> VerifyAuthenticatedAsync(
            string requestPath,
            string evidenceRoot,
            EvaluationExpectation expected,
            ArtifactGroup group,
            EvidenceEnvelope envelope,
            VerifiedClaims verified,
            CancellationToken cancellationToken)
        {
            if (!verified.Records.TryGetValue("producer", out VerificationRecord? producer) ||
                producer.Source != expected.Source || producer.Producer.RunId != expected.Producer.RunId ||
                producer.Producer.Attempt != expected.Producer.Attempt ||
                producer.Producer.DefinitionSha != expected.Producer.DefinitionSha ||
                producer.Release != expected.Release)
            {
                return new OciAnalysis([], [], [],
                    [new Finding("PRODUCER_IDENTITY", "OCI context requires independently verified producer proof.")]);
            }
            OciRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.OciRequest, cancellationToken).ConfigureAwait(false);
            var builds = new Dictionary<string, OciBuildExpectation>(StringComparer.Ordinal);
            int contexts = 0;
            foreach (DocumentRecord document in envelope.Documents.Where(d =>
                d.Type == "producer-record" && d.Format == "json" && d.Version == "1"))
            {
                FrozenFile[] bound = producer.Documents.Where(d =>
                    d.Path == document.Path && d.Digest == document.Digest).ToArray();
                string path = EvidenceFiles.Confined(evidenceRoot, document.Path);
                if (bound.Length != 1 || new FileInfo(path).Length != bound[0].Size ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != bound[0].Digest)
                {
                    throw new InvalidDataException(
                        "OCI build context is not bound by the authenticated producer record.");
                }
                using JsonDocument json = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                JsonElement value = json.RootElement;
                if (!value.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "oci-build-context")
                {
                    continue;
                }
                CheckProperties(value, ["schemaVersion", "kind", "relationship", "subjects"]);
                if (++contexts != 1 || value.GetProperty("schemaVersion").GetInt32() != 1 ||
                    value.GetProperty("relationship").GetString() != "same-source" ||
                    value.GetProperty("subjects").GetArrayLength() is 0 or > 64)
                {
                    throw new InvalidDataException("Unsupported or duplicate OCI build context.");
                }
                foreach (JsonElement subject in value.GetProperty("subjects").EnumerateArray())
                {
                    CheckProperties(subject, ["image", "rootDigest", "subjectDigest", "platform", "build"]);
                    string image = subject.GetProperty("image").GetString()!;
                    string rootDigest = subject.GetProperty("rootDigest").GetString()!;
                    string subjectDigest = subject.GetProperty("subjectDigest").GetString()!;
                    string platform = subject.GetProperty("platform").GetString()!;
                    if (request.Images.Count(i => i.Id == image && i.RootDigest == rootDigest) != 1 ||
                        expected.Artifacts.Count(a => a.Id == image && a.Digest == subjectDigest &&
                            a.Kind == "oci-manifest" && a.Scopes.Platforms.SequenceEqual([platform])) != 1)
                    {
                        throw new InvalidDataException(
                            "Authenticated OCI root/platform binding differs from the request.");
                    }
                    OciBuildExpectation build = ParseBuild(subject.GetProperty("build"));
                    if (!producer.Producer.Tools.Any(t =>
                        t.Id == "buildkit" && t.Digest == build.BuildkitDigest) ||
                        !producer.Producer.Tools.Any(t =>
                            t.Id == "buildkit-syft-scanner" && t.Digest == build.ScannerDigest))
                    {
                        throw new InvalidDataException(
                            "OCI native tool digests differ from authenticated producer tools.");
                    }
                    string repository = "https://github.com/" + expected.Source.Repository;
                    string[] source = build.SourceUri.Split('#');
                    if (build.SourceSha != expected.Source.ActualSha ||
                        (source[0] != repository && source[0] != repository + ".git") ||
                        source.Length > 2 ||
                        (source.Length == 2 && source[1] != expected.Source.ActualRef &&
                            source[1] != expected.Source.ActualSha) ||
                        !builds.TryAdd(image + "@" + subjectDigest, build))
                    {
                        throw new InvalidDataException("Authenticated OCI source or subject identity is inconsistent.");
                    }
                }
            }
            if (contexts != 1)
            {
                return new OciAnalysis([], [], [],
                    [new Finding("PROVENANCE_VERIFIED", "The authenticated native OCI build context is missing.")]);
            }
            OciAnalysis analysis = await VerifyNativeAsync(request,
                Path.GetDirectoryName(Path.GetFullPath(requestPath))!, expected, group, builds, cancellationToken)
                .ConfigureAwait(false);
            var findings = new List<Finding>(analysis.Findings);
            foreach (DocumentRecord native in analysis.Documents)
            {
                if (!producer.Documents.Any(d => d.Digest == native.Digest) ||
                    !envelope.Documents.Any(d => d.Digest == native.Digest && d.Subject == native.Subject))
                {
                    findings.Add(new Finding("PROVENANCE_VERIFIED",
                        "Native OCI predicate bytes are not included in the authenticated producer/evidence binding."));
                }
            }
            return analysis with { Findings = [.. findings] };
        }

        private static OciBuildExpectation ParseBuild(JsonElement build)
        {
            CheckProperties(build,
            [
                "builderId", "invocationId", "sourceUri", "sourceSha", "dockerfile", "scannerCreator",
                "scannerDigest", "buildkitDigest", "materials"
            ]);
            JsonElement materials = build.GetProperty("materials");
            if (materials.GetArrayLength() is 0 or > 4096)
            {
                throw new InvalidDataException("OCI material context exceeds its supported scope.");
            }
            var parsed = new List<OciMaterial>();
            foreach (JsonElement material in materials.EnumerateArray())
            {
                CheckProperties(material, ["uri", "algorithm", "digest"]);
                parsed.Add(new OciMaterial(
                    material.GetProperty("uri").GetString()!, material.GetProperty("algorithm").GetString()!,
                    material.GetProperty("digest").GetString()!));
            }
            if (parsed.Distinct().Count() != parsed.Count)
            {
                throw new InvalidDataException("Authenticated OCI material context contains duplicates.");
            }
            return new OciBuildExpectation(
                build.GetProperty("builderId").GetString()!, build.GetProperty("invocationId").GetString()!,
                build.GetProperty("sourceUri").GetString()!, build.GetProperty("sourceSha").GetString()!,
                build.GetProperty("dockerfile").GetString()!, build.GetProperty("scannerCreator").GetString()!,
                build.GetProperty("scannerDigest").GetString()!, build.GetProperty("buildkitDigest").GetString()!,
                [.. parsed]);
        }

        private static void CheckProperties(JsonElement value, string[] properties)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                value.EnumerateObject().Count() != properties.Length ||
                value.EnumerateObject().Any(p => !properties.Contains(p.Name, StringComparer.Ordinal)))
            {
                throw new InvalidDataException("Unknown or missing OCI build-context property.");
            }
        }

        private async Task CopyImmutableAsync(
            string source, string destination, string digest, CancellationToken cancellationToken)
        {
            byte[] bytes = await files.ReadAsync(source, cancellationToken).ConfigureAwait(false);
            if (EvidenceFiles.Digest(bytes) != digest)
            {
                throw new InvalidDataException("Native evidence changed during assembly.");
            }
            if (File.Exists(destination))
            {
                if (await files.DigestAsync(destination, cancellationToken).ConfigureAwait(false) != digest)
                {
                    throw new InvalidDataException("An immutable native evidence destination already differs.");
                }
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var stream = new FileStream(destination, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            });
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
    }
}
