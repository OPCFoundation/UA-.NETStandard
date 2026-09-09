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
    public sealed class ContainerEvidencePipelineTests
    {
        [TestCase("valid-pump")]
        [TestCase("valid-dual-platform")]
        [TestCase("record-request")]
        [TestCase("recorded-root-mismatch")]
        [TestCase("wrong-digest")]
        [TestCase("missing-platform")]
        [TestCase("attestation-descriptor")]
        [TestCase("wrong-attestation-subject")]
        [TestCase("private-path-filter")]
        [TestCase("private-field-filter")]
        [TestCase("pilot-incomplete")]
        [TestCase("required-stable")]
        [TestCase("required-four-part-version")]
        [TestCase("required-context-version")]
        [TestCase("required-preview")]
        [TestCase("baseline-failure")]
        [TestCase("pr-no-publish")]
        [TestCase("catalog-membership")]
        [TestCase("pump-membership")]
        [TestCase("unsupported-spdx")]
        [TestCase("aggregate-observed")]
        [TestCase("path-traversal")]
        public async Task ContainerPilotPreservesEvidenceBoundariesAsync(string scenario)
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            string repositoryRoot = directory?.FullName ??
                throw new DirectoryNotFoundException("Repository root is required for pipeline fixtures.");
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (string argument in new[]
            {
                "-NoLogo", "-NoProfile", "-File",
                Path.Combine(repositoryRoot, "tests", "Opc.Ua.Tools.Tests", "ContainerEvidencePipeline.fixture.ps1"),
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
                throw;
            }
            string output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.Zero, output);
        }
    }
}
#endif
