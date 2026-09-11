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
using System.Linq;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Describes the native executable format, Native AOT header, content digest, and size observed by assurance.
    /// </summary>
    /// <param name="Format">The native executable image format, such as pe32plus.</param>
    /// <param name="Machine">The target-machine value recorded in the native executable header.</param>
    /// <param name="NativeAotHeader">The signature identifying the Native AOT image header.</param>
    /// <param name="NativeAotHeaderMajor">The Native AOT image header's major version.</param>
    /// <param name="NativeAotHeaderMinor">The Native AOT image header's minor version.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    /// <param name="Size">The length of the referenced content in bytes.</param>
    internal sealed record NativeImageProof(
        string Format, string Machine, string NativeAotHeader, int NativeAotHeaderMajor,
        int NativeAotHeaderMinor, string Digest, long Size);

    /// <summary>
    /// Records the runtime's framework, architecture, dynamic-code capabilities, and reporting phase.
    /// </summary>
    /// <param name="Framework">The framework description reported by the running native executable.</param>
    /// <param name="Architecture">The processor architecture reported by the running executable.</param>
    /// <param name="IsDynamicCodeSupported">Whether the running executable reports support for dynamic code.</param>
    /// <param name="IsDynamicCodeCompiled">
    /// Whether the running executable reports that dynamic code is compiled.
    /// </param>
    /// <param name="ReportPhase">The phase at which the native runtime observation was reported.</param>
    internal sealed record NativeRuntimeProof(
        string Framework, string Architecture, bool IsDynamicCodeSupported,
        bool IsDynamicCodeCompiled, string ReportPhase);

    /// <summary>
    /// Identifies the .NET SDK and Native AOT compiler used to produce the tested executable.
    /// </summary>
    /// <param name="SdkVersion">The .NET SDK version used to produce the native executable.</param>
    /// <param name="CompilerVersion">The Native AOT compiler version used to produce the executable.</param>
    /// <param name="CompilerDigest">The content digest of the Native AOT compiler executable.</param>
    internal sealed record NativeToolProof(string SdkVersion, string CompilerVersion, string CompilerDigest);

    /// <summary>
    /// Binds a Native AOT assurance run to its source, producer, executable bytes, runtime observations, and tools.
    /// </summary>
    /// <param name="Project">
    /// The repository-relative project path associated with the build or assurance result.
    /// </param>
    /// <param name="Configuration">The build configuration associated with the artifact or assurance job.</param>
    /// <param name="HostTfm">The target framework used to run the assurance test host.</param>
    /// <param name="LibraryTfm">The target framework of the library exercised by the assurance job.</param>
    /// <param name="RuntimeIdentifier">The runtime identifier used to publish the native assurance executable.</param>
    /// <param name="PublishAot">Whether the assurance executable was published with Native AOT enabled.</param>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="Nonce">
    /// The unique nonce binding the native process observation to this assurance invocation.
    /// </param>
    /// <param name="ProcessId">The operating-system process identifier of the native assurance run.</param>
    /// <param name="ExitCode">The process exit code reported for the native assurance run.</param>
    /// <param name="ProducedDigest">The digest of the executable produced before the native assurance run.</param>
    /// <param name="LaunchedDigest">
    /// The executable digest recorded when the native assurance process was launched.
    /// </param>
    /// <param name="ObservedDigest">The executable digest reported by the running native assurance process.</param>
    /// <param name="PostRunDigest">
    /// The digest of the native executable measured after the assurance process completed.
    /// </param>
    /// <param name="StartedAt">The time at which the producer operation started.</param>
    /// <param name="CompletedAt">The time at which the producer operation completed.</param>
    /// <param name="Image">The observed executable format, Native AOT header, digest, and byte length.</param>
    /// <param name="Runtime">
    /// The framework and dynamic-code capabilities observed in the native assurance process.
    /// </param>
    /// <param name="Tools">The SDK and Native AOT compiler identities used to produce the executable.</param>
    internal sealed record NativeAssuranceProof(
        string Project, string Configuration, string HostTfm, string LibraryTfm,
        string RuntimeIdentifier, bool PublishAot,
        string SourceSha, string RunId, int Attempt, string Nonce, int ProcessId, int ExitCode,
        string ProducedDigest, string LaunchedDigest, string ObservedDigest, string PostRunDigest,
        DateTimeOffset StartedAt, DateTimeOffset CompletedAt, NativeImageProof Image,
        NativeRuntimeProof Runtime, NativeToolProof Tools);

    /// <summary>
    /// Identifies the CodeQL tool version and executable digest used for analysis.
    /// </summary>
    /// <param name="Name">The CodeQL tool name reported by the producer.</param>
    /// <param name="Version">The CodeQL command-line tool version used for analysis.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record CodeqlToolProof(string Name, string Version, string Digest);

    /// <summary>
    /// Records expected and extracted source coverage for one analyzed project.
    /// </summary>
    /// <param name="ProjectDigest">The digest identifying the analyzed project.</param>
    /// <param name="ExpectedSources">The number of source files expected to be extracted for the project.</param>
    /// <param name="ExtractedSources">The number of source files actually extracted for the project.</param>
    /// <param name="ExpectedSourceDigest">
    /// The digest of the source set expected to be extracted for the project.
    /// </param>
    /// <param name="ExtractedSourceDigest">The digest of the source set actually extracted for the project.</param>
    /// <param name="ExtractionErrors">The number of errors reported while extracting project sources.</param>
    internal sealed record CodeqlProjectProof(
        string ProjectDigest, int ExpectedSources, int ExtractedSources,
        string ExpectedSourceDigest, string ExtractedSourceDigest, int ExtractionErrors);

    /// <summary>
    /// Identifies an independent review record and its immutable content digest.
    /// </summary>
    /// <param name="Id">The identifier of the independently signed review record.</param>
    /// <param name="Digest">The content digest identifying the referenced bytes.</param>
    internal sealed record ReviewReference(string Id, string Digest);

    /// <summary>
    /// Binds a CodeQL analysis to its source extraction, query execution, finding population, and review disposition.
    /// </summary>
    /// <param name="Status">The completion state reported by the CodeQL producer.</param>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="SourceRef">The fully qualified source ref associated with the CodeQL analysis.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="AnalysisId">The identifier of the CodeQL analysis being assessed or reviewed.</param>
    /// <param name="SarifId">The identifier of the uploaded CodeQL SARIF result.</param>
    /// <param name="Category">The category assigned to the CodeQL analysis.</param>
    /// <param name="Tool">The pinned tool identity used for verification or analysis.</param>
    /// <param name="Projects">The project-level proofs of expected and extracted source coverage.</param>
    /// <param name="ExpectedQueries">The number of CodeQL queries required by the selected query set.</param>
    /// <param name="CompletedQueries">The number of CodeQL queries whose execution completed.</param>
    /// <param name="Findings">The number of CodeQL finding occurrences in the collected result population.</param>
    /// <param name="DistinctAlerts">The number of distinct alerts represented by the CodeQL findings.</param>
    /// <param name="Disposition">
    /// The classification indicating a clean analysis or an authenticated independent review.
    /// </param>
    /// <param name="DatabaseDigest">The digest identifying the CodeQL analysis database.</param>
    /// <param name="ExtractionDigest">The digest of the source-extraction evidence.</param>
    /// <param name="ConfigurationDigest">The digest of the CodeQL analysis configuration.</param>
    /// <param name="SuiteDigest">The digest of the selected CodeQL query suite.</param>
    /// <param name="PackDigest">The digest of the CodeQL query-pack identities.</param>
    /// <param name="QueryDigest">The digest binding the exact CodeQL query set.</param>
    /// <param name="QueryResultsDigest">The digest of the collected CodeQL query results.</param>
    /// <param name="BuildDigest">The digest of the build evidence associated with CodeQL extraction.</param>
    /// <param name="PopulationDigest">The digest binding the complete CodeQL finding population.</param>
    /// <param name="Review">The optional identifier and content digest of an independent review record.</param>
    internal sealed record CodeqlAssuranceProof(
        string Status, string SourceSha, string SourceRef, string RunId, int Attempt, string AnalysisId,
        string SarifId, string Category, CodeqlToolProof Tool, CodeqlProjectProof[] Projects,
        int ExpectedQueries, int CompletedQueries, int Findings, int DistinctAlerts, string Disposition,
        string DatabaseDigest, string ExtractionDigest, string ConfigurationDigest, string SuiteDigest,
        string PackDigest, string QueryDigest, string QueryResultsDigest, string BuildDigest, string PopulationDigest,
        ReviewReference? Review = null);

    /// <summary>
    /// Records the reviewed CodeQL finding population and the number of unresolved findings.
    /// </summary>
    /// <param name="SourceSha">The source commit hash associated with the build or assurance result.</param>
    /// <param name="RunId">The producer run identifier.</param>
    /// <param name="Attempt">The producer run attempt associated with this evidence.</param>
    /// <param name="AnalysisId">The identifier of the CodeQL analysis being assessed or reviewed.</param>
    /// <param name="QueryDigest">The digest binding the exact CodeQL query set.</param>
    /// <param name="PopulationDigest">The digest binding the complete CodeQL finding population.</param>
    /// <param name="ReviewedOccurrences">
    /// The number of CodeQL finding occurrences covered by the independent review.
    /// </param>
    /// <param name="ReviewedAlerts">The number of distinct CodeQL alerts covered by the independent review.</param>
    /// <param name="UnresolvedFindings">The number of findings that remain unresolved after review.</param>
    internal sealed record CodeqlDisposition(
        string SourceSha, string RunId, int Attempt, string AnalysisId, string QueryDigest, string PopulationDigest,
        int ReviewedOccurrences, int ReviewedAlerts, int UnresolvedFindings);

    /// <summary>
    /// Checks whether native execution and CodeQL proofs satisfy their selected assurance jobs.
    /// </summary>
    internal static class AssuranceVerification
    {
        /// <summary>
        /// Checks native executable identity, tool pins, runtime observations, and run timing against the selected job.
        /// </summary>
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

        /// <summary>
        /// Checks CodeQL extraction and query completeness and requires an authenticated review for nonempty findings.
        /// </summary>
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
