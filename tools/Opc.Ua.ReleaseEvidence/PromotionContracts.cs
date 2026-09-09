// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class PromotionRejectedException : Exception
    {
        public PromotionRejectedException()
            : base("Promotion request was rejected.")
        {
        }

        public PromotionRejectedException(string message)
            : base(message)
        {
        }

        public PromotionRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed record PromotionFile(string Path, string Digest);

    internal sealed record PromotionAlias(string Name, bool Immutable, string? ExpectedDigest = null);

    internal sealed record PromotionMember(
        string Id,
        string Version,
        string Destination,
        PromotionFile Content,
        PromotionFile[] Evidence,
        string? Alias = null,
        string? ExpectedAliasDigest = null,
        string Kind = "nuget-package",
        string? Platform = null,
        PromotionAlias[]? Aliases = null);

    internal sealed record PromotionRequest(
        int SchemaVersion,
        string Group,
        string Destination,
        string CandidateDigest,
        string EvidenceDigest,
        string IntentDigest,
        string PolicyDigest,
        string SourceSha,
        string RunId,
        int Attempt,
        PromotionMember[] Members);

    /// <summary>
    /// Immutable content readback is independent of alias targets. Official OCI evidence includes required referrer
    /// subject, manifest and artifact-type discovery, not merely the presence of blob hashes.
    /// </summary>
    internal sealed record PromotionObservation(
        string? ContentDigest,
        string[] EvidenceDigests,
        string? AliasDigest);

    internal sealed record PromotionEvent(
        int SchemaVersion,
        string EventId,
        string RequestDigest,
        string CandidateDigest,
        string EvidenceDigest,
        string IntentDigest,
        string PolicyDigest,
        string SourceSha,
        string RunId,
        int Attempt,
        string Group,
        string Destination,
        string Member,
        string Operation,
        string ContentDigest,
        string[] EvidenceDigests,
        string LeaseFence,
        string ObservedAt,
        string Kind = "nuget-package",
        string? Platform = null,
        string? Alias = null,
        string? ExpectedAliasDigest = null,
        bool? ImmutableAlias = null);

    internal sealed record PromotionResult(
        int SchemaVersion,
        string Status,
        string RequestDigest,
        int VerifiedMembers,
        bool OfficialDelivery);

    internal sealed record PromotionAssessment(
        int SchemaVersion,
        string Status,
        string RequestDigest,
        string CandidateDigest,
        string EvidenceDigest,
        string IntentDigest,
        string PolicyDigest);

    internal interface IPromotionLease : IAsyncDisposable
    {
        string Fence { get; }

        ValueTask AssertHeldAsync(CancellationToken cancellationToken);
    }

    internal interface IPromotionTransport
    {
        bool IsOfficial { get; }

        Task<IPromotionLease> AcquireLeaseAsync(
            string group,
            string destination,
            CancellationToken cancellationToken);

        Task<PromotionObservation> ReadAsync(
            PromotionMember member,
            CancellationToken cancellationToken);

        Task CreateImmutableAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken);

        Task RestoreEvidenceAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken);

        Task CompareExchangeAliasAsync(
            PromotionMember member,
            string? expectedDigest,
            IPromotionLease lease,
            CancellationToken cancellationToken);
    }

    internal interface IPromotionJournal
    {
        Task AppendAsync(PromotionEvent entry, CancellationToken cancellationToken);
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
    [JsonSerializable(typeof(PromotionRequest))]
    [JsonSerializable(typeof(PromotionEvent))]
    [JsonSerializable(typeof(PromotionResult))]
    [JsonSerializable(typeof(PromotionAssessment))]
    internal sealed partial class PromotionJsonContext : JsonSerializerContext;
}
