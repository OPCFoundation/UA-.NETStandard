// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record SourceRecord(
        string Repository,
        string ActualSha,
        string ActualRef,
        bool TrackedClean,
        string? EventSha = null,
        string? PullRequestHeadSha = null,
        string? SyntheticMergeSha = null);

    internal sealed record ToolRecord(string Id, string Version, string? Digest = null);

    internal sealed record ProducerRecord(
        string System,
        string Workflow,
        string DefinitionSha,
        string RunId,
        int Attempt,
        string Job,
        ToolRecord[] Tools,
        string? StartedAt = null,
        string? CompletedAt = null);

    internal sealed record ReleaseRecord(string Group, string Version, string Channel);

    internal sealed record PolicyRecord(string Id, string Version, string Digest, string Stage);

    internal sealed record ScopeRecord(string[] Tfms, string[] Rids, string[] Roslyn, string[] Platforms);

    internal sealed record ArtifactRecord(
        string Kind, string Id, string Version, string Configuration, string Digest,
        ScopeRecord Scopes, long? Size = null);

    internal sealed record SubjectRecord(string Kind, string Id, string Digest);

    internal sealed record DocumentRecord(
        string Type, string Format, string Version, string Path, string Digest, SubjectRecord Subject);

    internal sealed record InputRecord(string Id, string Kind, string Path, string Digest);

    internal sealed record CountsRecord(
        int? Total = null,
        int? Executed = null,
        int? Passed = null,
        int? Failed = null,
        int? Skipped = null,
        int? ExpectedTargets = null,
        int? ExecutedTargets = null,
        int? ExpectedInputs = null,
        int? ExecutedInputs = null,
        int? AnalyzedProjects = null,
        int? Findings = null,
        int? UnresolvedFindings = null);

    internal sealed record JobRecord(
        string Id,
        string Profile,
        string Project,
        string Configuration,
        string Host,
        string HostTfm,
        string LibraryTfm,
        string Platform,
        string Shard,
        string Filter,
        bool Selected,
        string Status,
        string[] InputIds,
        string? SourceSha = null,
        ProducerRecord? Producer = null,
        string? ResultDocument = null,
        string? NotApplicableRule = null,
        CountsRecord? Counts = null);

    internal sealed record AssuranceRecord(
        string[] Profiles,
        int Expected,
        int Selected,
        int Completed,
        int Failed,
        int Missing,
        int NotApplicable,
        JobRecord[] Jobs,
        InputRecord[] InputIdentities);

    internal sealed record AssessmentRecord(
        string Status,
        string[] UnmetControls,
        [property: JsonIgnore] string[]? PendingControls = null,
        [property: JsonIgnore] string[]? ObservedControls = null);

    internal sealed record EvidenceEnvelope(
        int SchemaVersion,
        SourceRecord Source,
        ProducerRecord Producer,
        ReleaseRecord Release,
        PolicyRecord Policy,
        ArtifactRecord[] Artifacts,
        DocumentRecord[] Documents,
        AssuranceRecord Assurance,
        AssessmentRecord Assessment);

    internal sealed record EvaluationExpectation(
        SourceRecord Source,
        ProducerRecord Producer,
        ReleaseRecord Release,
        string PolicyDigest,
        ArtifactRecord[] Artifacts);

    internal sealed record Finding(string Code, string Detail);

    internal sealed record EvaluationReport(
        int SchemaVersion,
        string Status,
        string Stage,
        string Channel,
        string Group,
        bool Blocking,
        bool BaselineFailed,
        bool ExternalVerificationPerformed,
        string[] UnmetControls,
        Finding[] Findings,
        string? InputEvidenceDigest = null,
        string? ProducerEvidenceDigest = null);

    internal sealed record ReadinessRecord(string Id, string Status);

    internal sealed record GraduationRecord(string Status, string[] VerificationRecords);

    internal sealed record PolicyConfiguration(
        int SchemaVersion,
        string Id,
        string Version,
        int CurrentMajor,
        string Stage,
        bool PublisherBoundaryVerified,
        int EvidenceSchemaVersion,
        string EvidenceSchema,
        string ArtifactCatalog,
        string AssuranceProfiles,
        string[] Groups,
        ReadinessRecord[] ProducerReadiness,
        GraduationRecord Graduation);

    internal sealed record ProfileJob(string Id, string Kind, string Project);

    internal sealed record ProfileConfiguration(
        string Id,
        string Status,
        string Configuration,
        string Host,
        string HostTfm,
        string LibraryTfm,
        string Platform,
        string Filter,
        ProfileJob[] Jobs);

    internal sealed record ProfilesConfiguration(
        int SchemaVersion, string Id, string Version, ProfileConfiguration[] Profiles);

    internal sealed record ImageConfiguration(string Id, string Dockerfile);

    internal sealed record VariantConfiguration(string Id, string Configuration, string[]? Sources = null);

    internal sealed record ArtifactGroup(
        string Id,
        string Producer,
        string Kind,
        string[] Profiles,
        string? ModernCatalog = null,
        VariantConfiguration[]? Variants = null,
        string? UpstreamRepositoryPrefix = null,
        string[]? Platforms = null,
        ImageConfiguration[]? Images = null);

    internal sealed record ArtifactsConfiguration(
        int SchemaVersion, string Id, string Version, ArtifactGroup[] Groups);

    internal sealed record CaptureRequest(
        SourceRecord Source,
        ProducerRecord Producer,
        string Version,
        string Configuration,
        string[] Projects);

    internal sealed record FrozenFile(string Path, string Digest, long Size);

    internal sealed record LicenseRecord(string Kind, string Value, string? Digest = null);

    internal sealed record PayloadCandidate(
        string File, string Digest, string Owner, string Version, string Origin, string Target, string? Roslyn = null);

    internal sealed record ResolvedComponent(
        string Id,
        string Version,
        string Type,
        string Target,
        Dictionary<string, string> Dependencies,
        LicenseRecord[] Licenses,
        string? Project = null);

    internal sealed record ProjectMapping(
        string Project,
        string Configuration,
        string PackageId,
        string PackageVersion,
        bool IsPackable,
        bool IncludeSymbols,
        string SymbolPackageFormat,
        string[] Tfms,
        string[] References,
        FrozenFile Assets,
        ResolvedComponent[] Components,
        PayloadCandidate[] Payloads,
        string? Roslyn = null);

    internal sealed record FrozenBundle(
        int SchemaVersion,
        SourceRecord Source,
        ProducerRecord Producer,
        string Version,
        string Configuration,
        ProjectMapping[] Mappings,
        FrozenFile[] Contracts,
        string[] UnmetControls);

    internal sealed record ConsumerDependency(string Id, string Range, string Target);

    internal sealed record InventoryPayload(
        string Path,
        string Digest,
        string Classification,
        string[] Targets,
        string[] Roslyn,
        string? Owner = null,
        string? Version = null);

    internal sealed record PackageInventory(
        int SchemaVersion,
        ArtifactRecord Artifact,
        LicenseRecord[] Licenses,
        ConsumerDependency[] ConsumerDependencies,
        string[] ExternalPrerequisites,
        ResolvedComponent[] ResolvedGraphs,
        InventoryPayload[] Payloads,
        string[] UnmetControls);

    internal sealed record ArchiveRecord(string Id, string Version, string Type, string File, string Sha256);

    internal sealed record MappingSummary(
        string Project, string Configuration, string PackageId, string Version, bool IsPackable,
        bool IncludeSymbols, string SymbolPackageFormat, bool HasPdb, string GraphDigest);

    internal sealed record SourceInputsRecord(
        int SchemaVersion, SourceRecord Source, string[] BuildInputDigests, MappingSummary[] Mappings,
        ProducerRecord[]? CapturedProducers = null);

    internal sealed record OciImageInput(string Id, string Layout, string RootDigest);

    internal sealed record OciRequest(OciImageInput[] Images);

    internal sealed record OciAttestation(
        string Image, string ManifestDigest, string SubjectDigest, string LayerDigest,
        string PredicateType, string FormatVersion);

    internal sealed record OciReport(
        int SchemaVersion, string Status, string Group, ArtifactRecord[] Artifacts,
        OciAttestation[] Attestations, string[] UnmetControls, Finding[] Findings);

    internal sealed record CollectedResultDocument(string Digest);

    internal sealed record ReplaySummary(
        string InventoryDigest, string TargetDigest, string ExecutionDigest,
        long ExpectedPairs, long ExecutedPairs, int AllowedEmptyRegressionSkips);

    internal sealed record CollectedResultSummary(
        int SchemaVersion, string Kind, string Status, CollectedResultDocument[] Documents,
        CountsRecord? Counts = null, ReplaySummary? Replay = null,
        NativeAssuranceProof? Native = null, CodeqlAssuranceProof? Analysis = null);

    internal sealed record ArchiveManifest(
        int SchemaVersion,
        string Repository,
        string Workflow,
        string RunId,
        string Ref,
        string Commit,
        string PackageVersion,
        int PackageCount,
        int SymbolPackageCount,
        int DebugPackageCount,
        ArchiveRecord[] Archives);

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(EvidenceEnvelope))]
    [JsonSerializable(typeof(EvaluationExpectation))]
    [JsonSerializable(typeof(EvaluationReport))]
    [JsonSerializable(typeof(PolicyConfiguration))]
    [JsonSerializable(typeof(ProfilesConfiguration))]
    [JsonSerializable(typeof(ArtifactsConfiguration))]
    [JsonSerializable(typeof(CaptureRequest))]
    [JsonSerializable(typeof(FrozenBundle))]
    [JsonSerializable(typeof(PackageInventory))]
    [JsonSerializable(typeof(ArchiveManifest))]
    [JsonSerializable(typeof(ProducerRecord))]
    [JsonSerializable(typeof(SourceInputsRecord))]
    [JsonSerializable(typeof(OciRequest))]
    [JsonSerializable(typeof(OciReport))]
    [JsonSerializable(typeof(CollectedResultSummary))]
    internal sealed partial class EvidenceJsonContext : JsonSerializerContext;
}
