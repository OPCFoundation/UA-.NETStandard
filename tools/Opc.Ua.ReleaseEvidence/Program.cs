// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

[assembly: CLSCompliant(true)]

namespace Opc.Ua.ReleaseEvidence
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var root = new RootCommand(
                "Offline, artifact-bound release evidence. Never publishes or restores packages.");
            NugetDeliveryCommands.Register(root);
            PromotionCommands.Register(root, VerifiedPromotion.VerifyAsync);
            OciAssemblyCommands.Register(root);
            ReadinessCommands.AddTo(root);
            var capture = new Command("capture", "Freeze final restored build inputs and evaluated mappings.");
            var captureRoot = new Option<string>("--repository-root") { Required = true };
            var request = new Option<string>("--request") { Required = true };
            var captureOutput = new Option<string>("--output") { Required = true };
            capture.Options.Add(captureRoot);
            capture.Options.Add(request);
            capture.Options.Add(captureOutput);
            capture.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    return await new BuildCapture(new EvidenceFiles(), new ProcessRunner()).CaptureAsync(
                        parse.GetRequiredValue(captureRoot), parse.GetRequiredValue(request),
                        parse.GetRequiredValue(captureOutput), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync($"Invalid input: {ex.Message}").ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(capture);
            var nuget = new Command("nuget", "Reconcile signed archives and write CycloneDX 1.6 sidecars.");
            var nugetRoot = new Option<string>("--repository-root") { Required = true };
            var packages = new Option<string>("--packages") { Required = true };
            var inputs = new Option<string[]>("--inputs") { Required = true, AllowMultipleArgumentsPerToken = true };
            var context = new Option<string>("--context") { Required = true };
            var manifest = new Option<string?>("--manifest");
            var nugetOutput = new Option<string>("--output") { Required = true };
            nuget.Options.Add(nugetRoot);
            nuget.Options.Add(packages);
            nuget.Options.Add(inputs);
            nuget.Options.Add(context);
            nuget.Options.Add(manifest);
            nuget.Options.Add(nugetOutput);
            nuget.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    var files = new EvidenceFiles();
                    return await new NugetEvidence(files, new PackageReconciler(files)).GenerateAsync(
                        parse.GetRequiredValue(nugetRoot), parse.GetRequiredValue(packages),
                        parse.GetRequiredValue(inputs),
                        parse.GetRequiredValue(context), parse.GetValue(manifest), parse.GetRequiredValue(nugetOutput),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync($"Invalid input: {ex.Message}").ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(nuget);
            var oci = new Command("oci", "Reconcile downloaded OCI layout descriptors without network access.");
            var ociRoot = new Option<string>("--repository-root") { Required = true };
            var ociRequest = new Option<string>("--request") { Required = true };
            var ociContext = new Option<string>("--context") { Required = true };
            var ociOutput = new Option<string>("--output") { Required = true };
            oci.Options.Add(ociRoot);
            oci.Options.Add(ociRequest);
            oci.Options.Add(ociContext);
            oci.Options.Add(ociOutput);
            oci.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    return await new OciReconciler(new EvidenceFiles()).ReconcileAsync(
                        parse.GetRequiredValue(ociRoot), parse.GetRequiredValue(ociRequest),
                        parse.GetRequiredValue(ociContext), parse.GetRequiredValue(ociOutput), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync($"Invalid input: {ex.Message}").ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(oci);
            var evaluate = new Command("evaluate", "Evaluate evidence against the protected current contract.");
            var repository = new Option<string>("--repository-root") { Required = true };
            var evidence = new Option<string>("--evidence") { Required = true };
            var expected = new Option<string>("--expected") { Required = true };
            expected.Aliases.Add("--expect");
            var output = new Option<string>("--output") { Required = true };
            var artifactsRoot = new Option<string?>("--artifacts-root");
            var verificationBundle = new Option<string?>("--verification-bundle");
            var trustPolicy = new Option<string?>("--trust-policy");
            evaluate.Options.Add(repository);
            evaluate.Options.Add(evidence);
            evaluate.Options.Add(expected);
            evaluate.Options.Add(output);
            evaluate.Options.Add(artifactsRoot);
            evaluate.Options.Add(verificationBundle);
            evaluate.Options.Add(trustPolicy);
            evaluate.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    return await new EvidenceEvaluator(new EvidenceFiles()).EvaluateAsync(
                        parse.GetRequiredValue(repository), parse.GetRequiredValue(evidence),
                        parse.GetRequiredValue(expected), parse.GetRequiredValue(output),
                        parse.GetValue(artifactsRoot), cancellationToken,
                        parse.GetValue(verificationBundle), parse.GetValue(trustPolicy))
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (IsInputError(ex))
                {
                    await Console.Error.WriteLineAsync($"Invalid input: {ex.Message}").ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(evaluate);
            CodeqlReviewCommands.Register(root);
            ApprovedNugetDelivery.Register(root);
            ParseResult result = root.Parse(args);
            int exitCode = await result.InvokeAsync().ConfigureAwait(false);
            return result.Errors.Count == 0 ? exitCode : 2;
        }

        private static bool IsInputError(Exception exception)
        {
            return exception is IOException or InvalidDataException or JsonException or ArgumentException or
                InvalidOperationException or System.Collections.Generic.KeyNotFoundException or
                System.Xml.XmlException or UnauthorizedAccessException or FormatException or
                Json.Schema.JsonSchemaException or OperationCanceledException or System.ComponentModel.Win32Exception;
        }
    }
}
