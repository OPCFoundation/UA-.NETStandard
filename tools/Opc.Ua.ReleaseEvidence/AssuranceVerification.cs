// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record NativeImageProof(
        string Format, string Machine, string NativeAotHeader, int NativeAotHeaderMajor,
        int NativeAotHeaderMinor, string Digest, long Size);

    internal sealed record NativeRuntimeProof(
        string Framework, string Architecture, bool IsDynamicCodeSupported,
        bool IsDynamicCodeCompiled, string ReportPhase);

    internal sealed record NativeToolProof(string SdkVersion, string CompilerVersion, string CompilerDigest);

    internal sealed record NativeAssuranceProof(
        string Project, string Configuration, string HostTfm, string LibraryTfm,
        string RuntimeIdentifier, bool PublishAot,
        string SourceSha, string RunId, int Attempt, string Nonce, int ProcessId, int ExitCode,
        string ProducedDigest, string LaunchedDigest, string ObservedDigest, string PostRunDigest,
        DateTimeOffset StartedAt, DateTimeOffset CompletedAt, NativeImageProof Image,
        NativeRuntimeProof Runtime, NativeToolProof Tools);

    internal sealed record CodeqlToolProof(string Name, string Version, string Digest);

    internal sealed record CodeqlProjectProof(
        string ProjectDigest, int ExpectedSources, int ExtractedSources,
        string ExpectedSourceDigest, string ExtractedSourceDigest, int ExtractionErrors);

    internal sealed record ReviewReference(string Id, string Digest);

    internal sealed record CodeqlAssuranceProof(
        string Status, string SourceSha, string SourceRef, string RunId, int Attempt, string AnalysisId,
        string SarifId, string Category, CodeqlToolProof Tool, CodeqlProjectProof[] Projects,
        int ExpectedQueries, int CompletedQueries, int Findings, int DistinctAlerts, string Disposition,
        string DatabaseDigest, string ExtractionDigest, string ConfigurationDigest, string SuiteDigest,
        string PackDigest, string QueryDigest, string QueryResultsDigest, string BuildDigest, string PopulationDigest,
        ReviewReference? Review = null);

    internal sealed record CodeqlDisposition(
        string SourceSha, string RunId, int Attempt, string AnalysisId, string QueryDigest, string PopulationDigest,
        int ReviewedOccurrences, int ReviewedAlerts, int UnresolvedFindings);

    internal static class AssuranceVerification
    {
        public static bool NativeComplete(CollectedResultSummary result, JobRecord job, DateTimeOffset now)
        {
            NativeAssuranceProof? native = result.Native;
            return native != null && result.Kind == "native-aot" && result.Status == "completed" &&
                native.Project == job.Project && native.Configuration == job.Configuration &&
                native.HostTfm == job.HostTfm && native.LibraryTfm == job.LibraryTfm &&
                native.RuntimeIdentifier == "win-x64" && native.PublishAot &&
                native.SourceSha == job.SourceSha && native.RunId == job.Producer?.RunId &&
                native.Attempt == job.Producer?.Attempt &&
                native.Nonce.Length == 32 && native.Nonce.All(c => char.IsAsciiHexDigitLower(c)) &&
                native.ProcessId > 0 && native.ExitCode == 0 &&
                VerificationControls.IsDigest(native.ProducedDigest) &&
                native.ProducedDigest == native.LaunchedDigest && native.ProducedDigest == native.ObservedDigest &&
                native.ProducedDigest == native.PostRunDigest && native.ProducedDigest == native.Image.Digest &&
                native.Image is
                {
                    Format: "pe32plus", Machine: "amd64", NativeAotHeader: "DNDH",
                    NativeAotHeaderMajor: 5, NativeAotHeaderMinor: 0, Size: >= 512
                } &&
                native.Runtime is
                {
                    Architecture: "x64", IsDynamicCodeSupported: false, IsDynamicCodeCompiled: false,
                    ReportPhase: "completed"
                } &&
                native.Runtime.Framework.StartsWith(".NET 10.", StringComparison.Ordinal) &&
                native.Tools.SdkVersion.StartsWith("10.", StringComparison.Ordinal) &&
                native.Tools.CompilerVersion.StartsWith("10.", StringComparison.Ordinal) &&
                VerificationControls.IsDigest(native.Tools.CompilerDigest) &&
                job.Producer!.Tools.Any(t => t.Id == "ilc" &&
                    t.Version == native.Tools.CompilerVersion && t.Digest == native.Tools.CompilerDigest) &&
                job.Producer.Tools.Any(t => t.Id == "dotnet" && t.Version == native.Tools.SdkVersion) &&
                native.CompletedAt > native.StartedAt && native.CompletedAt <= now &&
                native.CompletedAt - native.StartedAt <= TimeSpan.FromHours(1);
        }

        public static bool CodeqlComplete(CollectedResultSummary result, JobRecord job, VerifiedClaims verified)
        {
            CodeqlAssuranceProof? analysis = result.Analysis;
            if (analysis == null || result.Kind != "codeql" || analysis.Status != "completed" ||
                analysis.SourceSha != job.SourceSha || analysis.RunId != job.Producer?.RunId ||
                analysis.Attempt != job.Producer?.Attempt ||
                !(analysis.SourceRef == "refs/heads/master" ||
                    analysis.SourceRef.StartsWith("refs/heads/release/2.", StringComparison.Ordinal)) ||
                analysis.AnalysisId.Length == 0 || analysis.AnalysisId.Any(c => !char.IsAsciiDigit(c)) ||
                analysis.AnalysisId[0] == '0' || analysis.SarifId.Length == 0 ||
                analysis.Category != "assurance-csharp-net10" || analysis.Tool.Name != "CodeQL" ||
                !analysis.Tool.Version.StartsWith("2.", StringComparison.Ordinal) ||
                !VerificationControls.IsDigest(analysis.Tool.Digest) ||
                job.Producer == null || !job.Producer.Tools.Any(t => t.Id == "codeql" &&
                    t.Version == analysis.Tool.Version && t.Digest == analysis.Tool.Digest) ||
                analysis.ExpectedQueries <= 0 || analysis.ExpectedQueries != analysis.CompletedQueries ||
                analysis.Projects.Length == 0 || analysis.Projects.Length != job.Counts?.AnalyzedProjects ||
                analysis.Projects.Select(p => p.ProjectDigest).Distinct(StringComparer.Ordinal).Count() !=
                    analysis.Projects.Length ||
                analysis.Projects.Any(p => !VerificationControls.IsDigest(p.ProjectDigest) || p.ExpectedSources <= 0 ||
                    p.ExpectedSources != p.ExtractedSources || p.ExtractionErrors != 0 ||
                    !VerificationControls.IsDigest(p.ExpectedSourceDigest) ||
                    p.ExpectedSourceDigest != p.ExtractedSourceDigest) ||
                analysis.Findings < 0 || analysis.DistinctAlerts < 0 || analysis.DistinctAlerts > analysis.Findings ||
                analysis.Findings != job.Counts?.Findings ||
                new[]
                {
                    analysis.DatabaseDigest, analysis.ExtractionDigest, analysis.ConfigurationDigest,
                    analysis.SuiteDigest, analysis.PackDigest, analysis.QueryDigest, analysis.QueryResultsDigest,
                    analysis.BuildDigest, analysis.PopulationDigest
                }.Any(d => !VerificationControls.IsDigest(d)))
            {
                return false;
            }
            if (analysis.Findings == 0)
            {
                return analysis.DistinctAlerts == 0 && analysis.Disposition == "clean-analysis";
            }
            return analysis.Disposition == "authenticated-review" && analysis.Review != null &&
                verified.CodeqlReviews.TryGetValue(analysis.Review.Id, out VerifiedCodeqlReview? record) &&
                record.Digest == analysis.Review.Digest &&
                verified.Records.TryGetValue("assurance", out VerificationRecord? assurance) &&
                record.Record.Repository == assurance.Source.Repository &&
                record.Record.SourceRef == analysis.SourceRef &&
                VerificationControls.SameProducer(record.Record.Producer, job.Producer) &&
                record.Record.Review is { } review &&
                review.SourceSha == analysis.SourceSha && review.RunId == analysis.RunId &&
                review.Attempt == analysis.Attempt && review.AnalysisId == analysis.AnalysisId &&
                review.QueryDigest == analysis.QueryDigest && review.PopulationDigest == analysis.PopulationDigest &&
                review.ReviewedOccurrences == analysis.Findings && review.ReviewedAlerts == analysis.DistinctAlerts &&
                review.UnresolvedFindings == 0;
        }
    }
}
