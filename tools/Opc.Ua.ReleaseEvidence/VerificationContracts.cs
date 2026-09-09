// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record VerificationProof(string AuthorityId, string RecordPath, string BundlePath);

    internal sealed record VerificationBundle(
        int SchemaVersion, VerificationProof[] Proofs, string? OciRequestPath = null,
        VerificationProof[]? CodeqlReviews = null, NativeNugetProof[]? NativeNuget = null);

    internal sealed record NativeNugetProof(
        string Role, string AuthorityId, string BundlePath, string Digest, string? IndexPath = null);

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

    internal sealed record VerificationScope(
        string[] Groups, string[] Destinations, string[] RecordReferences, string[] Limitations);

    internal sealed record ArtifactSignatureProof(
        string Kind, string Id, string ArtifactDigest, string BundlePath, string SignatureDigest, string SignerDigest,
        string? AuthorityId = null);

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

    internal sealed record VerificationAuthority(
        string Id,
        string Repository,
        string Issuer,
        string CertificateIdentity,
        string Workflow,
        string DefinitionSha,
        string Ref,
        string[] RecordKinds);

    internal sealed record VerifierToolPin(string Path, string Digest, string Version);

    internal sealed record ProducerPin(
        string System, string Workflow, string DefinitionSha, string[] Jobs, ToolRecord[] Tools);

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

    internal interface ITrustPolicySource
    {
        Task<TrustedPolicySnapshot?> LoadAsync(
            string? path, string[] candidateRoots, CancellationToken cancellationToken);
    }

    internal interface IRecordSignatureVerifier
    {
        Task<bool> VerifyAsync(
            string recordPath,
            string bundlePath,
            VerificationAuthority authority,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken);
    }

    internal interface IArtifactSignatureVerifier
    {
        Task<bool> VerifyAsync(
            string artifactPath, ArtifactSignatureProof proof, string bundleRoot,
            TrustedPolicySnapshot policy, CancellationToken cancellationToken);
    }

    internal interface IStatementSignatureVerifier
    {
        Task<JsonDocument?> VerifyAsync(
            string subjectPath,
            string bundlePath,
            string predicateType,
            VerificationAuthority authority,
            TrustedPolicySnapshot policy,
            CancellationToken cancellationToken);
    }

    internal sealed class VerifiedClaims
    {
        internal VerifiedClaims(
            TrustedPolicySnapshot? policy,
            Dictionary<string, VerificationRecord> records,
            List<Finding> findings)
        {
            Policy = policy;
            Records = records;
            Findings = findings;
        }

        public TrustedPolicySnapshot? Policy { get; }

        public Dictionary<string, VerificationRecord> Records { get; }

        public List<Finding> Findings { get; }

        public Dictionary<string, string> RecordDigests { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, VerifiedCodeqlReview> CodeqlReviews { get; } = new(StringComparer.Ordinal);

        public string? BundleRoot { get; internal set; }

        public VerifiedNativeNuget? NativeNuget { get; internal set; }

        public bool Has(string kind)
        {
            return Records.ContainsKey(kind) || (kind == "producer" && NativeNuget != null);
        }
    }

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
