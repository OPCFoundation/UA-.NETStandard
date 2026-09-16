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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Locates an independent verification record and its signature bundle under a selected authority.
    /// </summary>
    /// <param name="AuthorityId">The identifier of the signing authority selected from protected policy.</param>
    /// <param name="RecordPath">The bundle-relative path to the independent verification record.</param>
    /// <param name="BundlePath">The bundle-relative path to the preserved signature or attestation bundle.</param>
    internal sealed record VerificationProof(string AuthorityId, string RecordPath, string BundlePath);

    /// <summary>
    /// Collects independent record proofs and optional OCI, CodeQL-review, and native NuGet verification inputs.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Proofs">The independent verification records and their preserved signature bundles.</param>
    /// <param name="OciRequestPath">The optional bundle-relative path to the downloaded OCI layout request.</param>
    /// <param name="CodeqlReviews">
    /// The optional independent CodeQL review records and their preserved signature bundles.
    /// </param>
    /// <param name="NativeNuget">The optional native NuGet pack and producer-index attestation proofs.</param>
    internal sealed record VerificationBundle(
        int SchemaVersion, VerificationProof[] Proofs, string? OciRequestPath = null,
        VerificationProof[]? CodeqlReviews = null, NativeNugetProof[]? NativeNuget = null);

    /// <summary>
    /// Identifies a native package or index attestation bundle and its authorized signer and content digest.
    /// </summary>
    /// <param name="Role">The native proof role identifying the producer index or package build configuration.</param>
    /// <param name="AuthorityId">The identifier of the signing authority selected from protected policy.</param>
    /// <param name="BundlePath">The bundle-relative path to the preserved signature or attestation bundle.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="IndexPath">The optional bundle-relative path to the immutable producer index.</param>
    internal sealed record NativeNugetProof(
        string Role, string AuthorityId, string BundlePath, string Digest, string? IndexPath = null);

    /// <summary>
    /// Binds an independent CodeQL review to source, producer, policy checkpoint, validity interval, and supporting
    /// records.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Id">The independent CodeQL review-record identifier.</param>
    /// <param name="Kind">The codeql-disposition discriminator identifying this review.</param>
    /// <param name="Repository">The source repository identified as owner and repository name.</param>
    /// <param name="SourceRef">The fully qualified source ref associated with the CodeQL analysis.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Review">The exact reviewed finding population and unresolved-finding counts.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="CheckpointSequence">The protected policy checkpoint sequence to which the record is bound.</param>
    /// <param name="IssuedAt">The start of the record's validity interval.</param>
    /// <param name="ExpiresAt">The end of the record's validity interval.</param>
    /// <param name="RecordReferences">The supporting record references bound to this verification.</param>
    internal sealed record CodeqlReviewRecord(
        int SchemaVersion,
        string Id,
        string Kind,
        string Repository,
        string SourceRef,
        ProducerRecord Producer,
        CodeqlDisposition Review,
        string PolicyDigest,
        long CheckpointSequence,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        string[] RecordReferences);

    /// <summary>
    /// Limits a verification record to approved groups, destinations, supporting records, and stated limitations.
    /// </summary>
    /// <param name="Groups">The artifact group identifiers covered by this record.</param>
    /// <param name="Destinations">The publication destinations covered by the verification scope.</param>
    /// <param name="RecordReferences">The supporting record references bound to this verification.</param>
    /// <param name="Limitations">The explicit limitations of the approved verification scope.</param>
    internal sealed record VerificationScope(
        string[] Groups, string[] Destinations, string[] RecordReferences, string[] Limitations);

    /// <summary>
    /// Binds an artifact's bytes to a preserved signature bundle and signer identity.
    /// </summary>
    /// <param name="Kind">The discriminator describing the record or artifact kind.</param>
    /// <param name="Id">The identifier used to distinguish this record within its declared scope.</param>
    /// <param name="ArtifactDigest">The SHA-256 digest of the exact artifact bytes.</param>
    /// <param name="BundlePath">The bundle-relative path to the preserved signature or attestation bundle.</param>
    /// <param name="SignatureDigest">The SHA-256 digest of the preserved signature content.</param>
    /// <param name="SignerDigest">The digest identifying the signer certificate used by the proof.</param>
    /// <param name="AuthorityId">The identifier of the signing authority selected from protected policy.</param>
    internal sealed record ArtifactSignatureProof(
        string Kind, string Id, string ArtifactDigest, string BundlePath, string SignatureDigest, string SignerDigest,
        string? AuthorityId = null);

    /// <summary>
    /// Binds independently verified release controls to exact evidence, artifacts, documents, jobs, and policy state.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Id">The unique independently signed verification-record identifier.</param>
    /// <param name="Kind">The record kind determining which release controls it can satisfy.</param>
    /// <param name="EvidenceDigest">The digest of the exact release-evidence document.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="ArtifactSetDigest">The canonical digest binding the complete artifact set.</param>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Release">The artifact group, release version, and delivery channel covered by this record.</param>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    /// <param name="Documents">The evidence documents covered by this record.</param>
    /// <param name="Jobs">The assurance jobs covered by this record.</param>
    /// <param name="Controls">The release controls asserted by the independently signed record.</param>
    /// <param name="CheckpointSequence">The protected policy checkpoint sequence to which the record is bound.</param>
    /// <param name="IssuedAt">The start of the record's validity interval.</param>
    /// <param name="ExpiresAt">The end of the record's validity interval.</param>
    /// <param name="Scope">
    /// The groups, destinations, references, and limitations authorized by the verification record.
    /// </param>
    /// <param name="IntentDigest">The digest binding the independent release intent.</param>
    /// <param name="CodeqlReview">The optional independent disposition of the CodeQL finding population.</param>
    /// <param name="ArtifactSignatures">The optional signature proofs for the exact release artifacts.</param>
    /// <param name="OciBuilds">
    /// The optional authenticated build expectations indexed by OCI image and subject digest.
    /// </param>
    /// <param name="PromotionRequestDigest">
    /// The optional digest authorizing the exact promotion request at the publication boundary.
    /// </param>
    internal sealed record VerificationRecord(
        int SchemaVersion,
        string Id,
        string Kind,
        string EvidenceDigest,
        string PolicyDigest,
        string ArtifactSetDigest,
        SourceRecord Source,
        ProducerRecord Producer,
        ReleaseRecord Release,
        ArtifactRecord[] Artifacts,
        FrozenFile[] Documents,
        JobRecord[] Jobs,
        string[] Controls,
        long CheckpointSequence,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        VerificationScope Scope,
        string? IntentDigest = null,
        CodeqlDisposition? CodeqlReview = null,
        ArtifactSignatureProof[]? ArtifactSignatures = null,
        Dictionary<string, OciBuildExpectation>? OciBuilds = null,
        string? PromotionRequestDigest = null);

    /// <summary>
    /// Pins the repository, workflow, certificate identity, source ref, and permitted record kinds of a signer.
    /// </summary>
    /// <param name="Id">The authority identifier referenced by independent verification proofs.</param>
    /// <param name="Repository">The source repository identified as owner and repository name.</param>
    /// <param name="Issuer">The certificate's approved OpenID Connect issuer.</param>
    /// <param name="CertificateIdentity">The exact certificate identity permitted by the signing authority.</param>
    /// <param name="Workflow">The repository-relative workflow or pipeline definition path.</param>
    /// <param name="DefinitionSha">The exact source revision of the producer or signer workflow definition.</param>
    /// <param name="Ref">The fully qualified source ref associated with this record.</param>
    /// <param name="RecordKinds">
    /// The verification-record kinds this signing authority is permitted to authenticate.
    /// </param>
    internal sealed record VerificationAuthority(
        string Id,
        string Repository,
        string Issuer,
        string CertificateIdentity,
        string Workflow,
        string DefinitionSha,
        string Ref,
        string[] RecordKinds);

    /// <summary>
    /// Pins a protected verifier executable or trust-root file by path, digest, and declared version.
    /// </summary>
    /// <param name="Path">The fully qualified path to the protected executable or trust-root file.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Version">The expected executable version or declared trust-root version.</param>
    internal sealed record VerifierToolPin(string Path, string Digest, string Version);

    /// <summary>
    /// Defines an approved producer workflow revision, allowed jobs, and exact tool identities.
    /// </summary>
    /// <param name="System">The producer system, such as GitHub Actions or Azure Pipelines.</param>
    /// <param name="Workflow">The repository-relative workflow or pipeline definition path.</param>
    /// <param name="DefinitionSha">The exact source revision of the producer or signer workflow definition.</param>
    /// <param name="Jobs">The job identifiers approved under this exact producer workflow definition.</param>
    /// <param name="Tools">The producer tool identities and versions recorded for the operation.</param>
    internal sealed record ProducerPin(
        string System, string Workflow, string DefinitionSha, string[] Jobs, ToolRecord[] Tools);

    /// <summary>
    /// Contains the independently anchored policy checkpoint, approved authorities, tool pins, and release
    /// expectations.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Authority">The protected release authority identifier.</param>
    /// <param name="Stage">The policy enforcement stage, such as pilot or required.</param>
    /// <param name="CheckpointSequence">The protected policy checkpoint sequence to which the record is bound.</param>
    /// <param name="IssuedAt">The start of the record's validity interval.</param>
    /// <param name="ExpiresAt">The end of the record's validity interval.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="ContractFiles">The required policy and schema files pinned by path, size, and digest.</param>
    /// <param name="ExpectedRelease">
    /// The artifact group, version, and channel independently authorized for release.
    /// </param>
    /// <param name="ExpectedIntentDigest">The digest of the release intent authorized by the protected policy.</param>
    /// <param name="Authorities">
    /// The independently approved signing authorities and their permitted record kinds.
    /// </param>
    /// <param name="Producers">The exact producer definitions, jobs, and tool identities approved by policy.</param>
    /// <param name="RevokedRecordIds">
    /// The verification-record identifiers revoked by the current policy checkpoint.
    /// </param>
    /// <param name="Tool">The pinned tool identity used for verification or analysis.</param>
    /// <param name="TrustedRoot">The independently protected Sigstore trust-root file pin.</param>
    /// <param name="NugetVerifier">The optional protected NuGet signature-verifier executable pin.</param>
    /// <param name="NugetAuthorFingerprints">
    /// The optional SHA-256 certificate fingerprints approved for NuGet author signing.
    /// </param>
    /// <param name="CosignVerifier">
    /// The optional protected cosign executable pin used for OCI artifact verification.
    /// </param>
    internal sealed record TrustedPolicySnapshot(
        int SchemaVersion,
        string Authority,
        string Stage,
        long CheckpointSequence,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        string PolicyDigest,
        FrozenFile[] ContractFiles,
        ReleaseRecord ExpectedRelease,
        string ExpectedIntentDigest,
        VerificationAuthority[] Authorities,
        ProducerPin[] Producers,
        string[] RevokedRecordIds,
        VerifierToolPin Tool,
        VerifierToolPin TrustedRoot,
        VerifierToolPin? NugetVerifier = null,
        string[]? NugetAuthorFingerprints = null,
        VerifierToolPin? CosignVerifier = null);

    /// <summary>
    /// Loads an independently anchored policy without trusting files supplied inside candidate roots.
    /// </summary>
    internal interface ITrustPolicySource
    {
        /// <summary>
        /// Loads trusted policy from the supplied location, returning null when no independent trust anchor is
        /// available.
        /// </summary>
        Task<TrustedPolicySnapshot?> LoadAsync(
            string? path, string[] candidateRoots, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Authenticates an independent verification record against a pinned signing authority and current policy.
    /// </summary>
    internal interface IRecordSignatureVerifier
    {
        /// <summary>
        /// Checks that a preserved signature bundle authenticates the exact record under the approved authority.
        /// </summary>
        Task<bool> VerifyAsync(
            string recordPath,
            string bundlePath,
            VerificationAuthority authority,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Authenticates artifact signatures against independently supplied signer and tool policy.
    /// </summary>
    internal interface IArtifactSignatureVerifier
    {
        /// <summary>
        /// Checks the artifact and preserved signature proof against approved signer identities and protected verifier
        /// pins.
        /// </summary>
        Task<bool> VerifyAsync(
            string artifactPath, ArtifactSignatureProof proof, string bundleRoot,
            TrustedPolicySnapshot policy, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Authenticates in-toto statements and returns only statements verified against the supplied subject and policy.
    /// </summary>
    internal interface IStatementSignatureVerifier
    {
        /// <summary>
        /// Returns the authenticated statement for the requested subject and predicate, or null when verification
        /// fails.
        /// </summary>
        Task<JsonDocument?> VerifyAsync(
            string subjectPath,
            string bundlePath,
            string predicateType,
            VerificationAuthority authority,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Collects the policy, authenticated records, native proofs, and findings produced by independent verification.
    /// </summary>
    internal sealed class VerifiedClaims
    {
        /// <summary>
        /// Creates a verification result using the supplied policy, accepted records, and shared finding collection.
        /// </summary>
        internal VerifiedClaims(
            TrustedPolicySnapshot? policy,
            Dictionary<string, VerificationRecord> records,
            List<Finding> findings)
        {
            Policy = policy;
            Records = records;
            Findings = findings;
        }

        /// <summary>
        /// Gets the loaded independently anchored policy, or null when no trust anchor is available.
        /// </summary>
        public TrustedPolicySnapshot? Policy { get; }

        /// <summary>
        /// Gets authenticated verification records indexed by their record kind.
        /// </summary>
        public Dictionary<string, VerificationRecord> Records { get; }

        /// <summary>
        /// Gets the findings accumulated while checking policy, proof identity, and required control coverage.
        /// </summary>
        public List<Finding> Findings { get; }

        /// <summary>
        /// Gets the preserved content digests of accepted verification records, indexed by record kind.
        /// </summary>
        public Dictionary<string, string> RecordDigests { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets authenticated independent CodeQL reviews indexed by review identifier.
        /// </summary>
        public Dictionary<string, VerifiedCodeqlReview> CodeqlReviews { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the local root used to resolve preserved verification-bundle inputs.
        /// </summary>
        public string? BundleRoot { get; internal set; }

        /// <summary>
        /// Gets or sets the authenticated native NuGet package and producer-index proof result.
        /// </summary>
        public VerifiedNativeNuget? NativeNuget { get; internal set; }

        /// <summary>
        /// Checks whether a record kind is authenticated, including native NuGet proofs as producer coverage.
        /// </summary>
        public bool Has(string kind)
        {
            return Records.ContainsKey(kind) || (kind == "producer" && NativeNuget != null);
        }
    }

    /// <summary>
    /// Provides source-generated JSON metadata for trusted policy, verification records, and approved delivery reports.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true)]
    [JsonSerializable(typeof(VerificationBundle))]
    [JsonSerializable(typeof(VerificationRecord))]
    [JsonSerializable(typeof(CodeqlReviewRecord))]
    [JsonSerializable(typeof(CodeqlReviewProjection))]
    [JsonSerializable(typeof(ProducerAssessment))]
    [JsonSerializable(typeof(ApprovedNugetDeliveryReport))]
    [JsonSerializable(typeof(TrustedPolicySnapshot))]
    [JsonSerializable(typeof(ArtifactRecord[]))]
    [JsonSerializable(typeof(JobRecord[]))]
    [JsonSerializable(typeof(ProducerRecord))]
    internal sealed partial class VerificationJsonContext : JsonSerializerContext;
}
