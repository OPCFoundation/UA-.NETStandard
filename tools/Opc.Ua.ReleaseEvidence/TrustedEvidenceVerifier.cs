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
    /// Defines record-to-control coverage and canonical identity checks shared by independent evidence verification.
    /// </summary>
    internal static class VerificationControls
    {
        /// <summary>
        /// Returns the release controls covered by a supported verification-record kind.
        /// </summary>
        public static string[] ForKind(string kind)
        {
            return kind switch
            {
                "release-intent" => ["RELEASE_INTENT"],
                "producer" => ["SOURCE_IDENTITY", "PRODUCER_IDENTITY", "PROVENANCE_VERIFIED"],
                "artifact-signatures" => ["SIGNATURE_VERIFIED"],
                "assurance" => ["ASSURANCE_COMPLETE"],
                "codeql-disposition" => ["ASSURANCE_COMPLETE"],
                "public-review" => ["PUBLIC_EVIDENCE_SAFE"],
                "producer-qualification" => ["PRODUCER_NOT_READY"],
                "publication-boundary" => ["PUBLISHER_BOUNDARY"],
                _ => throw new InvalidDataException("Unknown verification record kind.")
            };
        }

        /// <summary>
        /// Checks whether a value uses the canonical lowercase SHA-256 identifier format.
        /// </summary>
        public static bool IsDigest(string value)
        {
            if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
            {
                return false;
            }
            foreach (char character in value.AsSpan(7))
            {
                if (!char.IsAsciiHexDigitLower(character))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Computes a canonical artifact-set digest after normalizing versions and ordering artifacts and target
        /// scopes.
        /// </summary>
        public static string ArtifactSetDigest(ArtifactRecord[] artifacts)
        {
            ArtifactRecord[] canonical = [.. artifacts.Select(a => a with
            {
                Version = Versions.Parse(a.Version).ToNormalizedString(),
                Scopes = new ScopeRecord(
                    [.. a.Scopes.Tfms.Order(StringComparer.Ordinal)],
                    [.. a.Scopes.Rids.Order(StringComparer.Ordinal)],
                    [.. a.Scopes.Roslyn.Order(StringComparer.Ordinal)],
                    [.. a.Scopes.Platforms.Order(StringComparer.Ordinal)])
            }).OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
                .ThenBy(a => a.Configuration, StringComparer.Ordinal).ThenBy(a => a.Digest, StringComparer.Ordinal)];
            return EvidenceFiles.Digest(JsonSerializer.SerializeToUtf8Bytes(
                canonical, VerificationJsonContext.Default.ArtifactRecordArray));
        }

        /// <summary>
        /// Compares producer workflow, run attempt, job, and ordered tool identities without relying on timing
        /// metadata.
        /// </summary>
        public static bool SameProducer(ProducerRecord left, ProducerRecord right)
        {
            return left.System == right.System && left.Workflow == right.Workflow &&
                left.DefinitionSha == right.DefinitionSha && left.RunId == right.RunId &&
                left.Attempt == right.Attempt && left.Job == right.Job &&
                left.Tools.OrderBy(t => t.Id, StringComparer.Ordinal)
                    .SequenceEqual(right.Tools.OrderBy(t => t.Id, StringComparer.Ordinal));
        }

        /// <summary>
        /// Checks required policy-contract membership and verifies every pinned file's size and digest.
        /// </summary>
        internal static async Task<bool> PolicyFilesMatchAsync(
            TrustedPolicySnapshot policy, string repositoryRoot, EvidenceFiles files, CancellationToken cancellationToken)
        {
            string policyPath = EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json");
            if (!policy.ContractFiles.Any(f => f.Path == ".azurepipelines/release-policy.json") ||
                VerificationSchemas.Names.Any(name =>
                    !policy.ContractFiles.Any(f => f.Path == ".azurepipelines/" + name)) ||
                policy.ContractFiles.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() !=
                    policy.ContractFiles.Length ||
                await files.DigestAsync(policyPath, cancellationToken).ConfigureAwait(false) != policy.PolicyDigest)
            {
                return false;
            }
            foreach (FrozenFile contract in policy.ContractFiles)
            {
                string path = EvidenceFiles.Confined(repositoryRoot, contract.Path);
                if (!File.Exists(path) || new FileInfo(path).Length != contract.Size ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != contract.Digest)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Lists the verification-record kinds required for complete release-control coverage.
        /// </summary>
        internal static readonly string[] Kinds =
        [
            "release-intent", "producer", "artifact-signatures", "assurance",
            "public-review", "producer-qualification", "publication-boundary"
        ];
    }

    /// <summary>
    /// Authenticates current release claims against protected policy, pinned authorities, and independent proof
    /// records.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="policySource">The source of independently anchored trust-policy snapshots.</param>
    /// <param name="signatureVerifier">
    /// The service that independently authenticates signed verification records.
    /// </param>
    /// <param name="timeProvider">The clock used to validate policy and proof freshness.</param>
    /// <param name="statements">The optional service for authenticating native in-toto statements.</param>
    internal sealed class TrustedEvidenceVerifier(
        EvidenceFiles files,
        ITrustPolicySource policySource,
        IRecordSignatureVerifier signatureVerifier,
        TimeProvider timeProvider,
        IStatementSignatureVerifier? statements = null)
    {
        /// <summary>
        /// Creates the verifier with protected policy loading, pinned GitHub record verification, and the system clock.
        /// </summary>
        public TrustedEvidenceVerifier(EvidenceFiles files)
            : this(files, new ProtectedTrustPolicySource(files),
                new GitHubRecordSignatureVerifier(files, new ProcessRunner()), TimeProvider.System)
        {
        }

        /// <summary>
        /// Verifies current policy and proof bindings and returns authenticated claims together with unmet-control
        /// findings.
        /// </summary>
        public async Task<VerifiedClaims> VerifyAsync(
            string repositoryRoot,
            string evidencePath,
            EvidenceEnvelope envelope,
            string? bundlePath,
            string? trustPolicyPath,
            string? artifactsRoot,
            CancellationToken cancellationToken)
        {
            string bundleRoot = bundlePath == null
                ? Path.GetDirectoryName(Path.GetFullPath(evidencePath))!
                : Path.GetDirectoryName(Path.GetFullPath(bundlePath))!;
            string[] candidates =
            [
                repositoryRoot, bundleRoot, Path.GetDirectoryName(Path.GetFullPath(evidencePath))!,
                .. artifactsRoot == null ? Array.Empty<string>() : new[] { artifactsRoot }
            ];
            TrustedPolicySnapshot? policy = await policySource.LoadAsync(
                trustPolicyPath, candidates, cancellationToken).ConfigureAwait(false);
            var records = new Dictionary<string, VerificationRecord>(StringComparer.Ordinal);
            var findings = new List<Finding>();
            var claims = new VerifiedClaims(policy, records, findings) { BundleRoot = bundleRoot };
            if (policy == null)
            {
                findings.Add(new Finding("POLICY_IDENTITY", "No independently anchored trust policy is configured."));
                AddMissing(claims);
                return claims;
            }
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (policy.SchemaVersion != 1 || policy.Stage is not ("pilot" or "required") ||
                policy.CheckpointSequence < 1 || policy.IssuedAt > now || policy.ExpiresAt <= now ||
                policy.ExpiresAt <= policy.IssuedAt || policy.Authorities.Length == 0 ||
                policy.Producers.Length == 0 || !VerificationControls.IsDigest(policy.ExpectedIntentDigest) ||
                !VerificationControls.IsDigest(policy.PolicyDigest))
            {
                findings.Add(new Finding("POLICY_IDENTITY", "Protected checkpoint or authority pins are incomplete."));
                AddMissing(claims);
                return claims;
            }
            string policyPath = EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json");
            PolicyConfiguration candidatePolicy = await files.ReadModelAsync(
                policyPath, EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
            string[] requiredContracts =
            [
                ".azurepipelines/release-policy.json", candidatePolicy.EvidenceSchema,
                candidatePolicy.ArtifactCatalog, candidatePolicy.AssuranceProfiles,
                ".azurepipelines/verification-bundle.schema.json",
                ".azurepipelines/verification-record.schema.json",
                ".azurepipelines/trusted-policy-snapshot.schema.json"
            ];
            ArtifactsConfiguration catalog = await files.ReadModelAsync(
                EvidenceFiles.Confined(repositoryRoot, candidatePolicy.ArtifactCatalog),
                EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
            ArtifactGroup selectedGroup = catalog.Groups.Single(g => g.Id == envelope.Release.Group);
            requiredContracts =
            [
                .. requiredContracts,
                .. selectedGroup.ModernCatalog == null ? Array.Empty<string>() : new[] { selectedGroup.ModernCatalog },
                .. selectedGroup.Variants?.SelectMany(v => v.Sources ?? []) ?? []
            ];
            if (policy.ContractFiles.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() !=
                policy.ContractFiles.Length ||
                requiredContracts.Any(p => !policy.ContractFiles.Any(f => f.Path == p)) ||
                await files.DigestAsync(policyPath, cancellationToken).ConfigureAwait(false) != policy.PolicyDigest ||
                (policy.Stage == "required" && candidatePolicy.Stage != "required"))
            {
                findings.Add(new Finding("POLICY_IDENTITY", "Candidate policy differs from the protected floor."));
            }
            if (!await VerificationControls.PolicyFilesMatchAsync(policy, repositoryRoot, files, cancellationToken)
                .ConfigureAwait(false))
            {
                findings.Add(new Finding("POLICY_IDENTITY", "A protected policy/catalog/profile input changed."));
            }
            if (policy.ExpectedRelease != envelope.Release)
            {
                findings.Add(new Finding("RELEASE_INTENT", "Candidate classification differs from protected intent."));
            }
            if (!PinnedProducer(policy, envelope.Producer))
            {
                findings.Add(new Finding("PRODUCER_IDENTITY", "Producer definition/job/tool pins are not approved."));
            }
            if (bundlePath == null || findings.Any(f => f.Code == "POLICY_IDENTITY"))
            {
                AddMissing(claims);
                return claims;
            }
            VerificationSchemas schemas = await VerificationSchemas.LoadAsync(
                repositoryRoot, files, cancellationToken).ConfigureAwait(false);
            schemas.Validate("trusted-policy-snapshot.schema.json", JsonSerializer.SerializeToElement(
                policy, VerificationJsonContext.Default.TrustedPolicySnapshot));
            using (JsonDocument bundleDocument = await files.ReadJsonAsync(bundlePath, cancellationToken)
                .ConfigureAwait(false))
            {
                schemas.Validate("verification-bundle.schema.json", bundleDocument.RootElement);
            }
            VerificationBundle bundle = await files.ReadModelAsync(
                bundlePath, VerificationJsonContext.Default.VerificationBundle, cancellationToken)
                .ConfigureAwait(false);
            if (bundle.SchemaVersion != 1 || bundle.Proofs.Length > 64 ||
                bundle.Proofs.Select(p => p.RecordPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                    bundle.Proofs.Length)
            {
                throw new InvalidDataException("Unsupported or duplicate verification bundle records.");
            }
            string evidenceDigest = await files.DigestAsync(evidencePath, cancellationToken).ConfigureAwait(false);
            string artifactSetDigest = VerificationControls.ArtifactSetDigest(envelope.Artifacts);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (VerificationProof proof in bundle.Proofs)
            {
                string recordPath = EvidenceFiles.Confined(bundleRoot, proof.RecordPath);
                string signaturePath = EvidenceFiles.Confined(bundleRoot, proof.BundlePath);
                if (!File.Exists(recordPath) || !File.Exists(signaturePath))
                {
                    findings.Add(new Finding("EVIDENCE_FRESHNESS", "A referenced immutable proof is missing."));
                    continue;
                }
                using (JsonDocument recordDocument = await files.ReadJsonAsync(recordPath, cancellationToken)
                    .ConfigureAwait(false))
                {
                    schemas.Validate("verification-record.schema.json", recordDocument.RootElement);
                }
                VerificationRecord record = await files.ReadModelAsync(
                    recordPath, VerificationJsonContext.Default.VerificationRecord, cancellationToken)
                    .ConfigureAwait(false);
                Versions.ValidateIdentity(record.Source, record.Producer);
                Versions.Parse(record.Release.Version);
                if (record.Id.Length is 0 or > 128 ||
                    record.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) ||
                    record.Release.Channel is not ("stable" or "preview" or "development") ||
                    record.Release.Group is not ("nuget" or "containers" or "pump") ||
                    record.Scope.RecordReferences.Any(string.IsNullOrWhiteSpace))
                {
                    throw new InvalidDataException("Malformed verification record identity or scope.");
                }
                foreach (ArtifactSignatureProof artifactProof in record.ArtifactSignatures ?? [])
                {
                    EvidenceFiles.ValidateRelative(artifactProof.BundlePath);
                    if (!VerificationControls.IsDigest(artifactProof.ArtifactDigest) ||
                        !VerificationControls.IsDigest(artifactProof.SignatureDigest) ||
                        !VerificationControls.IsDigest(artifactProof.SignerDigest))
                    {
                        throw new InvalidDataException("Malformed artifact signature proof digest.");
                    }
                }
                string[] controls = VerificationControls.ForKind(record.Kind);
                if (record.SchemaVersion != 1 ||
                    !record.Controls.Order(StringComparer.Ordinal).SequenceEqual(
                        controls.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                    !seenIds.Add(record.Id) || records.ContainsKey(record.Kind))
                {
                    throw new InvalidDataException("Unsupported, duplicate, or incomplete verification contract.");
                }
                VerificationAuthority[] authorities = [.. policy.Authorities.Where(a => a.Id == proof.AuthorityId)];
                if (authorities.Length != 1 ||
                    !authorities[0].RecordKinds.Contains(record.Kind, StringComparer.Ordinal) ||
                    !await signatureVerifier.VerifyAsync(
                        recordPath, signaturePath, authorities[0], policy, cancellationToken).ConfigureAwait(false))
                {
                    foreach (string control in controls)
                    {
                        findings.Add(new Finding(control, "Record signature or qualified signer was rejected."));
                    }
                    continue;
                }
                bool bound = record.EvidenceDigest == evidenceDigest &&
                    record.PolicyDigest == policy.PolicyDigest &&
                    record.ArtifactSetDigest == artifactSetDigest &&
                    VerificationControls.ArtifactSetDigest(record.Artifacts) == artifactSetDigest &&
                    record.Source == envelope.Source && record.Source.TrackedClean &&
                    record.Source.Repository == authorities[0].Repository &&
                    VerificationControls.SameProducer(record.Producer, envelope.Producer) &&
                    record.Release == envelope.Release && record.Release == policy.ExpectedRelease &&
                    record.CheckpointSequence == policy.CheckpointSequence &&
                    record.IssuedAt <= now && record.ExpiresAt > now &&
                    record.ExpiresAt <= policy.ExpiresAt && record.IssuedAt >= policy.IssuedAt &&
                    !policy.RevokedRecordIds.Contains(record.Id, StringComparer.Ordinal) &&
                    record.Scope.Groups.SequenceEqual([envelope.Release.Group], StringComparer.Ordinal) &&
                    record.Scope.RecordReferences.Length > 0 && record.Scope.Limitations.Length == 0 &&
                    record.Documents.Length == envelope.Documents.Length &&
                    record.Documents.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
                        record.Documents.Length &&
                    envelope.Documents.All(d => record.Documents.Any(r => r.Path == d.Path && r.Digest == d.Digest));
                bound &= record.Kind == "release-intent"
                    ? record.IntentDigest == null &&
                        await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false) ==
                            policy.ExpectedIntentDigest
                    : record.IntentDigest == policy.ExpectedIntentDigest;
                if (record.Kind == "assurance")
                {
                    bound &= SameJobs(record.Jobs, envelope.Assurance.Jobs) &&
                        record.Jobs.All(j => j.Producer != null && PinnedProducer(policy, j.Producer));
                }
                else
                {
                    bound &= record.Jobs.Length == 0;
                }
                if (record.Kind is "publication-boundary" or "release-intent" or "public-review")
                {
                    bound &= record.Scope.Destinations.Length > 0 &&
                        record.Scope.Destinations.All(d =>
                            d is "nuget.org" or "github-packages" or "github-release-assets" or "ghcr-referrers");
                }
                if (!bound)
                {
                    findings.Add(new Finding("EVIDENCE_FRESHNESS",
                        "Signed record is stale, revoked, partially scoped, or bound to different evidence/intent."));
                    foreach (string control in controls)
                    {
                        findings.Add(new Finding(control, "Authenticated record does not establish this claim."));
                    }
                    continue;
                }
                records.Add(record.Kind, record);
                claims.RecordDigests.Add(record.Kind,
                    await files.DigestAsync(recordPath, cancellationToken).ConfigureAwait(false));
            }
            foreach (VerificationProof reviewProof in bundle.CodeqlReviews ?? [])
            {
                VerifiedCodeqlReview? review = await VerifiedCodeqlReview.VerifyAsync(
                    repositoryRoot, bundleRoot, reviewProof, policy, files, signatureVerifier, timeProvider,
                    cancellationToken).ConfigureAwait(false);
                if (review == null)
                {
                    findings.Add(new Finding("ASSURANCE_COMPLETE", "Independent finding review was not authenticated."));
                }
                else if (!claims.CodeqlReviews.TryAdd(review.Record.Id, review))
                {
                    throw new InvalidDataException("Duplicate independent finding review.");
                }
            }
            if (bundle.NativeNuget != null)
            {
                if (artifactsRoot != null)
                {
                    claims.NativeNuget = await VerifiedNativeNuget.VerifyAsync(
                        repositoryRoot, evidencePath, artifactsRoot, bundleRoot, envelope, bundle.NativeNuget, policy, files,
                        statements ?? new GitHubStatementSignatureVerifier(files, new ProcessRunner()),
                        cancellationToken).ConfigureAwait(false);
                }
                if (claims.NativeNuget == null)
                {
                    findings.Add(new Finding(
                        "PROVENANCE_VERIFIED", "Native package/index proof scope or authentication is incomplete."));
                }
            }
            AddMissing(claims);
            return claims;
        }

        /// <summary>
        /// Requires exactly one policy pin matching the producer definition, job, and complete digest-bound tool set.
        /// </summary>
        internal static bool PinnedProducer(TrustedPolicySnapshot policy, ProducerRecord producer)
        {
            return policy.Producers.Count(p =>
                p.System == producer.System && p.Workflow == producer.Workflow &&
                p.DefinitionSha == producer.DefinitionSha && p.Jobs.Contains(producer.Job, StringComparer.Ordinal) &&
                producer.RunId != "0" && producer.Attempt > 0 && producer.Tools.Length > 0 &&
                producer.Tools.All(t => t.Digest != null && VerificationControls.IsDigest(t.Digest)) &&
                p.Tools.OrderBy(t => t.Id, StringComparer.Ordinal)
                    .SequenceEqual(producer.Tools.OrderBy(t => t.Id, StringComparer.Ordinal))) == 1;
        }

        private static bool SameJobs(JobRecord[] left, JobRecord[] right)
        {
            return JsonSerializer.Serialize(
                left.OrderBy(j => j.Id, StringComparer.Ordinal).ToArray(),
                VerificationJsonContext.Default.JobRecordArray) ==
                JsonSerializer.Serialize(
                    right.OrderBy(j => j.Id, StringComparer.Ordinal).ToArray(),
                    VerificationJsonContext.Default.JobRecordArray);
        }

        private static void AddMissing(VerifiedClaims claims)
        {
            foreach (string kind in VerificationControls.Kinds.Where(kind => !claims.Has(kind)))
            {
                foreach (string control in VerificationControls.ForKind(kind))
                {
                    claims.Findings.Add(new Finding(control, $"Missing authenticated {kind} record."));
                }
            }
            if (claims.Policy == null)
            {
                claims.Findings.Add(new Finding("EVIDENCE_FRESHNESS", "Current proof coverage is not authenticated."));
            }
        }
    }
}
