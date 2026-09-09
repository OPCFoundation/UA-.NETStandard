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
    /// Exercises NuGet pipeline boundaries for publication, source attribution, receipts, and aggregated evidence.
    /// </summary>
    [TestFixture]
    public sealed class NugetEvidencePipelineTests
    {
        /// <summary>
        /// Checks that publication and release attachment require the expected evidence, authority, and immutable
        /// subjects.
        /// </summary>
        [TestCase("active-incomplete")]
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
        public async Task NugetEvidencePreservesPublicationBoundariesAsync(string scenario)
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
                Path.Combine(root, "tests", "Opc.Ua.Tools.Tests", "Fixtures", "NugetEvidencePipeline.fixture.ps1"),
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
