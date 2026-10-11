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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for .github/scripts/update-test-durations.ps1, which
    /// rewrites the weights get-ci-matrix.ps1 packs test batches with. A
    /// regression there would silently skew every batch of every later run,
    /// so the selection rules are pinned against temporary summaries.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiTestDurationsScriptTests
    {
        private string m_workDirectory = string.Empty;
        private string m_tablePath = string.Empty;
        private string m_resultsPath = string.Empty;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_workDirectory = Path.Combine(Path.GetTempPath(), $"ci-durations-{Guid.NewGuid():N}");
            m_resultsPath = Path.Combine(m_workDirectory, "results");
            Directory.CreateDirectory(m_resultsPath);
            m_tablePath = Path.Combine(m_workDirectory, "ci-test-durations.json");
            await File.WriteAllTextAsync(
                m_tablePath,
                """
                {
                  "source": "seed",
                  "defaultMinutes": 5,
                  "projects": {
                    "Opc.Ua.Kept.Tests": 9,
                    "Opc.Ua.Slow.Tests": 2
                  }
                }
                """).ConfigureAwait(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_workDirectory))
            {
                Directory.Delete(m_workDirectory, recursive: true);
            }
        }

        /// <summary>
        /// A project's weight is its slowest profile's passed test time, rounded
        /// up to whole minutes, plus one minute of incremental build.
        /// </summary>
        [Test]
        public async Task WeightIsTheSlowestPassedProfilePlusOneMinuteAsync()
        {
            await WriteSummaryAsync("linux", ("tests/Opc.Ua.Slow.Tests/Opc.Ua.Slow.Tests.csproj", "passed", 130))
                .ConfigureAwait(false);
            await WriteSummaryAsync("macos", ("tests/Opc.Ua.Slow.Tests/Opc.Ua.Slow.Tests.csproj", "passed", 250))
                .ConfigureAwait(false);

            ScriptResult result = await RunScriptAsync().ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(await ReadWeightsAsync().ConfigureAwait(false), Does.ContainKey("Opc.Ua.Slow.Tests").WithValue(6));
        }

        /// <summary>
        /// Failed and not-applicable records say nothing about a healthy run's
        /// duration and must not move a weight, however long they took.
        /// </summary>
        [Test]
        public async Task FailedAndNotApplicableRecordsAreIgnoredAsync()
        {
            await WriteSummaryAsync(
                "windows",
                ("tests/Opc.Ua.Slow.Tests/Opc.Ua.Slow.Tests.csproj", "passed", 60),
                ("tests/Opc.Ua.Slow.Tests/Opc.Ua.Slow.Tests.csproj", "failed", 9999),
                ("tests/Opc.Ua.Kept.Tests/Opc.Ua.Kept.Tests.csproj", "not-applicable", 9999)).ConfigureAwait(false);

            ScriptResult result = await RunScriptAsync().ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Dictionary<string, int> weights = await ReadWeightsAsync().ConfigureAwait(false);
            Assert.That(weights, Does.ContainKey("Opc.Ua.Slow.Tests").WithValue(2));
            Assert.That(weights, Does.ContainKey("Opc.Ua.Kept.Tests").WithValue(9));
        }

        /// <summary>
        /// A project the run did not execute keeps its weight, a newly seen one
        /// is added, and the table keeps its default and records its source.
        /// </summary>
        [Test]
        public async Task UnobservedProjectsKeepTheirWeightAndNewOnesAreAddedAsync()
        {
            await WriteSummaryAsync("linux", ("tests/Opc.Ua.New.Tests/Opc.Ua.New.Tests.csproj", "passed", 30))
                .ConfigureAwait(false);

            ScriptResult result = await RunScriptAsync().ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Dictionary<string, int> weights = await ReadWeightsAsync().ConfigureAwait(false);
            Assert.That(weights, Does.ContainKey("Opc.Ua.Kept.Tests").WithValue(9));
            Assert.That(weights, Does.ContainKey("Opc.Ua.Slow.Tests").WithValue(2));
            Assert.That(weights, Does.ContainKey("Opc.Ua.New.Tests").WithValue(2));

            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(m_tablePath).ConfigureAwait(false));
            Assert.That(document.RootElement.GetProperty("defaultMinutes").GetInt32(), Is.EqualTo(5));
            Assert.That(document.RootElement.GetProperty("source").GetString(), Is.EqualTo("test run"));
        }

        /// <summary>
        /// Pointing the script at a directory without summaries is a mistake
        /// (usually a missing download), not a run that observed nothing.
        /// </summary>
        [Test]
        public async Task MissingSummariesFailWithoutTouchingTheTableAsync()
        {
            string before = await File.ReadAllTextAsync(m_tablePath).ConfigureAwait(false);

            ScriptResult result = await RunScriptAsync().ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("No batch-summary.json"));
            Assert.That(await File.ReadAllTextAsync(m_tablePath).ConfigureAwait(false), Is.EqualTo(before));
        }

        private async Task WriteSummaryAsync(string batch, params (string Project, string Outcome, int TestSeconds)[] records)
        {
            string directory = Path.Combine(m_resultsPath, $"dotnet-results-{batch}", "TestResults");
            Directory.CreateDirectory(directory);
            var document = new
            {
                projects = records.Select(record => new
                {
                    project = record.Project,
                    outcome = record.Outcome,
                    buildSeconds = 60,
                    testSeconds = record.TestSeconds
                }).ToArray()
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "batch-summary.json"),
                JsonSerializer.Serialize(document)).ConfigureAwait(false);
        }

        private async Task<Dictionary<string, int>> ReadWeightsAsync()
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(m_tablePath).ConfigureAwait(false));
            return document.RootElement.GetProperty("projects").EnumerateObject()
                .ToDictionary(project => project.Name, project => project.Value.GetInt32(), StringComparer.Ordinal);
        }

        private async Task<ScriptResult> RunScriptAsync()
        {
            string root = FindRepositoryRoot();
            using var process = new Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.WorkingDirectory = root;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            PowerShellScriptOutput.ConfigureDeterministicOutput(process.StartInfo);
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-File");
            process.StartInfo.ArgumentList.Add(Path.Combine(root, ".github", "scripts", "update-test-durations.ps1"));
            process.StartInfo.ArgumentList.Add("-ResultsPath");
            process.StartInfo.ArgumentList.Add(m_resultsPath);
            process.StartInfo.ArgumentList.Add("-Source");
            process.StartInfo.ArgumentList.Add("test run");
            process.StartInfo.ArgumentList.Add("-DurationsPath");
            process.StartInfo.ArgumentList.Add(m_tablePath);

            Assert.That(process.Start(), Is.True);
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            return new ScriptResult(process.ExitCode, PowerShellScriptOutput.Normalize(output + error));
        }

        private static string FindRepositoryRoot()
        {
            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, ".github", "scripts", "update-test-durations.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private sealed record ScriptResult(int ExitCode, string Output);
    }
}
#endif
