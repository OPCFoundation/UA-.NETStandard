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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for .github/scripts/assert-tests-ran.ps1, the gate the
    /// opt-in stress, stability and long-haul workflows use to fail a run whose
    /// test filter matched nothing. 'dotnet test' exits 0 in that case, which
    /// is how the Connection Stability workflow stayed green while running no
    /// test at all; if this gate weakened, that could happen again unnoticed.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiAssertTestsRanTests
    {
        private string m_resultsDirectory = null!;

        /// <summary>
        /// Creates an empty results directory for each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_resultsDirectory = Path.Combine(
                Path.GetTempPath(),
                "assert-tests-ran-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_resultsDirectory);
        }

        /// <summary>
        /// Removes the results directory.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_resultsDirectory))
            {
                Directory.Delete(m_resultsDirectory, recursive: true);
            }
        }

        /// <summary>
        /// A run that wrote no TRX at all produced no results and fails.
        /// </summary>
        [Test]
        public async Task NoTrxFailsAsync()
        {
            (int exitCode, string output) = await InvokeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("No TRX file"));
            });
        }

        /// <summary>
        /// The case the gate exists for: the filter matched nothing, so the TRX
        /// records zero executed tests.
        /// </summary>
        [Test]
        public async Task ZeroExecutedFailsAsync()
        {
            WriteTrx("empty.trx", executed: 0, passed: 0);

            (int exitCode, string output) = await InvokeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("matched no runnable test"));
            });
        }

        /// <summary>
        /// Tests that executed without a single pass do not count as a run.
        /// </summary>
        [Test]
        public async Task ExecutedButNonePassedFailsAsync()
        {
            WriteTrx("failed.trx", executed: 1, passed: 0);

            (int exitCode, string output) = await InvokeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(output, Does.Contain("none passed"));
            });
        }

        /// <summary>
        /// A run with passing tests succeeds; counters are summed across TRX
        /// files, including ones in subdirectories.
        /// </summary>
        [Test]
        public async Task PassingRunSucceedsAsync()
        {
            WriteTrx("empty.trx", executed: 0, passed: 0);
            WriteTrx(Path.Combine("nested", "passed.trx"), executed: 2, passed: 2);

            (int exitCode, string output) = await InvokeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero, output);
                Assert.That(output, Does.Contain("Executed 2 test(s), 2 passed, across 2 TRX file(s)."));
            });
        }

        private void WriteTrx(string relativePath, int executed, int passed)
        {
            string path = Path.Combine(m_resultsDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" +
                    "<ResultSummary outcome=\"Completed\">" +
                    "<Counters total=\"{0}\" executed=\"{0}\" passed=\"{1}\" failed=\"{2}\" />" +
                    "</ResultSummary></TestRun>",
                    executed,
                    passed,
                    executed - passed));
        }

        private async Task<(int ExitCode, string Output)> InvokeAsync()
        {
            string root = FindRepositoryRoot();
            string script = Path.Combine(root, ".github", "scripts", "assert-tests-ran.ps1");

            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(script);
            process.StartInfo.ArgumentList.Add("-ResultsDirectory");
            process.StartInfo.ArgumentList.Add(m_resultsDirectory);

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            return (process.ExitCode, PowerShellScriptOutput.Normalize(output + error));
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".github", "scripts", "assert-tests-ran.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }
    }
}
#endif
