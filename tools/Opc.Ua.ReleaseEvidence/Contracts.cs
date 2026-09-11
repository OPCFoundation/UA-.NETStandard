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
    /// Identifies the actual checkout and distinguishes it from event, pull-request, and synthetic-merge revisions.
    /// </summary>
    /// <param name="Repository">The source repository identified as owner and repository name.</param>
    /// <param name="ActualSha">The commit hash of the checkout actually consumed by the producer.</param>
    /// <param name="ActualRef">The fully qualified ref of the checkout actually consumed by the producer.</param>
    /// <param name="TrackedClean">Whether tracked files in the actual checkout were unmodified when captured.</param>
    /// <param name="EventSha">The optional commit hash supplied by the triggering event.</param>
    /// <param name="PullRequestHeadSha">
    /// The optional source commit of the pull request, distinct from a synthetic merge.
    /// </param>
    /// <param name="SyntheticMergeSha">The optional synthetic merge commit tested for a pull request.</param>
    internal sealed record SourceRecord(
        string Repository,
        string ActualSha,
        string ActualRef,
        bool TrackedClean,
        string? EventSha = null,
        string? PullRequestHeadSha = null,
        string? SyntheticMergeSha = null);

    /// <summary>
    /// Identifies a producer tool by name, version, and optional content digest.
    /// </summary>
    /// <param name="Id">The tool identifier used to match producer policy pins.</param>
    /// <param name="Version">The declared version of the producer tool.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record ToolRecord(string Id, string Version, string? Digest = null);

    /// <summary>
    /// Identifies the workflow definition, run attempt, job, and tools that produced evidence.
    /// </summary>
    /// <param name="System">The producer system, such as GitHub Actions or Azure Pipelines.</param>
    /// <param name="Workflow">The repository-relative workflow or pipeline definition path.</param>
    /// <param name="DefinitionSha">The exact source revision of the producer or signer workflow definition.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="Job">The producer job identifier.</param>
    /// <param name="Tools">The producer tool identities and versions recorded for the operation.</param>
    /// <param name="StartedAt">The time at which the producer operation started.</param>
    /// <param name="CompletedAt">The time at which the producer operation completed.</param>
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

    /// <summary>
    /// Identifies the artifact group, version, and delivery channel of a release.
    /// </summary>
    /// <param name="Group">The release artifact group identifier.</param>
    /// <param name="Version">The NuGet-compatible release version assigned to the artifact group.</param>
    /// <param name="Channel">The stable, preview, or development release channel.</param>
    internal sealed record ReleaseRecord(string Group, string Version, string Channel);

    /// <summary>
    /// Identifies the policy version, exact policy bytes, and enforcement stage applied to evidence.
    /// </summary>
    /// <param name="Id">The identifier of the release policy applied to the evidence.</param>
    /// <param name="Version">The version of the release policy applied to the evidence.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Stage">The policy enforcement stage, such as pilot or required.</param>
    internal sealed record PolicyRecord(string Id, string Version, string Digest, string Stage);

    /// <summary>
    /// Describes the target-framework, runtime, Roslyn, and platform scopes covered by an artifact.
    /// </summary>
    /// <param name="Tfms">The target framework monikers covered by the artifact or evaluated project.</param>
    /// <param name="Rids">The runtime identifiers covered by the artifact.</param>
    /// <param name="Roslyn">The Roslyn API compatibility bands covered by the artifact.</param>
    /// <param name="Platforms">
    /// The operating-system and architecture combinations covered by the artifact group or scope.
    /// </param>
    internal sealed record ScopeRecord(string[] Tfms, string[] Rids, string[] Roslyn, string[] Platforms);

    /// <summary>
    /// Identifies an artifact's release identity, immutable bytes, build configuration, and target scopes.
    /// </summary>
    /// <param name="Kind">
    /// The artifact kind, such as a NuGet package, symbol package, OCI index, or OCI manifest.
    /// </param>
    /// <param name="Id">The package or image repository identifier.</param>
    /// <param name="Version">The release version associated with the artifact.</param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Scopes">
    /// The target frameworks, runtimes, Roslyn bands, and platforms covered by the artifact.
    /// </param>
    /// <param name="Size">The length of the referenced content in bytes.</param>
    internal sealed record ArtifactRecord(
        string Kind, string Id, string Version, string Configuration, string Digest,
        ScopeRecord Scopes, long? Size = null);

    /// <summary>
    /// Identifies the content-addressed subject described by an evidence document.
    /// </summary>
    /// <param name="Kind">The kind of content-addressed subject described by the document.</param>
    /// <param name="Id">The source, artifact, or artifact-set identifier described by the document.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record SubjectRecord(string Kind, string Id, string Digest);

    /// <summary>
    /// Describes a versioned evidence document, its bundle-relative location, and its bound subject.
    /// </summary>
    /// <param name="Type">The role of the document, such as inventory, SBOM, policy, or producer record.</param>
    /// <param name="Format">The document serialization or interchange format, such as JSON or CycloneDX.</param>
    /// <param name="Version">The version of the document's format or contract.</param>
    /// <param name="Path">The bundle-relative path to the evidence document.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Subject">The content-addressed subject described by the evidence document.</param>
    internal sealed record DocumentRecord(
        string Type, string Format, string Version, string Path, string Digest, SubjectRecord Subject);

    /// <summary>
    /// Identifies an assurance input and the exact file bytes selected for execution.
    /// </summary>
    /// <param name="Id">The assurance input identifier referenced by selected jobs.</param>
    /// <param name="Kind">The assurance input kind used to distinguish its role in execution.</param>
    /// <param name="Path">The relative path locating the referenced content.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record InputRecord(string Id, string Kind, string Path, string Digest);

    /// <summary>
    /// Collects optional execution, coverage, and finding counts reported by an assurance job.
    /// </summary>
    /// <param name="Total">The total number of tests or checks represented by the result.</param>
    /// <param name="Executed">The number of tests or checks actually executed.</param>
    /// <param name="Passed">The number of tests or checks reported as passing.</param>
    /// <param name="Failed">The number of tests, checks, or jobs reported as failed.</param>
    /// <param name="Skipped">The number of tests or checks reported as skipped.</param>
    /// <param name="ExpectedTargets">The number of assurance targets selected for execution.</param>
    /// <param name="ExecutedTargets">The number of assurance targets actually exercised.</param>
    /// <param name="ExpectedInputs">The number of assurance inputs selected for execution.</param>
    /// <param name="ExecutedInputs">The number of assurance inputs actually exercised.</param>
    /// <param name="AnalyzedProjects">The number of projects covered by the collected analysis.</param>
    /// <param name="Findings">The number of finding occurrences reported by analysis.</param>
    /// <param name="UnresolvedFindings">The number of findings that remain unresolved after review.</param>
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

    /// <summary>
    /// Describes an expected assurance job, its execution selection, input identities, and collected result.
    /// </summary>
    /// <param name="Id">The assurance job identifier within the expected workload.</param>
    /// <param name="Profile">The assurance profile that selected this job.</param>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Host">The execution host required by the assurance profile or job.</param>
    /// <param name="HostTfm">The target framework used to run the assurance test host.</param>
    /// <param name="LibraryTfm">The target framework of the library exercised by the assurance job.</param>
    /// <param name="Platform">The operating-system and architecture scope associated with the artifact or job.</param>
    /// <param name="Shard">The shard of the assurance workload represented by this job.</param>
    /// <param name="Filter">The selection filter applied to the assurance job.</param>
    /// <param name="Selected">Whether the producer selected this job for execution.</param>
    /// <param name="Status">The job result state, including missing, completed, failed, or not applicable.</param>
    /// <param name="InputIds">The identifiers of inputs selected for this assurance job.</param>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="ResultDocument">The optional relative path to the collected assurance result summary.</param>
    /// <param name="NotApplicableRule">
    /// The optional rule explaining why the job does not apply to the selected scope.
    /// </param>
    /// <param name="Counts">The optional execution and finding counts collected for the result.</param>
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

    /// <summary>
    /// Summarizes expected and completed assurance jobs together with their selected input identities.
    /// </summary>
    /// <param name="Profiles">
    /// The identifiers of the assurance profiles required for the selected artifact group.
    /// </param>
    /// <param name="Expected">The number of assurance jobs required by the selected profiles.</param>
    /// <param name="Selected">The number of assurance jobs selected for execution.</param>
    /// <param name="Completed">The number of assurance jobs with completed results.</param>
    /// <param name="Failed">The number of assurance jobs reported as failed.</param>
    /// <param name="Missing">The number of required assurance jobs without a collected result.</param>
    /// <param name="NotApplicable">The number of assurance jobs excluded by an applicable policy rule.</param>
    /// <param name="Jobs">The assurance jobs covered by this record.</param>
    /// <param name="InputIdentities">The exact input files and digests selected for assurance execution.</param>
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

    /// <summary>
    /// Records unmet controls and optionally separates pending verification from observed failures.
    /// </summary>
    /// <param name="Status">The assessment or lifecycle state reported for this record.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    /// <param name="PendingControls">
    /// The unmet controls that still require independent verification rather than describing observed failures.
    /// </param>
    /// <param name="ObservedControls">The unmet controls classified as observed failures by the producer.</param>
    internal sealed record AssessmentRecord(
        string Status,
        string[] UnmetControls,
        [property: JsonIgnore] string[]? PendingControls = null,
        [property: JsonIgnore] string[]? ObservedControls = null);

    /// <summary>
    /// Combines source, producer, release, artifact, document, and assurance evidence with its assessment.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Release">The artifact group, release version, and delivery channel covered by this record.</param>
    /// <param name="Policy">The exact policy identity and enforcement stage applied to the evidence.</param>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    /// <param name="Documents">The evidence documents covered by this record.</param>
    /// <param name="Assurance">
    /// The expected jobs, collected results, and input identities for release assurance.
    /// </param>
    /// <param name="Assessment">The control assessment associated with the collected evidence.</param>
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

    /// <summary>
    /// Carries independently supplied identities and artifact membership against which evidence is evaluated.
    /// </summary>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Release">The artifact group, release version, and delivery channel covered by this record.</param>
    /// <param name="PolicyDigest">The digest of the policy bytes to which this record is bound.</param>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    internal sealed record EvaluationExpectation(
        SourceRecord Source,
        ProducerRecord Producer,
        ReleaseRecord Release,
        string PolicyDigest,
        ArtifactRecord[] Artifacts);

    /// <summary>
    /// Associates an unmet control code with the reason it could not be satisfied.
    /// </summary>
    /// <param name="Code">The identifier of the control that is not satisfied.</param>
    /// <param name="Detail">The reason the control could not be satisfied.</param>
    internal sealed record Finding(string Code, string Detail);

    /// <summary>
    /// Records the release-evidence assessment, blocking decision, verification activity, and findings.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">Whether the evaluated evidence is complete or incomplete.</param>
    /// <param name="Stage">The policy enforcement stage, such as pilot or required.</param>
    /// <param name="Channel">The stable, preview, or development release channel.</param>
    /// <param name="Group">The release artifact group identifier.</param>
    /// <param name="Blocking">Whether the assessment prevents the release from proceeding.</param>
    /// <param name="BaselineFailed">Whether a baseline integrity or assurance check failed.</param>
    /// <param name="ExternalVerificationPerformed">
    /// Whether independently authenticated records or native proofs were obtained.
    /// </param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    /// <param name="Findings">The findings describing failed checks or unmet controls.</param>
    /// <param name="InputEvidenceDigest">The digest of the evidence document consumed by the evaluation.</param>
    /// <param name="ProducerEvidenceDigest">
    /// The digest of the immutable producer evidence retained for independent assessment.
    /// </param>
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

    /// <summary>
    /// Tracks the declared readiness status of a producer or operational control.
    /// </summary>
    /// <param name="Id">The identifier of the producer or operational readiness check.</param>
    /// <param name="Status">The declared readiness state of the identified check.</param>
    internal sealed record ReadinessRecord(string Id, string Status);

    /// <summary>
    /// Records policy graduation status and the verification records supporting it.
    /// </summary>
    /// <param name="Status">The policy's declared progress toward required-stage graduation.</param>
    /// <param name="VerificationRecords">The verification records cited as evidence for policy graduation.</param>
    internal sealed record GraduationRecord(string Status, string[] VerificationRecords);

    /// <summary>
    /// Defines release-evidence contracts, supported groups, enforcement stage, and operational readiness.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Id">The release policy identifier.</param>
    /// <param name="Version">The release policy version.</param>
    /// <param name="CurrentMajor">The release major version to which required stable-release controls apply.</param>
    /// <param name="Stage">The policy enforcement stage, such as pilot or required.</param>
    /// <param name="PublisherBoundaryVerified">
    /// Whether policy declares the publication boundary independently verified.
    /// </param>
    /// <param name="EvidenceSchemaVersion">The release-evidence schema version required by policy.</param>
    /// <param name="EvidenceSchema">The repository-relative path to the release-evidence JSON schema.</param>
    /// <param name="ArtifactCatalog">The repository-relative path to the release artifact catalog.</param>
    /// <param name="AssuranceProfiles">The repository-relative path to the assurance profile catalog.</param>
    /// <param name="Groups">The artifact group identifiers covered by this record.</param>
    /// <param name="ProducerReadiness">The declared readiness states of the required producers.</param>
    /// <param name="Graduation">The policy's graduation status and supporting verification references.</param>
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

    /// <summary>
    /// Identifies a job and project within an assurance profile.
    /// </summary>
    /// <param name="Id">The job identifier within the assurance profile.</param>
    /// <param name="Kind">The assurance workload kind executed by the job.</param>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    internal sealed record ProfileJob(string Id, string Kind, string Project);

    /// <summary>
    /// Defines an assurance profile's execution environment, filtering, and required jobs.
    /// </summary>
    /// <param name="Id">The assurance profile identifier used by artifact-group requirements.</param>
    /// <param name="Status">The declared availability or activation status of the assurance profile.</param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Host">The execution host required by the assurance profile or job.</param>
    /// <param name="HostTfm">The target framework used to run the assurance test host.</param>
    /// <param name="LibraryTfm">The target framework of the library exercised by the assurance job.</param>
    /// <param name="Platform">The operating-system and architecture scope associated with the artifact or job.</param>
    /// <param name="Filter">The selection filter applied to the assurance job.</param>
    /// <param name="Jobs">The assurance jobs covered by this record.</param>
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

    /// <summary>
    /// Contains the versioned catalog of assurance profile definitions.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Id">The assurance profile catalog identifier.</param>
    /// <param name="Version">The assurance profile catalog version.</param>
    /// <param name="Profiles">The complete assurance profile definitions in the catalog.</param>
    internal sealed record ProfilesConfiguration(
        int SchemaVersion, string Id, string Version, ProfileConfiguration[] Profiles);

    /// <summary>
    /// Maps an image identifier to the Dockerfile used to build it.
    /// </summary>
    /// <param name="Id">The image name within the artifact group's approved repository prefix.</param>
    /// <param name="Dockerfile">The repository-relative Dockerfile path selected for the image build.</param>
    internal sealed record ImageConfiguration(string Id, string Dockerfile);

    /// <summary>
    /// Describes an artifact variant's build configuration and optional source definitions.
    /// </summary>
    /// <param name="Id">The artifact variant identifier within its group.</param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Sources">The optional repository-relative source definitions for this artifact variant.</param>
    internal sealed record VariantConfiguration(string Id, string Configuration, string[]? Sources = null);

    /// <summary>
    /// Defines an artifact group's producer, required profiles, variants, and optional container-image scope.
    /// </summary>
    /// <param name="Id">The release artifact group identifier.</param>
    /// <param name="Producer">The producer identifier assigned to the artifact group.</param>
    /// <param name="Kind">The artifact-family discriminator, such as nuget or oci.</param>
    /// <param name="Profiles">
    /// The identifiers of the assurance profiles required for the selected artifact group.
    /// </param>
    /// <param name="ModernCatalog">
    /// The optional repository-relative catalog of modern NuGet package identifiers.
    /// </param>
    /// <param name="Variants">The optional build and packaging variants included in the artifact group.</param>
    /// <param name="UpstreamRepositoryPrefix">
    /// The optional approved repository prefix for the group's container images.
    /// </param>
    /// <param name="Platforms">
    /// The operating-system and architecture combinations covered by the artifact group or scope.
    /// </param>
    /// <param name="Images">The image identifiers and Dockerfiles required for this container group.</param>
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

    /// <summary>
    /// Contains the versioned catalog of release artifact groups.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Id">The artifact catalog identifier.</param>
    /// <param name="Version">The artifact catalog version.</param>
    /// <param name="Groups">The complete artifact-group definitions in the catalog.</param>
    internal sealed record ArtifactsConfiguration(
        int SchemaVersion, string Id, string Version, ArtifactGroup[] Groups);

    /// <summary>
    /// Specifies the source, producer, version, configuration, and projects whose build inputs must be frozen.
    /// </summary>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Version">The release version required for every evaluated packable project.</param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Projects">The repository-relative project paths selected for build-input capture.</param>
    internal sealed record CaptureRequest(
        SourceRecord Source,
        ProducerRecord Producer,
        string Version,
        string Configuration,
        string[] Projects);

    /// <summary>
    /// Identifies immutable file content by its relative path, digest, and byte length.
    /// </summary>
    /// <param name="Path">The relative path locating the referenced content.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Size">The length of the referenced content in bytes.</param>
    internal sealed record FrozenFile(string Path, string Digest, long Size);

    /// <summary>
    /// Records declared license information and an optional digest for license-file content.
    /// </summary>
    /// <param name="Kind">Whether the license is an expression, file, URL, or unknown declaration.</param>
    /// <param name="Value">The license expression, file path, or declared URL.</param>
    /// <param name="Digest">The optional digest of the license-file content.</param>
    internal sealed record LicenseRecord(string Kind, string Value, string? Digest = null);

    /// <summary>
    /// Identifies a built or restored payload candidate and its owner, source, and target compatibility.
    /// </summary>
    /// <param name="File">The filename identifying the archive or payload.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Owner">The package or assembly identity associated with the payload bytes.</param>
    /// <param name="Version">The declared version of the tool, artifact, or contract described by this record.</param>
    /// <param name="Origin">The project or restored-package source from which the payload candidate originated.</param>
    /// <param name="Target">The dependency or framework target associated with the payload.</param>
    /// <param name="Roslyn">The optional Roslyn API compatibility band reported by the evaluated project.</param>
    internal sealed record PayloadCandidate(
        string File, string Digest, string Owner, string Version, string Origin, string Target, string? Roslyn = null);

    /// <summary>
    /// Describes one resolved dependency-graph component, its dependency edges, and declared licenses.
    /// </summary>
    /// <param name="Id">The resolved package or project component identifier.</param>
    /// <param name="Version">The resolved component version.</param>
    /// <param name="Type">Whether the resolved dependency component is a package or project.</param>
    /// <param name="Target">The dependency or framework target associated with the payload.</param>
    /// <param name="Dependencies">The component's resolved dependency identifiers and version requirements.</param>
    /// <param name="Licenses">The declared licenses associated with the package or resolved component.</param>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    internal sealed record ResolvedComponent(
        string Id,
        string Version,
        string Type,
        string Target,
        Dictionary<string, string> Dependencies,
        LicenseRecord[] Licenses,
        string? Project = null);

    /// <summary>
    /// Binds evaluated package properties and target frameworks to a frozen dependency graph and payload candidates.
    /// </summary>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="PackageId">The NuGet package identifier read from evaluated settings or package metadata.</param>
    /// <param name="PackageVersion">
    /// The NuGet package version associated with the evaluated project or archived release.
    /// </param>
    /// <param name="IsPackable">Whether the evaluated project produces a NuGet package.</param>
    /// <param name="IncludeSymbols">Whether evaluated package settings request symbol-package output.</param>
    /// <param name="SymbolPackageFormat">The evaluated symbol-package format, such as snupkg.</param>
    /// <param name="Tfms">The target framework monikers covered by the artifact or evaluated project.</param>
    /// <param name="References">
    /// The repository-relative project references included in the captured build closure.
    /// </param>
    /// <param name="Assets">The frozen restored-assets file and its verified byte identity.</param>
    /// <param name="Components">The resolved dependency-graph components for the project.</param>
    /// <param name="Payloads">The archive payloads or frozen payload candidates covered by the inventory.</param>
    /// <param name="Roslyn">The Roslyn API compatibility band associated with this build or payload.</param>
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

    /// <summary>
    /// Contains the captured project mappings and contract files for one source and build configuration.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="Producer">The workflow, run, job, and tool identity that produced the evidence.</param>
    /// <param name="Version">The release version bound to the frozen project mappings.</param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="Mappings">The evaluated project and package mappings retained from the build.</param>
    /// <param name="Contracts">The immutable policy and catalog files retained with the captured build inputs.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    internal sealed record FrozenBundle(
        int SchemaVersion,
        SourceRecord Source,
        ProducerRecord Producer,
        string Version,
        string Configuration,
        ProjectMapping[] Mappings,
        FrozenFile[] Contracts,
        string[] UnmetControls);

    /// <summary>
    /// Describes a package dependency range declared for a consumer target framework.
    /// </summary>
    /// <param name="Id">The NuGet dependency package identifier.</param>
    /// <param name="Range">The dependency version range declared for package consumers.</param>
    /// <param name="Target">The consumer target framework to which the declared dependency applies.</param>
    internal sealed record ConsumerDependency(string Id, string Range, string Target);

    /// <summary>
    /// Classifies one archive entry and records its bytes, ownership, and target compatibility.
    /// </summary>
    /// <param name="Path">The relative path locating the referenced content.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Classification">
    /// The payload classification distinguishing metadata, shipped content, symbols, and unknown ownership.
    /// </param>
    /// <param name="Targets">The dependency or framework targets compatible with the payload.</param>
    /// <param name="Roslyn">The Roslyn API compatibility bands inferred for this archive payload.</param>
    /// <param name="Owner">The package or assembly identity associated with the payload bytes.</param>
    /// <param name="Version">The declared version of the tool, artifact, or contract described by this record.</param>
    internal sealed record InventoryPayload(
        string Path,
        string Digest,
        string Classification,
        string[] Targets,
        string[] Roslyn,
        string? Owner = null,
        string? Version = null);

    /// <summary>
    /// Combines package identity, licenses, dependency graphs, and reconciled archive payload coverage.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Artifact">The exact release artifact described by the inventory.</param>
    /// <param name="Licenses">The declared licenses associated with the package or resolved component.</param>
    /// <param name="ConsumerDependencies">The dependency ranges declared for package consumers.</param>
    /// <param name="ExternalPrerequisites">
    /// The external frameworks or platform prerequisites declared by the package.
    /// </param>
    /// <param name="ResolvedGraphs">
    /// The resolved dependency components retained for the package's project closure.
    /// </param>
    /// <param name="Payloads">The archive payloads or frozen payload candidates covered by the inventory.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    internal sealed record PackageInventory(
        int SchemaVersion,
        ArtifactRecord Artifact,
        LicenseRecord[] Licenses,
        ConsumerDependency[] ConsumerDependencies,
        string[] ExternalPrerequisites,
        ResolvedComponent[] ResolvedGraphs,
        InventoryPayload[] Payloads,
        string[] UnmetControls);

    /// <summary>
    /// Identifies an archive in the legacy release manifest by package identity, filename, and SHA-256 hash.
    /// </summary>
    /// <param name="Id">The embedded NuGet package identifier.</param>
    /// <param name="Version">The package version stored in the archive metadata.</param>
    /// <param name="Type">Whether the archive contains a normal package or a symbol package.</param>
    /// <param name="File">The filename identifying the archive or payload.</param>
    /// <param name="Sha256">The lowercase hexadecimal SHA-256 archive hash without an algorithm prefix.</param>
    internal sealed record ArchiveRecord(string Id, string Version, string Type, string File, string Sha256);

    /// <summary>
    /// Summarizes evaluated package output and symbol availability for a project and configuration.
    /// </summary>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="PackageId">The NuGet package identifier read from evaluated settings or package metadata.</param>
    /// <param name="Version">The evaluated package version for this project and configuration.</param>
    /// <param name="IsPackable">Whether the evaluated project produces a NuGet package.</param>
    /// <param name="IncludeSymbols">Whether evaluated package settings request symbol-package output.</param>
    /// <param name="SymbolPackageFormat">The evaluated symbol-package format, such as snupkg.</param>
    /// <param name="HasPdb">
    /// Whether evaluated output includes a project-owned PDB eligible for symbol packaging.
    /// </param>
    /// <param name="GraphDigest">The digest of the frozen restored dependency graph.</param>
    internal sealed record MappingSummary(
        string Project, string Configuration, string PackageId, string Version, bool IsPackable,
        bool IncludeSymbols, string SymbolPackageFormat, bool HasPdb, string GraphDigest);

    /// <summary>
    /// Binds final project mappings and captured producers to the frozen build-input bundles.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Source">The actual checkout identity associated with the evidence.</param>
    /// <param name="BuildInputDigests">
    /// The digests of the immutable build-input bundles used to derive these mappings.
    /// </param>
    /// <param name="Mappings">The evaluated project and package mappings retained from the build.</param>
    /// <param name="CapturedProducers">
    /// The optional producer identities retained from the frozen build-input bundles.
    /// </param>
    internal sealed record SourceInputsRecord(
        int SchemaVersion, SourceRecord Source, string[] BuildInputDigests, MappingSummary[] Mappings,
        ProducerRecord[]? CapturedProducers = null);

    /// <summary>
    /// Identifies an OCI image layout and the immutable root digest selected for evaluation.
    /// </summary>
    /// <param name="Id">The OCI image repository identifier selected for reconciliation.</param>
    /// <param name="Layout">The request-relative directory containing the downloaded OCI layout.</param>
    /// <param name="RootDigest">The immutable digest of the requested OCI image root.</param>
    internal sealed record OciImageInput(string Id, string Layout, string RootDigest);

    /// <summary>
    /// Selects the OCI images and layouts to reconcile or verify.
    /// </summary>
    /// <param name="Images">The OCI images selected for the request.</param>
    internal sealed record OciRequest(OciImageInput[] Images);

    /// <summary>
    /// Identifies a native attestation layer and its manifest, subject, predicate type, and format version.
    /// </summary>
    /// <param name="Image">The OCI image repository identifier.</param>
    /// <param name="ManifestDigest">
    /// The digest of the OCI manifest containing the referenced attestation or artifact.
    /// </param>
    /// <param name="SubjectDigest">The digest of the OCI subject described by the attestation or referrer.</param>
    /// <param name="LayerDigest">The digest of the layer containing the native attestation statement.</param>
    /// <param name="PredicateType">The URI identifying the attestation predicate contract.</param>
    /// <param name="FormatVersion">The native SPDX or provenance format version recorded for the attestation.</param>
    internal sealed record OciAttestation(
        string Image, string ManifestDigest, string SubjectDigest, string LayerDigest,
        string PredicateType, string FormatVersion);

    /// <summary>
    /// Reports reconciled OCI artifacts, native attestations, and controls that remain unmet.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Status">The completeness state of the OCI reconciliation report.</param>
    /// <param name="Group">The release artifact group identifier.</param>
    /// <param name="Artifacts">The exact artifacts and target scopes covered by this record.</param>
    /// <param name="Attestations">The native OCI attestations discovered during reconciliation.</param>
    /// <param name="UnmetControls">The controls not yet satisfied by the available evidence.</param>
    /// <param name="Findings">The findings describing failed checks or unmet controls.</param>
    internal sealed record OciReport(
        int SchemaVersion, string Status, string Group, ArtifactRecord[] Artifacts,
        OciAttestation[] Attestations, string[] UnmetControls, Finding[] Findings);

    /// <summary>
    /// Identifies the exact bytes of a result document included in an assurance summary.
    /// </summary>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record CollectedResultDocument(string Digest);

    /// <summary>
    /// Binds fuzz replay inventories and target-input execution counts to their collected digests.
    /// </summary>
    /// <param name="InventoryDigest">The digest of the frozen fuzz-input inventory.</param>
    /// <param name="TargetDigest">The digest of the selected fuzz-target inventory.</param>
    /// <param name="ExecutionDigest">The digest of the target-input replay execution record.</param>
    /// <param name="ExpectedPairs">The number of target-input pairs required for complete replay.</param>
    /// <param name="ExecutedPairs">The number of target-input pairs actually replayed.</param>
    /// <param name="AllowedEmptyRegressionSkips">
    /// The number of regression targets allowed to skip an empty input corpus.
    /// </param>
    internal sealed record ReplaySummary(
        string InventoryDigest, string TargetDigest, string ExecutionDigest,
        long ExpectedPairs, long ExecutedPairs, int AllowedEmptyRegressionSkips);

    /// <summary>
    /// Summarizes collected assurance results and optional fuzz replay, Native AOT, or CodeQL proofs.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Kind">The result kind, such as test execution, fuzz replay, Native AOT, or CodeQL analysis.</param>
    /// <param name="Status">The collection status of the assurance result.</param>
    /// <param name="Documents">The content digests of the collected raw result documents.</param>
    /// <param name="Counts">The optional execution and finding counts collected for the result.</param>
    /// <param name="Replay">The optional proof of complete target-input fuzz replay.</param>
    /// <param name="Native">The optional native executable and runtime assurance proof.</param>
    /// <param name="Analysis">
    /// The optional proof of CodeQL extraction, query execution, and finding disposition.
    /// </param>
    internal sealed record CollectedResultSummary(
        int SchemaVersion, string Kind, string Status, CollectedResultDocument[] Documents,
        CountsRecord? Counts = null, ReplaySummary? Replay = null,
        NativeAssuranceProof? Native = null, CodeqlAssuranceProof? Analysis = null);

    /// <summary>
    /// Describes the legacy release archive set and its source, producer, version, and package counts.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Repository">The source repository identified as owner and repository name.</param>
    /// <param name="Workflow">The repository-relative workflow or pipeline definition path.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Ref">The fully qualified source ref associated with this record.</param>
    /// <param name="Commit">The source commit associated with the archived release.</param>
    /// <param name="PackageVersion">
    /// The NuGet package version associated with the evaluated project or archived release.
    /// </param>
    /// <param name="PackageCount">The number of normal package archives in the release manifest.</param>
    /// <param name="SymbolPackageCount">The number of symbol-package archives in the release manifest.</param>
    /// <param name="DebugPackageCount">The number of Debug-configuration package archives in the manifest.</param>
    /// <param name="Archives">The package and symbol archives included in the release manifest.</param>
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

    /// <summary>
    /// Provides source-generated JSON metadata for release evidence, build inputs, inventories, and result summaries.
    /// </summary>
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
