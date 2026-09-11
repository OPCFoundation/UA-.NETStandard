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

#if NET10_0
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Exercises immutable release promotion transport and the guards that keep unconfigured authority inactive.
    /// </summary>
    [TestFixture]
    public sealed class ReleasePromotionTests
    {
        /// <summary>
        /// Checks delivery identity preservation and rejection of callers, policies, or evidence without promotion
        /// authority.
        /// </summary>
        [TestCase("ReleasePromotionTransport.fixture.ps1", "")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "unconfigured")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "foreign-caller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "pr-caller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "candidate-controller")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "candidate-trust")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "missing-assessment")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "dormant-policy")]
        [TestCase("ReleasePromotionPipeline.fixture.ps1", "physical-temp-alias")]
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
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", fixture)
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
