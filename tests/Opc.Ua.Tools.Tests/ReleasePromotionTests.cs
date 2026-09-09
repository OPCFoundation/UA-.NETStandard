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
    public sealed class ReleasePromotionTests
    {
        [TestCase("ReleasePromotionTransport.fixture.ps1", "")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "unconfigured")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "foreign-caller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "pr-caller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "candidate-controller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "candidate-trust")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "missing-assessment")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "dormant-policy")]
        public async Task PromotionScriptsPreserveImmutableDeliveryAndDormantAuthorityAsync(
            string fixture, string scenario)
        {
            DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
            {
                directory = directory.Parent;
            }
            string root = directory?.FullName ??
                throw new DirectoryNotFoundException("Repository root is required for promotion fixtures.");
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
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", fixture)
            })
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            if (scenario.Length != 0)
            {
                process.StartInfo.ArgumentList.Add("-Scenario");
                process.StartInfo.ArgumentList.Add(scenario);
            }
            process.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
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
