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
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Records delivery-content comparison and independent approval of the original and delivered NuGet signatures.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">
    /// Whether delivered content and approved signatures are verified or remain incomplete.
    /// </param>
    /// <param name="BaselineFailed">Whether a baseline integrity or assurance check failed.</param>
    /// <param name="SignatureVerificationPerformed">
    /// Whether cryptographic signature verification was actually performed.
    /// </param>
    /// <param name="AuthorDigest">The SHA-256 digest of the original author package archive.</param>
    /// <param name="DeliveredDigest">The SHA-256 digest of the delivered package archive.</param>
    /// <param name="EvidenceDigest">The digest of the exact release-evidence document.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="SignerDigest">The digest identifying the signer certificate used by the proof.</param>
    internal sealed record ApprovedNugetDeliveryReport(
        int SchemaVersion, string Status, bool BaselineFailed, bool SignatureVerificationPerformed,
        string AuthorDigest, string DeliveredDigest, string EvidenceDigest, string[] UnmetControls,
        string? PolicyDigest = null, string? SignerDigest = null);

    /// <summary>
    /// Combines preserved-content checks with trusted policy and approved NuGet author-signature verification.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="verifier">The verifier for independently authenticated release claims.</param>
    /// <param name="signatures">The verifier for approved primary NuGet author signatures.</param>
    internal sealed class ApprovedNugetDelivery(
        EvidenceFiles files, TrustedEvidenceVerifier verifier, IArtifactSignatureVerifier signatures)
    {
        /// <summary>
        /// Registers the command that verifies delivered NuGet content and independently approved author signatures.
        /// </summary>
        public static void Register(RootCommand root)
        {
            var command = new Command("verify-delivery-approved",
                "Verify preserved delivery contents and independently approved primary NuGet signatures.");
            var repository = new Option<string>("--repository-root") { Required = true };
            var evidence = new Option<string>("--evidence") { Required = true };
            var bundle = new Option<string>("--verification-bundle") { Required = true };
            var trust = new Option<string>("--trust-policy") { Required = true };
            var author = new Option<string>("--author") { Required = true };
            var delivered = new Option<string>("--delivered") { Required = true };
            var output = new Option<string>("--output") { Required = true };
            command.Options.Add(repository);
            command.Options.Add(evidence);
            command.Options.Add(bundle);
            command.Options.Add(trust);
            command.Options.Add(author);
            command.Options.Add(delivered);
            command.Options.Add(output);
            command.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    var files = new EvidenceFiles();
                    return await new ApprovedNugetDelivery(files, new TrustedEvidenceVerifier(files),
                        new NugetAuthorSignatureVerifier(files, new ProcessRunner())).VerifyAsync(
                            parse.GetRequiredValue(repository), parse.GetRequiredValue(evidence),
                            parse.GetRequiredValue(bundle), parse.GetRequiredValue(trust),
                            parse.GetRequiredValue(author), parse.GetRequiredValue(delivered),
                            parse.GetRequiredValue(output), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or
                    ArgumentException or InvalidOperationException or UnauthorizedAccessException or
                    Json.Schema.JsonSchemaException or OperationCanceledException or
                    System.ComponentModel.Win32Exception)
                {
                    await Console.Error.WriteLineAsync("Approved delivery verification input is unusable.")
                        .ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(command);
        }

        /// <summary>
        /// Writes an approved-delivery report and returns success only when content and approved signatures are
        /// verified.
        /// </summary>
        public async Task<int> VerifyAsync(
            string repositoryRoot, string evidencePath, string bundlePath, string trustPolicyPath,
            string authorPath, string deliveredPath, string output, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            cancellationToken = deadline.Token;
            string comparisonPath = output + ".content.json";
            int contentCode = await new NugetDeliveryVerifier(files).VerifyAsync(
                authorPath, deliveredPath, comparisonPath, cancellationToken).ConfigureAwait(false);
            NugetDeliveryContentReport content = await files.ReadModelAsync(
                comparisonPath, DeliveryJsonContext.Default.NugetDeliveryContentReport, cancellationToken)
                .ConfigureAwait(false);
            bool failed = contentCode != 0;
            bool performed = false;
            bool approved = false;
            string? signer = null;
            string evidenceDigest = await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false);
            string? policyDigest = null;
            if (!failed)
            {
                EvidenceEnvelope envelope = await files.ReadModelAsync(
                    evidencePath, EvidenceJsonContext.Default.EvidenceEnvelope, cancellationToken).ConfigureAwait(false);
                VerifiedClaims claims = await verifier.VerifyAsync(
                    repositoryRoot, evidencePath, envelope, bundlePath, trustPolicyPath,
                    Path.GetDirectoryName(Path.GetFullPath(authorPath)), cancellationToken).ConfigureAwait(false);
                policyDigest = claims.Policy?.PolicyDigest;
                VerificationRecord? record = claims.Records.GetValueOrDefault("artifact-signatures");
                ArtifactRecord[] artifacts = [.. envelope.Artifacts.Where(a =>
                    a.Kind == "nuget-package" && a.Digest == content.AuthorArchiveDigest &&
                    a.Id == content.PackageId && Versions.Equal(a.Version, content.Version))];
                ArtifactSignatureProof[] proofs = [.. record?.ArtifactSignatures?.Where(p =>
                    p.Kind == "nuget-package" && p.Id == content.PackageId &&
                    p.ArtifactDigest == content.AuthorArchiveDigest) ?? []];
                if (claims.Policy is { NugetVerifier: not null, NugetAuthorFingerprints: not null } policy &&
                    claims.BundleRoot != null && claims.Has("producer") &&
                    !claims.Findings.Any(f => f.Code is "POLICY_IDENTITY" or "EVIDENCE_FRESHNESS" or
                        "SOURCE_IDENTITY" or "PRODUCER_IDENTITY" or "PROVENANCE_VERIFIED" or "SIGNATURE_VERIFIED") &&
                    artifacts.Length == 1 && proofs.Length == 1 &&
                    policy.NugetAuthorFingerprints.Contains(proofs[0].SignerDigest, StringComparer.Ordinal))
                {
                    signer = proofs[0].SignerDigest;
                    NugetPrimaryIdentity? deliveredIdentity = await NugetPrimaryIdentity.ReadAsync(
                        deliveredPath, cancellationToken).ConfigureAwait(false);
                    if (deliveredIdentity != null && deliveredIdentity.CertificateDigest == signer)
                    {
                        performed = true;
                        approved = await signatures.VerifyAsync(
                            authorPath, proofs[0], claims.BundleRoot, policy, cancellationToken).ConfigureAwait(false) &&
                            await signatures.VerifyAsync(deliveredPath, proofs[0] with
                            {
                                ArtifactDigest = content.DeliveredArchiveDigest,
                                SignatureDigest = deliveredIdentity.SignatureDigest
                            }, claims.BundleRoot, policy, cancellationToken).ConfigureAwait(false);
                    }
                    failed = !approved;
                }
            }
            await files.WriteModelAsync(output, new ApprovedNugetDeliveryReport(
                1, approved ? "artifact-delivery-verified" : "incomplete", failed, performed,
                content.AuthorArchiveDigest, content.DeliveredArchiveDigest, evidenceDigest,
                approved ? [] : ["SIGNATURE_VERIFIED"], policyDigest, signer),
                VerificationJsonContext.Default.ApprovedNugetDeliveryReport, cancellationToken).ConfigureAwait(false);
            return approved ? 0 : 1;
        }
    }
}
