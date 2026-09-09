// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.ReleaseEvidence
{
    internal static class OciAssemblyCommands
    {
        public static void Register(RootCommand root)
        {
            var command = new Command("oci-assemble",
                "Assemble immutable native OCI v2 producer evidence; this does not authorize release.");
            var repository = new Option<string>("--repository-root") { Required = true };
            var request = new Option<string>("--request") { Required = true };
            var context = new Option<string>("--context") { Required = true };
            var output = new Option<string>("--output") { Required = true };
            var assurance = new Option<string?>("--assurance");
            var referrers = new Option<string?>("--referrers");
            command.Options.Add(repository);
            command.Options.Add(request);
            command.Options.Add(context);
            command.Options.Add(output);
            command.Options.Add(assurance);
            command.Options.Add(referrers);
            command.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    await AssembleAsync(parse.GetRequiredValue(repository), parse.GetRequiredValue(request),
                        parse.GetRequiredValue(context), parse.GetRequiredValue(output), parse.GetValue(assurance),
                        parse.GetValue(referrers), cancellationToken).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or
                    ArgumentException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
                {
                    Console.Error.WriteLine("OCI evidence assembly rejected invalid input; no release was authorized.");
                    return 2;
                }
            });
            root.Subcommands.Add(command);
        }

        private static async Task AssembleAsync(
            string repositoryRoot,
            string requestPath,
            string contextPath,
            string outputDirectory,
            string? assurancePath,
            string? referrerContextPath,
            CancellationToken cancellationToken)
        {
            var files = new EvidenceFiles();
            EvaluationExpectation context = await files.ReadModelAsync(
                contextPath, EvidenceJsonContext.Default.EvaluationExpectation, cancellationToken)
                .ConfigureAwait(false);
            AssuranceRecord assurance;
            string? assuranceRoot = null;
            if (assurancePath != null)
            {
                assurance = await files.ReadModelAsync(
                    assurancePath, EvidenceJsonContext.Default.AssuranceRecord, cancellationToken)
                    .ConfigureAwait(false);
                assuranceRoot = Path.GetDirectoryName(Path.GetFullPath(assurancePath))!;
            }
            else
            {
                PolicyConfiguration policy = await files.ReadModelAsync(
                    EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release-policy.json"),
                    EvidenceJsonContext.Default.PolicyConfiguration, cancellationToken).ConfigureAwait(false);
                ArtifactsConfiguration catalog = await files.ReadModelAsync(
                    EvidenceFiles.Confined(repositoryRoot, policy.ArtifactCatalog),
                    EvidenceJsonContext.Default.ArtifactsConfiguration, cancellationToken).ConfigureAwait(false);
                ProfilesConfiguration profiles = await files.ReadModelAsync(
                    EvidenceFiles.Confined(repositoryRoot, policy.AssuranceProfiles),
                    EvidenceJsonContext.Default.ProfilesConfiguration, cancellationToken).ConfigureAwait(false);
                ArtifactGroup group = catalog.Groups.Single(g => g.Id == context.Release.Group);
                JobRecord[] jobs = [.. profiles.Profiles.Where(p =>
                    group.Profiles.Contains(p.Id, StringComparer.Ordinal)).SelectMany(p => p.Jobs.Select(j =>
                        new JobRecord(j.Id, p.Id, j.Project, p.Configuration, p.Host, p.HostTfm, p.LibraryTfm,
                            p.Platform, "all", p.Filter, false, "missing", [])))];
                assurance = new AssuranceRecord(group.Profiles, jobs.Length, 0, 0, 0, jobs.Length, 0, jobs, []);
            }
            EvidenceEnvelope envelope = await new OciArtifactAdapter(files).AssembleAsync(
                repositoryRoot, requestPath, context, assurance, outputDirectory, cancellationToken, assuranceRoot,
                referrerContextPath)
                .ConfigureAwait(false);
            await files.WriteModelAsync(Path.Combine(outputDirectory, "release-evidence.json"), envelope,
                EvidenceJsonContext.Default.EvidenceEnvelope, cancellationToken).ConfigureAwait(false);
        }
    }
}
