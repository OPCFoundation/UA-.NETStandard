// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed record CodeqlReviewProjection(
        int SchemaVersion, string Kind, string Id, string Digest,
        string SourceSha, string RunId, int Attempt, string AnalysisId, string QueryDigest, string PopulationDigest,
        int ReviewedOccurrences, int ReviewedAlerts, int UnresolvedFindings, bool Revoked,
        DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

    internal sealed class CodeqlReviewCommands(
        EvidenceFiles files, ITrustPolicySource policies, IRecordSignatureVerifier signatures, TimeProvider clock)
    {
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
