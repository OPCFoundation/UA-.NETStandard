// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    [TestFixture]
    public sealed class OciCompletionTests
    {
        [TestCase("containers", 9, 18)]
        [TestCase("pump", 1, 1)]
        public async Task NativeGroupPreservesAllSubjectsAndOriginalStatementsAsync(
            string group, int expectedImages, int expectedPlatforms)
        {
            using var fixture = new NativeFixture(group);
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (int code, JsonNode report, string log) = await fixture.RunAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero, log);
            JsonArray artifacts = report["artifacts"]!.AsArray();
            Assert.That(
                artifacts.Count(a => a!["kind"]!.GetValue<string>() == "oci-index"), Is.EqualTo(expectedImages));
            Assert.That(artifacts.Count(a => a!["kind"]!.GetValue<string>() == "oci-manifest"),
                Is.EqualTo(expectedPlatforms));
            Assert.That(report["attestations"]!.AsArray(), Has.Count.EqualTo(expectedPlatforms * 2));
            Assert.That(report["unmetControls"]!.AsArray().Select(c => c!.GetValue<string>()),
                Does.Not.Contain("ARTIFACT_MEMBERSHIP"));
            Assert.That(report["status"]!.GetValue<string>(), Is.EqualTo("incomplete"),
                "Unsigned producer context cannot establish required acceptance.");
            Assert.That(report["findings"]!.ToJsonString(), Does.Not.Contain("layer evidence is invalid"));
            foreach ((string path, string digest) in fixture.OriginalStatements)
            {
                Assert.That(Digest(await File.ReadAllBytesAsync(path).ConfigureAwait(false)), Is.EqualTo(digest));
            }
        }

        [TestCase("wrong-platform", "ARTIFACT_INTEGRITY")]
        [TestCase("wrong-subject", "ARTIFACT_INTEGRITY")]
        [TestCase("missing-child", "INVENTORY_COMPLETE")]
        [TestCase("bad-diff-id", "INVENTORY_COMPLETE")]
        [TestCase("traversal", "INVENTORY_COMPLETE")]
        [TestCase("unclaimed-native", "INVENTORY_COMPLETE")]
        [TestCase("stale-whiteout-claim", "INVENTORY_COMPLETE")]
        [TestCase("wrong-file-hash", "INVENTORY_COMPLETE")]
        [TestCase("missing-owner", "INVENTORY_COMPLETE")]
        [TestCase("malformed-spdx", "INVENTORY_COMPLETE")]
        public async Task NativePayloadAndSubjectGapsRemainUnmetAsync(string scenario, string expectedControl)
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync(scenario).ConfigureAwait(false);
            (int code, JsonNode report, string log) = await fixture.RunAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero, log);
            Assert.That(report["status"]!.GetValue<string>(), Is.EqualTo("incomplete"));
            Assert.That(report["unmetControls"]!.AsArray().Select(c => c!.GetValue<string>()),
                Does.Contain(expectedControl));
            string detail = report["findings"]!.ToJsonString();
            if (scenario is "bad-diff-id" or "traversal")
            {
                Assert.That(detail, Does.Contain("layer evidence is invalid"));
            }
            if (scenario is "unclaimed-native")
            {
                Assert.That(detail, Does.Contain("Unclaimed final-image payload"));
            }
            if (scenario is "wrong-file-hash" or "missing-owner" or "stale-whiteout-claim")
            {
                Assert.That(detail, Does.Contain("package ownership/source"));
            }
            if (scenario == "malformed-spdx")
            {
                Assert.That(detail, Does.Contain("fails the frozen 2.3 schema"));
            }
        }

        [TestCase("containers", "valid")]
        [TestCase("pump", "valid")]
        [TestCase("pump", "valid-v1")]
        public async Task AuthenticatedNativeGroupSatisfiesRequiredEvaluationAsync(string group, string scenario)
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync(group, scenario)
                .ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero, JsonSerializer.Serialize(report, EvidenceJsonContext.Default.EvaluationReport));
            Assert.That(report.Status, Is.EqualTo("complete"));
            Assert.That(report.UnmetControls, Is.Empty);
            Assert.That(report.ExternalVerificationPerformed, Is.True);
            Assert.That(fixture.Envelope.Assurance.Completed, Is.EqualTo(7));
            Assert.That(fixture.Envelope.Artifacts.Count(a => a.Kind == "oci-manifest"),
                Is.EqualTo(group == "containers" ? 18 : 1));
        }

        [TestCase("containers", 27)]
        [TestCase("pump", 2)]
        public async Task SignedOciPromotionPreservesAllPlatformsAndDiscoverableProofsAsync(string group, int count)
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync(group, "valid")
                .ConfigureAwait(false);
            VerifiedPromotion grant = await fixture.VerifyPromotionAsync("valid").ConfigureAwait(false);
            Assert.That(grant.Request.Members, Has.Length.EqualTo(count));
            Assert.That(grant.Request.Members.Where(m => m.Kind == "oci-index")
                .All(m => m.Aliases is [{ Immutable: true, Name: "2.0.0" }]), Is.True);
        }

        [TestCase("missing-layer")]
        [TestCase("missing-version-alias")]
        [TestCase("mutable-version-alias")]
        [TestCase("child-alias")]
        public async Task SignedOciGrantCannotOmitContentOrImmutableVersionProtectionAsync(string mutation)
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync("pump", "valid")
                .ConfigureAwait(false);
            Assert.That(() => fixture.VerifyPromotionAsync(mutation), Throws.TypeOf<PromotionRejectedException>());
        }

        [Test]
        public async Task AssemblyCliEmitsImmutableNativeV2RatherThanEligibilityAsync()
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (int code, JsonNode envelope, string log) = await fixture.RunAsync(assemble: true).ConfigureAwait(false);
            Assert.That(code, Is.Zero, log);
            Assert.That(envelope["schemaVersion"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(envelope["release"]!["group"]!.GetValue<string>(), Is.EqualTo("pump"));
            Assert.That(envelope["assessment"]!["status"]!.GetValue<string>(), Is.EqualTo("incomplete"));
            Assert.That(envelope["assurance"]!["missing"]!.GetValue<int>(), Is.EqualTo(7));
            foreach (JsonNode? document in envelope["documents"]!.AsArray()
                .Where(d => d!["type"]!.GetValue<string>() is "sbom" or "provenance"))
            {
                string path = EvidenceFiles.Confined(Path.Combine(fixture.Root, "assembled"),
                    document!["path"]!.GetValue<string>());
                Assert.That(Digest(await File.ReadAllBytesAsync(path).ConfigureAwait(false)),
                    Is.EqualTo(document["digest"]!.GetValue<string>()));
            }
        }

        [TestCase("containers", 9)]
        [TestCase("pump", 1)]
        public async Task PromotionMappingRetainsCompleteNativeAndReferrerClosureAsync(string group, int count)
        {
            using var fixture = new NativeFixture(group);
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (EvaluationExpectation expected, OciReferrerBinding[] referrers) =
                await fixture.CreateClosureInputsAsync("valid").ConfigureAwait(false);
            OciImageClosure[] closures = await new OciClosureReader(new EvidenceFiles()).ReadAsync(
                Path.Combine(fixture.Root, "request.json"), expected, referrers, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(closures, Has.Length.EqualTo(count));
            var members = new List<PromotionMember>();
            foreach (OciImageClosure closure in closures)
            {
                foreach (ArtifactRecord artifact in expected.Artifacts.Where(a => a.Id == closure.Image))
                {
                    PromotionMember member = OciClosureReader.ToPromotionMember(
                        closure, artifact, Path.Combine(fixture.Root, "request.json"), fixture.Root,
                        "ghcr-referrers", []);
                    Assert.That(member.Content.Digest, Is.EqualTo(artifact.Digest));
                    Assert.That(member.Kind, Is.EqualTo(artifact.Kind));
                    Assert.That(member.Platform, Is.EqualTo(artifact.Scopes.Platforms.SingleOrDefault()));
                    Assert.That(member.Evidence.Select(e => e.Digest), Is.EquivalentTo(
                        closure.Blobs.Where(b => b.Digest != artifact.Digest).Select(b => b.Digest)));
                    Assert.That(member.Alias, Is.Null);
                    members.Add(member);
                }
                Assert.That(closure.Blobs.Select(b => b.MediaType), Does.Contain("application/vnd.in-toto+json"));
                Assert.That(closure.Blobs.Select(b => b.MediaType),
                    Does.Contain("application/vnd.oci.image.layer.v1.tar"));
                Assert.That(closure.Referrers, Has.Length.EqualTo(group == "containers" ? 3 : 2));
                foreach (OciBlobReference blob in closure.Blobs)
                {
                    Assert.That(Digest(await File.ReadAllBytesAsync(
                        EvidenceFiles.Confined(fixture.Root, blob.Path)).ConfigureAwait(false)),
                        Is.EqualTo(blob.Digest));
                }
            }
            Assert.That(members, Has.Count.EqualTo(expected.Artifacts.Length));
            string digest = Digest(Encoding.UTF8.GetBytes("offline closure validation, not release authority"));
            await PromotionRequestValidator.ValidateAsync(new PromotionRequest(
                1, group, "ghcr-referrers", VerificationControls.ArtifactSetDigest(expected.Artifacts),
                digest, digest, digest, expected.Source.ActualSha, expected.Producer.RunId, expected.Producer.Attempt,
                [.. members]), fixture.Root, new EvidenceFiles(), CancellationToken.None).ConfigureAwait(false);
        }

        [TestCase("missing-referrer")]
        [TestCase("wrong-referrer-subject")]
        [TestCase("changed-layer")]
        [TestCase("missing-layer")]
        [TestCase("unsafe-layout")]
        public async Task PromotionClosureRejectsIncompleteOrAlteredObjectsAsync(string scenario)
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (EvaluationExpectation expected, OciReferrerBinding[] referrers) =
                await fixture.CreateClosureInputsAsync(scenario).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(() => new OciClosureReader(new EvidenceFiles()).ReadAsync(
                Path.Combine(fixture.Root, "request.json"), expected, referrers, CancellationToken.None));
        }

        [TestCase("foreign-subject")]
        [TestCase("platform-alias")]
        public async Task PromotionMappingRejectsForeignIdentityOrAdditionalPlatformTagAsync(string scenario)
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (EvaluationExpectation expected, OciReferrerBinding[] referrers) =
                await fixture.CreateClosureInputsAsync("valid").ConfigureAwait(false);
            OciImageClosure[] closures = await new OciClosureReader(new EvidenceFiles()).ReadAsync(
                Path.Combine(fixture.Root, "request.json"), expected, referrers, CancellationToken.None)
                .ConfigureAwait(false);
            ArtifactRecord artifact = expected.Artifacts.Single(a => a.Kind == "oci-manifest");
            if (scenario == "foreign-subject")
            {
                artifact = artifact with { Id = "ghcr.io/foreign/image" };
            }
            Assert.Throws<InvalidDataException>(() => OciClosureReader.ToPromotionMember(
                closures[0], artifact, Path.Combine(fixture.Root, "request.json"), fixture.Root,
                "ghcr-referrers", [], scenario == "platform-alias" ? "2.0.0" : null));
        }

        [TestCase("nested")]
        [TestCase("outside")]
        public async Task PromotionMappingConfinesRequestRelativeBlobsToCandidateRootAsync(string scenario)
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (EvaluationExpectation expected, OciReferrerBinding[] referrers) =
                await fixture.CreateClosureInputsAsync("valid").ConfigureAwait(false);
            string nested = Path.Combine(fixture.Root, "staged");
            Directory.CreateDirectory(nested);
            string requestPath = Path.Combine(fixture.Root, "request.json");
            if (scenario == "nested")
            {
                Directory.Move(Path.Combine(fixture.Root, "layout"), Path.Combine(nested, "layout"));
                File.Move(requestPath, Path.Combine(nested, "request.json"));
                requestPath = Path.Combine(nested, "request.json");
            }
            OciImageClosure[] closures = await new OciClosureReader(new EvidenceFiles()).ReadAsync(
                requestPath, expected, referrers, CancellationToken.None).ConfigureAwait(false);
            ArtifactRecord artifact = expected.Artifacts.Single(a => a.Kind == "oci-index");
            if (scenario == "outside")
            {
                Assert.Throws<InvalidDataException>(() => OciClosureReader.ToPromotionMember(
                    closures[0], artifact, requestPath, nested, "ghcr-referrers", []));
                return;
            }
            PromotionMember member = OciClosureReader.ToPromotionMember(
                closures[0], artifact, requestPath, fixture.Root, "ghcr-referrers", []);
            foreach (PromotionFile file in member.Evidence.Prepend(member.Content))
            {
                Assert.That(file.Path, Does.StartWith("staged/layout/blobs/sha256/"));
                Assert.That(Digest(await File.ReadAllBytesAsync(
                    EvidenceFiles.Confined(fixture.Root, file.Path)).ConfigureAwait(false)),
                    Is.EqualTo(file.Digest));
            }
        }

        [Test]
        public async Task AuthenticatedClosureRetainsExactSignatureBundlesAsync()
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync("pump", "valid")
                .ConfigureAwait(false);
            OciImageClosure[] closures = await fixture.ReadAuthenticatedClosureAsync("valid").ConfigureAwait(false);
            Assert.That(closures, Has.Length.EqualTo(1));
            Assert.That(closures[0].Referrers, Has.Length.EqualTo(2));
        }

        [TestCase("tampered-context")]
        [TestCase("unbound-signature")]
        public async Task AuthenticatedClosureRejectsDetachedOrChangedProofsAsync(string scenario)
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync("pump", "valid")
                .ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(() => fixture.ReadAuthenticatedClosureAsync(scenario));
        }

        [Test]
        public async Task AssemblyCliRetainsOriginalReferrerContextWithoutAuthorizationAsync()
        {
            using var fixture = new NativeFixture("pump");
            await fixture.CreateAsync("valid").ConfigureAwait(false);
            (_, OciReferrerBinding[] referrers) = await fixture.CreateClosureInputsAsync("valid")
                .ConfigureAwait(false);
            string path = Path.Combine(fixture.Root, "referrers.json");
            await NativeFixture.WriteReferrerContextAsync(path, referrers).ConfigureAwait(false);
            (int code, JsonNode envelope, string log) = await fixture.RunAsync(true, path).ConfigureAwait(false);
            Assert.That(code, Is.Zero, log);
            string expectedDigest = Digest(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            JsonNode document = envelope["documents"]!.AsArray().Single(d =>
                d!["type"]!.GetValue<string>() == "producer-record" &&
                d["digest"]!.GetValue<string>() == expectedDigest)!;
            Assert.That(document["digest"]!.GetValue<string>(),
                Is.EqualTo(expectedDigest));
            Assert.That(envelope["assessment"]!["status"]!.GetValue<string>(), Is.EqualTo("incomplete"));
        }

        [TestCase("unsigned")]
        [TestCase("wrong-issuer")]
        [TestCase("wrong-ref")]
        [TestCase("wrong-workflow")]
        [TestCase("wrong-definition")]
        [TestCase("wrong-root")]
        [TestCase("missing-subject")]
        [TestCase("wrong-repository")]
        [TestCase("wrong-platform-reference")]
        [TestCase("wrong-builder")]
        [TestCase("wrong-tool")]
        [TestCase("predicate-tampering")]
        [TestCase("artifact-signature")]
        [TestCase("stale")]
        [TestCase("unclaimed-native")]
        [TestCase("missing-owner")]
        public async Task SignedNativeClaimsCannotHideInvalidGroupEvidenceAsync(string scenario)
        {
            using AuthenticatedFixture fixture = await AuthenticatedFixture.CreateAsync("pump", scenario)
                .ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1), scenario);
            Assert.That(report.Status, Is.EqualTo("incomplete"));
            Assert.That(report.UnmetControls, Is.Not.Empty);
        }

        private static string Digest(byte[] bytes)
        {
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        }

        private sealed class AuthenticatedFixture : IDisposable, ITrustPolicySource,
            IRecordSignatureVerifier, IArtifactSignatureVerifier
        {
            private AuthenticatedFixture(string group)
            {
                m_native = new NativeFixture(group);
                m_signer = RSA.Create(2048);
                m_verifier = RSA.Create();
                m_verifier.ImportParameters(m_signer.ExportParameters(false));
            }

            public EvidenceEnvelope Envelope { get; private set; } = null!;

            private string EvidenceRoot => Path.Combine(m_native.Root, "evidence");
            private string Repository => Path.Combine(m_native.Root, "repository");
            private string BundlePath => Path.Combine(m_native.Root, "verification-bundle.json");
            private string EvidencePath => Path.Combine(EvidenceRoot, "release-evidence.json");
            private string ExpectedPath => Path.Combine(EvidenceRoot, "expected.json");
            private string PromotionWork => m_native.Root + "-promotion-controller";
            private string KeyDigest => Digest(m_verifier.ExportSubjectPublicKeyInfo());

            public static async Task<AuthenticatedFixture> CreateAsync(string group, string scenario)
            {
                var fixture = new AuthenticatedFixture(group);
                await fixture.InitializeAsync(scenario).ConfigureAwait(false);
                return fixture;
            }

            public async Task<(int Code, EvaluationReport Report)> EvaluateAsync()
            {
                var verifier = new TrustedEvidenceVerifier(m_files, this, this, TimeProvider.System);
                string output = Path.Combine(m_native.Root, "verified-report.json");
                int code = await new EvidenceEvaluator(m_files, verifier, this).EvaluateAsync(
                    Repository, EvidencePath, ExpectedPath, output, null, CancellationToken.None,
                    BundlePath, Path.Combine(m_native.Root, "independent-test-anchor.json")).ConfigureAwait(false);
                return (code, await m_files.ReadModelAsync(
                    output, EvidenceJsonContext.Default.EvaluationReport, CancellationToken.None)
                    .ConfigureAwait(false));
            }

            public async Task<OciImageClosure[]> ReadAuthenticatedClosureAsync(string scenario)
            {
                VerificationRecord signatures = await m_files.ReadModelAsync(
                    Path.Combine(m_native.Root, "artifact-signatures.record.json"),
                    VerificationJsonContext.Default.VerificationRecord, CancellationToken.None).ConfigureAwait(false);
                (_, OciReferrerBinding[] referrers) = await m_native.CreateClosureInputsAsync(
                    "valid", scenario == "unbound-signature" ? null : signatures.ArtifactSignatures)
                    .ConfigureAwait(false);
                string contextPath = Path.Combine(EvidenceRoot, "oci-referrers.json");
                await NativeFixture.WriteReferrerContextAsync(contextPath, referrers).ConfigureAwait(false);
                Envelope = Envelope with
                {
                    Documents =
                    [
                        .. Envelope.Documents,
                        new("producer-record", "json", "1", "oci-referrers.json",
                            await m_files.DigestAsync(contextPath, CancellationToken.None).ConfigureAwait(false),
                            Envelope.Documents.Single(d => d.Type == "source-record").Subject)
                    ]
                };
                await SaveProofsAsync().ConfigureAwait(false);
                var verifier = new TrustedEvidenceVerifier(m_files, this, this, TimeProvider.System);
                VerifiedClaims claims = await verifier.VerifyAsync(
                    Repository, EvidencePath, Envelope, BundlePath,
                    Path.Combine(m_native.Root, "independent-test-anchor.json"), null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(claims.Findings, Is.Empty);
                foreach (ArtifactSignatureProof proof in signatures.ArtifactSignatures!)
                {
                    bool valid = await VerifyAsync(
                        Path.Combine(m_native.Root, "layout", "blobs", "sha256", proof.ArtifactDigest[7..]),
                        proof, m_native.Root, m_policy, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(valid, Is.True, "Closure fixtures require genuine artifact signatures.");
                }
                if (scenario == "tampered-context")
                {
                    await File.AppendAllTextAsync(contextPath, " ").ConfigureAwait(false);
                }
                return await new OciClosureReader(m_files).ReadAuthenticatedAsync(
                    Path.Combine(m_native.Root, "request.json"), EvidencePath, Envelope, claims,
                    CancellationToken.None)
                    .ConfigureAwait(false);
            }

            public async Task<VerifiedPromotion> VerifyPromotionAsync(string mutation)
            {
                OciImageClosure[] closures = await ReadAuthenticatedClosureAsync("valid").ConfigureAwait(false);
                string requestPath = Path.Combine(m_native.Root, "promotion-request.json");
                string envelopeDigest = await m_files.DigestAsync(EvidencePath, CancellationToken.None)
                    .ConfigureAwait(false);
                PromotionFile[] common =
                [
                    new(Path.GetRelativePath(m_native.Root, EvidencePath).Replace('\\', '/'), envelopeDigest),
                    .. Envelope.Documents.DistinctBy(d => d.Digest).Select(d =>
                        new PromotionFile("evidence/" + d.Path, d.Digest))
                ];
                PromotionMember[] members = [.. Envelope.Artifacts.Select(a =>
                {
                    OciImageClosure closure = closures.Single(c => c.Image == a.Id);
                    PromotionMember member = OciClosureReader.ToPromotionMember(
                        closure, a, Path.Combine(m_native.Root, "request.json"), m_native.Root, "ghcr-referrers", common);
                    return a.Digest == closure.RootDigest
                        ? member with { Aliases = [new(a.Version, true)] } : member;
                })];
                int root = Array.FindIndex(members, m => m.Kind == "oci-index");
                int child = Array.FindIndex(members, m => m.Kind == "oci-manifest");
                if (mutation == "missing-layer")
                {
                    members[root] = members[root] with { Evidence = members[root].Evidence[..^1] };
                }
                else if (mutation == "missing-version-alias")
                {
                    members[root] = members[root] with { Aliases = [] };
                }
                else if (mutation == "mutable-version-alias")
                {
                    members[root] = members[root] with { Aliases = [new("2.0.0", false)] };
                }
                else if (mutation == "child-alias")
                {
                    members[child] = members[child] with { Aliases = [new("child-only", true)] };
                }
                var request = new PromotionRequest(
                    1, Envelope.Release.Group, "ghcr-referrers",
                    VerificationControls.ArtifactSetDigest(Envelope.Artifacts), envelopeDigest,
                    m_policy.ExpectedIntentDigest, m_policy.PolicyDigest, Envelope.Source.ActualSha,
                    Envelope.Producer.RunId, Envelope.Producer.Attempt, members);
                await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(
                    request, PromotionJsonContext.Default.PromotionRequest)).ConfigureAwait(false);
                string boundaryPath = Path.Combine(m_native.Root, "publication-boundary.record.json");
                VerificationRecord boundary = await m_files.ReadModelAsync(
                    boundaryPath, VerificationJsonContext.Default.VerificationRecord, CancellationToken.None)
                    .ConfigureAwait(false);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(boundary with
                {
                    PromotionRequestDigest = await m_files.DigestAsync(requestPath, CancellationToken.None)
                        .ConfigureAwait(false)
                }, VerificationJsonContext.Default.VerificationRecord);
                await File.WriteAllBytesAsync(boundaryPath, bytes).ConfigureAwait(false);
                var dsse = new DsseFixture(kPayloadType, Convert.ToBase64String(bytes),
                    [new(KeyDigest, Convert.ToBase64String(m_signer.SignData(
                        Pae(bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))]);
                await File.WriteAllBytesAsync(Path.Combine(m_native.Root, "publication-boundary.dsse.json"),
                    JsonSerializer.SerializeToUtf8Bytes(dsse, TrustedTestJsonContext.Default.DsseFixture))
                    .ConfigureAwait(false);
                Directory.CreateDirectory(PromotionWork);
                return await VerifiedPromotion.VerifyAsync(
                    new PromotionVerificationInput(Repository, m_native.Root, EvidencePath, ExpectedPath, BundlePath,
                        Path.Combine(m_native.Root, "independent-test-anchor.json"), requestPath, PromotionWork),
                    m_files, new TrustedEvidenceVerifier(m_files, this, this, TimeProvider.System), this,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public Task<TrustedPolicySnapshot?> LoadAsync(
                string? path, string[] candidateRoots, CancellationToken cancellationToken)
            {
                return Task.FromResult<TrustedPolicySnapshot?>(m_policy);
            }

            public async Task<bool> VerifyAsync(
                string recordPath, string bundlePath, VerificationAuthority authority,
                TrustedPolicySnapshot policy, CancellationToken cancellationToken)
            {
                DsseFixture proof = await m_files.ReadModelAsync(
                    bundlePath, TrustedTestJsonContext.Default.DsseFixture, cancellationToken).ConfigureAwait(false);
                byte[] bytes = await File.ReadAllBytesAsync(recordPath, cancellationToken).ConfigureAwait(false);
                return authority.Id == "isolated-oci-authority" && authority.Issuer == "https://test.invalid" &&
                    authority.CertificateIdentity == "isolated-oci-controller" &&
                    authority.Ref == "refs/heads/master" && authority.Workflow == ".github/workflows/release.yml" &&
                    authority.DefinitionSha == new string('c', 40) &&
                    proof.PayloadType == kPayloadType && proof.Signatures.Length == 1 &&
                    proof.Signatures[0].KeyId == KeyDigest &&
                    Convert.FromBase64String(proof.Payload).AsSpan().SequenceEqual(bytes) &&
                    m_verifier.VerifyData(Pae(bytes), Convert.FromBase64String(proof.Signatures[0].Sig),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            public async Task<bool> VerifyAsync(
                string artifactPath, ArtifactSignatureProof proof, string bundleRoot,
                TrustedPolicySnapshot policy, CancellationToken cancellationToken)
            {
                byte[] bytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken).ConfigureAwait(false);
                byte[] signature = await File.ReadAllBytesAsync(
                    EvidenceFiles.Confined(bundleRoot, proof.BundlePath), cancellationToken).ConfigureAwait(false);
                return proof.ArtifactDigest == Digest(bytes) && proof.SignatureDigest == Digest(signature) &&
                    proof.SignerDigest == KeyDigest &&
                    m_verifier.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            public void Dispose()
            {
                m_signer.Dispose();
                m_verifier.Dispose();
                m_native.Dispose();
                if (Directory.Exists(PromotionWork))
                {
                    Directory.Delete(PromotionWork, true);
                }
            }

            private async Task InitializeAsync(string scenario)
            {
                await m_native.CreateAsync(scenario).ConfigureAwait(false);
                Directory.CreateDirectory(Path.Combine(Repository, ".azurepipelines"));
                Directory.CreateDirectory(EvidenceRoot);
                foreach (string name in new[]
                {
                    "release-policy.json", "release-evidence.schema.json", "release-artifacts.json",
                    "assurance-profiles.json", "verification-bundle.schema.json", "verification-record.schema.json",
                    "trusted-policy-snapshot.schema.json"
                })
                {
                    byte[] bytes = await File.ReadAllBytesAsync(
                        Path.Combine(NativeFixture.RepositoryRoot, ".azurepipelines", name)).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(Path.Combine(Repository, ".azurepipelines", name), bytes)
                        .ConfigureAwait(false);
                }
                string policyPath = Path.Combine(Repository, ".azurepipelines", "release-policy.json");
                JsonNode policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath).ConfigureAwait(false))!;
                policy["stage"] = "required";
                await File.WriteAllTextAsync(policyPath, policy.ToJsonString()).ConfigureAwait(false);
                string policyDigest = await m_files.DigestAsync(policyPath, CancellationToken.None)
                    .ConfigureAwait(false);
                EvaluationExpectation context = await m_files.ReadModelAsync(
                    Path.Combine(m_native.Root, "context.json"), EvidenceJsonContext.Default.EvaluationExpectation,
                    CancellationToken.None).ConfigureAwait(false);
                OciBuildExpectation build = m_native.Builds.Values.First();
                ToolRecord[] tools =
                [
                    new("dotnet", "10.0.100", TextDigest("dotnet")),
                    new("ilc", "10.0.100", TextDigest("ilc")),
                    new("codeql", "2.23.0", TextDigest("codeql")),
                    new("buildkit", "synthetic", build.BuildkitDigest),
                    new("buildkit-syft-scanner", "synthetic", build.ScannerDigest)
                ];
                context = context with
                {
                    PolicyDigest = policyDigest,
                    Producer = context.Producer with { Tools = tools }
                };
                (AssuranceRecord scope, DocumentRecord[] results, ProducerPin[] producers) =
                    await CreateAssuranceAsync(context).ConfigureAwait(false);
                Envelope = await new OciArtifactAdapter(m_files).AssembleAsync(
                    Repository, Path.Combine(m_native.Root, "request.json"), context, scope, EvidenceRoot,
                    CancellationToken.None).ConfigureAwait(false);
                var documents = new List<DocumentRecord>(Envelope.Documents);
                documents.AddRange(results);
                Envelope = Envelope with { Documents = [.. documents] };
                FrozenFile[] contracts = [.. await Task.WhenAll(Directory.EnumerateFiles(
                    Path.Combine(Repository, ".azurepipelines")).Select(async path => new FrozenFile(
                        ".azurepipelines/" + Path.GetFileName(path),
                        await m_files.DigestAsync(path, CancellationToken.None).ConfigureAwait(false),
                        new FileInfo(path).Length))).ConfigureAwait(false)];
                DateTimeOffset now = DateTimeOffset.UtcNow;
                m_policy = new TrustedPolicySnapshot(
                    1, "isolated-oci-test-only", "required", 1, now.AddHours(-1), now.AddHours(1),
                    policyDigest, contracts, Envelope.Release, Digest(Encoding.UTF8.GetBytes("not-initialized")),
                    [new("isolated-oci-authority", context.Source.Repository, "https://test.invalid",
                        "isolated-oci-controller", ".github/workflows/release.yml", new string('c', 40),
                        "refs/heads/master", VerificationControls.Kinds)],
                    [.. producers, new(context.Producer.System, context.Producer.Workflow,
                        context.Producer.DefinitionSha, [context.Producer.Job], tools)], [],
                    new(Path.Combine(m_native.Root, "protected-tool"), KeyDigest, "synthetic"),
                    new(Path.Combine(m_native.Root, "protected-root"), KeyDigest, "synthetic"));
                if (scenario == "wrong-tool")
                {
                    string key = m_native.Builds.Keys.First();
                    m_native.Builds[key] = m_native.Builds[key] with
                    {
                        ScannerDigest = Digest(Encoding.UTF8.GetBytes("unapproved scanner"))
                    };
                }
                if (scenario == "missing-subject")
                {
                    m_native.Builds.Remove(m_native.Builds.Keys.First());
                }
                await SaveProofsAsync().ConfigureAwait(false);
                await MutateAsync(scenario).ConfigureAwait(false);
            }

            private async Task<(AssuranceRecord Scope, DocumentRecord[] Documents, ProducerPin[] Producers)>
                CreateAssuranceAsync(EvaluationExpectation context)
            {
                string profilePath = Path.Combine(Repository, ".azurepipelines", "assurance-profiles.json");
                ProfilesConfiguration profiles = await m_files.ReadModelAsync(profilePath,
                    EvidenceJsonContext.Default.ProfilesConfiguration, CancellationToken.None).ConfigureAwait(false);
                var jobs = new List<JobRecord>();
                var documents = new List<DocumentRecord>();
                var source = new SubjectRecord("source", context.Source.Repository,
                    Digest(JsonSerializer.SerializeToUtf8Bytes(
                        context.Source, EvidenceJsonContext.Default.SourceRecord)));
                foreach (ProfileConfiguration profile in profiles.Profiles)
                {
                    foreach (ProfileJob definition in profile.Jobs)
                    {
                        var counts = definition.Kind == "analysis"
                            ? new CountsRecord(AnalyzedProjects: 1, Findings: 0, UnresolvedFindings: 0)
                            : new CountsRecord(1, 1, 1, 0, 0, 1, 1, 1, 1);
                        ProducerRecord producer = context.Producer with
                        {
                            Workflow = definition.Kind == "analysis" ? ".github/workflows/codeql-analysis.yml" :
                                definition.Kind == "native-test" ? ".azurepipelines/test-aot.yml" :
                                    ".github/workflows/buildandtest.yml",
                            RunId = (2000 + jobs.Count).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Job = definition.Id
                        };
                        var job = new JobRecord(definition.Id, profile.Id, definition.Project,
                            profile.Configuration, profile.Host, profile.HostTfm, profile.LibraryTfm,
                            profile.Platform, "all", profile.Filter, true, "completed", ["profile"],
                            context.Source.ActualSha, producer, definition.Id + ".result.json", Counts: counts);
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        var native = new NativeAssuranceProof(
                            job.Project, "Release", "net10.0", "net10.0", "win-x64", true,
                            context.Source.ActualSha, producer.RunId, 1, new string('c', 32), 100, 0,
                            TextDigest("native-image"), TextDigest("native-image"),
                            TextDigest("native-image"), TextDigest("native-image"),
                            now.AddMinutes(-2), now.AddMinutes(-1),
                            new("pe32plus", "amd64", "DNDH", 5, 0, TextDigest("native-image"), 4096),
                            new(".NET 10.0.1", "x64", false, false, "completed"),
                            new("10.0.100", "10.0.100", TextDigest("ilc")));
                        var analysis = new CodeqlAssuranceProof(
                            "completed", context.Source.ActualSha, context.Source.ActualRef, producer.RunId, 1,
                            "3000", "synthetic-sarif", "assurance-csharp-net10",
                            new("CodeQL", "2.23.0", TextDigest("codeql")),
                            [new(TextDigest("project"), 1, 1, TextDigest("sources"), TextDigest("sources"), 0)],
                            1, 1, 0, 0, "clean-analysis", TextDigest("db"), TextDigest("extract"),
                            TextDigest("config"),
                            TextDigest("suite"), TextDigest("pack"), TextDigest("query"), TextDigest("query-results"),
                            TextDigest("build"), TextDigest("population"));
                        var summary = new CollectedResultSummary(1, definition.Kind switch
                        {
                            "native-test" => "native-aot",
                            "analysis" => "codeql",
                            "fuzz-replay" => "fuzz-replay",
                            _ => "trx"
                        }, "completed", [new(TextDigest("actual-result"))], counts,
                            definition.Kind == "fuzz-replay"
                                ? new(TextDigest("inventory"), TextDigest("targets"), TextDigest("execution"), 1, 1, 0)
                                : null,
                            definition.Kind == "native-test" ? native : null,
                            definition.Kind == "analysis" ? analysis : null);
                        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                            summary, EvidenceJsonContext.Default.CollectedResultSummary);
                        await File.WriteAllBytesAsync(Path.Combine(EvidenceRoot, job.ResultDocument!), bytes)
                            .ConfigureAwait(false);
                        documents.Add(new DocumentRecord(
                            "assurance-summary", "json", "1", job.ResultDocument!, Digest(bytes), source));
                        jobs.Add(job);
                    }
                }
                string profileDigest = await m_files.DigestAsync(profilePath, CancellationToken.None)
                    .ConfigureAwait(false);
                var scope = new AssuranceRecord([.. profiles.Profiles.Select(p => p.Id)],
                    jobs.Count, jobs.Count, jobs.Count, 0, 0, 0, [.. jobs],
                    [new("profile", "profile", "contracts/assurance-profiles.json", profileDigest)]);
                ProducerPin[] producers = [.. jobs.Select(j => j.Producer!)
                    .GroupBy(p => p.Workflow, StringComparer.Ordinal).Select(g => new ProducerPin(
                        g.First().System, g.Key, g.First().DefinitionSha, [.. g.Select(p => p.Job)],
                        context.Producer.Tools))];
                return (scope, [.. documents], producers);
            }

            private async Task SaveProofsAsync()
            {
                await File.WriteAllBytesAsync(EvidencePath, JsonSerializer.SerializeToUtf8Bytes(
                    Envelope, EvidenceJsonContext.Default.EvidenceEnvelope)).ConfigureAwait(false);
                await File.WriteAllBytesAsync(ExpectedPath, JsonSerializer.SerializeToUtf8Bytes(
                    new EvaluationExpectation(Envelope.Source, Envelope.Producer, Envelope.Release,
                        Envelope.Policy.Digest, Envelope.Artifacts),
                    EvidenceJsonContext.Default.EvaluationExpectation))
                    .ConfigureAwait(false);
                string evidenceDigest = await m_files.DigestAsync(EvidencePath, CancellationToken.None)
                    .ConfigureAwait(false);
                FrozenFile[] documents = [.. Envelope.Documents.Select(d => new FrozenFile(
                    d.Path, d.Digest, new FileInfo(EvidenceFiles.Confined(EvidenceRoot, d.Path)).Length))];
                var signatures = new List<ArtifactSignatureProof>();
                int index = 0;
                foreach (ArtifactRecord artifact in Envelope.Artifacts)
                {
                    byte[] bytes = await File.ReadAllBytesAsync(
                        Path.Combine(m_native.Root, "layout", "blobs", "sha256", artifact.Digest[7..]))
                        .ConfigureAwait(false);
                    byte[] signature = m_signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    string path = $"artifact-{index++}.sig";
                    await File.WriteAllBytesAsync(Path.Combine(m_native.Root, path), signature).ConfigureAwait(false);
                    signatures.Add(new(
                        artifact.Kind, artifact.Id, artifact.Digest, path, Digest(signature), KeyDigest));
                }
                var proofs = new List<VerificationProof>();
                string? intent = null;
                foreach (string kind in VerificationControls.Kinds)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    var record = new VerificationRecord(
                        1, "record-" + kind, kind, evidenceDigest, m_policy.PolicyDigest,
                        VerificationControls.ArtifactSetDigest(Envelope.Artifacts), Envelope.Source, Envelope.Producer,
                        Envelope.Release, Envelope.Artifacts, documents,
                        kind == "assurance" ? Envelope.Assurance.Jobs : [], VerificationControls.ForKind(kind),
                        1, now.AddMinutes(-5), now.AddMinutes(30),
                        new([Envelope.Release.Group], ["ghcr-referrers"], ["isolated-synthetic-record"], []),
                        intent, ArtifactSignatures: kind == "artifact-signatures" ? [.. signatures] : null,
                        OciBuilds: kind == "producer" ? m_native.Builds : null);
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record,
                        VerificationJsonContext.Default.VerificationRecord);
                    string recordPath = kind + ".record.json";
                    await File.WriteAllBytesAsync(Path.Combine(m_native.Root, recordPath), bytes)
                        .ConfigureAwait(false);
                    var dsse = new DsseFixture(kPayloadType, Convert.ToBase64String(bytes),
                        [new(KeyDigest, Convert.ToBase64String(m_signer.SignData(
                            Pae(bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))]);
                    string signaturePath = kind + ".dsse.json";
                    await File.WriteAllBytesAsync(Path.Combine(m_native.Root, signaturePath),
                        JsonSerializer.SerializeToUtf8Bytes(dsse, TrustedTestJsonContext.Default.DsseFixture))
                        .ConfigureAwait(false);
                    proofs.Add(new("isolated-oci-authority", recordPath, signaturePath));
                    if (kind == "release-intent")
                    {
                        intent = Digest(bytes);
                    }
                }
                m_policy = m_policy with { ExpectedIntentDigest = intent! };
                await File.WriteAllBytesAsync(BundlePath, JsonSerializer.SerializeToUtf8Bytes(
                    new VerificationBundle(1, [.. proofs], "request.json"),
                    VerificationJsonContext.Default.VerificationBundle)).ConfigureAwait(false);
            }

            private async Task MutateAsync(string scenario)
            {
                switch (scenario)
                {
                    case "unsigned":
                        foreach (string path in Directory.EnumerateFiles(m_native.Root, "*.dsse.json"))
                        {
                            DsseFixture signature = await m_files.ReadModelAsync(
                                path, TrustedTestJsonContext.Default.DsseFixture, CancellationToken.None)
                                .ConfigureAwait(false);
                            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(
                                signature with { Signatures = [] }, TrustedTestJsonContext.Default.DsseFixture))
                                .ConfigureAwait(false);
                        }
                        break;
                    case "wrong-issuer":
                        m_policy = m_policy with
                        {
                            Authorities = [m_policy.Authorities[0] with { Issuer = "https://other.invalid" }]
                        };
                        break;
                    case "wrong-ref":
                        m_policy = m_policy with
                        {
                            Authorities = [m_policy.Authorities[0] with { Ref = "refs/heads/foreign" }]
                        };
                        break;
                    case "wrong-workflow":
                        m_policy = m_policy with
                        {
                            Authorities = [m_policy.Authorities[0] with { Workflow = ".github/workflows/foreign.yml" }]
                        };
                        break;
                    case "wrong-definition":
                        Envelope = Envelope with
                        {
                            Producer = Envelope.Producer with { DefinitionSha = new string('d', 40) }
                        };
                        await File.WriteAllBytesAsync(EvidencePath, JsonSerializer.SerializeToUtf8Bytes(
                            Envelope, EvidenceJsonContext.Default.EvidenceEnvelope)).ConfigureAwait(false);
                        break;
                    case "wrong-root":
                        string request = Path.Combine(m_native.Root, "request.json");
                        JsonNode value = JsonNode.Parse(await File.ReadAllTextAsync(request).ConfigureAwait(false))!;
                        value["images"]![0]!["rootDigest"] = "sha256:" + new string('e', 64);
                        await File.WriteAllTextAsync(request, value.ToJsonString()).ConfigureAwait(false);
                        break;
                    case "predicate-tampering":
                        await File.AppendAllTextAsync(m_native.OriginalStatements[0].Path, " ").ConfigureAwait(false);
                        break;
                    case "artifact-signature":
                        await File.WriteAllBytesAsync(Path.Combine(m_native.Root, "artifact-0.sig"), [0, 1, 2])
                            .ConfigureAwait(false);
                        break;
                    case "stale":
                        m_policy = m_policy with { CheckpointSequence = 2 };
                        break;
                }
            }

            private static byte[] Pae(byte[] bytes)
            {
                return [.. Encoding.UTF8.GetBytes(
                    $"DSSEv1 {Encoding.UTF8.GetByteCount(kPayloadType)} {kPayloadType} {bytes.Length} "), .. bytes];
            }

            private static string TextDigest(string value)
            {
                return Digest(Encoding.UTF8.GetBytes(value));
            }

            private const string kPayloadType = "application/vnd.opcua.release-record+json";
            private readonly EvidenceFiles m_files = new();
            private readonly NativeFixture m_native;
            private readonly RSA m_signer;
            private readonly RSA m_verifier;
            private TrustedPolicySnapshot m_policy = null!;
        }

        private sealed class NativeFixture(string group) : IDisposable
        {
            public List<(string Path, string Digest)> OriginalStatements { get; } = [];

            public Dictionary<string, OciBuildExpectation> Builds { get; } = new(StringComparer.Ordinal);

            public string Root => m_work;

            public static string RepositoryRoot => FindRoot();

            public async Task CreateAsync(string scenario)
            {
                string root = FindRoot();
                JsonNode catalog = JsonNode.Parse(await File.ReadAllTextAsync(
                    Path.Combine(root, ".azurepipelines", "release-artifacts.json")).ConfigureAwait(false))!;
                JsonNode definition = catalog["groups"]!.AsArray().Single(g => g!["id"]!.GetValue<string>() == group)!;
                string policy = Path.Combine(root, ".azurepipelines", "release-policy.json");
                var context = new JsonObject
                {
                    ["source"] = new JsonObject
                    {
                        ["repository"] = "OPCFoundation/UA-.NETStandard", ["actualSha"] = kSource,
                        ["actualRef"] = "refs/heads/master", ["trackedClean"] = true
                    },
                    ["producer"] = new JsonObject
                    {
                        ["system"] = "github-actions", ["workflow"] = definition["producer"]!.DeepClone(),
                        ["definitionSha"] = new string('b', 40), ["runId"] = "42", ["attempt"] = 1,
                        ["job"] = "synthetic-native", ["tools"] = new JsonArray()
                    },
                    ["release"] = new JsonObject { ["group"] = group, ["version"] = "2.0.0", ["channel"] = "stable" },
                    ["policyDigest"] = Digest(await File.ReadAllBytesAsync(policy).ConfigureAwait(false)),
                    ["artifacts"] = new JsonArray()
                };
                await File.WriteAllTextAsync(Path.Combine(m_work, "context.json"), context.ToJsonString())
                    .ConfigureAwait(false);
                var images = new JsonArray();
                Directory.CreateDirectory(Path.Combine(m_work, "layout", "blobs", "sha256"));
                foreach (JsonNode? image in definition["images"]!.AsArray())
                {
                    string id = definition["upstreamRepositoryPrefix"]!.GetValue<string>() + "/" +
                        image!["id"]!.GetValue<string>();
                    var children = new JsonArray();
                    foreach (JsonNode? platformValue in definition["platforms"]!.AsArray())
                    {
                        string platform = platformValue!.GetValue<string>();
                        JsonObject runnable = await CreateSubjectAsync(platform, scenario).ConfigureAwait(false);
                        string subject = runnable["digest"]!.GetValue<string>();
                        Builds.Add(id + "@" + subject, new OciBuildExpectation(
                            "https://example.invalid/synthetic-builder", "synthetic-build",
                            "https://github.com/OPCFoundation/UA-.NETStandard.git", kSource,
                            image["dockerfile"]!.GetValue<string>(), "Tool: synthetic-scanner-1",
                            Digest(Encoding.UTF8.GetBytes("synthetic-scanner")),
                            Digest(Encoding.UTF8.GetBytes("synthetic-buildkit")),
                            [new("https://github.com/OPCFoundation/UA-.NETStandard.git", "sha1", kSource)]));
                        runnable["platform"] = new JsonObject
                        {
                            ["os"] = "linux", ["architecture"] = platform.Split('/')[1]
                        };
                        if (platform.EndsWith("/v8", StringComparison.Ordinal))
                        {
                            runnable["platform"]!["variant"] = "v8";
                        }
                        children.Add(runnable);
                        JsonObject sbom = Statement("https://spdx.dev/Document", id,
                            scenario == "wrong-subject" ? "sha256:" + new string('e', 64) : subject,
                            CreateSpdx(scenario));
                        if (scenario is "wrong-repository" or "wrong-platform-reference")
                        {
                            sbom["subject"]![0]!["name"] = scenario == "wrong-repository"
                                ? "ghcr.io/foreign/image"
                                : "pkg:docker/" + id + "@2.0.0?platform=linux%2Farm64";
                        }
                        bool v1 = scenario == "valid-v1";
                        string provenanceType = v1
                            ? "https://slsa.dev/provenance/v1" : "https://slsa.dev/provenance/v0.2";
                        JsonObject provenance = Statement(provenanceType, id, subject,
                            CreateProvenance(image["dockerfile"]!.GetValue<string>(), v1));
                        if (scenario == "wrong-builder")
                        {
                            provenance["predicate"]!["builder"]!["id"] = "https://example.invalid/other-builder";
                        }
                        JsonObject sbomLayer = await AddJsonBlobAsync(sbom, "application/vnd.in-toto+json")
                            .ConfigureAwait(false);
                        JsonObject provenanceLayer = await AddJsonBlobAsync(provenance, "application/vnd.in-toto+json")
                            .ConfigureAwait(false);
                        sbomLayer["annotations"] = new JsonObject
                        {
                            ["in-toto.io/predicate-type"] = "https://spdx.dev/Document"
                        };
                        provenanceLayer["annotations"] = new JsonObject
                        {
                            ["in-toto.io/predicate-type"] = provenanceType
                        };
                        foreach (JsonObject native in new[] { sbomLayer, provenanceLayer })
                        {
                            string hash = native["digest"]!.GetValue<string>();
                            OriginalStatements.Add((BlobPath(hash), hash));
                        }
                        JsonObject config = await AddJsonBlobAsync(new JsonObject(),
                            "application/vnd.oci.image.config.v1+json").ConfigureAwait(false);
                        JsonObject attestation = await AddJsonBlobAsync(new JsonObject
                        {
                            ["schemaVersion"] = 2, ["mediaType"] = kManifestType, ["config"] = config,
                            ["layers"] = scenario == "missing-child"
                                ? new JsonArray(provenanceLayer) : new JsonArray(sbomLayer, provenanceLayer)
                        }, kManifestType).ConfigureAwait(false);
                        attestation["platform"] = new JsonObject { ["os"] = "unknown", ["architecture"] = "unknown" };
                        attestation["annotations"] = new JsonObject
                        {
                            ["vnd.docker.reference.type"] = "attestation-manifest",
                            ["vnd.docker.reference.digest"] = subject
                        };
                        // Native predicates may appear before the runnable sibling they describe.
                        children.Insert(0, attestation);
                    }
                    JsonObject index = await AddJsonBlobAsync(new JsonObject
                    {
                        ["schemaVersion"] = 2, ["mediaType"] = "application/vnd.oci.image.index.v1+json",
                        ["manifests"] = children
                    }, "application/vnd.oci.image.index.v1+json").ConfigureAwait(false);
                    images.Add(new JsonObject
                    {
                        ["id"] = id, ["layout"] = "layout", ["rootDigest"] = index["digest"]!.DeepClone()
                    });
                }
                await File.WriteAllTextAsync(Path.Combine(m_work, "request.json"),
                    new JsonObject { ["images"] = images }.ToJsonString()).ConfigureAwait(false);
            }

            public async Task<(int Code, JsonNode Report, string Log)> RunAsync(
                bool assemble = false, string? referrers = null)
            {
                string root = FindRoot();
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo("dotnet")
                    {
                        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
                    }
                };
                string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
                foreach (string argument in new[]
                {
                    Path.Combine(root, "tools", "Opc.Ua.ReleaseEvidence", "bin", configuration, "net10.0",
                        "Opc.Ua.ReleaseEvidence.dll"),
                    assemble ? "oci-assemble" : "oci", "--repository-root", root,
                    "--request", Path.Combine(m_work, "request.json"),
                    "--context", Path.Combine(m_work, "context.json"), "--output",
                    Path.Combine(m_work, assemble ? "assembled" : "report.json")
                })
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }
                if (referrers != null)
                {
                    process.StartInfo.ArgumentList.Add("--referrers");
                    process.StartInfo.ArgumentList.Add(referrers);
                }
                process.Start();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }
                string log = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
                string reportPath = assemble
                    ? Path.Combine(m_work, "assembled", "release-evidence.json") : Path.Combine(m_work, "report.json");
                Assert.That(File.Exists(reportPath), Is.True, log);
                JsonNode report = JsonNode.Parse(await File.ReadAllTextAsync(reportPath)
                    .ConfigureAwait(false))!;
                return (process.ExitCode, report, log);
            }

            public async Task<(EvaluationExpectation Expected, OciReferrerBinding[] Referrers)>
                CreateClosureInputsAsync(string scenario, ArtifactSignatureProof[]? signatureProofs = null)
            {
                var files = new EvidenceFiles();
                EvaluationExpectation expected = await files.ReadModelAsync(
                    Path.Combine(m_work, "context.json"), EvidenceJsonContext.Default.EvaluationExpectation,
                    CancellationToken.None).ConfigureAwait(false);
                ArtifactsConfiguration catalog = await files.ReadModelAsync(
                    Path.Combine(RepositoryRoot, ".azurepipelines", "release-artifacts.json"),
                    EvidenceJsonContext.Default.ArtifactsConfiguration, CancellationToken.None).ConfigureAwait(false);
                OciRequest request = await files.ReadModelAsync(Path.Combine(m_work, "request.json"),
                    EvidenceJsonContext.Default.OciRequest, CancellationToken.None).ConfigureAwait(false);
                OciAnalysis analysis = await new OciReconciler(files).AnalyzeAsync(
                    request, m_work, expected, catalog.Groups.Single(g => g.Id == group), null, CancellationToken.None)
                    .ConfigureAwait(false);
                expected = expected with { Artifacts = analysis.Artifacts };
                var referrers = new List<OciReferrerBinding>();
                foreach (ArtifactRecord artifact in expected.Artifacts)
                {
                    byte[] subject = await File.ReadAllBytesAsync(BlobPath(artifact.Digest)).ConfigureAwait(false);
                    using JsonDocument document = JsonDocument.Parse(subject);
                    JsonObject config = await AddJsonBlobAsync(
                        new JsonObject { ["kind"] = "fixture-signature" },
                        "application/vnd.opcua.fixture.signature.config").ConfigureAwait(false);
                    byte[] proofBytes = signatureProofs == null
                        ? Encoding.UTF8.GetBytes("offline closure fixture " + artifact.Id + "@" + artifact.Digest)
                        : await File.ReadAllBytesAsync(EvidenceFiles.Confined(m_work,
                            signatureProofs.Single(p =>
                                p.Id == artifact.Id && p.ArtifactDigest == artifact.Digest).BundlePath))
                            .ConfigureAwait(false);
                    JsonObject proof = await AddBlobAsync(proofBytes,
                        "application/vnd.opcua.fixture.signature").ConfigureAwait(false);
                    JsonObject manifest = await AddJsonBlobAsync(new JsonObject
                    {
                        ["schemaVersion"] = 2, ["mediaType"] = kManifestType,
                        ["artifactType"] = "application/vnd.opcua.fixture.signature",
                        ["config"] = config, ["layers"] = new JsonArray(proof),
                        ["subject"] = new JsonObject
                        {
                            ["digest"] = scenario == "wrong-referrer-subject"
                                ? "sha256:" + new string('e', 64) : artifact.Digest,
                            ["mediaType"] = document.RootElement.GetProperty("mediaType").GetString(),
                            ["size"] = subject.Length
                        }
                    }, kManifestType).ConfigureAwait(false);
                    referrers.Add(new(artifact.Id, artifact.Digest, manifest["digest"]!.GetValue<string>(),
                        "application/vnd.opcua.fixture.signature"));
                }
                if (scenario == "missing-referrer")
                {
                    referrers.RemoveAt(0);
                }
                if (scenario is "changed-layer" or "missing-layer")
                {
                    ArtifactRecord runnable = expected.Artifacts.First(a => a.Kind == "oci-manifest");
                    using JsonDocument document = await files.ReadJsonAsync(
                        BlobPath(runnable.Digest), CancellationToken.None).ConfigureAwait(false);
                    string path = BlobPath(
                        document.RootElement.GetProperty("layers")[0].GetProperty("digest").GetString()!);
                    if (scenario == "changed-layer")
                    {
                        await File.WriteAllBytesAsync(path, [0]).ConfigureAwait(false);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                if (scenario == "unsafe-layout")
                {
                    request = request with { Images = [request.Images[0] with { Layout = "../outside" }] };
                    await File.WriteAllBytesAsync(Path.Combine(m_work, "request.json"),
                        JsonSerializer.SerializeToUtf8Bytes(request, EvidenceJsonContext.Default.OciRequest))
                        .ConfigureAwait(false);
                }
                return (expected, [.. referrers]);
            }

            public static Task WriteReferrerContextAsync(string path, OciReferrerBinding[] referrers)
            {
                var bindings = new JsonArray();
                foreach (OciReferrerBinding referrer in referrers)
                {
                    bindings.Add(new JsonObject
                    {
                        ["image"] = referrer.Image, ["subjectDigest"] = referrer.SubjectDigest,
                        ["manifestDigest"] = referrer.ManifestDigest, ["artifactType"] = referrer.ArtifactType
                    });
                }
                return File.WriteAllTextAsync(path, new JsonObject
                {
                    ["schemaVersion"] = 1, ["kind"] = "oci-referrer-context", ["referrers"] = bindings
                }.ToJsonString());
            }

            public void Dispose()
            {
                Directory.Delete(m_work, recursive: true);
            }

            private async Task<JsonObject> CreateSubjectAsync(string platform, string scenario)
            {
                byte[] lower = await TarAsync([("app/removed.so", kPayload), ("cache/old.so", kPayload)])
                    .ConfigureAwait(false);
                var entries = new List<(string Name, byte[] Bytes)>
                {
                    ("app/.wh.removed.so", []), ("cache/.wh..wh..opq", []), ("app/current.so", kPayload)
                };
                if (scenario == "traversal")
                {
                    entries.Add(("../outside.so", kPayload));
                }
                if (scenario == "unclaimed-native")
                {
                    entries.Add(("app/unclaimed.so", kPayload));
                }
                byte[] upper = await TarAsync(entries).ConfigureAwait(false);
                JsonObject lowerDescriptor = await AddBlobAsync(lower, "application/vnd.oci.image.layer.v1.tar")
                    .ConfigureAwait(false);
                JsonObject upperDescriptor = await AddBlobAsync(upper, "application/vnd.oci.image.layer.v1.tar")
                    .ConfigureAwait(false);
                JsonObject configuration = await AddJsonBlobAsync(new JsonObject
                {
                    ["os"] = "linux",
                    ["architecture"] = scenario == "wrong-platform" ? "arm64" : platform.Split('/')[1],
                    ["rootfs"] = new JsonObject
                    {
                        ["type"] = "layers", ["diff_ids"] = new JsonArray(
                            scenario == "bad-diff-id" ? "sha256:" + new string('f', 64) : Digest(lower), Digest(upper))
                    },
                    ["config"] = new JsonObject
                    {
                        ["Labels"] = new JsonObject
                        {
                            ["org.opencontainers.image.revision"] = kSource,
                            ["org.opencontainers.image.version"] = "2.0.0"
                        }
                    }
                }, "application/vnd.oci.image.config.v1+json").ConfigureAwait(false);
                return await AddJsonBlobAsync(new JsonObject
                {
                    ["schemaVersion"] = 2, ["mediaType"] = kManifestType,
                    ["config"] = configuration, ["layers"] = new JsonArray(lowerDescriptor, upperDescriptor)
                }, kManifestType).ConfigureAwait(false);
            }

            private static JsonObject CreateSpdx(string scenario)
            {
                var result = JsonNode.Parse("""
                    {"spdxVersion":"SPDX-2.3","SPDXID":"SPDXRef-DOCUMENT","dataLicense":"CC0-1.0",
                    "name":"synthetic-final-image","documentNamespace":"https://example.invalid/synthetic-image",
                    "creationInfo":{"created":"2026-09-08T00:00:00Z","creators":["Tool: synthetic-scanner-1"]},
                    "packages":[{"SPDXID":"SPDXRef-Package","name":"synthetic-native","versionInfo":"1.0.0",
                    "downloadLocation":"NOASSERTION","filesAnalyzed":false,
                    "externalRefs":[{"referenceCategory":"PACKAGE-MANAGER","referenceType":"purl",
                    "referenceLocator":"pkg:generic/synthetic-native@1.0.0"}]}],
                    "documentDescribes":["SPDXRef-Package"],
                    "files":[{"SPDXID":"SPDXRef-File","fileName":"app/current.so",
                    "checksums":[{"algorithm":"SHA256","checksumValue":""}]}],
                    "relationships":[{"spdxElementId":"SPDXRef-Package","relationshipType":"CONTAINS",
                    "relatedSpdxElement":"SPDXRef-File"}]}
                    """)!.AsObject();
                result["files"]![0]!["checksums"]![0]!["checksumValue"] = scenario == "wrong-file-hash"
                    ? new string('f', 64) : Digest(kPayload)[7..];
                if (scenario == "stale-whiteout-claim")
                {
                    result["files"]![0]!["fileName"] = "app/removed.so";
                }
                if (scenario == "missing-owner")
                {
                    result["relationships"] = new JsonArray();
                }
                if (scenario == "malformed-spdx")
                {
                    result["packages"]![0]!["downloadLocation"] = 42;
                }
                return result;
            }

            private static JsonObject CreateProvenance(string dockerfile, bool v1)
            {
                if (v1)
                {
                    return new JsonObject
                    {
                        ["buildDefinition"] = new JsonObject
                        {
                            ["buildType"] =
                                "https://github.com/moby/buildkit/blob/master/docs/attestations/slsa-definitions.md",
                            ["externalParameters"] = new JsonObject
                            {
                                ["configSource"] = new JsonObject
                                {
                                    ["uri"] = "https://github.com/OPCFoundation/UA-.NETStandard.git",
                                    ["digest"] = new JsonObject { ["sha1"] = kSource }, ["path"] = dockerfile
                                }
                            },
                            ["resolvedDependencies"] = new JsonArray(new JsonObject
                            {
                                ["uri"] = "https://github.com/OPCFoundation/UA-.NETStandard.git",
                                ["digest"] = new JsonObject { ["sha1"] = kSource }
                            })
                        },
                        ["runDetails"] = new JsonObject
                        {
                            ["builder"] = new JsonObject { ["id"] = "https://example.invalid/synthetic-builder" },
                            ["metadata"] = new JsonObject
                            {
                                ["invocationId"] = "synthetic-build",
                                ["startedOn"] = "2026-09-08T00:00:00Z", ["finishedOn"] = "2026-09-08T00:00:01Z"
                            }
                        }
                    };
                }
                return new JsonObject
                {
                    ["builder"] = new JsonObject { ["id"] = "https://example.invalid/synthetic-builder" },
                    ["buildType"] = "https://mobyproject.org/buildkit@v1",
                    ["invocation"] = new JsonObject
                    {
                        ["configSource"] = new JsonObject
                        {
                            ["uri"] = "https://github.com/OPCFoundation/UA-.NETStandard.git",
                            ["digest"] = new JsonObject { ["sha1"] = kSource }, ["entryPoint"] = dockerfile
                        }
                    },
                    ["materials"] = new JsonArray(new JsonObject
                    {
                        ["uri"] = "https://github.com/OPCFoundation/UA-.NETStandard.git",
                        ["digest"] = new JsonObject { ["sha1"] = kSource }
                    }),
                    ["metadata"] = new JsonObject
                    {
                        ["buildInvocationID"] = "synthetic-build",
                        ["buildStartedOn"] = "2026-09-08T00:00:00Z", ["buildFinishedOn"] = "2026-09-08T00:00:01Z"
                    }
                };
            }

            private static JsonObject Statement(string type, string image, string digest, JsonObject predicate)
            {
                return new JsonObject
                {
                    ["_type"] = "https://in-toto.io/Statement/v0.1", ["predicateType"] = type,
                    ["subject"] = new JsonArray(new JsonObject
                    {
                        ["name"] = image, ["digest"] = new JsonObject { ["sha256"] = digest[7..] }
                    }),
                    ["predicate"] = predicate
                };
            }

            private Task<JsonObject> AddJsonBlobAsync(JsonObject value, string mediaType)
            {
                return AddBlobAsync(Encoding.UTF8.GetBytes(value.ToJsonString()), mediaType);
            }

            private async Task<JsonObject> AddBlobAsync(byte[] bytes, string mediaType)
            {
                string hash = Digest(bytes);
                await File.WriteAllBytesAsync(BlobPath(hash), bytes).ConfigureAwait(false);
                return new JsonObject { ["digest"] = hash, ["size"] = bytes.Length, ["mediaType"] = mediaType };
            }

            private string BlobPath(string hash)
            {
                return Path.Combine(m_work, "layout", "blobs", "sha256", hash[7..]);
            }

            private static async Task<byte[]> TarAsync(IEnumerable<(string Name, byte[] Bytes)> entries)
            {
                using var stream = new MemoryStream();
                using (var writer = new TarWriter(stream, leaveOpen: true))
                {
                    foreach ((string name, byte[] bytes) in entries)
                    {
                        using var payload = new MemoryStream(bytes);
                        var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = payload };
                        await writer.WriteEntryAsync(entry).ConfigureAwait(false);
                    }
                }
                return stream.ToArray();
            }

            private static string FindRoot()
            {
                DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
                while (current != null && !File.Exists(Path.Combine(current.FullName, "UA.slnx")))
                {
                    current = current.Parent;
                }
                return current?.FullName ?? throw new DirectoryNotFoundException("Repository root is required.");
            }

            private const string kSource = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            private const string kManifestType = "application/vnd.oci.image.manifest.v1+json";
            private static readonly byte[] kPayload = [0x7f, 0x45, 0x4c, 0x46, 0x01, 0x02, 0x03, 0x04];
            private readonly string m_work = Directory.CreateTempSubdirectory("opcua-oci-native-").FullName;
        }
    }
}
