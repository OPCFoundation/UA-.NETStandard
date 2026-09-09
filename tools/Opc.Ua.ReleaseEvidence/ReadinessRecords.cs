// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record ReadinessProgress
    {
        public required int SchemaVersion { get; init; }
        public required string Kind { get; init; }
        public required string PolicyPath { get; init; }
        public required List<ReadinessItem> Records { get; init; }
    }

    internal sealed record ReadinessItem
    {
        public required string Id { get; init; }
        public required string Category { get; init; }
        public required string Status { get; init; }
        public required string ResponsibleFunction { get; init; }
        public required List<string> Groups { get; init; }
        public required List<string> Destinations { get; init; }
        public required List<string> Limitations { get; init; }
        public required List<string> RevalidationTriggers { get; init; }
        public string? PolicyDigest { get; init; }
        public string? ControllerSha { get; init; }
        public string? EvidenceRecord { get; init; }
        public string? ApprovalRecord { get; init; }
        public string? ReviewerAuthorityRecord { get; init; }
        public string? PrimaryAssignmentRecord { get; init; }
        public string? DeputyAssignmentRecord { get; init; }
        public string? ReviewedAt { get; init; }
        public string? RevalidateAfter { get; init; }
    }

    internal sealed record ReadinessCheck(string Id, string State);

    internal sealed record ReadinessValidation(
        int SchemaVersion,
        string Status,
        bool StructurallyValid,
        bool OperationalApprovalVerified,
        string PolicyDigest,
        List<ReadinessCheck> Records);

    internal sealed record TabletopRequest
    {
        public required int SchemaVersion { get; init; }
        public required bool Synthetic { get; init; }
        public required string Route { get; init; }
        public required string CalendarTimeZone { get; init; }
        public string DevelopmentInvolvement { get; init; } = "unknown";
        public string ActivelyExploited { get; init; } = "unknown";
        public string ProvidedDevelopmentSystem { get; init; } = "unknown";
        public string ProductSecurityImpact { get; init; } = "unknown";
        public string? Awareness { get; init; }
        public string? RemedyAvailable { get; init; }
        public string? IncidentNotification { get; init; }
    }

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
