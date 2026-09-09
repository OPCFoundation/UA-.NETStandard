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
    internal static class PromotionCommands
    {
        public static void Register(
            RootCommand root,
            Func<PromotionVerificationInput, CancellationToken, Task<VerifiedPromotion>> verify,
            IPromotionTransport? officialTransport = null)
        {
            foreach (string operation in new[] { "verify", "offline", "write" })
            {
                var command = new Command(
                    "promotion-" + operation,
                    operation == "offline"
                        ? "Exercise verified delivery against a bounded local fixture; never official publication."
                        : "Dormant protected release promotion; current independent authorization is mandatory.");
                var repository = new Option<string>("--repository-root") { Required = true };
                var candidate = new Option<string>("--candidate-root") { Required = true };
                var evidence = new Option<string>("--evidence") { Required = true };
                var expected = new Option<string>("--expected") { Required = true };
                var bundle = new Option<string>("--verification-bundle") { Required = true };
                var trust = new Option<string>("--trust-policy") { Required = true };
                var request = new Option<string>("--request") { Required = true };
                var work = new Option<string>("--work") { Required = true };
                var output = new Option<string>("--output") { Required = true };
                var assessment = new Option<string>("--assessment") { Required = operation != "verify" };
                var assessmentDigest = new Option<string>("--assessment-digest") { Required = operation != "verify" };
                var journal = new Option<string>("--journal") { Required = operation != "verify" };
                var destination = new Option<string>("--offline-destination") { Required = operation == "offline" };
                command.Options.Add(repository);
                command.Options.Add(candidate);
                command.Options.Add(evidence);
                command.Options.Add(expected);
                command.Options.Add(bundle);
                command.Options.Add(trust);
                command.Options.Add(request);
                command.Options.Add(work);
                command.Options.Add(output);
                command.Options.Add(assessment);
                command.Options.Add(assessmentDigest);
                command.Options.Add(journal);
                if (operation == "offline")
                {
                    command.Options.Add(destination);
                }
                command.SetAction(async (parse, cancellationToken) =>
                {
                    try
                    {
                        var files = new EvidenceFiles();
                        var input = new PromotionVerificationInput(
                            parse.GetRequiredValue(repository), parse.GetRequiredValue(candidate),
                            parse.GetRequiredValue(evidence), parse.GetRequiredValue(expected),
                            parse.GetRequiredValue(bundle), parse.GetRequiredValue(trust),
                            parse.GetRequiredValue(request), parse.GetRequiredValue(work));
                        RejectNested(input.Work, input.CandidateRoot);
                        RejectNested(parse.GetRequiredValue(output), input.CandidateRoot);
                        var eligibility = new VerifiedEligibility(input, verify);
                        VerifiedPromotion verified = await eligibility.VerifyAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (operation == "verify")
                        {
                            PromotionRequest plan = verified.Request;
                            await files.WriteModelAsync(
                                parse.GetRequiredValue(output),
                                new PromotionAssessment(
                                    1, "eligible", verified.RequestDigest, plan.CandidateDigest,
                                    plan.EvidenceDigest, plan.IntentDigest, plan.PolicyDigest),
                                PromotionJsonContext.Default.PromotionAssessment, cancellationToken)
                                .ConfigureAwait(false);
                            return 0;
                        }
                        string assessmentPath = parse.GetRequiredValue(assessment);
                        if (await files.DigestAsync(assessmentPath, cancellationToken).ConfigureAwait(false) !=
                            parse.GetRequiredValue(assessmentDigest))
                        {
                            throw new PromotionRejectedException("The read-only assessment changed before the writer.");
                        }
                        PromotionAssessment prior = await files.ReadModelAsync(
                            assessmentPath, PromotionJsonContext.Default.PromotionAssessment, cancellationToken)
                            .ConfigureAwait(false);
                        if (prior.SchemaVersion != 1 || prior.Status != "eligible" ||
                            prior.RequestDigest != verified.RequestDigest ||
                            prior.CandidateDigest != verified.Request.CandidateDigest ||
                            prior.EvidenceDigest != verified.Request.EvidenceDigest ||
                            prior.IntentDigest != verified.Request.IntentDigest ||
                            prior.PolicyDigest != verified.Request.PolicyDigest)
                        {
                            throw new PromotionRejectedException(
                                "The assessment does not bind the current verified request.");
                        }
                        IPromotionTransport transport;
                        if (operation == "offline")
                        {
                            string store = parse.GetRequiredValue(destination);
                            string marker = EvidenceFiles.Confined(store, ".offline-promotion-fixture");
                            if (!File.Exists(marker) ||
                                (await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(false)).Trim() !=
                                    "isolated-local-fixture-not-an-official-registry")
                            {
                                throw new InvalidDataException("The destination is not an isolated offline fixture.");
                            }
                            RejectNested(store, input.CandidateRoot);
                            RejectNested(input.CandidateRoot, store);
                            transport = new PromotionFileTransport(store, files);
                        }
                        else if (officialTransport is { IsOfficial: true })
                        {
                            transport = officialTransport;
                        }
                        else
                        {
                            await Console.Error.WriteLineAsync(
                                "Promotion blocked: no administrator-approved official transport is registered.")
                                .ConfigureAwait(false);
                            return 1;
                        }
                        string journalRoot = parse.GetRequiredValue(journal);
                        RejectNested(journalRoot, input.CandidateRoot);
                        PromotionResult result = await new PromotionCoordinator(
                            eligibility, transport, new PromotionFileJournal(journalRoot, files), TimeProvider.System)
                            .PromoteAsync(cancellationToken).ConfigureAwait(false);
                        await files.WriteModelAsync(
                            parse.GetRequiredValue(output), result,
                            PromotionJsonContext.Default.PromotionResult, cancellationToken).ConfigureAwait(false);
                        return 0;
                    }
                    catch (OperationCanceledException)
                    {
                        await Console.Error.WriteLineAsync(
                            "Promotion cancelled; resume from destination readback, not from an unchecked receipt.")
                            .ConfigureAwait(false);
                        return 1;
                    }
                    catch (Exception exception) when (exception is
                        IOException or InvalidDataException or JsonException or ArgumentException or
                        PromotionRejectedException or UnauthorizedAccessException or InvalidOperationException or
                        FormatException)
                    {
                        await Console.Error.WriteLineAsync($"Promotion rejected: {exception.Message}")
                            .ConfigureAwait(false);
                        return exception is PromotionRejectedException ||
                            (exception is IOException && exception is not InvalidDataException) ? 1 : 2;
                    }
                });
                root.Subcommands.Add(command);
            }
        }

        private static void RejectNested(string path, string candidateRoot)
        {
            string full = Path.GetFullPath(path);
            string candidate = Path.GetFullPath(candidateRoot);
            if (full.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(
                    candidate.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Mutable output cannot overlap immutable candidate inputs.");
            }
        }

        private sealed class VerifiedEligibility(
            PromotionVerificationInput input,
            Func<PromotionVerificationInput, CancellationToken, Task<VerifiedPromotion>> verify)
            : IPromotionEligibility
        {
            public Task<VerifiedPromotion> VerifyAsync(CancellationToken cancellationToken)
            {
                return verify(input, cancellationToken);
            }
        }
    }
}
