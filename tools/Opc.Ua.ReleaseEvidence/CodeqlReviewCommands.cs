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
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Projects an authenticated independent review into the finding-disposition data consumed by assurance.
    /// </summary>
    /// <param name="SchemaVersion">The version of this JSON document contract.</param>
    /// <param name="Kind">The codeql-disposition discriminator identifying this projection.</param>
    /// <param name="Id">The identifier of the independently authenticated review record.</param>
    /// <param name="Digest">The digest of the independently authenticated review-record file.</param>
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
    /// <param name="Revoked">Whether the review record is marked as revoked.</param>
    /// <param name="IssuedAt">The start of the record's validity interval.</param>
    /// <param name="ExpiresAt">The end of the record's validity interval.</param>
    internal sealed record CodeqlReviewProjection(
        int SchemaVersion, string Kind, string Id, string Digest,
        string SourceSha, string RunId, int Attempt, string AnalysisId, string QueryDigest, string PopulationDigest,
        int ReviewedOccurrences, int ReviewedAlerts, int UnresolvedFindings, bool Revoked,
        DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Authenticates independently signed CodeQL reviews against protected policy and the current clock.
    /// </summary>
    /// <param name="files">The service for bounded evidence-file access and content digests.</param>
    /// <param name="policies">The source of independently anchored trust-policy snapshots.</param>
    /// <param name="signatures">The verifier that authenticates independently signed CodeQL review records.</param>
    /// <param name="clock">The clock used to check proof validity and freshness.</param>
    internal sealed class CodeqlReviewCommands(
        EvidenceFiles files, ITrustPolicySource policies, IRecordSignatureVerifier signatures, TimeProvider clock)
    {
        /// <summary>
        /// Registers the command that authenticates and projects a complete independent CodeQL review.
        /// </summary>
        public static void Register(RootCommand root)
        {
            var command = new Command("verify-codeql-review", "Authenticate an independent, complete finding review.");
            var repository = new Option<string>("--repository-root") { Required = true };
            var bundle = new Option<string>("--verification-bundle") { Required = true };
            var trust = new Option<string>("--trust-policy") { Required = true };
            var id = new Option<string>("--review-id") { Required = true };
            var digest = new Option<string>("--review-digest") { Required = true };
            var output = new Option<string>("--output") { Required = true };
            command.Options.Add(repository);
            command.Options.Add(bundle);
            command.Options.Add(trust);
            command.Options.Add(id);
            command.Options.Add(digest);
            command.Options.Add(output);
            command.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    var files = new EvidenceFiles();
                    var handler = new CodeqlReviewCommands(files, new ProtectedTrustPolicySource(files),
                        new GitHubRecordSignatureVerifier(files, new ProcessRunner()), TimeProvider.System);
                    return await handler.VerifyAsync(
                        parse.GetRequiredValue(repository), parse.GetRequiredValue(bundle),
                        parse.GetRequiredValue(trust), parse.GetRequiredValue(id), parse.GetRequiredValue(digest),
                        parse.GetRequiredValue(output), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or
                    ArgumentException or InvalidOperationException or UnauthorizedAccessException or
                    Json.Schema.JsonSchemaException or OperationCanceledException or
                    System.ComponentModel.Win32Exception)
                {
                    await Console.Error.WriteLineAsync("Independent review input or verification contract is unusable.")
                        .ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(command);
        }

        /// <summary>
        /// Writes a disposition projection only for a unique authenticated review with no unresolved findings.
        /// </summary>
        public async Task<int> VerifyAsync(
            string repositoryRoot, string bundlePath, string trustPath,
            string reviewId, string reviewDigest, string output, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            cancellationToken = deadline.Token;
            string bundleRoot = Path.GetDirectoryName(Path.GetFullPath(bundlePath))!;
            ProtectedTrustPolicySource.RequireOutside(Path.GetFullPath(output), [bundleRoot]);
            if (!VerificationControls.IsDigest(reviewDigest) || string.IsNullOrWhiteSpace(reviewId))
            {
                throw new InvalidDataException("Malformed review identity.");
            }
            TrustedPolicySnapshot? policy = await policies.LoadAsync(
                trustPath, [repositoryRoot, bundleRoot], cancellationToken).ConfigureAwait(false);
            if (policy == null ||
                !await VerificationControls.PolicyFilesMatchAsync(policy, repositoryRoot, files, cancellationToken)
                    .ConfigureAwait(false))
            {
                return 1;
            }
            VerificationSchemas schemas = await VerificationSchemas.LoadAsync(
                repositoryRoot, files, cancellationToken).ConfigureAwait(false);
            schemas.Validate("trusted-policy-snapshot.schema.json", JsonSerializer.SerializeToElement(
                policy, VerificationJsonContext.Default.TrustedPolicySnapshot));
            using JsonDocument bundleDocument = await files.ReadJsonAsync(bundlePath, cancellationToken)
                .ConfigureAwait(false);
            schemas.Validate("verification-bundle.schema.json", bundleDocument.RootElement);
            VerificationBundle bundle = bundleDocument.Deserialize(VerificationJsonContext.Default.VerificationBundle)!;
            VerifiedCodeqlReview? match = null;
            foreach (VerificationProof proof in bundle.CodeqlReviews ?? [])
            {
                string path = EvidenceFiles.Confined(bundleRoot, proof.RecordPath);
                if (!File.Exists(path) ||
                    await files.DigestAsync(path, cancellationToken).ConfigureAwait(false) != reviewDigest)
                {
                    continue;
                }
                VerifiedCodeqlReview? verified = await VerifiedCodeqlReview.VerifyAsync(
                    repositoryRoot, bundleRoot, proof, policy, files, signatures, clock, cancellationToken)
                    .ConfigureAwait(false);
                if (verified?.Record.Id != reviewId)
                {
                    continue;
                }
                if (match != null)
                {
                    throw new InvalidDataException("Ambiguous signed review identity.");
                }
                match = verified;
            }
            if (match == null || match.Record.Review.UnresolvedFindings != 0)
            {
                return 1;
            }
            CodeqlDisposition review = match.Record.Review;
            await files.WriteModelAsync(output, new CodeqlReviewProjection(
                1, "codeql-disposition", match.Record.Id, match.Digest,
                review.SourceSha, review.RunId, review.Attempt, review.AnalysisId, review.QueryDigest,
                review.PopulationDigest, review.ReviewedOccurrences, review.ReviewedAlerts, review.UnresolvedFindings,
                false, match.Record.IssuedAt, match.Record.ExpiresAt),
                VerificationJsonContext.Default.CodeqlReviewProjection, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
