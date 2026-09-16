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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Contains the policy reference and handoff records submitted for structural readiness validation.
    /// </summary>
    internal sealed record ReadinessProgress
    {
        /// <summary>
        /// Gets the version of the readiness-progress JSON contract.
        /// </summary>
        public required int SchemaVersion { get; init; }

        /// <summary>
        /// Gets the discriminator identifying the readiness-progress document.
        /// </summary>
        public required string Kind { get; init; }

        /// <summary>
        /// Gets the repository-relative policy path used to determine required readiness checks.
        /// </summary>
        public required string PolicyPath { get; init; }

        /// <summary>
        /// Gets the graduation and stewardship handoff records to validate.
        /// </summary>
        public required List<ReadinessItem> Records { get; init; }
    }

    /// <summary>
    /// Describes a scoped readiness handoff, its responsible function, supporting records, and revalidation interval.
    /// </summary>
    internal sealed record ReadinessItem
    {
        /// <summary>
        /// Gets the identifier of the required graduation or stewardship check.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Gets the category used to match this handoff to the required readiness checks.
        /// </summary>
        public required string Category { get; init; }

        /// <summary>
        /// Gets the declared handoff status before structural and freshness validation.
        /// </summary>
        public required string Status { get; init; }

        /// <summary>
        /// Gets the organizational function responsible for the readiness handoff.
        /// </summary>
        public required string ResponsibleFunction { get; init; }

        /// <summary>
        /// Gets the artifact groups covered by this readiness record.
        /// </summary>
        public required List<string> Groups { get; init; }

        /// <summary>
        /// Gets the publication destinations covered by this readiness record.
        /// </summary>
        public required List<string> Destinations { get; init; }

        /// <summary>
        /// Gets the limitations declared for the readiness scope.
        /// </summary>
        public required List<string> Limitations { get; init; }

        /// <summary>
        /// Gets the changes or events that require renewed review of this handoff.
        /// </summary>
        public required List<string> RevalidationTriggers { get; init; }

        /// <summary>
        /// Gets the digest of the policy against which the handoff was reviewed.
        /// </summary>
        public string? PolicyDigest { get; init; }

        /// <summary>
        /// Gets the controller revision associated with the readiness review.
        /// </summary>
        public string? ControllerSha { get; init; }

        /// <summary>
        /// Gets the reference to evidence supporting the handoff.
        /// </summary>
        public string? EvidenceRecord { get; init; }

        /// <summary>
        /// Gets the reference to the claimed approval record, which still requires authentication.
        /// </summary>
        public string? ApprovalRecord { get; init; }

        /// <summary>
        /// Gets the reference establishing the reviewer's authority.
        /// </summary>
        public string? ReviewerAuthorityRecord { get; init; }

        /// <summary>
        /// Gets the reference assigning the primary responsible function or role.
        /// </summary>
        public string? PrimaryAssignmentRecord { get; init; }

        /// <summary>
        /// Gets the reference assigning the deputy responsible function or role.
        /// </summary>
        public string? DeputyAssignmentRecord { get; init; }

        /// <summary>
        /// Gets the declared review timestamp with an explicit ISO 8601 offset.
        /// </summary>
        public string? ReviewedAt { get; init; }

        /// <summary>
        /// Gets the timestamp after which the submitted handoff must be reviewed again.
        /// </summary>
        public string? RevalidateAfter { get; init; }
    }

    /// <summary>
    /// Reports the validated state of one required readiness handoff.
    /// </summary>
    /// <param name="Id">The identifier of the required graduation or stewardship handoff.</param>
    /// <param name="State">The state assigned to the readiness handoff after validation.</param>
    internal sealed record ReadinessCheck(string Id, string State);

    /// <summary>
    /// Reports structural readiness validation separately from operational approval.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">The overall readiness state, kept separate from authenticated operational approval.</param>
    /// <param name="StructurallyValid">
    /// Whether the readiness document satisfies its schema and required handoff structure.
    /// </param>
    /// <param name="OperationalApprovalVerified">
    /// Whether independent operational approval was authenticated rather than merely declared.
    /// </param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="Records">The validated states of the required readiness handoffs.</param>
    internal sealed record ReadinessValidation(
        int SchemaVersion,
        string Status,
        bool StructurallyValid,
        bool OperationalApprovalVerified,
        string PolicyDigest,
        List<ReadinessCheck> Records);

    /// <summary>
    /// Defines a synthetic stewardship reporting scenario and its explicit scope facts and time assumptions.
    /// </summary>
    internal sealed record TabletopRequest
    {
        /// <summary>
        /// Gets the version of the synthetic tabletop request contract.
        /// </summary>
        public required int SchemaVersion { get; init; }

        /// <summary>
        /// Gets whether the request is explicitly synthetic, as required for tabletop calculation.
        /// </summary>
        public required bool Synthetic { get; init; }

        /// <summary>
        /// Gets the vulnerability, incident, voluntary, or cooperation route to exercise.
        /// </summary>
        public required string Route { get; init; }

        /// <summary>
        /// Gets the time zone used for calendar-based reporting calculations.
        /// </summary>
        public required string CalendarTimeZone { get; init; }

        /// <summary>
        /// Gets the yes, no, or unknown assessment of development involvement for vulnerability scope.
        /// </summary>
        public string DevelopmentInvolvement { get; init; } = "unknown";

        /// <summary>
        /// Gets the yes, no, or unknown assessment of active exploitation in the synthetic vulnerability scenario.
        /// </summary>
        public string ActivelyExploited { get; init; } = "unknown";

        /// <summary>
        /// Gets whether the incident scenario concerns a provided development system, or whether that fact is unknown.
        /// </summary>
        public string ProvidedDevelopmentSystem { get; init; } = "unknown";

        /// <summary>
        /// Gets the yes, no, or unknown assessment of product-security impact for incident scope.
        /// </summary>
        public string ProductSecurityImpact { get; init; } = "unknown";

        /// <summary>
        /// Gets the explicit-offset awareness timestamp used as the reporting-clock origin.
        /// </summary>
        public string? Awareness { get; init; }

        /// <summary>
        /// Gets the optional remedy-availability timestamp used for the vulnerability final-report calculation.
        /// </summary>
        public string? RemedyAvailable { get; init; }

        /// <summary>
        /// Gets the optional incident-notification timestamp used for the calendar-month final-report calculation.
        /// </summary>
        public string? IncidentNotification { get; init; }
    }

    /// <summary>
    /// Reports synthetic scope and reporting-clock calculations that still require Foundation review.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Synthetic">
    /// Whether the scenario is explicitly synthetic rather than an operational exercise.
    /// </param>
    /// <param name="Scope">
    /// The synthetic assessment of mandatory scope or the selected voluntary or cooperation route.
    /// </param>
    /// <param name="UserInformation">
    /// The synthetic assessment of whether incident user-information obligations are triggered.
    /// </param>
    /// <param name="ClockAssumption">
    /// The elapsed-time or calendar-time assumptions applied to the synthetic reporting clocks.
    /// </param>
    /// <param name="CalendarTimeZone">
    /// The explicit time-zone identifier used for calendar-based reporting calculations.
    /// </param>
    /// <param name="EarlyWarning">The optional synthetic early-warning deadline with an explicit offset.</param>
    /// <param name="Notification">The optional synthetic notification deadline with an explicit offset.</param>
    /// <param name="FinalReport">The optional synthetic final-report deadline with an explicit offset.</param>
    /// <param name="FoundationReviewRequired">
    /// Whether Foundation review is still required before making an operational decision.
    /// </param>
    /// <param name="OperationalExerciseCompleted">
    /// Whether a real operational exercise was completed rather than only a synthetic calculation.
    /// </param>
    /// <param name="RequiredRecords">
    /// The supporting records still required for Foundation review of the synthetic scenario.
    /// </param>
    internal sealed record TabletopResult(
        int SchemaVersion,
        bool Synthetic,
        string Scope,
        string UserInformation,
        string ClockAssumption,
        string CalendarTimeZone,
        string? EarlyWarning,
        string? Notification,
        string? FinalReport,
        bool FoundationReviewRequired,
        bool OperationalExerciseCompleted,
        List<string> RequiredRecords);

    /// <summary>
    /// Provides source-generated JSON metadata for readiness handoffs and synthetic tabletop requests and results.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true)]
    [JsonSerializable(typeof(ReadinessProgress))]
    [JsonSerializable(typeof(ReadinessValidation))]
    [JsonSerializable(typeof(TabletopRequest))]
    [JsonSerializable(typeof(TabletopResult))]
    internal sealed partial class ReadinessJsonContext : JsonSerializerContext;
}
