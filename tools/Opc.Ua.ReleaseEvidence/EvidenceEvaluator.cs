// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class EvidenceEvaluator(
        EvidenceFiles files,
        TrustedEvidenceVerifier? verifier = null,
        IArtifactSignatureVerifier? artifactVerifier = null)
    {
        public async Task<int> EvaluateAsync(
            string repositoryRoot,
            string evidencePath,
            string expectedPath,
            string output,
            string? artifactsRoot,
            CancellationToken cancellationToken,
            string? verificationBundle = null,
            string? trustPolicy = null)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(30));
            cancellationToken = deadline.Token;
            PolicyConfiguration policy = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json"),
                EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
            EvaluationExpectation expected = await files.ReadModelAsync(
                expectedPath, EvidenceJsonContext.Default.EvaluationExpectation, cancellationToken)
                .ConfigureAwait(false);
            Versions.ValidateIdentity(expected.Source, expected.Producer);
            if (policy.SchemaVersion != 1 ||
                policy.EvidenceSchemaVersion != 2 ||
                policy.Stage is not ("pilot" or "required") ||
                expected.Release.Channel is not ("stable" or "preview" or "development") ||
                !policy.Groups.Contains(expected.Release.Group, StringComparer.Ordinal))
            {
                throw new InvalidDataException("Unknown policy, group or release channel.");
            }
            Versions.Parse(expected.Release.Version);
            var findings = new List<Finding>();
            byte[] evidenceBytes = await files.ReadAsync(evidencePath, cancellationToken).ConfigureAwait(false);
            using JsonDocument document = EvidenceFiles.ParseJson(evidenceBytes);
            string inputDigest = EvidenceFiles.Digest(evidenceBytes);
            if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement version) ||
                !version.TryGetInt32(out int schemaVersion) ||
                schemaVersion is not (1 or 2))
            {
                throw new InvalidDataException("Unknown evidence schema.");
            }
            bool baselineFailed = false;
            VerifiedClaims? verified = null;
            bool requiredStable = Versions.RequiresStableControls(policy, expected.Release);
            if (schemaVersion == 1)
            {
                ArchiveManifest legacy = document.Deserialize(EvidenceJsonContext.Default.ArchiveManifest)!;
                if (legacy.Archives == null ||
                    legacy.Repository == null ||
                    legacy.Workflow == null ||
                    legacy.RunId == null ||
                    legacy.Ref == null ||
                    legacy.Commit == null ||
                    legacy.PackageVersion == null)
                {
                    throw new InvalidDataException("The v1 archive manifest is missing required fields.");
                }
                Versions.Parse(legacy.PackageVersion);
                if (legacy.Commit != expected.Source.ActualSha || legacy.Repository != expected.Source.Repository)
                {
                    findings.Add(new Finding("SOURCE_IDENTITY", "The v1 manifest has a different source identity."));
                }
                if (legacy.RunId != expected.Producer.RunId || legacy.Workflow != expected.Producer.Workflow)
                {
                    findings.Add(new Finding(
                        "PRODUCER_IDENTITY", "The v1 manifest has a different producer identity."));
                }
                findings.Add(new Finding("EVIDENCE_SCHEMA", "Only a v2 companion can satisfy the evidence contract."));
            }
            else
            {
                string schemaPath = EvidenceFiles.Confined(repositoryRoot, policy.EvidenceSchema);
                string schemaText = System.Text.Encoding.UTF8.GetString(
                    await files.ReadAsync(schemaPath, cancellationToken).ConfigureAwait(false));
                var schema = JsonSchema.FromText(
                    schemaText, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
                if (!schema.Evaluate(document.RootElement, new EvaluationOptions
                {
                    RequireFormatValidation = true
                }).IsValid)
                {
                    throw new InvalidDataException("The evidence does not satisfy the closed v2 JSON schema.");
                }
                EvidenceEnvelope envelope = document.Deserialize(EvidenceJsonContext.Default.EvidenceEnvelope)!;
                if (envelope.Artifacts.Length > 1024 || envelope.Documents.Length > 16384 ||
                    envelope.Assurance.Jobs.Length > 512 || envelope.Assurance.InputIdentities.Length > 16384)
                {
                    throw new InvalidDataException("Evidence exceeds the bounded artifact/document/job scope.");
                }
                verified = await (verifier ?? new TrustedEvidenceVerifier(files)).VerifyAsync(
                    repositoryRoot, evidencePath, envelope, verificationBundle, trustPolicy, artifactsRoot,
                    cancellationToken).ConfigureAwait(false);
                if (verified.Records.Values.Any(r => r.EvidenceDigest != inputDigest) ||
                    (verified.NativeNuget != null && verified.NativeNuget.EvidenceDigest != inputDigest))
                {
                    throw new InvalidDataException("Authentication and evaluation consumed different evidence bytes.");
                }
                findings.AddRange(verified.Findings);
                if (verified.Policy is { Stage: "required" } floor &&
                    floor.ExpectedRelease.Channel == "stable" &&
                    Versions.Parse(floor.ExpectedRelease.Version).Major == policy.CurrentMajor)
                {
                    requiredStable = true;
                }
                requiredStable |= Versions.RequiresStableControls(policy, envelope.Release);
                baselineFailed = await CheckEnvelopeAsync(
                    repositoryRoot, Path.GetDirectoryName(Path.GetFullPath(evidencePath))!,
                    envelope, expected, policy, findings, verified, cancellationToken).ConfigureAwait(false);
                if (verified.NativeNuget?.ProducerBaselineFailed == true)
                {
                    baselineFailed = true;
                    findings.Add(new Finding("ASSURANCE_COMPLETE",
                        "An observed failure in the immutable producer evidence cannot be erased by later assessment."));
                }
                if (artifactsRoot != null)
                {
                    await CheckArchiveBytesAsync(artifactsRoot, envelope, findings, cancellationToken)
                        .ConfigureAwait(false);
                    if (envelope.Release.Group == "nuget")
                    {
                        await new NugetAcceptance(files).VerifyAsync(
                            repositoryRoot, Path.GetDirectoryName(Path.GetFullPath(evidencePath))!,
                            artifactsRoot, envelope, policy, findings, cancellationToken).ConfigureAwait(false);
                        await CheckArtifactSignaturesAsync(
                            artifactsRoot, envelope, verified, findings, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (envelope.Release.Group == "nuget")
                {
                    findings.Add(new Finding(
                        "ARTIFACT_INTEGRITY", "Archive bytes were not supplied for digest verification."));
                }
                if (envelope.Release.Group is "containers" or "pump")
                {
                    await CheckOciAsync(
                        repositoryRoot, Path.GetDirectoryName(Path.GetFullPath(evidencePath))!,
                        envelope, expected, policy, verified,
                        verificationBundle, findings, cancellationToken).ConfigureAwait(false);
                }
            }
            if (verified == null)
            {
                findings.Add(new Finding("POLICY_IDENTITY", "Legacy manifests have no authenticated current policy."));
            }
            if (await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false) != inputDigest)
            {
                throw new InvalidDataException("Evidence changed before its final assessment.");
            }
            bool blocking = baselineFailed || (requiredStable && findings.Count != 0);
            var report = new EvaluationReport(
                1, findings.Count == 0 ? "complete" : "incomplete",
                verified?.Policy?.Stage ?? policy.Stage,
                verified?.Policy?.ExpectedRelease.Channel ?? expected.Release.Channel, expected.Release.Group,
                blocking, baselineFailed, verified?.Records.Count > 0 ||
                    verified?.NativeNuget != null || verified?.CodeqlReviews.Count > 0,
                [.. findings.Select(f => f.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                [.. findings], inputDigest, verified?.NativeNuget?.ProducerEvidenceDigest);
            await files.WriteModelAsync(
                output, report, EvidenceJsonContext.Default.EvaluationReport, cancellationToken)
                .ConfigureAwait(false);
            return blocking ? 1 : 0;
        }

        private async Task CheckOciAsync(
            string repositoryRoot,
            string evidenceRoot,
            EvidenceEnvelope envelope,
            EvaluationExpectation expected,
            PolicyConfiguration policy,
            VerifiedClaims verified,
            string? bundlePath,
            List<Finding> findings,
            CancellationToken cancellationToken)
        {
            if (bundlePath == null || verified.BundleRoot == null ||
                !verified.Has("producer"))
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "OCI bytes and authenticated native build inputs are missing."));
                return;
            }
            VerificationBundle bundle = await files.ReadModelAsync(
                bundlePath, VerificationJsonContext.Default.VerificationBundle, cancellationToken)
                .ConfigureAwait(false);
            if (bundle.OciRequestPath == null)
            {
                findings.Add(new Finding("ARTIFACT_INTEGRITY", "OCI verification has no downloaded layout request."));
                return;
            }
            string requestPath = EvidenceFiles.Confined(verified.BundleRoot, bundle.OciRequestPath);
            OciRequest request = await files.ReadModelAsync(
                requestPath, EvidenceJsonContext.Default.OciRequest, cancellationToken).ConfigureAwait(false);
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == envelope.Release.Group);
            VerificationRecord producer = verified.Records["producer"];
            var adapter = new OciArtifactAdapter(files);
            OciAnalysis analysis;
            if (producer.OciBuilds != null)
            {
                ArtifactRecord[] runnable = [.. envelope.Artifacts.Where(a => a.Kind == "oci-manifest")];
                if (producer.OciBuilds.Count != runnable.Length ||
                    runnable.Any(a => !producer.OciBuilds.TryGetValue(a.Id + "@" + a.Digest,
                        out OciBuildExpectation? build) || !ValidOciBuild(build, a, group, envelope)))
                {
                    findings.Add(new Finding(
                        "PROVENANCE_VERIFIED", "Authenticated native build/source/tool scope is incomplete."));
                    return;
                }
                analysis = await adapter.VerifyNativeAsync(
                    request, Path.GetDirectoryName(requestPath)!, expected, group,
                    producer.OciBuilds, cancellationToken).ConfigureAwait(false);
                foreach (DocumentRecord document in envelope.Documents.Where(
                    d => d.Type == "producer-record" && d.Format == "json"))
                {
                    using JsonDocument context = await files.ReadJsonAsync(
                        EvidenceFiles.Confined(evidenceRoot, document.Path), cancellationToken).ConfigureAwait(false);
                    if (context.RootElement.TryGetProperty("kind", out JsonElement kind) &&
                        kind.ValueKind == JsonValueKind.String && kind.GetString() == "oci-build-context")
                    {
                        OciAnalysis contextual = await adapter.VerifyAuthenticatedAsync(
                            requestPath, evidenceRoot, expected, group, envelope, verified, cancellationToken)
                            .ConfigureAwait(false);
                        findings.AddRange(contextual.Findings);
                        break;
                    }
                }
            }
            else
            {
                analysis = await adapter.VerifyAuthenticatedAsync(
                    requestPath, evidenceRoot, expected, group, envelope, verified, cancellationToken)
                    .ConfigureAwait(false);
            }
            findings.AddRange(analysis.Findings);
            foreach (DocumentRecord native in analysis.Documents)
            {
                if (!producer.Documents.Any(d => d.Digest == native.Digest) ||
                    !envelope.Documents.Any(d => d.Digest == native.Digest && d.Subject == native.Subject))
                {
                    findings.Add(new Finding(
                        "PROVENANCE_VERIFIED", "Native predicate is outside the authenticated document set."));
                }
            }
            if (VerificationControls.ArtifactSetDigest(analysis.Artifacts) !=
                VerificationControls.ArtifactSetDigest(envelope.Artifacts))
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "Verified OCI subjects differ from the release envelope."));
            }
            VerificationRecord? signatures = verified.Records.GetValueOrDefault("artifact-signatures");
            if (signatures?.ArtifactSignatures == null ||
                signatures.ArtifactSignatures.Length != envelope.Artifacts.Length)
            {
                findings.Add(new Finding(
                    "SIGNATURE_VERIFIED", "Separate OCI root/platform signature bundles are missing."));
                return;
            }
            foreach (ArtifactRecord artifact in envelope.Artifacts)
            {
                ArtifactSignatureProof[] proofs = [.. signatures.ArtifactSignatures.Where(p =>
                    p.Kind == artifact.Kind && p.Id == artifact.Id && p.ArtifactDigest == artifact.Digest)];
                string[] paths = [.. request.Images.Select(image =>
                    EvidenceFiles.Confined(Path.GetDirectoryName(requestPath)!,
                        image.Layout + "/blobs/sha256/" + artifact.Digest[7..])).Where(File.Exists)];
                IArtifactSignatureVerifier signaturesVerifier =
                    artifactVerifier ?? new CosignArtifactSignatureVerifier(files, new ProcessRunner());
                if (proofs.Length != 1 || paths.Length == 0 ||
                    !await signaturesVerifier.VerifyAsync(
                        paths[0], proofs[0], verified.BundleRoot, verified.Policy!, cancellationToken)
                        .ConfigureAwait(false))
                {
                    findings.Add(new Finding(
                        "SIGNATURE_VERIFIED", "An OCI root/platform signature bundle is invalid."));
                }
            }
        }

        private static bool ValidOciBuild(
            OciBuildExpectation build, ArtifactRecord artifact, ArtifactGroup group, EvidenceEnvelope envelope)
        {
            string repository = "https://github.com/" + envelope.Source.Repository;
            string[] source = build.SourceUri.Split('#');
            return build.SourceSha == envelope.Source.ActualSha && source.Length <= 2 &&
                (source[0] == repository || source[0] == repository + ".git") &&
                (source.Length == 1 || source[1] == envelope.Source.ActualRef ||
                    source[1] == envelope.Source.ActualSha) &&
                group.Images!.Any(i => group.UpstreamRepositoryPrefix + "/" + i.Id == artifact.Id &&
                    i.Dockerfile == build.Dockerfile) &&
                envelope.Producer.Tools.Any(t => t.Id == "buildkit" && t.Digest == build.BuildkitDigest) &&
                envelope.Producer.Tools.Any(t => t.Id == "buildkit-syft-scanner" && t.Digest == build.ScannerDigest) &&
                build.Materials.All(m => m.Algorithm != "sha1" ||
                    m.Uri == build.SourceUri && m.Digest == envelope.Source.ActualSha);
        }

        private async Task CheckArtifactSignaturesAsync(
            string root,
            EvidenceEnvelope envelope,
            VerifiedClaims verified,
            List<Finding> findings,
            CancellationToken cancellationToken)
        {
            if (!verified.Records.TryGetValue("artifact-signatures", out VerificationRecord? record) ||
                record.ArtifactSignatures == null || verified.Policy == null || verified.BundleRoot == null)
            {
                findings.Add(new Finding("SIGNATURE_VERIFIED", "Actual package signature proofs are unavailable."));
                return;
            }
            if (record.ArtifactSignatures.Length != envelope.Artifacts.Length)
            {
                findings.Add(new Finding(
                    "SIGNATURE_VERIFIED", "Signature proof scope does not cover the entire group."));
            }
            Dictionary<string, string> archives = await files.IndexArchivesAsync(root, cancellationToken)
                .ConfigureAwait(false);
            foreach (ArtifactRecord artifact in envelope.Artifacts)
            {
                ArtifactSignatureProof[] proofs = [.. record.ArtifactSignatures.Where(p =>
                    p.Kind == artifact.Kind && p.Id == artifact.Id && p.ArtifactDigest == artifact.Digest)];
                archives.TryGetValue(artifact.Digest, out string? path);
                IArtifactSignatureVerifier signaturesVerifier =
                    artifactVerifier ?? new NugetAuthorSignatureVerifier(files, new ProcessRunner());
                if (proofs.Length != 1 || path == null ||
                    !await signaturesVerifier.VerifyAsync(
                        path, proofs[0], verified.BundleRoot, verified.Policy, cancellationToken)
                        .ConfigureAwait(false))
                {
                    findings.Add(new Finding(
                        "SIGNATURE_VERIFIED", "Archive signature verification failed or is unsupported."));
                }
            }
        }

        private async Task<bool> CheckEnvelopeAsync(
            string repositoryRoot,
            string bundleRoot,
            EvidenceEnvelope envelope,
            EvaluationExpectation expected,
            PolicyConfiguration policy,
            List<Finding> findings,
            VerifiedClaims verified,
            CancellationToken cancellationToken)
        {
            Versions.ValidateIdentity(envelope.Source, envelope.Producer);
            if (envelope.Source != expected.Source || !envelope.Source.TrackedClean)
            {
                findings.Add(new Finding(
                    "SOURCE_IDENTITY", "Actual source does not match the expected clean checkout."));
            }
            if (!SameProducer(envelope.Producer, expected.Producer))
            {
                findings.Add(new Finding(
                    "EVIDENCE_FRESHNESS", "Producer run, attempt or definition differs from expectation."));
            }
            string policyDigest = await files.DigestAsync(
                EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json"), cancellationToken)
                .ConfigureAwait(false);
            if (envelope.Policy.Digest != policyDigest ||
                expected.PolicyDigest != policyDigest ||
                envelope.Policy.Stage != policy.Stage ||
                envelope.Policy.Id != policy.Id ||
                envelope.Policy.Version != policy.Version)
            {
                findings.Add(new Finding(
                    "POLICY_IDENTITY", "Protected policy bytes/identity differ from the recorded floor."));
            }
            if (envelope.Release.Group != expected.Release.Group ||
                !Versions.Equal(envelope.Release.Version, expected.Release.Version) ||
                envelope.Release.Channel != expected.Release.Channel ||
                (expected.Release.Channel == "stable" &&
                    (Versions.Parse(expected.Release.Version).IsPrerelease ||
                        Versions.Parse(expected.Release.Version).Major != policy.CurrentMajor)) ||
                (expected.Release.Channel == "preview" && !Versions.Parse(expected.Release.Version).IsPrerelease))
            {
                findings.Add(new Finding(
                    "RELEASE_INTENT", "Actual version/group/channel contradict the requested intent."));
            }
            if (expected.Release.Channel != "development" && !verified.Has("release-intent"))
            {
                findings.Add(new Finding(
                    "RELEASE_INTENT", "Protected official release intent has not been authenticated."));
            }
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup group = catalog.Groups.Single(g => g.Id == expected.Release.Group);
            CheckArtifactMembership(repositoryRoot, envelope, expected, group, findings);
            bool restrictedPublicPayload = await CheckDocumentsAsync(
                bundleRoot, envelope, findings, cancellationToken).ConfigureAwait(false);
            ProfilesConfiguration profiles = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, policy.AssuranceProfiles),
                EvidenceJsonContext.Default.ProfilesConfiguration, cancellationToken).ConfigureAwait(false);
            bool failed = await CheckAssuranceAsync(
                bundleRoot, envelope, group, profiles, findings, verified, cancellationToken).ConfigureAwait(false);
            AssessmentRecord assessment = await ProducerAssessments.ReadAsync(
                envelope, bundleRoot, files, cancellationToken).ConfigureAwait(false);
            if ((assessment.PendingControls == null) != (assessment.ObservedControls == null) ||
                (assessment.PendingControls != null &&
                    !assessment.PendingControls.Concat(assessment.ObservedControls!)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SequenceEqual(
                            envelope.Assessment.UnmetControls.Order(StringComparer.Ordinal), StringComparer.Ordinal)))
            {
                throw new InvalidDataException("Pending/observed classification must cover the exact unmet controls.");
            }
            foreach (string control in envelope.Assessment.UnmetControls)
            {
                bool pending = assessment.PendingControls?.Contains(control, StringComparer.Ordinal) == true;
                bool observed = assessment.ObservedControls?
                    .Contains(control, StringComparer.Ordinal) == true;
                if (!verified.Has("producer") || !pending || observed)
                {
                    findings.Add(new Finding(control, "Observed or unclassified producer finding remains unmet."));
                }
            }
            return failed || restrictedPublicPayload;
        }

        private static void CheckArtifactMembership(
            string repositoryRoot,
            EvidenceEnvelope envelope,
            EvaluationExpectation expected,
            ArtifactGroup group,
            List<Finding> findings)
        {
            string[] actual = [.. envelope.Artifacts.Select(ArtifactKey)];
            if (actual.Length == 0 ||
                actual.Distinct(StringComparer.Ordinal).Count() != actual.Length ||
                expected.Artifacts.Length == 0 ||
                !actual.Order(StringComparer.Ordinal).SequenceEqual(
                    expected.Artifacts.Select(ArtifactKey).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                findings.Add(new Finding(
                    "ARTIFACT_MEMBERSHIP", "Empty, duplicate, missing or unexpected artifact subjects."));
            }
            foreach (ArtifactRecord artifact in envelope.Artifacts)
            {
                if (!Versions.Equal(artifact.Version, envelope.Release.Version))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_MEMBERSHIP", "An artifact belongs to a different release version."));
                }
            }
            if (group.Kind == "nuget")
            {
                string[] ids = [.. File.ReadAllLines(EvidenceFiles.Confined(repositoryRoot, group.ModernCatalog!))
                    .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#'))];
                if (ids.Length == 0 || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
                {
                    throw new InvalidDataException("Modern package catalog is empty or contains duplicates.");
                }
                foreach (string id in ids)
                {
                    if (envelope.Artifacts.Count(a =>
                        a.Kind == "nuget-package" &&
                        a.Configuration == "Release" &&
                        string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) != 1)
                    {
                        findings.Add(new Finding(
                            "ARTIFACT_MEMBERSHIP", $"Missing or duplicate Release package: {id}."));
                    }
                }
                if (!envelope.Artifacts.Any(a =>
                    a.Kind == "nuget-package" &&
                    a.Configuration == "Debug" &&
                    a.Id.EndsWith(".Debug", StringComparison.Ordinal)))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_MEMBERSHIP", "No distinct evaluated Debug packages were supplied."));
                }
                foreach (ArtifactRecord symbol in envelope.Artifacts.Where(a => a.Kind == "nuget-symbols"))
                {
                    if (envelope.Artifacts.Count(a =>
                        a.Kind == "nuget-package" &&
                        a.Id == symbol.Id &&
                        a.Configuration == symbol.Configuration &&
                        Versions.Equal(a.Version, symbol.Version)) != 1)
                    {
                        findings.Add(new Finding("ARTIFACT_MEMBERSHIP", "Orphaned or ambiguous symbol package."));
                    }
                }
            }
            else
            {
                string[] imageIds = [.. group.Images!.Select(i => group.UpstreamRepositoryPrefix + "/" + i.Id)];
                if (envelope.Artifacts.Any(a =>
                    !imageIds.Contains(a.Id, StringComparer.Ordinal) ||
                    a.Configuration != "not-applicable" ||
                    a.Kind is not ("oci-index" or "oci-manifest") ||
                    (a.Kind == "oci-index" && a.Scopes.Platforms.Length != 0) ||
                    (a.Kind == "oci-manifest" &&
                        (a.Scopes.Platforms.Length != 1 ||
                            !group.Platforms!.Contains(a.Scopes.Platforms[0], StringComparer.Ordinal)))))
                {
                    findings.Add(new Finding("ARTIFACT_MEMBERSHIP", "Unexpected OCI image, kind, or platform scope."));
                }
                foreach (ImageConfiguration image in group.Images!)
                {
                    string id = group.UpstreamRepositoryPrefix + "/" + image.Id;
                    int indexes = envelope.Artifacts.Count(a => a.Kind == "oci-index" && a.Id == id);
                    if (indexes > 1 || (group.Platforms!.Length > 1 && indexes != 1))
                    {
                        findings.Add(new Finding(
                            "ARTIFACT_MEMBERSHIP", "OCI root index scope is missing or duplicated."));
                    }
                    foreach (string platform in group.Platforms!)
                    {
                        if (envelope.Artifacts.Count(a => a.Kind == "oci-manifest" &&
                            a.Id == id &&
                            a.Scopes.Platforms.SequenceEqual([platform], StringComparer.Ordinal)) != 1)
                        {
                            findings.Add(new Finding(
                                "ARTIFACT_MEMBERSHIP", $"Missing or duplicate OCI subject: {id}/{platform}."));
                        }
                    }
                }
            }
        }

        private async Task<bool> CheckDocumentsAsync(
            string root,
            EvidenceEnvelope envelope,
            List<Finding> findings,
            CancellationToken cancellationToken)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool restricted = false;
            foreach (DocumentRecord document in envelope.Documents)
            {
                string path = EvidenceFiles.Confined(root, document.Path);
                if (paths.TryGetValue(document.Path, out string? digest) && digest != document.Digest)
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY", "Conflicting document path identities."));
                }
                paths[document.Path] = document.Digest;
                if (!File.Exists(path) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != document.Digest)
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY", $"Missing or altered document: {document.Path}."));
                    continue;
                }
                using (JsonDocument payload = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    if (!PublicEvidenceSafety.Check(payload.RootElement))
                    {
                        restricted = true;
                        findings.Add(new Finding(
                            "PUBLIC_EVIDENCE_SAFE", "A referenced public payload contains restricted fields or values."));
                    }
                }
                if (document.Subject.Kind is not ("artifact-set" or "source") &&
                    !envelope.Artifacts.Any(a => a.Kind == document.Subject.Kind &&
                        a.Id == document.Subject.Id &&
                        a.Digest == document.Subject.Digest))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_INTEGRITY", "A document is bound to the wrong artifact subject."));
                }
                if (document.Type == "sbom" && document.Format == "CycloneDX")
                {
                    using JsonDocument bom = await files.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
                    if (document.Version != "1.6" ||
                        !bom.RootElement.TryGetProperty("specVersion", out JsonElement spec) ||
                        spec.GetString() != "1.6" ||
                        !bom.RootElement.TryGetProperty("bomFormat", out JsonElement format) ||
                        format.GetString() != "CycloneDX")
                    {
                        findings.Add(new Finding("INVENTORY_COMPLETE", "The NuGet SBOM is not CycloneDX JSON 1.6."));
                    }
                    else if (!CycloneDX.Json.Validator.Validate(
                        bom.RootElement.GetRawText(), CycloneDX.SpecificationVersion.v1_6).Valid)
                    {
                        findings.Add(new Finding("INVENTORY_COMPLETE", "CycloneDX 1.6 schema validation failed."));
                    }
                    else
                    {
                        ArtifactRecord? artifact = envelope.Artifacts.SingleOrDefault(a =>
                            a.Kind == document.Subject.Kind &&
                            a.Id == document.Subject.Id &&
                            a.Digest == document.Subject.Digest);
                        if (artifact == null ||
                            !bom.RootElement.TryGetProperty("metadata", out JsonElement metadata) ||
                            !metadata.TryGetProperty("component", out JsonElement component) ||
                            component.GetProperty("name").GetString() != artifact.Id ||
                            !component.TryGetProperty("version", out JsonElement artifactVersion) ||
                            !Versions.Equal(artifactVersion.GetString()!, artifact.Version) ||
                            !component.TryGetProperty("hashes", out JsonElement hashes) ||
                            hashes.EnumerateArray().Count(h => h.GetProperty("alg").GetString() == "SHA-256" &&
                                "sha256:" + h.GetProperty("content").GetString() == artifact.Digest) != 1)
                        {
                            findings.Add(new Finding(
                                "ARTIFACT_INTEGRITY", "SBOM content disagrees with its artifact subject."));
                        }
                    }
                }
                if (document.Type == "inventory")
                {
                    PackageInventory inventory = await files.ReadModelAsync(
                        path, EvidenceJsonContext.Default.PackageInventory, cancellationToken).ConfigureAwait(false);
                    if (inventory.Artifact.Digest != document.Subject.Digest ||
                        inventory.Artifact.Id != document.Subject.Id ||
                        inventory.Artifact.Kind != document.Subject.Kind)
                    {
                        findings.Add(new Finding(
                            "ARTIFACT_INTEGRITY", "Inventory content disagrees with its document subject."));
                    }
                    if (inventory.Payloads.Any(p => p.Classification.EndsWith("unowned", StringComparison.Ordinal)) ||
                        inventory.UnmetControls.Length != 0)
                    {
                        findings.Add(new Finding(
                            "INVENTORY_COMPLETE", "Actual inventory has unknown ownership or unmet controls."));
                    }
                }
                if (document.Type == "archive-manifest")
                {
                    ArchiveManifest manifest = await files.ReadModelAsync(
                        path, EvidenceJsonContext.Default.ArchiveManifest, cancellationToken).ConfigureAwait(false);
                    if (document.Subject.Kind != "artifact-set" ||
                        document.Subject.Digest != document.Digest ||
                        manifest.Commit != envelope.Source.ActualSha ||
                        manifest.Repository != envelope.Source.Repository ||
                        manifest.RunId != envelope.Producer.RunId)
                    {
                        findings.Add(new Finding(
                            "ARTIFACT_INTEGRITY", "V1 manifest source/producer/artifact-set binding differs."));
                    }
                    foreach (ArchiveRecord archive in manifest.Archives)
                    {
                        EvidenceFiles.ValidateRelative(archive.File);
                        if (!envelope.Artifacts.Any(a =>
                            a.Id == archive.Id &&
                            Versions.Equal(a.Version, archive.Version) &&
                            a.Digest == "sha256:" + archive.Sha256 &&
                            a.Kind == (archive.Type == "symbols" ? "nuget-symbols" : "nuget-package")))
                        {
                            findings.Add(new Finding(
                                "ARTIFACT_INTEGRITY", "V1 archive inventory differs from the v2 subjects."));
                        }
                    }
                }
            }
            foreach (ArtifactRecord artifact in envelope.Artifacts)
            {
                if (artifact.Kind != "oci-index" && !envelope.Documents.Any(d => d.Type == "sbom" &&
                    d.Subject.Kind == artifact.Kind &&
                    d.Subject.Id == artifact.Id &&
                    d.Subject.Digest == artifact.Digest))
                {
                    findings.Add(new Finding("INVENTORY_COMPLETE", $"Missing per-subject SBOM: {artifact.Id}."));
                }
            }
            return restricted;
        }

        private async Task CheckArchiveBytesAsync(
            string root, EvidenceEnvelope envelope, List<Finding> findings, CancellationToken cancellationToken)
        {
            EvidenceFiles.RejectLinks(root);
            string[] archivePaths = [.. Directory.GetFiles(root).Where(p =>
                p.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in archivePaths)
            {
                string digest = await files.DigestAsync(path, cancellationToken).ConfigureAwait(false);
                string kind = path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
                    ? "nuget-symbols" : "nuget-package";
                ArtifactRecord[] matching = [.. envelope.Artifacts.Where(a => a.Kind == kind && a.Digest == digest)];
                if (matching.Length != 1 || !seen.Add(digest))
                {
                    findings.Add(new Finding("ARTIFACT_INTEGRITY", "Unexpected, duplicate or altered archive bytes."));
                    continue;
                }
                using ZipArchive archive = ZipFile.OpenRead(path);
                PackageReconciler.ValidateEntries(archive);
                ZipArchiveEntry[] entries = [.. archive.Entries.Where(e =>
                    !e.FullName.Contains('/', StringComparison.Ordinal) &&
                    e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))];
                if (entries.Length != 1)
                {
                    throw new InvalidDataException("An archive does not contain exactly one root nuspec.");
                }
                using Stream stream = entries[0].Open();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                System.Xml.Linq.XElement metadata = NuspecMetadata.Read(buffer.ToArray());
                if (!string.Equals(
                    NuspecMetadata.Required(metadata, "id"), matching[0].Id, StringComparison.OrdinalIgnoreCase) ||
                    !Versions.Equal(NuspecMetadata.Required(metadata, "version"), matching[0].Version) ||
                    (matching[0].Size != null && matching[0].Size != new FileInfo(path).Length))
                {
                    findings.Add(new Finding(
                        "ARTIFACT_INTEGRITY", "Archive nuspec/size does not match its recorded subject."));
                }
            }
            if (envelope.Artifacts.Any(a => a.Kind is "nuget-package" or "nuget-symbols" && !seen.Contains(a.Digest)))
            {
                findings.Add(new Finding(
                    "ARTIFACT_INTEGRITY", "A recorded archive is missing from the supplied signed directory."));
            }
        }

        private async Task<bool> CheckAssuranceAsync(
            string root,
            EvidenceEnvelope envelope,
            ArtifactGroup group,
            ProfilesConfiguration configuration,
            List<Finding> findings,
            VerifiedClaims verified,
            CancellationToken cancellationToken)
        {
            AssuranceRecord assurance = envelope.Assurance;
            ProfileConfiguration[] profiles = [.. configuration.Profiles.Where(p =>
                group.Profiles.Contains(p.Id, StringComparer.Ordinal))];
            string[] expectedIds = [.. profiles.SelectMany(p => p.Jobs.Select(j => j.Id))];
            if (!assurance.Profiles.Order(StringComparer.Ordinal).SequenceEqual(
                group.Profiles.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !assurance.Jobs.Select(j => j.Id).Order(StringComparer.Ordinal).SequenceEqual(
                    expectedIds.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                assurance.Expected != expectedIds.Length ||
                assurance.Expected !=
                    assurance.Completed + assurance.Failed + assurance.Missing + assurance.NotApplicable ||
                assurance.Selected != assurance.Jobs.Count(j => j.Selected) ||
                assurance.Completed != assurance.Jobs.Count(j => j.Status == "completed") ||
                assurance.Failed != assurance.Jobs.Count(j => j.Status == "failed") ||
                assurance.Missing != assurance.Jobs.Count(j => j.Status == "missing") ||
                assurance.NotApplicable != assurance.Jobs.Count(j => j.Status == "not-applicable"))
            {
                findings.Add(new Finding(
                    "ASSURANCE_COMPLETE", "Expected seven-job scope or status accounting is incomplete."));
            }
            foreach (InputRecord input in assurance.InputIdentities)
            {
                string path = EvidenceFiles.Confined(root, input.Path);
                if (!File.Exists(path) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != input.Digest)
                {
                    findings.Add(new Finding("INPUT_IDENTITY", "Assurance input is missing or altered."));
                }
            }
            bool baselineFailed = assurance.Jobs.Any(j => j.Status == "failed");
            foreach (JobRecord job in assurance.Jobs)
            {
                ProfileConfiguration? profile = profiles.SingleOrDefault(p => p.Id == job.Profile);
                ProfileJob? definition = profile?.Jobs.SingleOrDefault(j => j.Id == job.Id);
                if (profile == null ||
                    definition == null ||
                    job.Project != definition.Project ||
                    job.Host != profile.Host ||
                    job.HostTfm != profile.HostTfm ||
                    job.LibraryTfm != profile.LibraryTfm ||
                    job.Configuration != profile.Configuration ||
                    job.Platform != profile.Platform ||
                    job.Filter != profile.Filter ||
                    job.Shard != "all" ||
                    job.Status is "missing" or "not-applicable" ||
                    !job.Selected)
                {
                    findings.Add(new Finding(
                        "ASSURANCE_COMPLETE", $"Missing or inapplicable initial profile job: {job.Id}."));
                    continue;
                }
                if (job.SourceSha != envelope.Source.ActualSha ||
                    job.Producer == null ||
                    job.Producer.RunId == "0" ||
                    job.Producer.Attempt < 1)
                {
                    findings.Add(new Finding("EVIDENCE_FRESHNESS", $"Unbound or cross-source result: {job.Id}."));
                }
                CollectedResultSummary? result = null;
                if (job.ResultDocument != null)
                {
                    string path = EvidenceFiles.Confined(root, job.ResultDocument);
                    if (File.Exists(path))
                    {
                        string digest = await files.DigestAsync(path, cancellationToken).ConfigureAwait(false);
                        if (envelope.Documents.Any(d =>
                            d.Path == job.ResultDocument && d.Type == "assurance-summary" && d.Digest == digest))
                        {
                            result = await files.ReadModelAsync(
                                path, EvidenceJsonContext.Default.CollectedResultSummary, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }
                bool verifiedReplay = definition.Kind == "fuzz-replay" &&
                    IsCompleteReplay(result, job.Counts);
                bool countsValid = definition.Kind == "analysis"
                    ? job.Counts is { AnalyzedProjects: > 0, UnresolvedFindings: 0 }
                    : job.Counts is { Total: > 0, Executed: > 0, Failed: 0, Skipped: >= 0, Passed: > 0 } counts &&
                        (counts.Skipped == 0 || verifiedReplay) &&
                        counts.Executed == counts.Passed + counts.Failed &&
                        counts.Total == counts.Executed + counts.Skipped;
                bool observedFailure = job.Status == "failed" ||
                    job.Counts?.Failed > 0 ||
                    (definition.Kind == "analysis"
                        ? job.Counts?.AnalyzedProjects == 0 || job.Counts?.UnresolvedFindings > 0
                        : job.Counts?.Executed == 0 ||
                            (job.Counts?.Skipped > 0 && !verifiedReplay) ||
                            (job.Counts is
                            {
                                Total: not null, Executed: not null, Passed: not null,
                                Failed: not null, Skipped: not null
                            } &&
                                !countsValid));
                if (observedFailure)
                {
                    baselineFailed = true;
                    findings.Add(new Finding(
                        "ASSURANCE_COMPLETE", $"Failed, zero or inconsistent executed results: {job.Id}."));
                }
                else if (!countsValid)
                {
                    findings.Add(new Finding(
                        "ASSURANCE_COMPLETE", $"Applicable result counts are unknown: {job.Id}."));
                }
                if (job.ResultDocument == null)
                {
                    findings.Add(new Finding("ASSURANCE_COMPLETE", $"Missing actual result document: {job.Id}."));
                }
                else
                {
                    string resultPath = EvidenceFiles.Confined(root, job.ResultDocument);
                    if (!envelope.Documents.Any(d => d.Path == job.ResultDocument && d.Type == "assurance-summary"))
                    {
                        findings.Add(new Finding("ASSURANCE_COMPLETE", $"Unbound result document: {job.Id}."));
                    }
                    else if (File.Exists(resultPath) && result != null)
                    {
                        if (result.SchemaVersion != 1 ||
                            result.Status != job.Status ||
                            result.Documents.Length == 0 ||
                            result.Counts != job.Counts ||
                            (definition.Kind == "fuzz-replay"
                                ? !verifiedReplay
                                : definition.Kind == "analysis"
                                ? !AssuranceVerification.CodeqlComplete(result, job, verified)
                                : definition.Kind == "native-test"
                                ? !AssuranceVerification.NativeComplete(result, job, DateTimeOffset.UtcNow)
                                : result.Kind is not ("trx" or "mtp-trx")))
                        {
                            findings.Add(new Finding(
                                "ASSURANCE_COMPLETE", $"Actual result summary disagrees with job: {job.Id}."));
                        }
                    }
                }
                if (job.InputIds.Any(id => assurance.InputIdentities.Count(i => i.Id == id) != 1))
                {
                    findings.Add(new Finding("INPUT_IDENTITY", $"Unknown or duplicate job input: {job.Id}."));
                }
                if (!verified.Has("assurance"))
                {
                    findings.Add(new Finding("ASSURANCE_COMPLETE",
                        $"{definition.Kind} results/run authentication needs independent verification: {job.Id}."));
                }
            }
            return baselineFailed;
        }

        private static bool IsCompleteReplay(CollectedResultSummary? result, CountsRecord? counts)
        {
            if (result is not
                {
                    SchemaVersion: 1, Kind: "fuzz-replay", Status: "completed",
                    Documents.Length: > 0, Replay: { } replay
                } ||
                counts is not
                {
                    ExpectedTargets: > 0, ExecutedTargets: > 0,
                    ExpectedInputs: > 0, ExecutedInputs: > 0, Skipped: >= 0
                } ||
                result.Counts != counts)
            {
                return false;
            }
            return counts.ExpectedTargets == counts.ExecutedTargets &&
                counts.ExpectedInputs == counts.ExecutedInputs &&
                replay.ExpectedPairs == (long)counts.ExpectedTargets.Value * counts.ExpectedInputs.Value &&
                replay.ExecutedPairs == replay.ExpectedPairs &&
                replay.AllowedEmptyRegressionSkips == counts.Skipped &&
                IsReplayDigest(replay.InventoryDigest) &&
                IsReplayDigest(replay.TargetDigest) &&
                IsReplayDigest(replay.ExecutionDigest);
        }

        private static bool IsReplayDigest(string digest)
        {
            if (digest.Length != 71 || !digest.StartsWith("sha256:", StringComparison.Ordinal))
            {
                return false;
            }
            foreach (char value in digest.AsSpan(7))
            {
                if (value is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static string ArtifactKey(ArtifactRecord artifact)
        {
            return string.Join('|', artifact.Kind, artifact.Id.ToUpperInvariant(),
                Versions.Parse(artifact.Version).ToNormalizedString(), artifact.Configuration, artifact.Digest,
                string.Join(',', artifact.Scopes.Tfms.Order(StringComparer.Ordinal)),
                string.Join(',', artifact.Scopes.Rids.Order(StringComparer.Ordinal)),
                string.Join(',', artifact.Scopes.Roslyn.Order(StringComparer.Ordinal)),
                string.Join(',', artifact.Scopes.Platforms.Order(StringComparer.Ordinal)));
        }

        private static bool SameProducer(ProducerRecord first, ProducerRecord second)
        {
            return first.System == second.System &&
                first.Workflow == second.Workflow &&
                first.DefinitionSha == second.DefinitionSha &&
                first.RunId == second.RunId &&
                first.Attempt == second.Attempt &&
                first.Job == second.Job;
        }
    }
}
