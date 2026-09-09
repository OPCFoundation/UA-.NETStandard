// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

#if NET10_0
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    public sealed class NugetEvidencePipelineTests
    {
        [TestCase("pilot-incomplete")]
        [TestCase("required-incomplete")]
        [TestCase("required-complete-reader")]
        [TestCase("required-preview")]
        [TestCase("deferred-major")]
        [TestCase("baseline-failed")]
        [TestCase("invalid-reader")]
        [TestCase("malformed-expected")]
        [TestCase("malformed-manifest")]
        [TestCase("saved-bundle-forwarded")]
        [TestCase("public-bom-exact-subject")]
        [TestCase("public-bom-wrong-subject")]
        [TestCase("public-bom-unknown-field")]
        [TestCase("public-bom-unapproved-property")]
        [TestCase("source-mismatch")]
        [TestCase("attempt-mismatch")]
        [TestCase("definition-mismatch")]
        [TestCase("foreign-source")]
        [TestCase("missing-source-authority")]
        [TestCase("receipt-preserves-author-identity")]
        [TestCase("receipt-source-mismatch")]
        [TestCase("receipt-symbols-unresolved")]
        [TestCase("aggregate-configurations")]
        [TestCase("aggregate-classified-controls")]
        [TestCase("aggregate-partial-classification")]
        [TestCase("aggregate-overlapping-classification")]
        [TestCase("aggregate-missing-classification")]
        [TestCase("aggregate-changed-archive")]
        [TestCase("aggregate-missing-debug")]
        [TestCase("aggregate-wrong-attempt")]
        [TestCase("aggregate-wrong-definition")]
        [TestCase("aggregate-provenance-source-mismatch")]
        [TestCase("aggregate-restricted-field")]
        [TestCase("aggregate-hidden-payload")]
        [TestCase("aggregate-duplicate-payload")]
        [TestCase("feed-content-matched-unapproved")]
        [TestCase("feed-content-mismatch")]
        [TestCase("feed-native-failed")]
        [TestCase("feed-malformed-reader")]
        [TestCase("attach-bare-version-tag")]
        [TestCase("attach-prefixed-version-tag")]
        [TestCase("attach-wrong-source")]
        public async Task NugetPilotPreservesPublicationBoundariesAsync(string scenario)
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            string root = directory?.FullName ??
                throw new DirectoryNotFoundException("Repository root is required for pipeline fixtures.");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (string argument in new[]
            {
                "-NoLogo", "-NoProfile", "-File",
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "NugetEvidencePipeline.fixture.ps1"),
                "-Scenario", scenario
            })
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            string output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, output);
        }
    }
}
#endif
