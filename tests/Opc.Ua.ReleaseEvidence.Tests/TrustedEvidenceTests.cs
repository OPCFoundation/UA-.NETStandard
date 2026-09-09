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
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Exercises the real evaluator with independent ephemeral cryptographic roots and a small approved catalog.
    /// </summary>
    [TestFixture]
    public sealed class TrustedEvidenceTests
    {
        /// <summary>
        /// Verifies that independently signed required-group evidence satisfies all sixteen controls without rewriting
        /// assessment.
        /// </summary>
        [Test]
        public async Task CompleteSignedRequiredGroupSatisfiesAllSixteenControlsAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(report.Status, Is.EqualTo("complete"), JsonSerializer.Serialize(
                report, EvidenceJsonContext.Default.EvaluationReport));
            Assert.That(report.UnmetControls, Is.Empty);
            Assert.That(report.Blocking, Is.False);
            Assert.That(report.ExternalVerificationPerformed, Is.True);
            Assert.That(fixture.Envelope.Assurance.Completed, Is.EqualTo(7));
            Assert.That(fixture.Envelope.Source.ActualSha, Is.Not.EqualTo(fixture.Envelope.Producer.DefinitionSha));
            using JsonDocument original = JsonDocument.Parse(
                await File.ReadAllTextAsync(fixture.EvidencePath).ConfigureAwait(false));
            JsonElement assessment = original.RootElement.GetProperty("assessment");
            Assert.That(assessment.EnumerateObject().Count(), Is.EqualTo(2));
            Assert.That(assessment.TryGetProperty("status", out _), Is.True);
            Assert.That(assessment.TryGetProperty("unmetControls", out _), Is.True);
            Assert.That(fixture.Envelope.Documents.Any(d => d.Path == "producer-assessment.json"), Is.True);
        }

        /// <summary>
        /// Verifies that a signed review created before its referencing envelope authenticates the complete finding
        /// population.
        /// </summary>
        [Test]
        public async Task SignedFindingReviewCanPrecedeTheEnvelopeThatReferencesItAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await fixture.AddCodeqlReviewAsync().ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero, JsonSerializer.Serialize(report, EvidenceJsonContext.Default.EvaluationReport));
            Assert.That(report.Status, Is.EqualTo("complete"));
            Assert.That(report.UnmetControls, Is.Empty);
            CodeqlReviewProjection projection = await fixture.VerifyReviewProjectionAsync().ConfigureAwait(false);
            Assert.That(projection.ReviewedOccurrences, Is.EqualTo(3));
            Assert.That(projection.ReviewedAlerts, Is.EqualTo(2));
            Assert.That(projection.UnresolvedFindings, Is.Zero);
            Assert.That(projection.Revoked, Is.False);
        }

        /// <summary>
        /// Verifies that original native pack and index statement signatures authenticate a complete NuGet release.
        /// </summary>
        [Test]
        public async Task NativePackAndIndexBundlesAuthenticateWithoutRelabelledSignaturesAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await fixture.UseNativeNugetProofsAsync().ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero, JsonSerializer.Serialize(report, EvidenceJsonContext.Default.EvaluationReport));
            Assert.That(report.Status, Is.EqualTo("complete"));
            Assert.That(report.UnmetControls, Is.Empty);
        }

        /// <summary>
        /// Verifies that native proof authentication rejects mismatched source, run, signer, or configuration
        /// membership.
        /// </summary>
        [TestCase("unsigned")]
        [TestCase("wrong-key")]
        [TestCase("index-source")]
        [TestCase("index-attempt")]
        [TestCase("pack-source")]
        [TestCase("pack-attempt")]
        [TestCase("pack-subject")]
        [TestCase("missing-debug")]
        [TestCase("wrong-definition")]
        public async Task NativeProofsCannotAuthenticateDifferentSourceRunOrMembershipAsync(string mutation)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await fixture.UseNativeNugetProofsAsync(mutation).ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1), mutation);
            Assert.That(report.UnmetControls, Does.Contain("PROVENANCE_VERIFIED"));
        }

        /// <summary>
        /// Verifies that later assurance retains the original producer envelope and cannot erase an observed baseline
        /// failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LaterAssuranceLinksProducerBytesAndCannotEraseObservedFailureAsync(bool originalFailure)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            string originalDigest = await fixture.LinkLaterAssuranceAsync(originalFailure).ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(originalFailure ? 1 : 0),
                JsonSerializer.Serialize(report, EvidenceJsonContext.Default.EvaluationReport));
            Assert.That(report.BaselineFailed, Is.EqualTo(originalFailure));
            Assert.That(await new EvidenceFiles().DigestAsync(
                Path.Combine(fixture.BundleRoot, "producer", "release-evidence.json"), CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(originalDigest));
            Assert.That(fixture.Envelope.Documents.Any(d =>
                d.Type == "producer-record" && d.Version == "2" && d.Digest == originalDigest), Is.True);
        }

        /// <summary>
        /// Verifies that matching delivered package identity still requires independent approval of the author
        /// signature.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DeliveredPrimaryIdentityNeedsIndependentApprovedSignatureVerificationAsync(bool approved)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateWithPrimarySignatureAsync()
                .ConfigureAwait(false);
            ApprovedNugetDeliveryReport report = await fixture.VerifyApprovedDeliveryAsync(approved)
                .ConfigureAwait(false);
            Assert.That(report.Status, Is.EqualTo(approved ? "artifact-delivery-verified" : "incomplete"));
            Assert.That(report.SignatureVerificationPerformed, Is.EqualTo(approved));
            Assert.That(report.BaselineFailed, Is.False);
            Assert.That(report.AuthorDigest, Is.EqualTo(report.DeliveredDigest));
        }

        /// <summary>
        /// Verifies that an authenticated outer index cannot make restricted fields in referenced documents
        /// publishable.
        /// </summary>
        [TestCase("pilot")]
        [TestCase("required")]
        public async Task AReviewedOuterIndexCannotMakeRestrictedTransitiveFieldsPublicAsync(string stage)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync(stage).ConfigureAwait(false);
            await fixture.AddRestrictedDocumentAsync().ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.UnmetControls, Does.Contain("PUBLIC_EVIDENCE_SAFE"));
            Assert.That(report.BaselineFailed, Is.True);
        }

        /// <summary>
        /// Verifies that signed finding reviews remain current, unrevoked, and complete for the bound query population.
        /// </summary>
        [TestCase("partial-occurrences")]
        [TestCase("partial-alerts")]
        [TestCase("query")]
        [TestCase("population")]
        [TestCase("attempt")]
        [TestCase("expired")]
        [TestCase("revoked")]
        [TestCase("unresolved")]
        [TestCase("unsigned")]
        public async Task SignedReviewMustCoverTheCurrentWholeFindingPopulationAsync(string mutation)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await fixture.AddCodeqlReviewAsync(mutation).ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1), mutation);
            Assert.That(report.UnmetControls, Does.Contain("ASSURANCE_COMPLETE"));
        }

        /// <summary>
        /// Verifies that signed promotion authority binds the exact request digest, artifact set, and member count.
        /// </summary>
        [Test]
        public async Task SignedPromotionAuthorizationBindsExactRequestAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            PromotionVerificationInput input = await fixture.CreatePromotionInputAsync("valid").ConfigureAwait(false);
            var files = new EvidenceFiles();
            VerifiedPromotion verified = await VerifiedPromotion.VerifyAsync(
                input, files, new TrustedEvidenceVerifier(files, fixture, fixture, TimeProvider.System),
                fixture, CancellationToken.None).ConfigureAwait(false);
            Assert.That(verified.RequestDigest,
                Is.EqualTo(await files.DigestAsync(input.Request, CancellationToken.None).ConfigureAwait(false)));
            Assert.That(verified.Request.CandidateDigest,
                Is.EqualTo(VerificationControls.ArtifactSetDigest(fixture.Envelope.Artifacts)));
            Assert.That(verified.Request.Members, Has.Length.EqualTo(fixture.Envelope.Artifacts.Length));
            Assert.That(verified.OfficialTransportAuthorized, Is.True);
        }

        /// <summary>
        /// Verifies that promotion rejects unbound requests, changed aliases, incorrect member identities, or missing
        /// evidence.
        /// </summary>
        [TestCase("unbound")]
        [TestCase("alias-change")]
        [TestCase("wrong-id")]
        [TestCase("wrong-kind")]
        [TestCase("missing-evidence")]
        public async Task PromotionRequiresRequestAuthorizationAndExactMemberIdentityAsync(string mutation)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            PromotionVerificationInput input = await fixture.CreatePromotionInputAsync(mutation).ConfigureAwait(false);
            var files = new EvidenceFiles();
            Assert.That(() => VerifiedPromotion.VerifyAsync(
                input, files, new TrustedEvidenceVerifier(files, fixture, fixture, TimeProvider.System),
                fixture, CancellationToken.None), Throws.TypeOf<PromotionRejectedException>());
        }

        /// <summary>
        /// Verifies idempotent promotion recovery after content is written but before evidence and receipts are
        /// completed.
        /// </summary>
        [Test]
        public async Task CryptographicPromotionRecoversAfterContentWriteBeforeReceiptAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            PromotionVerificationInput input = await fixture.CreatePromotionInputAsync("valid").ConfigureAwait(false);
            var files = new EvidenceFiles();
            var transport = new PromotionFileTransport(Path.Combine(fixture.Root, "destination"), files);
            var journal = new PromotionFileJournal(Path.Combine(fixture.Root, "journal"), files);
            var eligibility = new FixtureEligibility(fixture, input);
            var interrupted = new PromotionCoordinator(
                eligibility, transport, new InterruptAfterContent(journal), TimeProvider.System);
            Assert.That(() => interrupted.PromoteAsync(CancellationToken.None), Throws.TypeOf<IOException>());
            VerifiedPromotion grant = await eligibility.VerifyAsync(CancellationToken.None).ConfigureAwait(false);
            PromotionObservation partial = await transport.ReadAsync(grant.Request.Members[0], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(partial.ContentDigest, Is.EqualTo(grant.Request.Members[0].Content.Digest));
            Assert.That(partial.EvidenceDigests, Is.Empty);
            var resumed = new PromotionCoordinator(eligibility, transport, journal, TimeProvider.System);
            PromotionResult result = await resumed.PromoteAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo("complete"));
            Assert.That(result.VerifiedMembers, Is.EqualTo(4));
            Assert.That(result.OfficialDelivery, Is.False);
            PromotionResult repeated = await resumed.PromoteAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(repeated.RequestDigest, Is.EqualTo(result.RequestDigest));
            Assert.That(repeated.VerifiedMembers, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies that publication revocation at the creation boundary prevents any destination content or evidence
        /// write.
        /// </summary>
        [Test]
        public async Task RevokedGrantIsReverifiedImmediatelyBeforeMutationAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            PromotionVerificationInput input = await fixture.CreatePromotionInputAsync("valid").ConfigureAwait(false);
            var files = new EvidenceFiles();
            var eligibility = new FixtureEligibility(fixture, input);
            VerifiedPromotion original = await eligibility.VerifyAsync(CancellationToken.None).ConfigureAwait(false);
            var transport = new PromotionFileTransport(Path.Combine(fixture.Root, "destination"), files);
            var coordinator = new PromotionCoordinator(eligibility, transport,
                new RevokeOnCreate(fixture), TimeProvider.System);
            Assert.That(() => coordinator.PromoteAsync(CancellationToken.None),
                Throws.TypeOf<PromotionRejectedException>());
            foreach (PromotionMember member in original.Request.Members)
            {
                PromotionObservation observed = await transport.ReadAsync(member, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(observed.ContentDigest, Is.Null);
                Assert.That(observed.EvidenceDigests, Is.Empty);
            }
        }

        /// <summary>
        /// Verifies that missing new assurance blocks isolated promotion only for the required stable release cohort.
        /// </summary>
        [TestCase("required", "stable", "2.0.0")]
        [TestCase("required", "preview", "2.0.0-preview")]
        [TestCase("pilot", "stable", "2.0.0")]
        public async Task IsolatedPromotionKeepsNewAssuranceAdvisoryOutsideRequiredStableAsync(
            string stage, string channel, string version)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync(stage, channel, version)
                .ConfigureAwait(false);
            PromotionVerificationInput input = await fixture.CreatePromotionInputAsync("valid").ConfigureAwait(false);
            await fixture.OmitProofAsync("assurance").ConfigureAwait(false);
            var eligibility = new FixtureEligibility(fixture, input);
            if (stage == "required" && channel == "stable")
            {
                Assert.That(() => eligibility.VerifyAsync(CancellationToken.None),
                    Throws.TypeOf<PromotionRejectedException>());
            }
            else
            {
                VerifiedPromotion grant = await eligibility.VerifyAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.That(grant.Request.Members, Has.Length.EqualTo(4));
            }
        }

        /// <summary>
        /// Verifies that signed record shapes cannot conceal missing or mismatched source, authority, artifact, or
        /// execution proof.
        /// </summary>
        [TestCase("unsigned")]
        [TestCase("source")]
        [TestCase("definition")]
        [TestCase("run")]
        [TestCase("attempt")]
        [TestCase("intent")]
        [TestCase("policy")]
        [TestCase("revoked")]
        [TestCase("partial")]
        [TestCase("signer")]
        [TestCase("artifact-signature")]
        [TestCase("native-image")]
        [TestCase("analysis-scope")]
        public async Task AuthenticatedShapesCannotHideMissingOrMismatchedProofAsync(string mutation)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await fixture.MutateAsync(mutation).ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1), mutation);
            Assert.That(report.Status, Is.EqualTo("incomplete"));
            Assert.That(report.UnmetControls, Is.Not.Empty);
            Assert.That(report.Blocking, Is.True);
        }

        /// <summary>
        /// Verifies that observed baseline failures block legacy pilot configurations and preview releases.
        /// </summary>
        [TestCase("pilot", "stable", "2.0.0")]
        [TestCase("required", "preview", "2.0.0-preview")]
        public async Task ObservedBaselineFailureAlwaysBlocksAsync(
            string stage, string channel, string version)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync(stage, channel, version)
                .ConfigureAwait(false);
            await fixture.MutateAsync("baseline").ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.BaselineFailed, Is.True);
        }

        /// <summary>
        /// Verifies that missing new cryptographic proof remains advisory outside the currently required stable release
        /// line.
        /// </summary>
        [TestCase("pilot", "stable", "2.0.0")]
        [TestCase("required", "preview", "2.0.0-preview")]
        [TestCase("required", "development", "2.0.0")]
        [TestCase("required", "stable", "1.5.378")]
        public async Task MissingNewProofRemainsAdvisoryOutsideCurrentRequiredStableAsync(
            string stage, string channel, string version)
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync(stage, channel, version)
                .ConfigureAwait(false);
            await fixture.MutateAsync("unsigned").ConfigureAwait(false);
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.Zero);
            Assert.That(report.Status, Is.EqualTo("incomplete"));
            Assert.That(report.Blocking, Is.False);
        }

        /// <summary>
        /// Verifies that caller-controlled development labels cannot override independently protected stable release
        /// intent.
        /// </summary>
        [Test]
        public async Task BothCallerContextsCannotDowngradeProtectedStableIntentAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            fixture.Envelope = fixture.Envelope with
            {
                Release = fixture.Envelope.Release with { Channel = "development" }
            };
            (int code, EvaluationReport report) = await fixture.EvaluateAsync().ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.UnmetControls, Does.Contain("RELEASE_INTENT"));
            Assert.That(report.Channel, Is.EqualTo("stable"));
        }

        /// <summary>
        /// Verifies that production evaluator composition does not authenticate policy merely from protected-looking
        /// filenames.
        /// </summary>
        [Test]
        public async Task ProductionCompositionDoesNotTrustUnsignedProtectedFileNamesAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            var evaluator = new EvidenceEvaluator(new EvidenceFiles());
            string output = Path.Combine(fixture.Root, "production-report.json");
            int code = await evaluator.EvaluateAsync(
                fixture.Repository, fixture.EvidencePath, fixture.ExpectedPath, output, fixture.Packages,
                CancellationToken.None, fixture.BundlePath, null).ConfigureAwait(false);
            EvaluationReport report = await new EvidenceFiles().ReadModelAsync(
                output, EvidenceJsonContext.Default.EvaluationReport, CancellationToken.None).ConfigureAwait(false);
            Assert.That(code, Is.EqualTo(1));
            Assert.That(report.ExternalVerificationPerformed, Is.False);
            Assert.That(report.UnmetControls, Does.Contain("POLICY_IDENTITY"));
        }

        /// <summary>
        /// Verifies that an unknown trust claim in the verification bundle is a fatal contract error rather than
        /// advisory evidence.
        /// </summary>
        [Test]
        public async Task MalformedVerificationContractIsFatalRatherThanAdvisoryAsync()
        {
            using SyntheticRelease fixture = await SyntheticRelease.CreateAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(fixture.BundlePath,
                /*lang=json,strict*/ "{\"schemaVersion\":1,\"proofs\":[],\"trusted\":true}").ConfigureAwait(false);
            Assert.That(() => fixture.EvaluateAsync(),
                Throws.TypeOf<InvalidDataException>());
        }

        private sealed class SyntheticRelease : IDisposable, ITrustPolicySource,
            IRecordSignatureVerifier, IArtifactSignatureVerifier, IStatementSignatureVerifier
        {
            private SyntheticRelease(bool primarySignature = false)
            {
                Root = Path.Combine(
                    TestContext.CurrentContext.TestDirectory, ".trusted", Guid.NewGuid().ToString("N"));
                Repository = Path.Combine(Root, "candidate");
                Packages = Path.Combine(Root, "packages");
                BundleRoot = Path.Combine(Root, "evidence");
                Directory.CreateDirectory(Path.Combine(Repository, ".azurepipelines"));
                Directory.CreateDirectory(Packages);
                Directory.CreateDirectory(BundleRoot);
                m_signer = RSA.Create(2048);
                m_verifier = RSA.Create();
                m_verifier.ImportParameters(m_signer.ExportParameters(false));
                if (primarySignature)
                {
                    var certificate = new CertificateRequest(
                        "CN=Ephemeral release evidence test", m_signer, HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                    m_certificate = certificate.CreateSelfSigned(
                        DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddHours(1));
                }
            }

            /// <summary>
            /// Gets the temporary root owning all candidate, package, and verification fixture files.
            /// </summary>
            public string Root { get; }

            /// <summary>
            /// Gets the synthetic candidate repository containing its copied release contracts.
            /// </summary>
            public string Repository { get; }

            /// <summary>
            /// Gets the directory containing package and symbol artifacts submitted for verification.
            /// </summary>
            public string Packages { get; }

            /// <summary>
            /// Gets the directory containing the evidence envelope, referenced documents, and cryptographic proofs.
            /// </summary>
            public string BundleRoot { get; }

            /// <summary>
            /// Gets the evidence-envelope path evaluated by the real release verifier.
            /// </summary>
            public string EvidencePath => Path.Combine(BundleRoot, "release-evidence.json");

            /// <summary>
            /// Gets the expected-release context path saved alongside the evidence envelope.
            /// </summary>
            public string ExpectedPath => Path.Combine(BundleRoot, "expected.json");

            /// <summary>
            /// Gets the verification bundle path containing references to independently authenticated records.
            /// </summary>
            public string BundlePath => Path.Combine(BundleRoot, "verification-bundle.json");

            /// <summary>
            /// Gets or sets the candidate envelope mutated by evidence-validation scenarios.
            /// </summary>
            public EvidenceEnvelope Envelope { get; set; } = null!;

            /// <summary>
            /// Gets the independently supplied policy snapshot, including approved authorities and revocations.
            /// </summary>
            public TrustedPolicySnapshot Policy { get; private set; } = null!;

            /// <summary>
            /// Creates a signed synthetic release for the requested rollout stage, channel, and version.
            /// </summary>
            public static async Task<SyntheticRelease> CreateAsync(
                string stage = "required", string channel = "stable", string version = "2.0.0")
            {
                var fixture = new SyntheticRelease();
                await fixture.InitializeAsync(stage, channel, version).ConfigureAwait(false);
                return fixture;
            }

            /// <summary>
            /// Creates a required stable release with a primary NuGet signature and a separately approved author
            /// identity.
            /// </summary>
            public static async Task<SyntheticRelease> CreateWithPrimarySignatureAsync()
            {
                var fixture = new SyntheticRelease(primarySignature: true);
                await fixture.InitializeAsync("required", "stable", "2.0.0").ConfigureAwait(false);
                fixture.Policy = fixture.Policy with
                {
                    NugetVerifier = new(Path.Combine(fixture.Root, "test-verifier"), Digest("test-verifier"), "10.0.100"),
                    NugetAuthorFingerprints = [fixture.KeyDigest]
                };
                return fixture;
            }

            /// <summary>
            /// Releases ephemeral keys and certificates and removes the synthetic release workspace.
            /// </summary>
            public void Dispose()
            {
                m_signer.Dispose();
                m_verifier.Dispose();
                m_certificate?.Dispose();
                Directory.Delete(Root, true);
            }

            /// <summary>
            /// Saves the current envelope and evaluates it against the fixture's independent policy and signature
            /// verifiers.
            /// </summary>
            public async Task<(int Code, EvaluationReport Report)> EvaluateAsync()
            {
                await SaveEnvelopeAsync().ConfigureAwait(false);
                var verifier = new TrustedEvidenceVerifier(m_files, this, this, TimeProvider.System, this);
                string output = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".report.json");
                int code = await new EvidenceEvaluator(m_files, verifier, this).EvaluateAsync(
                    Repository, EvidencePath, ExpectedPath, output, Packages, CancellationToken.None,
                    BundlePath, Path.Combine(Root, "protected-anchor.json")).ConfigureAwait(false);
                EvaluationReport report = await m_files.ReadModelAsync(
                    output, EvidenceJsonContext.Default.EvaluationReport, CancellationToken.None)
                    .ConfigureAwait(false);
                return (code, report);
            }

            /// <summary>
            /// Stages package evidence, binds publication authority to a promotion request, and applies a requested
            /// mutation.
            /// </summary>
            public async Task<PromotionVerificationInput> CreatePromotionInputAsync(string mutation)
            {
                Directory.CreateDirectory(Path.Combine(Packages, "evidence"));
                var attachments = new List<PromotionFile>();
                string envelopeDigest = await m_files.DigestAsync(EvidencePath, CancellationToken.None)
                    .ConfigureAwait(false);
                var index = new PromotionFile("evidence/index.json", envelopeDigest);
                await File.WriteAllBytesAsync(EvidenceFiles.Confined(Packages, index.Path),
                    await File.ReadAllBytesAsync(EvidencePath).ConfigureAwait(false)).ConfigureAwait(false);
                attachments.Add(index);
                foreach (DocumentRecord document in Envelope.Documents.DistinctBy(d => d.Digest))
                {
                    var attachment = new PromotionFile("evidence/" + document.Digest[7..] + ".json", document.Digest);
                    await File.WriteAllBytesAsync(EvidenceFiles.Confined(Packages, attachment.Path),
                        await File.ReadAllBytesAsync(EvidenceFiles.Confined(BundleRoot, document.Path))
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    attachments.Add(attachment);
                }
                VerificationBundle bundle = await m_files.ReadModelAsync(
                    BundlePath, VerificationJsonContext.Default.VerificationBundle, CancellationToken.None)
                    .ConfigureAwait(false);
                foreach (NativeNugetProof proof in bundle.NativeNuget ?? [])
                {
                    var attachment = new PromotionFile("evidence/" + proof.Digest[7..] + ".json", proof.Digest);
                    await File.WriteAllBytesAsync(EvidenceFiles.Confined(Packages, attachment.Path),
                        await File.ReadAllBytesAsync(EvidenceFiles.Confined(BundleRoot, proof.BundlePath))
                            .ConfigureAwait(false)).ConfigureAwait(false);
                    attachments.Add(attachment);
                }
                PromotionMember[] members = [.. Envelope.Artifacts.Select(a => new PromotionMember(
                    a.Id, a.Version, "nuget.org",
                    new PromotionFile(a.Id + (a.Kind == "nuget-symbols" ? ".snupkg" : ".nupkg"), a.Digest),
                    [.. attachments], Kind: a.Kind))];
                if (mutation == "wrong-id")
                {
                    members[0] = members[0] with { Id = members[0].Id + ".unauthorized" };
                }
                else if (mutation == "wrong-kind")
                {
                    members = [.. members.Select(m => m with
                    {
                        Kind = m.Kind == "nuget-package" ? "nuget-symbols" : "nuget-package"
                    })];
                }
                else if (mutation == "missing-evidence")
                {
                    members[0] = members[0] with { Evidence = members[0].Evidence[..^1] };
                }
                var request = new PromotionRequest(
                    1, Envelope.Release.Group, "nuget.org",
                    VerificationControls.ArtifactSetDigest(Envelope.Artifacts), envelopeDigest,
                    Policy.ExpectedIntentDigest, Policy.PolicyDigest, Envelope.Source.ActualSha,
                    Envelope.Producer.RunId, Envelope.Producer.Attempt, members);
                string requestPath = Path.Combine(BundleRoot, "promotion-request.json");
                await WriteAsync(requestPath, request, PromotionJsonContext.Default.PromotionRequest)
                    .ConfigureAwait(false);
                if (mutation != "unbound")
                {
                    VerificationRecord boundary = await m_files.ReadModelAsync(
                        Path.Combine(BundleRoot, "publication-boundary.record.json"),
                        VerificationJsonContext.Default.VerificationRecord, CancellationToken.None)
                        .ConfigureAwait(false);
                    await WriteSignedRecordAsync("publication-boundary", boundary with
                    {
                        PromotionRequestDigest = await m_files.DigestAsync(requestPath, CancellationToken.None)
                            .ConfigureAwait(false)
                    }).ConfigureAwait(false);
                }
                if (mutation == "alias-change")
                {
                    members[0] = members[0] with { Alias = "changed-after-approval" };
                    await WriteAsync(requestPath, request, PromotionJsonContext.Default.PromotionRequest)
                        .ConfigureAwait(false);
                }
                string work = Path.Combine(Root, "promotion-work");
                Directory.CreateDirectory(work);
                return new PromotionVerificationInput(
                    Repository, Packages, EvidencePath, ExpectedPath, BundlePath,
                    Path.Combine(Root, "protected-anchor.json"), requestPath, work);
            }

            /// <summary>
            /// Adds a signed CodeQL disposition and updates assurance bindings with optional review-validity mutations.
            /// </summary>
            public async Task AddCodeqlReviewAsync(string mutation = "valid")
            {
                JobRecord job = Envelope.Assurance.Jobs.Single(j => j.Id == "codeql-csharp");
                string summaryPath = Path.Combine(BundleRoot, job.ResultDocument!);
                CollectedResultSummary summary = await m_files.ReadModelAsync(
                    summaryPath, EvidenceJsonContext.Default.CollectedResultSummary, CancellationToken.None)
                    .ConfigureAwait(false);
                CodeqlAssuranceProof analysis = summary.Analysis!;
                var review = new CodeqlReviewRecord(
                    1, "review-analysis", "codeql-disposition", Envelope.Source.Repository,
                    Envelope.Source.ActualRef, job.Producer!,
                    new(analysis.SourceSha, analysis.RunId, analysis.Attempt, analysis.AnalysisId,
                        analysis.QueryDigest, analysis.PopulationDigest, 3, 2, 0),
                    Policy.PolicyDigest, Policy.CheckpointSequence,
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(30),
                    ["record:synthetic-review"]);
                review = mutation switch
                {
                    "partial-occurrences" => review with
                    {
                        Review = review.Review with { ReviewedOccurrences = 2 }
                    },
                    "partial-alerts" => review with { Review = review.Review with { ReviewedAlerts = 1 } },
                    "query" => review with { Review = review.Review with { QueryDigest = Digest("other query") } },
                    "population" => review with
                    {
                        Review = review.Review with { PopulationDigest = Digest("other population") }
                    },
                    "attempt" => review with { Review = review.Review with { Attempt = 2 } },
                    "expired" => review with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
                    "unresolved" => review with { Review = review.Review with { UnresolvedFindings = 1 } },
                    _ => review
                };
                string recordPath = "review.record.json";
                byte[] recordBytes = JsonSerializer.SerializeToUtf8Bytes(
                    review, VerificationJsonContext.Default.CodeqlReviewRecord);
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, recordPath), recordBytes).ConfigureAwait(false);
                string proofPath = "review.dsse.json";
                var dsse = new DsseFixture(PayloadType, Convert.ToBase64String(recordBytes),
                    [new(KeyDigest, Convert.ToBase64String(m_signer.SignData(
                        Pae(recordBytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))]);
                if (mutation == "unsigned")
                {
                    dsse = dsse with { Signatures = [] };
                }
                await WriteAsync(Path.Combine(BundleRoot, proofPath), dsse, TrustedTestJsonContext.Default.DsseFixture)
                    .ConfigureAwait(false);
                CountsRecord counts = job.Counts! with { Findings = 3, UnresolvedFindings = 0 };
                summary = summary with
                {
                    Counts = counts,
                    Analysis = analysis with
                    {
                        Findings = 3, DistinctAlerts = 2, Disposition = "authenticated-review",
                        Review = new(review.Id, EvidenceFiles.Digest(recordBytes))
                    }
                };
                await WriteAsync(summaryPath, summary, EvidenceJsonContext.Default.CollectedResultSummary)
                    .ConfigureAwait(false);
                string summaryDigest = await m_files.DigestAsync(summaryPath, CancellationToken.None)
                    .ConfigureAwait(false);
                Envelope = Envelope with
                {
                    Documents = [.. Envelope.Documents.Select(d =>
                        d.Path == job.ResultDocument ? d with { Digest = summaryDigest } : d)],
                    Assurance = Envelope.Assurance with
                    {
                        Jobs = [.. Envelope.Assurance.Jobs.Select(j => j.Id == job.Id ? j with { Counts = counts } : j)]
                    }
                };
                m_reviews = [new("isolated-test-authority", recordPath, proofPath)];
                Policy = Policy with
                {
                    Authorities = [.. Policy.Authorities.Select(a =>
                        a with { RecordKinds = [.. a.RecordKinds, "codeql-disposition"] })],
                    RevokedRecordIds = mutation == "revoked" ? [review.Id] : []
                };
                await CreateProofsAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Removes proof references for the selected record kind from the bundle while retaining its record files.
            /// </summary>
            public async Task OmitProofAsync(string kind)
            {
                VerificationBundle bundle = await m_files.ReadModelAsync(
                    BundlePath, VerificationJsonContext.Default.VerificationBundle, CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteAsync(BundlePath, bundle with
                {
                    Proofs = [.. bundle.Proofs.Where(p => p.RecordPath != kind + ".record.json")]
                }, VerificationJsonContext.Default.VerificationBundle).ConfigureAwait(false);
            }

            /// <summary>
            /// Revokes the publication-boundary record in the policy used by subsequent verification calls.
            /// </summary>
            public void RevokePublication()
            {
                Policy = Policy with { RevokedRecordIds = ["record-publication-boundary"] };
            }

            /// <summary>
            /// Adds a referenced document containing a restricted field and refreshes the enclosing signed proofs.
            /// </summary>
            public async Task AddRestrictedDocumentAsync()
            {
                const string path = "restricted-fixture.json";
                byte[] bytes = "{\"clientSecret\":\"FIXTURE-NOT-A-CREDENTIAL\"}"u8.ToArray();
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, path), bytes).ConfigureAwait(false);
                Envelope = Envelope with
                {
                    Documents =
                    [
                        .. Envelope.Documents,
                        new("producer-record", "json", "1", path, EvidenceFiles.Digest(bytes),
                            new SubjectRecord("source", Envelope.Source.Repository, EvidenceFiles.Digest(bytes)))
                    ]
                };
                await CreateProofsAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Replaces the producer-record proof with native signed index and per-configuration NuGet pack statements.
            /// </summary>
            public async Task UseNativeNugetProofsAsync(string mutation = "valid")
            {
                Directory.CreateDirectory(Path.Combine(BundleRoot, "producer"));
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, "producer", "release-evidence.json"),
                    await File.ReadAllBytesAsync(EvidencePath).ConfigureAwait(false)).ConfigureAwait(false);
                var proofs = new List<NativeNugetProof>();
                foreach (string role in new[] { "index", "Release", "Debug" })
                {
                    string predicateType;
                    var subjects = new JsonArray();
                    JsonNode predicate;
                    if (role == "index")
                    {
                        predicateType = "https://github.com/OPCFoundation/UA-.NETStandard/release-evidence/v1";
                        subjects.Add(new JsonObject
                        {
                            ["name"] = Path.GetFileName(EvidencePath),
                            ["digest"] = new JsonObject
                            {
                                ["sha256"] = (await m_files.DigestAsync(EvidencePath, CancellationToken.None)
                                    .ConfigureAwait(false))[7..]
                            }
                        });
                        predicate = JsonSerializer.SerializeToNode(
                            new EvaluationExpectation(Envelope.Source, Envelope.Producer, Envelope.Release,
                                Envelope.Policy.Digest, []), EvidenceJsonContext.Default.EvaluationExpectation)!;
                    }
                    else
                    {
                        predicateType = "https://slsa.dev/provenance/v1";
                        foreach (ArtifactRecord artifact in Envelope.Artifacts.Where(a => a.Configuration == role))
                        {
                            subjects.Add(new JsonObject
                            {
                                ["name"] = artifact.Id +
                                    (artifact.Kind == "nuget-symbols" ? ".snupkg" : ".nupkg"),
                                ["digest"] = new JsonObject { ["sha256"] = artifact.Digest[7..] }
                            });
                        }
                        predicate = new JsonObject
                        {
                            ["buildDefinition"] = new JsonObject
                            {
                                ["buildType"] = "https://github.com/OPCFoundation/UA-.NETStandard/nuget-pack/v1",
                                ["externalParameters"] = new JsonObject
                                {
                                    ["configuration"] = role, ["version"] = Envelope.Release.Version
                                },
                                ["internalParameters"] = new JsonObject(),
                                ["resolvedDependencies"] = new JsonArray(new JsonObject
                                {
                                    ["uri"] = "git+https://github.com/" + Envelope.Source.Repository +
                                        "@" + Envelope.Source.ActualRef,
                                    ["digest"] = new JsonObject { ["gitCommit"] = Envelope.Source.ActualSha }
                                })
                            },
                            ["runDetails"] = new JsonObject
                            {
                                ["builder"] = new JsonObject
                                {
                                    ["id"] = "https://github.com/" + Envelope.Source.Repository + "/" +
                                        Envelope.Producer.Workflow + "@" + Envelope.Producer.DefinitionSha
                                },
                                ["metadata"] = new JsonObject
                                {
                                    ["invocationId"] = "https://github.com/" + Envelope.Source.Repository +
                                        "/actions/runs/" + Envelope.Producer.RunId + "/attempts/" +
                                        Envelope.Producer.Attempt
                                }
                            }
                        };
                    }
                    if (role == "index" && mutation == "index-source")
                    {
                        predicate["source"]!["actualSha"] = new string('f', 40);
                    }
                    else if (role == "index" && mutation == "index-attempt")
                    {
                        predicate["producer"]!["attempt"] = 2;
                    }
                    else if (role == "Release" && mutation == "pack-source")
                    {
                        predicate["buildDefinition"]!["resolvedDependencies"]![0]!["digest"]!["gitCommit"] =
                            new string('f', 40);
                    }
                    else if (role == "Release" && mutation == "pack-attempt")
                    {
                        predicate["runDetails"]!["metadata"]!["invocationId"] =
                            "https://github.com/" + Envelope.Source.Repository + "/actions/runs/1000/attempts/2";
                    }
                    else if (role == "Release" && mutation == "pack-subject")
                    {
                        subjects.RemoveAt(0);
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(new JsonObject
                    {
                        ["_type"] = "https://in-toto.io/Statement/v1",
                        ["predicateType"] = predicateType,
                        ["subject"] = subjects, ["predicate"] = predicate
                    }.ToJsonString());
                    string path = "native-" + role + ".dsse.json";
                    var dsse = new DsseFixture(StatementPayloadType, Convert.ToBase64String(bytes),
                        [new(KeyDigest, Convert.ToBase64String(m_signer.SignData(
                            StatementPae(bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))]);
                    if (mutation == "unsigned")
                    {
                        dsse = dsse with { Signatures = [] };
                    }
                    else if (mutation == "wrong-key")
                    {
                        dsse = dsse with
                        {
                            Signatures = [dsse.Signatures[0] with { KeyId = "unapproved" }]
                        };
                    }
                    await WriteAsync(Path.Combine(BundleRoot, path), dsse, TrustedTestJsonContext.Default.DsseFixture)
                        .ConfigureAwait(false);
                    proofs.Add(new(role, "isolated-test-authority", path,
                        await m_files.DigestAsync(Path.Combine(BundleRoot, path), CancellationToken.None)
                            .ConfigureAwait(false),
                        role == "index" ? "producer/release-evidence.json" : null));
                }
                Policy = Policy with
                {
                    Authorities = [.. Policy.Authorities.Select(a => a with
                    {
                        RecordKinds = [.. a.RecordKinds, "native-nuget-index", "native-nuget-pack"],
                        DefinitionSha = mutation == "wrong-definition" ? new string('f', 40) : a.DefinitionSha
                    })]
                };
                VerificationBundle bundle = await m_files.ReadModelAsync(
                    BundlePath, VerificationJsonContext.Default.VerificationBundle, CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteAsync(BundlePath, bundle with
                {
                    Proofs = [.. bundle.Proofs.Where(p => p.RecordPath != "producer.record.json")],
                    NativeNuget = [.. proofs.Where(p => mutation != "missing-debug" || p.Role != "Debug")]
                }, VerificationJsonContext.Default.VerificationBundle).ConfigureAwait(false);
            }

            /// <summary>
            /// Links later complete assurance to the original producer envelope and returns that envelope's unchanged
            /// digest.
            /// </summary>
            public async Task<string> LinkLaterAssuranceAsync(bool originalFailure)
            {
                EvidenceEnvelope completed = Envelope;
                Envelope = completed with
                {
                    Documents = [.. completed.Documents.Where(d => d.Type != "assurance-summary")],
                    Assurance = completed.Assurance with
                    {
                        Completed = 0, Selected = originalFailure ? 1 : 0,
                        Failed = originalFailure ? 1 : 0, Missing = originalFailure ? 6 : 7,
                        Jobs = [.. completed.Assurance.Jobs.Select((j, i) => j with
                        {
                            Selected = originalFailure && i == 0,
                            Status = originalFailure && i == 0 ? "failed" : "missing",
                            Counts = originalFailure && i == 0 ? new CountsRecord(1, 1, 0, 1, 0) : null,
                            ResultDocument = null
                        })]
                    }
                };
                await SaveEnvelopeAsync().ConfigureAwait(false);
                await UseNativeNugetProofsAsync().ConfigureAwait(false);
                VerificationBundle native = await m_files.ReadModelAsync(
                    BundlePath, VerificationJsonContext.Default.VerificationBundle, CancellationToken.None)
                    .ConfigureAwait(false);
                string digest = await m_files.DigestAsync(EvidencePath, CancellationToken.None).ConfigureAwait(false);
                const string path = "original-producer-index.json";
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, path),
                    await File.ReadAllBytesAsync(EvidencePath).ConfigureAwait(false)).ConfigureAwait(false);
                Envelope = completed with
                {
                    Documents =
                    [
                        .. completed.Documents,
                        new("producer-record", "json", "2", path, digest,
                            new SubjectRecord("artifact-set", "nuget", digest))
                    ]
                };
                await CreateProofsAsync().ConfigureAwait(false);
                VerificationBundle current = await m_files.ReadModelAsync(
                    BundlePath, VerificationJsonContext.Default.VerificationBundle, CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteAsync(BundlePath, current with
                {
                    Proofs = [.. current.Proofs.Where(p => p.RecordPath != "producer.record.json")],
                    NativeNuget = native.NativeNuget
                }, VerificationJsonContext.Default.VerificationBundle).ConfigureAwait(false);
                return digest;
            }

            /// <summary>
            /// Copies the author package to a delivery file and verifies it under the selected approved-signer policy.
            /// </summary>
            public async Task<ApprovedNugetDeliveryReport> VerifyApprovedDeliveryAsync(bool approved)
            {
                string author = Path.Combine(Packages, "Synthetic.nupkg");
                string delivered = Path.Combine(Root, "delivered.nupkg");
                File.Copy(author, delivered);
                if (!approved)
                {
                    Policy = Policy with { NugetAuthorFingerprints = [Digest("different approved signer")] };
                }
                NugetPrimaryIdentity? identity = await NugetPrimaryIdentity.ReadAsync(author, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(identity?.CertificateDigest, Is.EqualTo(KeyDigest));
                string output = Path.Combine(Root, "approved-delivery.json");
                int code = await new ApprovedNugetDelivery(
                    m_files, new TrustedEvidenceVerifier(m_files, this, this, TimeProvider.System, this), this)
                    .VerifyAsync(Repository, EvidencePath, BundlePath, Path.Combine(Root, "protected-anchor.json"),
                        author, delivered, output, CancellationToken.None).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(approved ? 0 : 1));
                return await m_files.ReadModelAsync(
                    output, VerificationJsonContext.Default.ApprovedNugetDeliveryReport, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Returns the fixture's independent policy snapshot instead of reading candidate-provided trust files.
            /// </summary>
            public Task<TrustedPolicySnapshot?> LoadAsync(
                string? path, string[] candidateRoots, CancellationToken cancellationToken)
            {
                return Task.FromResult<TrustedPolicySnapshot?>(Policy);
            }

            /// <summary>
            /// Verifies the signed CodeQL review record and returns its public review projection.
            /// </summary>
            public async Task<CodeqlReviewProjection> VerifyReviewProjectionAsync()
            {
                string output = Path.Combine(Root, "review-projection.json");
                string digest = await m_files.DigestAsync(
                    Path.Combine(BundleRoot, m_reviews![0].RecordPath), CancellationToken.None).ConfigureAwait(false);
                int code = await new CodeqlReviewCommands(m_files, this, this, TimeProvider.System).VerifyAsync(
                    Repository, BundlePath, Path.Combine(Root, "protected-anchor.json"),
                    "review-analysis", digest, output, CancellationToken.None).ConfigureAwait(false);
                Assert.That(code, Is.Zero);
                return await m_files.ReadModelAsync(
                    output, VerificationJsonContext.Default.CodeqlReviewProjection, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            /// <summary>
            /// Verifies the fixture authority and key identity, exact record payload, and DSSE RSA signature.
            /// </summary>
            public async Task<bool> VerifyAsync(
                string recordPath, string bundlePath, VerificationAuthority authority,
                TrustedPolicySnapshot policy, CancellationToken cancellationToken)
            {
                DsseFixture proof = await m_files.ReadModelAsync(
                    bundlePath, TrustedTestJsonContext.Default.DsseFixture, cancellationToken).ConfigureAwait(false);
                byte[] record = await File.ReadAllBytesAsync(recordPath, cancellationToken).ConfigureAwait(false);
                return proof.PayloadType == PayloadType && proof.Signatures.Length == 1 &&
                    proof.Signatures[0].KeyId == KeyDigest && authority.Id == "isolated-test-authority" &&
                    Convert.FromBase64String(proof.Payload).AsSpan().SequenceEqual(record) &&
                    m_verifier.VerifyData(Pae(record), Convert.FromBase64String(proof.Signatures[0].Sig),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            /// <summary>
            /// Verifies artifact bytes and signer identity, including primary NuGet signature identity when configured.
            /// </summary>
            public async Task<bool> VerifyAsync(
                string artifactPath, ArtifactSignatureProof proof, string bundleRoot,
                TrustedPolicySnapshot policy, CancellationToken cancellationToken)
            {
                byte[] signature = await File.ReadAllBytesAsync(
                    EvidenceFiles.Confined(bundleRoot, proof.BundlePath), cancellationToken).ConfigureAwait(false);
                byte[] artifact = await File.ReadAllBytesAsync(artifactPath, cancellationToken).ConfigureAwait(false);
                bool identityMatches = proof.SignatureDigest == EvidenceFiles.Digest(signature);
                if (!identityMatches && m_certificate != null)
                {
                    NugetPrimaryIdentity? primary = await NugetPrimaryIdentity.ReadAsync(artifactPath, cancellationToken)
                        .ConfigureAwait(false);
                    identityMatches = primary?.CertificateDigest == KeyDigest &&
                        primary.SignatureDigest == proof.SignatureDigest;
                }
                return proof.SignerDigest == KeyDigest && identityMatches &&
                    proof.ArtifactDigest == EvidenceFiles.Digest(artifact) &&
                    m_verifier.VerifyData(artifact, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            /// <summary>
            /// Authenticates an in-toto DSSE statement with the fixture key and returns its parsed payload or null.
            /// </summary>
            public async Task<JsonDocument?> VerifyAsync(
                string subjectPath, string bundlePath, string predicateType,
                VerificationAuthority authority, TrustedPolicySnapshot policy, CancellationToken cancellationToken)
            {
                DsseFixture proof = await m_files.ReadModelAsync(
                    bundlePath, TrustedTestJsonContext.Default.DsseFixture, cancellationToken).ConfigureAwait(false);
                byte[] bytes = Convert.FromBase64String(proof.Payload);
                if (proof.PayloadType != StatementPayloadType || proof.Signatures.Length != 1 ||
                    proof.Signatures[0].KeyId != KeyDigest || authority.Id != "isolated-test-authority" ||
                    !m_verifier.VerifyData(StatementPae(bytes), Convert.FromBase64String(proof.Signatures[0].Sig),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return null;
                }
                return JsonDocument.Parse(bytes);
            }

            /// <summary>
            /// Applies targeted source, authority, membership, execution-proof, or signature mutations to the release
            /// fixture.
            /// </summary>
            public async Task MutateAsync(string mutation)
            {
                switch (mutation)
                {
                    case "unsigned":
                    case "signer":
                        foreach (string path in Directory.EnumerateFiles(BundleRoot, "*.dsse.json"))
                        {
                            DsseFixture proof = await m_files.ReadModelAsync(
                                path, TrustedTestJsonContext.Default.DsseFixture, CancellationToken.None)
                                .ConfigureAwait(false);
                            proof = proof with
                            {
                                Signatures = mutation == "unsigned" ? [] :
                                    [proof.Signatures[0] with { KeyId = "unapproved-key" }]
                            };
                            await WriteAsync(path, proof, TrustedTestJsonContext.Default.DsseFixture)
                                .ConfigureAwait(false);
                        }
                        break;
                    case "source":
                        Envelope = Envelope with { Source = Envelope.Source with { ActualSha = new string('c', 40) } };
                        break;
                    case "definition":
                        Envelope = Envelope with
                        {
                            Producer = Envelope.Producer with { DefinitionSha = new string('c', 40) }
                        };
                        break;
                    case "run":
                        Envelope = Envelope with { Producer = Envelope.Producer with { RunId = "9999" } };
                        break;
                    case "attempt":
                        Envelope = Envelope with { Producer = Envelope.Producer with { Attempt = 2 } };
                        break;
                    case "intent":
                        Policy = Policy with { ExpectedIntentDigest = Digest("different intent") };
                        break;
                    case "policy":
                        Policy = Policy with { PolicyDigest = Digest("different protected policy") };
                        break;
                    case "revoked":
                        Policy = Policy with { RevokedRecordIds = ["record-producer"] };
                        break;
                    case "partial":
                        Envelope = Envelope with { Artifacts = Envelope.Artifacts[..^1] };
                        break;
                    case "artifact-signature":
                        await File.WriteAllBytesAsync(
                            Path.Combine(BundleRoot, "artifact-0.sig"), [0, 1, 2]).ConfigureAwait(false);
                        break;
                    case "baseline":
                        JobRecord[] jobs = [.. Envelope.Assurance.Jobs];
                        jobs[0] = jobs[0] with
                        {
                            Status = "failed",
                            Counts = jobs[0].Counts! with { Passed = 0, Failed = 1 }
                        };
                        Envelope = Envelope with
                        {
                            Assurance = Envelope.Assurance with { Jobs = jobs, Completed = 6, Failed = 1 }
                        };
                        break;
                    case "native-image":
                    case "analysis-scope":
                        string jobId = mutation == "native-image" ? "native-aot" : "codeql-csharp";
                        JobRecord job = Envelope.Assurance.Jobs.Single(j => j.Id == jobId);
                        string resultPath = Path.Combine(BundleRoot, job.ResultDocument!);
                        CollectedResultSummary summary = await m_files.ReadModelAsync(
                            resultPath, EvidenceJsonContext.Default.CollectedResultSummary, CancellationToken.None)
                            .ConfigureAwait(false);
                        summary = mutation == "native-image"
                            ? summary with
                            {
                                Native = summary.Native! with { ObservedDigest = Digest("another image") }
                            }
                            : summary with
                            {
                                Analysis = summary.Analysis! with
                                {
                                    Projects = [summary.Analysis.Projects[0] with { ExtractedSources = 0 }]
                                }
                            };
                        await WriteAsync(resultPath, summary, EvidenceJsonContext.Default.CollectedResultSummary)
                            .ConfigureAwait(false);
                        string updatedDigest = await m_files.DigestAsync(resultPath, CancellationToken.None)
                            .ConfigureAwait(false);
                        Envelope = Envelope with
                        {
                            Documents = [.. Envelope.Documents.Select(d =>
                                d.Path == job.ResultDocument ? d with { Digest = updatedDigest } : d)]
                        };
                        await CreateProofsAsync().ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            }

            private async Task InitializeAsync(string stage, string channel, string version)
            {
                string original = FindRoot();
                foreach (string name in new[]
                {
                    "release-evidence.schema.json", "assurance-profiles.json", "verification-bundle.schema.json",
                    "verification-record.schema.json", "trusted-policy-snapshot.schema.json", "codeql-review.schema.json"
                })
                {
                    File.Copy(Path.Combine(original, ".azurepipelines", name),
                        Path.Combine(Repository, ".azurepipelines", name));
                }
                var source = new SourceRecord(
                    "Synthetic/ReleaseTests", new string('a', 40), "refs/heads/master", true);
                ToolRecord[] tools =
                [
                    new("dotnet", "10.0.100", Digest("dotnet")),
                    new("ilc", "10.0.100", Digest("ilc")),
                    new("codeql", "2.23.0", Digest("codeql"))
                ];
                var producer = new ProducerRecord(
                    "github-actions", ".github/workflows/nuget-publish.yml", new string('b', 40),
                    "1000", 1, "pack", tools);
                var release = new ReleaseRecord("nuget", version, channel);
                ProfilesConfiguration profiles = await m_files.ReadModelAsync(
                    Path.Combine(Repository, ".azurepipelines", "assurance-profiles.json"),
                    EvidenceJsonContext.Default.ProfilesConfiguration, CancellationToken.None).ConfigureAwait(false);
                string[] profileIds = [.. profiles.Profiles.Select(p => p.Id)];
                var catalog = new ArtifactsConfiguration(1, "synthetic-catalog", "1.0.0",
                [
                    new ArtifactGroup("nuget", producer.Workflow, "nuget", profileIds,
                        ".azurepipelines/packages.txt",
                        [new("modern", "Release"), new("debug", "Debug"),
                            new("metapackages", "Release", [".azurepipelines/meta.nuspec"])])
                ]);
                var policy = new PolicyConfiguration(
                    1, "synthetic-policy", "1.0.0", 2, stage, false, 2,
                    ".azurepipelines/release-evidence.schema.json", ".azurepipelines/release-artifacts.json",
                    ".azurepipelines/assurance-profiles.json", ["nuget"], [], new("incomplete", []));
                await WriteAsync(Path.Combine(Repository, ".azurepipelines", "release-policy.json"),
                    policy, EvidenceJsonContext.Default.PolicyConfiguration).ConfigureAwait(false);
                await WriteAsync(Path.Combine(Repository, ".azurepipelines", "release-artifacts.json"),
                    catalog, EvidenceJsonContext.Default.ArtifactsConfiguration).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(Repository, ".azurepipelines", "packages.txt"), "Synthetic\n")
                    .ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(Repository, ".azurepipelines", "meta.nuspec"),
                    Nuspec("Synthetic.Meta", version)).ConfigureAwait(false);
                string policyDigest = await m_files.DigestAsync(
                    Path.Combine(Repository, ".azurepipelines", "release-policy.json"), CancellationToken.None)
                    .ConfigureAwait(false);
                var documents = new List<DocumentRecord>();
                var artifacts = new List<ArtifactRecord>();
                foreach ((string id, string configuration, bool symbols, bool meta) in new[]
                {
                    ("Synthetic", "Release", false, false),
                    ("Synthetic.Debug", "Debug", false, false),
                    ("Synthetic.Meta", "Release", false, true),
                    ("Synthetic", "Release", true, false)
                })
                {
                    PackageInventory inventory = await CreatePackageAsync(id, version, configuration, symbols, meta)
                        .ConfigureAwait(false);
                    artifacts.Add(inventory.Artifact);
                    string prefix = id + (symbols ? ".symbols" : ".package");
                    await AddDocumentAsync(prefix + ".inventory.json",
                        JsonSerializer.SerializeToUtf8Bytes(inventory, EvidenceJsonContext.Default.PackageInventory),
                        "inventory", new SubjectRecord(inventory.Artifact.Kind, id, inventory.Artifact.Digest),
                        documents)
                        .ConfigureAwait(false);
                    await AddDocumentAsync(prefix + ".cdx.json",
                        Encoding.UTF8.GetBytes(CycloneDxInventory.Serialize(inventory)),
                        "sbom", new SubjectRecord(inventory.Artifact.Kind, id, inventory.Artifact.Digest), documents,
                        "CycloneDX", "1.6").ConfigureAwait(false);
                }
                var mappings = new SourceInputsRecord(1, source, [Digest("final-build-input")],
                [
                    new("Synthetic.csproj", "Release", "Synthetic", version,
                        true, true, "snupkg", true, Digest("graph")),
                    new("Synthetic.csproj", "Debug", "Synthetic.Debug", version,
                        true, false, "snupkg", false, Digest("graph"))
                ]);
                byte[] mappingBytes = JsonSerializer.SerializeToUtf8Bytes(
                    mappings, EvidenceJsonContext.Default.SourceInputsRecord);
                var sourceSubject = new SubjectRecord("source", source.Repository, EvidenceFiles.Digest(mappingBytes));
                await AddDocumentAsync("source-inputs.json", mappingBytes, "input-manifest", sourceSubject, documents)
                    .ConfigureAwait(false);
                foreach ((string name, string type) in new[]
                {
                    ("release-policy.json", "policy"), ("release-artifacts.json", "artifact-catalog"),
                    ("assurance-profiles.json", "profile")
                })
                {
                    await AddDocumentAsync(name, await File.ReadAllBytesAsync(
                        Path.Combine(Repository, ".azurepipelines", name)).ConfigureAwait(false),
                        type, sourceSubject, documents).ConfigureAwait(false);
                }
                var jobs = new List<JobRecord>();
                foreach (ProfileConfiguration profile in profiles.Profiles)
                {
                    foreach (ProfileJob definition in profile.Jobs)
                    {
                        var counts = definition.Kind == "analysis"
                            ? new CountsRecord(AnalyzedProjects: 1, Findings: 0, UnresolvedFindings: 0)
                            : new CountsRecord(1, 1, 1, 0, 0, 1, 1, 1, 1);
                        ProducerRecord jobProducer = producer with
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
                            source.ActualSha, jobProducer, definition.Id + ".result.json", Counts: counts);
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        var native = new NativeAssuranceProof(
                            job.Project, "Release", "net10.0", "net10.0", "win-x64", true,
                            source.ActualSha, jobProducer.RunId, 1, new string('c', 32), 100, 0,
                            Digest("native-image"), Digest("native-image"),
                            Digest("native-image"), Digest("native-image"),
                            now.AddMinutes(-2), now.AddMinutes(-1),
                            new("pe32plus", "amd64", "DNDH", 5, 0, Digest("native-image"), 4096),
                            new(".NET 10.0.1", "x64", false, false, "completed"),
                            new("10.0.100", "10.0.100", Digest("ilc")));
                        var analysis = new CodeqlAssuranceProof(
                            "completed", source.ActualSha, source.ActualRef, jobProducer.RunId, 1,
                            "3000", "synthetic-sarif", "assurance-csharp-net10",
                            new("CodeQL", "2.23.0", Digest("codeql")),
                            [new(Digest("project"), 1, 1, Digest("sources"), Digest("sources"), 0)],
                            1, 1, 0, 0, "clean-analysis", Digest("db"), Digest("extract"), Digest("config"),
                            Digest("suite"), Digest("pack"), Digest("query"), Digest("query-results"),
                            Digest("build"), Digest("population"));
                        var summary = new CollectedResultSummary(1, definition.Kind switch
                        {
                            "native-test" => "native-aot",
                            "analysis" => "codeql",
                            "fuzz-replay" => "fuzz-replay",
                            _ => "trx"
                        }, "completed", [new(Digest("actual-result"))], counts,
                            definition.Kind == "fuzz-replay"
                                ? new(Digest("inventory"), Digest("targets"), Digest("execution"), 1, 1, 0) : null,
                            definition.Kind == "native-test" ? native : null,
                            definition.Kind == "analysis" ? analysis : null);
                        await AddDocumentAsync(job.ResultDocument!,
                            JsonSerializer.SerializeToUtf8Bytes(
                                summary, EvidenceJsonContext.Default.CollectedResultSummary),
                            "assurance-summary", sourceSubject, documents).ConfigureAwait(false);
                        jobs.Add(job);
                    }
                }
                Envelope = new EvidenceEnvelope(2, source, producer, release,
                    new PolicyRecord(policy.Id, policy.Version, policyDigest, stage), [.. artifacts], [.. documents],
                    new(profileIds, 7, 7, 7, 0, 0, 0, [.. jobs],
                        [.. documents.Where(d => d.Type == "profile")
                            .Select(d => new InputRecord("profile", "profile", d.Path, d.Digest))]),
                    new("incomplete", [.. VerificationControls.Kinds.SelectMany(VerificationControls.ForKind)],
                        [.. VerificationControls.Kinds.SelectMany(VerificationControls.ForKind)], []));
                FrozenFile[] contractFiles = [.. await Task.WhenAll(Directory.EnumerateFiles(
                    Path.Combine(Repository, ".azurepipelines")).Select(async path => new FrozenFile(
                        ".azurepipelines/" + Path.GetFileName(path),
                        await m_files.DigestAsync(path, CancellationToken.None).ConfigureAwait(false),
                        new FileInfo(path).Length))).ConfigureAwait(false)];
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;
                Policy = new TrustedPolicySnapshot(1, "isolated-test-only", stage, 1,
                    timestamp.AddHours(-1), timestamp.AddHours(1), policyDigest, contractFiles, release,
                    Digest("not-initialized"),
                    [new("isolated-test-authority", source.Repository, "https://test.invalid", "isolated-test-signer",
                        producer.Workflow, producer.DefinitionSha, source.ActualRef, VerificationControls.Kinds)],
                    [.. jobs.Select(j => j.Producer!).Append(producer).GroupBy(p => p.Workflow, StringComparer.Ordinal)
                        .Select(g => new ProducerPin(g.First().System, g.Key, g.First().DefinitionSha,
                            [.. g.Select(p => p.Job)], tools))], [],
                    new(Path.Combine(Root, "protected-gh"), Digest("gh"), "2.80.0"),
                    new(Path.Combine(Root, "protected-root"), Digest("sigstore-root"), "1"));
                await CreateProofsAsync().ConfigureAwait(false);
            }

            private async Task<PackageInventory> CreatePackageAsync(
                string id, string version, string configuration, bool symbols, bool meta)
            {
                string file = id + (symbols ? ".snupkg" : ".nupkg");
                string path = Path.Combine(Packages, file);
                var payloads = new List<InventoryPayload>();
                using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    byte[] spec = Encoding.UTF8.GetBytes(Nuspec(id, version));
                    using (Stream output = archive.CreateEntry(id + ".nuspec").Open())
                    {
                        await output.WriteAsync(spec).ConfigureAwait(false);
                    }
                    payloads.Add(new(id + ".nuspec", EvidenceFiles.Digest(spec), "metadata", [], []));
                    if (m_certificate != null)
                    {
                        byte[] primary = CreatePrimarySignature();
                        using Stream signature = archive.CreateEntry(".signature.p7s").Open();
                        await signature.WriteAsync(primary).ConfigureAwait(false);
                        payloads.Add(new(".signature.p7s", EvidenceFiles.Digest(primary), "metadata", [], []));
                    }
                    if (!meta)
                    {
                        string entry = "lib/net10.0/Synthetic." + (symbols ? "pdb" : "dll");
                        byte[] bytes = Encoding.UTF8.GetBytes("synthetic-" + id + "-" + symbols);
                        using Stream output = archive.CreateEntry(entry).Open();
                        await output.WriteAsync(bytes).ConfigureAwait(false);
                        payloads.Add(new(entry, EvidenceFiles.Digest(bytes), symbols ? "symbols" : "shipped",
                            ["net10.0"], [], id, version));
                    }
                }
                var artifact = new ArtifactRecord(
                    symbols ? "nuget-symbols" : "nuget-package", id, version, configuration,
                    await m_files.DigestAsync(path, CancellationToken.None).ConfigureAwait(false),
                    new(meta ? [] : ["net10.0"], [], [], []), new FileInfo(path).Length);
                return new PackageInventory(1, artifact, [new("expression", "MIT")], [], [],
                    meta ? [] :
                        [new(id, version, "project", "net10.0", [], [new("expression", "MIT")], "Synthetic.csproj")],
                    [.. payloads], []);
            }

            private async Task AddDocumentAsync(
                string path, byte[] bytes, string type, SubjectRecord subject,
                List<DocumentRecord> documents, string format = "json", string version = "1")
            {
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, path), bytes).ConfigureAwait(false);
                documents.Add(new(type, format, version, path, EvidenceFiles.Digest(bytes), subject));
            }

            private async Task CreateProofsAsync()
            {
                await SaveEnvelopeAsync().ConfigureAwait(false);
                string evidenceDigest = await m_files.DigestAsync(EvidencePath, CancellationToken.None)
                    .ConfigureAwait(false);
                FrozenFile[] documents = [.. Envelope.Documents.Select(d =>
                    new FrozenFile(d.Path, d.Digest, new FileInfo(Path.Combine(BundleRoot, d.Path)).Length))];
                var signatures = new List<ArtifactSignatureProof>();
                int index = 0;
                foreach (ArtifactRecord artifact in Envelope.Artifacts)
                {
                    string path = Path.Combine(Packages,
                        artifact.Id + (artifact.Kind == "nuget-symbols" ? ".snupkg" : ".nupkg"));
                    byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                    byte[] signature = m_signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    string proofPath = "artifact-" + index++ + ".sig";
                    await File.WriteAllBytesAsync(Path.Combine(BundleRoot, proofPath), signature)
                        .ConfigureAwait(false);
                    signatures.Add(new(artifact.Kind, artifact.Id, artifact.Digest, proofPath,
                        EvidenceFiles.Digest(signature), KeyDigest));
                }
                var proofs = new List<VerificationProof>();
                string? intentDigest = null;
                foreach (string kind in VerificationControls.Kinds)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    var record = new VerificationRecord(1, "record-" + kind, kind, evidenceDigest, Policy.PolicyDigest,
                        VerificationControls.ArtifactSetDigest(Envelope.Artifacts), Envelope.Source, Envelope.Producer,
                        Envelope.Release, Envelope.Artifacts, documents,
                        kind == "assurance" ? Envelope.Assurance.Jobs : [],
                        VerificationControls.ForKind(kind), 1, now.AddMinutes(-5), now.AddMinutes(30),
                        new(["nuget"], ["nuget.org"], ["synthetic-qualification"], []),
                        intentDigest, ArtifactSignatures: kind == "artifact-signatures" ? [.. signatures] : null);
                    VerificationProof proof = await WriteSignedRecordAsync(kind, record).ConfigureAwait(false);
                    proofs.Add(proof);
                    if (kind == "release-intent")
                    {
                        intentDigest = await m_files.DigestAsync(
                            Path.Combine(BundleRoot, proof.RecordPath), CancellationToken.None).ConfigureAwait(false);
                    }
                }
                Policy = Policy with { ExpectedIntentDigest = intentDigest! };
                await WriteAsync(BundlePath, new VerificationBundle(1, [.. proofs], CodeqlReviews: m_reviews),
                    VerificationJsonContext.Default.VerificationBundle).ConfigureAwait(false);
            }

            private async Task<VerificationProof> WriteSignedRecordAsync(string kind, VerificationRecord record)
            {
                string recordPath = kind + ".record.json";
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                    record, VerificationJsonContext.Default.VerificationRecord);
                await File.WriteAllBytesAsync(Path.Combine(BundleRoot, recordPath), bytes).ConfigureAwait(false);
                string proofPath = kind + ".dsse.json";
                var dsse = new DsseFixture(PayloadType, Convert.ToBase64String(bytes),
                    [new(KeyDigest, Convert.ToBase64String(m_signer.SignData(
                        Pae(bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))]);
                await WriteAsync(Path.Combine(BundleRoot, proofPath), dsse, TrustedTestJsonContext.Default.DsseFixture)
                    .ConfigureAwait(false);
                return new VerificationProof("isolated-test-authority", recordPath, proofPath);
            }

            private async Task SaveEnvelopeAsync()
            {
                if (!Envelope.Documents.Any(d => d.Path == "producer-assessment.json"))
                {
                    Envelope = await ProducerAssessments.AttachAsync(
                        Envelope, BundleRoot, m_files, CancellationToken.None).ConfigureAwait(false);
                }
                await WriteAsync(EvidencePath, Envelope, EvidenceJsonContext.Default.EvidenceEnvelope)
                    .ConfigureAwait(false);
                await WriteAsync(ExpectedPath,
                    new EvaluationExpectation(Envelope.Source, Envelope.Producer, Envelope.Release,
                        Envelope.Policy.Digest, Envelope.Artifacts),
                    EvidenceJsonContext.Default.EvaluationExpectation).ConfigureAwait(false);
            }

            private static Task WriteAsync<T>(
                string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
            {
                return File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, type));
            }

            private static byte[] Pae(byte[] payload)
            {
                return [.. Encoding.UTF8.GetBytes(
                    $"DSSEv1 {Encoding.UTF8.GetByteCount(PayloadType)} {PayloadType} {payload.Length} "), .. payload];
            }

            private static byte[] StatementPae(byte[] payload)
            {
                return [.. Encoding.UTF8.GetBytes(
                    $"DSSEv1 {Encoding.UTF8.GetByteCount(StatementPayloadType)} {StatementPayloadType} {payload.Length} "),
                    .. payload];
            }

            private static string Nuspec(string id, string version)
            {
                return $"<package><metadata><id>{id}</id><version>{version}</version>" +
                    "<license type=\"expression\">MIT</license></metadata></package>";
            }

            private static string Digest(string value)
            {
                return EvidenceFiles.Digest(Encoding.UTF8.GetBytes(value));
            }

            private static string FindRoot()
            {
                var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (root != null && !File.Exists(Path.Combine(root.FullName, "UA.slnx")))
                {
                    root = root.Parent;
                }
                return root?.FullName ?? throw new DirectoryNotFoundException("Repository root missing.");
            }

            private byte[] CreatePrimarySignature()
            {
                byte[] content = "Synthetic primary content; archive authentication uses a separate real signature."u8
                    .ToArray();
                var writer = new AsnWriter(AsnEncodingRules.DER);
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                    using (writer.PushSequence())
                    {
                        writer.WriteInteger(1);
                        using (writer.PushSetOf())
                        using (writer.PushSequence())
                        {
                            writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                        }
                        using (writer.PushSequence())
                        {
                            writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
                            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                            {
                                writer.WriteOctetString(content);
                            }
                        }
                        using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                        {
                            writer.WriteEncodedValue(m_certificate!.RawData);
                        }
                        using (writer.PushSetOf())
                        using (writer.PushSequence())
                        {
                            writer.WriteInteger(1);
                            using (writer.PushSequence())
                            {
                                writer.WriteEncodedValue(m_certificate!.IssuerName.RawData);
                                writer.WriteInteger(new System.Numerics.BigInteger(
                                    m_certificate.GetSerialNumber(), isUnsigned: true));
                            }
                            using (writer.PushSequence())
                            {
                                writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                            }
                            using (writer.PushSequence())
                            {
                                writer.WriteObjectIdentifier("1.2.840.113549.1.1.1");
                                writer.WriteNull();
                            }
                            writer.WriteOctetString(m_signer.SignData(
                                content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                        }
                    }
                }
                return writer.Encode();
            }

            private string KeyDigest => m_certificate == null
                ? EvidenceFiles.Digest(m_verifier.ExportSubjectPublicKeyInfo()) : EvidenceFiles.Digest(m_certificate.RawData);

            private const string PayloadType = "application/vnd.opcua.release-record+json";
            private const string StatementPayloadType = "application/vnd.in-toto+json";
            private readonly EvidenceFiles m_files = new();
            private readonly RSA m_signer;
            private readonly RSA m_verifier;
            private readonly X509Certificate2? m_certificate;
            private VerificationProof[]? m_reviews;
        }

        /// <summary>
        /// Re-evaluates promotion eligibility using the fixture's current independent policy and cryptographic
        /// verifiers.
        /// </summary>
        /// <param name="fixture">The release fixture supplying policy, keys, and signature verification.</param>
        /// <param name="input">The immutable promotion request and evidence locations to verify.</param>
        private sealed class FixtureEligibility(SyntheticRelease fixture, PromotionVerificationInput input)
            : IPromotionEligibility
        {
            /// <summary>
            /// Verifies the bound request against the latest fixture policy before a promotion operation proceeds.
            /// </summary>
            public Task<VerifiedPromotion> VerifyAsync(CancellationToken cancellationToken)
            {
                var files = new EvidenceFiles();
                return VerifiedPromotion.VerifyAsync(input, files,
                    new TrustedEvidenceVerifier(files, fixture, fixture, TimeProvider.System, fixture),
                    fixture, cancellationToken);
            }
        }

        /// <summary>
        /// Wraps a promotion journal to simulate interruption after content readback but before it is recorded.
        /// </summary>
        /// <param name="inner">The journal that receives entries not selected for interruption.</param>
        private sealed class InterruptAfterContent(IPromotionJournal inner) : IPromotionJournal
        {
            /// <summary>
            /// Rejects content-readback entries with a synthetic I/O failure and forwards other entries to the journal.
            /// </summary>
            public Task AppendAsync(PromotionEvent entry, CancellationToken cancellationToken)
            {
                if (entry.Operation == "content-readback")
                {
                    throw new IOException("Synthetic interruption after destination write.");
                }
                return inner.AppendAsync(entry, cancellationToken);
            }
        }

        /// <summary>
        /// Simulates publication revocation at the create-started journal boundary.
        /// </summary>
        /// <param name="fixture">The release fixture whose publication authority is revoked during creation.</param>
        private sealed class RevokeOnCreate(SyntheticRelease fixture) : IPromotionJournal
        {
            /// <summary>
            /// Honors cancellation and revokes publication on create-started entries without persisting a journal.
            /// </summary>
            public Task AppendAsync(PromotionEvent entry, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Operation == "create-started")
                {
                    fixture.RevokePublication();
                }
                return Task.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Represents a DSSE envelope carrying a base64-encoded fixture payload and its signatures.
    /// </summary>
    /// <param name="PayloadType">The media type authenticated alongside the payload.</param>
    /// <param name="Payload">The base64-encoded record or in-toto statement bytes.</param>
    /// <param name="Signatures">The signature entries authenticating the encoded payload.</param>
    internal sealed record DsseFixture(string PayloadType, string Payload, DsseFixtureSignature[] Signatures);

    /// <summary>
    /// Represents one fixture signing-key identity and its encoded DSSE signature.
    /// </summary>
    /// <param name="KeyId">The digest identifying the ephemeral verification key or author certificate.</param>
    /// <param name="Sig">The base64-encoded signature over the DSSE pre-authentication encoding.</param>
    internal sealed record DsseFixtureSignature(string KeyId, string Sig);

    /// <summary>
    /// Provides source-generated JSON metadata for the DSSE test envelope and signature records.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DsseFixture))]
    internal sealed partial class TrustedTestJsonContext : JsonSerializerContext;
}
