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
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Signals that a promotion request or destination state fails a required authorization or integrity condition.
    /// </summary>
    internal sealed class PromotionRejectedException : Exception
    {
        /// <summary>
        /// Creates a rejection exception with the standard promotion-rejected message.
        /// </summary>
        public PromotionRejectedException()
            : base("Promotion request was rejected.")
        {
        }

        /// <summary>
        /// Creates a rejection exception explaining the failed promotion condition.
        /// </summary>
        public PromotionRejectedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a rejection exception that preserves the underlying cause of the failed promotion condition.
        /// </summary>
        public PromotionRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Identifies candidate-relative content that must be preserved exactly during promotion.
    /// </summary>
    /// <param name="Path">The candidate-relative path to the immutable artifact or evidence bytes.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record PromotionFile(string Path, string Digest);

    /// <summary>
    /// Describes an authorized alias mutation, its immutability, and its expected prior digest.
    /// </summary>
    /// <param name="Name">The registry alias or tag name authorized for mutation.</param>
    /// <param name="Immutable">Whether the alias must never be repointed to different artifact bytes.</param>
    /// <param name="ExpectedDigest">The expected prior alias digest; null specifies an absence precondition.</param>
    internal sealed record PromotionAlias(string Name, bool Immutable, string? ExpectedDigest = null);

    /// <summary>
    /// Binds one artifact and its required evidence to a destination and optional authorized alias operations.
    /// </summary>
    /// <param name="Id">The package or image repository identifier being promoted.</param>
    /// <param name="Version">The release version assigned to the promoted artifact.</param>
    /// <param name="Destination">The destination authorized for the promotion or delivery operation.</param>
    /// <param name="Content">The immutable artifact content to deliver.</param>
    /// <param name="Evidence">The required evidence objects that must accompany the promoted artifact.</param>
    /// <param name="Alias">The optional selected registry alias or tag.</param>
    /// <param name="ExpectedAliasDigest">
    /// The expected prior alias digest; null specifies an absence precondition.
    /// </param>
    /// <param name="Kind">The NuGet or OCI artifact kind being promoted.</param>
    /// <param name="Platform">The operating-system and architecture scope associated with the artifact or job.</param>
    /// <param name="Aliases">The optional explicit set of authorized alias operations.</param>
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

    /// <summary>
    /// Binds the exact candidate group and destination to source, producer, policy, evidence, and release intent.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Group">The release artifact group identifier.</param>
    /// <param name="Destination">The destination authorized for the promotion or delivery operation.</param>
    /// <param name="CandidateDigest">The canonical digest of the complete candidate artifact set.</param>
    /// <param name="EvidenceDigest">The digest of the exact release-evidence document.</param>
    /// <param name="IntentDigest">The digest binding the independent release intent.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="Members">
    /// The exact artifacts, evidence objects, and alias operations authorized for promotion.
    /// </param>
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
    /// <param name="ContentDigest">
    /// The immutable artifact digest currently read from the destination, or null if absent.
    /// </param>
    /// <param name="EvidenceDigests">The digests of evidence objects discovered at the destination.</param>
    /// <param name="AliasDigest">
    /// The digest currently referenced by the selected alias, or null when it is absent.
    /// </param>
    internal sealed record PromotionObservation(
        string? ContentDigest,
        string[] EvidenceDigests,
        string? AliasDigest);

    /// <summary>
    /// Records a promotion operation, its content readback, authorization identities, and lease fence.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="EventId">The unique identifier of this promotion journal event.</param>
    /// <param name="RequestDigest">The digest of the exact promotion request.</param>
    /// <param name="CandidateDigest">The canonical digest of the complete candidate artifact set.</param>
    /// <param name="EvidenceDigest">The digest of the exact release-evidence document.</param>
    /// <param name="IntentDigest">The digest binding the independent release intent.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="Group">The release artifact group identifier.</param>
    /// <param name="Destination">The destination authorized for the promotion or delivery operation.</param>
    /// <param name="Member">The artifact identifier affected by the promotion operation.</param>
    /// <param name="Operation">The promotion mutation or readback operation being recorded.</param>
    /// <param name="ContentDigest">
    /// The observed content digest, or the approved digest recorded before initial creation.
    /// </param>
    /// <param name="EvidenceDigests">The digests of evidence objects discovered at the destination.</param>
    /// <param name="LeaseFence">The lease ownership marker held when the promotion event was recorded.</param>
    /// <param name="ObservedAt">
    /// The explicit-offset timestamp at which the promotion operation or readback was recorded.
    /// </param>
    /// <param name="Kind">The artifact kind affected by the promotion operation.</param>
    /// <param name="Platform">The operating-system and architecture scope associated with the artifact or job.</param>
    /// <param name="Alias">The optional selected registry alias or tag.</param>
    /// <param name="ExpectedAliasDigest">
    /// The expected prior alias digest; null specifies an absence precondition.
    /// </param>
    /// <param name="ImmutableAlias">Whether the affected alias is immutable, when this event concerns an alias.</param>
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

    /// <summary>
    /// Summarizes completed promotion readback and distinguishes official delivery from an offline exercise.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">The completion state of the verified promotion operation.</param>
    /// <param name="RequestDigest">The digest of the exact promotion request.</param>
    /// <param name="VerifiedMembers">
    /// The number of promoted members whose destination content and evidence were verified.
    /// </param>
    /// <param name="OfficialDelivery">Whether the completed delivery used an official transport.</param>
    internal sealed record PromotionResult(
        int SchemaVersion,
        string Status,
        string RequestDigest,
        int VerifiedMembers,
        bool OfficialDelivery);

    /// <summary>
    /// Binds a read-only eligibility assessment to the exact promotion request and its authorization digests.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">The eligibility state assigned by read-only promotion verification.</param>
    /// <param name="RequestDigest">The digest of the exact promotion request.</param>
    /// <param name="CandidateDigest">The canonical digest of the complete candidate artifact set.</param>
    /// <param name="EvidenceDigest">The digest of the exact release-evidence document.</param>
    /// <param name="IntentDigest">The digest binding the independent release intent.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    internal sealed record PromotionAssessment(
        int SchemaVersion,
        string Status,
        string RequestDigest,
        string CandidateDigest,
        string EvidenceDigest,
        string IntentDigest,
        string PolicyDigest);

    /// <summary>
    /// Represents a destination-scoped promotion lease whose continued ownership must be checked before mutations.
    /// </summary>
    internal interface IPromotionLease : IAsyncDisposable
    {
        /// <summary>
        /// Gets the lease ownership marker recorded with promotion journal events.
        /// </summary>
        string Fence { get; }

        /// <summary>
        /// Checks cancellation and rejects an expired, lost, or disposed promotion lease.
        /// </summary>
        ValueTask AssertHeldAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides leased immutable delivery, evidence restoration, destination readback, and conditional alias updates.
    /// </summary>
    internal interface IPromotionTransport
    {
        /// <summary>
        /// Gets whether this transport represents official delivery rather than an offline fixture.
        /// </summary>
        bool IsOfficial { get; }

        /// <summary>
        /// Acquires exclusive promotion ownership for the selected artifact group and destination.
        /// </summary>
        Task<IPromotionLease> AcquireLeaseAsync(
            string group,
            string destination,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reads current immutable content, discoverable evidence, and the selected alias target from the destination.
        /// </summary>
        Task<PromotionObservation> ReadAsync(
            PromotionMember member,
            CancellationToken cancellationToken);

        /// <summary>
        /// Creates the exact approved artifact content under the supplied lease without overwriting conflicting bytes.
        /// </summary>
        Task CreateImmutableAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken);

        /// <summary>
        /// Restores the member's required evidence from immutable candidate inputs while holding the supplied lease.
        /// </summary>
        Task RestoreEvidenceAsync(
            PromotionMember member,
            string candidateRoot,
            IPromotionLease lease,
            CancellationToken cancellationToken);

        /// <summary>
        /// Updates the selected alias only when its current digest satisfies the approved precondition.
        /// </summary>
        Task CompareExchangeAliasAsync(
            PromotionMember member,
            string? expectedDigest,
            IPromotionLease lease,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Persists attributable promotion events without treating journal entries as destination readback.
    /// </summary>
    internal interface IPromotionJournal
    {
        /// <summary>
        /// Appends an event describing a promotion operation and its bound authorization and lease identities.
        /// </summary>
        Task AppendAsync(PromotionEvent entry, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides source-generated JSON metadata for promotion requests, events, results, and eligibility assessments.
    /// </summary>
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
