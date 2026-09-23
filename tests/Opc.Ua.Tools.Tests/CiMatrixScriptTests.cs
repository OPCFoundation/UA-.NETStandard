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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Contract tests for .github/scripts/get-ci-matrix.ps1, which decides what
    /// every pull request and every manual full-scope run actually executes. A
    /// silent regression there would not fail any build: it would quietly stop
    /// running part of the suite and still report green, so the properties that
    /// make the matrix trustworthy are asserted here instead.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CiMatrixScriptTests
    {
        /// <summary>
        /// GitHub refuses to start a run whose matrices expand past 256 jobs.
        /// The script sizes its batches to fit; this proves both scopes do.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task ScopeFitsTheGitHubMatrixLimitAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            Assert.That(matrix.Tests, Is.Not.Empty);
            Assert.That(matrix.Builds, Is.Not.Empty);
            Assert.That(
                matrix.Tests.Count + matrix.Builds.Count,
                Is.LessThanOrEqualTo(256),
                "The expanded matrix must fit GitHub's per-run job ceiling.");
        }

        /// <summary>
        /// The batch size is computed from the whole profile table so that
        /// turning macOS off does not reshuffle which projects run together on
        /// the remaining runners, which would make two runs incomparable.
        /// </summary>
        [Test]
        public async Task ExcludingMacOsKeepsTheRemainingBatchesIdenticalAsync()
        {
            MatrixResult withMac = await RunMatrixAsync("pr").ConfigureAwait(false);
            MatrixResult withoutMac = await RunMatrixAsync("pr", excludeMacOs: true).ConfigureAwait(false);

            Assert.That(withoutMac.BatchSize, Is.EqualTo(withMac.BatchSize));
            Assert.That(
                withoutMac.Tests.Any(entry => entry.Os == "macos"),
                Is.False,
                "-ExcludeMacOS must drop every macOS entry.");

            Dictionary<string, string> expected = withMac.Tests
                .Where(entry => entry.Os != "macos")
                .ToDictionary(entry => entry.Id, entry => entry.Projects);
            Dictionary<string, string> actual = withoutMac.Tests
                .ToDictionary(entry => entry.Id, entry => entry.Projects);

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Every test project on disk has to be claimed by the matrix. The
        /// exclusions are the four projects other jobs own; anything else that
        /// stopped being scheduled would silently stop being tested.
        /// </summary>
        [Test]
        public async Task EveryTestProjectIsCoveredOrExplicitlyExcludedAsync()
        {
            MatrixResult matrix = await RunMatrixAsync("full").ConfigureAwait(false);

            HashSet<string> scheduled = matrix.Tests
                .SelectMany(entry => entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);

            string root = FindRepositoryRoot();
            string[] excluded =
            [
                "Opc.Ua.Aot.Tests.csproj",
                "Opc.Ua.Stress.Tests.csproj",
                "Opc.Ua.OneFuzz.Validator.Tests.csproj"
            ];

            List<string> missing = [];
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };
            foreach (string path in Directory.EnumerateFiles(root, "*.Tests.csproj", options))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) ||
                    relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }
                if (excluded.Contains(Path.GetFileName(path), StringComparer.Ordinal))
                {
                    continue;
                }
                if (!scheduled.Contains(relative))
                {
                    missing.Add(relative);
                }
            }

            Assert.That(missing, Is.Empty, "These test projects are on disk but nothing schedules them.");
        }

        /// <summary>
        /// The fuzz projects live under fuzzing/, not tests/. An earlier
        /// discovery step only swept tests/ and therefore never ran them on
        /// GitHub-hosted runners; this pins the fix.
        /// </summary>
        [Test]
        public async Task FuzzProjectsAreScheduledAsync()
        {
            MatrixResult matrix = await RunMatrixAsync("pr").ConfigureAwait(false);

            string[] fuzzProjects = matrix.Tests
                .SelectMany(entry => entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries))
                .Where(project => project.StartsWith("fuzzing/", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.That(fuzzProjects, Is.Not.Empty);
        }

        /// <summary>
        /// The tiers that lift the category filter are the only way the
        /// LongRunning and durable-subscription tests ever run. They belong to
        /// the full scope and must stay out of the pull-request scope.
        /// </summary>
        [Test]
        public async Task UnfilteredTiersRunOnlyInTheFullScopeAsync()
        {
            MatrixResult pr = await RunMatrixAsync("pr").ConfigureAwait(false);
            MatrixResult full = await RunMatrixAsync("full").ConfigureAwait(false);

            Assert.That(pr.Tests.All(entry => entry.Tier == "mainline"), Is.True);
            Assert.That(full.Tests.Any(entry => entry.Tier == "long-running"), Is.True);
            Assert.That(full.Tests.Any(entry => entry.Tier == "durable"), Is.True);
            Assert.That(
                full.Tests.Where(entry => entry.Tier != "mainline").All(entry => entry.Filter.Length == 0),
                Is.True,
                "A tier that exists to lift the category filter must not carry one.");
        }

        /// <summary>
        /// coverlet.collector ships build assets for net8.0 and newer only, so
        /// attaching it to a .NET Framework test host produces a warning and no
        /// report. Requesting coverage there would make the merged report look
        /// complete while silently missing those legs.
        /// </summary>
        [Test]
        public async Task CoverageIsNeverRequestedOnANetFrameworkHostAsync()
        {
            MatrixResult full = await RunMatrixAsync("full").ConfigureAwait(false);

            MatrixEntry[] offenders = full.Tests
                .Where(entry => entry.Framework.StartsWith("net4", StringComparison.Ordinal) && entry.Coverage)
                .ToArray();

            Assert.That(offenders.Select(entry => entry.Id), Is.Empty);
        }

        /// <summary>
        /// The pull-request scope has to be the union of what the two CI systems
        /// covered separately, otherwise migrating to Actions loses a leg.
        /// </summary>
        [Test]
        public async Task PullRequestScopeCoversBothFormerCiSystemsAsync()
        {
            MatrixResult pr = await RunMatrixAsync("pr").ConfigureAwait(false);

            string[] profiles = pr.Tests.Select(entry => entry.Profile).Distinct(StringComparer.Ordinal).ToArray();

            Assert.That(profiles, Does.Contain("windows-net48"));
            Assert.That(profiles, Does.Contain("windows-net10.0"));
            Assert.That(profiles, Does.Contain("linux-net10.0"));
            Assert.That(profiles, Does.Contain("macos-net10.0"));
        }

        /// <summary>
        /// The private-fuzz-corpus job narrows the same profile table to one
        /// project instead of repeating the profile list, so the narrowing has
        /// to keep the profiles and drop everything else.
        /// </summary>
        [Test]
        public async Task OnlyProjectNarrowsTheMatrixToThatProjectAsync()
        {
            const string project = "fuzzing/Opc.Ua.Encoders.Fuzz.Tests/Opc.Ua.Encoders.Fuzz.Tests.csproj";
            MatrixResult matrix = await RunMatrixAsync("full", onlyProject: project).ConfigureAwait(false);

            Assert.That(matrix.Tests, Is.Not.Empty);
            Assert.That(
                matrix.Tests.All(entry => entry.Projects == project),
                Is.True,
                "Narrowing must not leave any other project scheduled.");
            Assert.That(
                matrix.Tests.All(entry => entry.Tier == "mainline"),
                Is.True,
                "The named tiers cover named projects and must drop out when narrowing.");
            Assert.That(
                matrix.Tests.Select(entry => entry.Profile).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(matrix.Tests.Count),
                "One project produces exactly one batch per profile.");
        }

        /// <summary>
        /// A typo in the narrowing argument must not produce an empty matrix
        /// that reports success without replaying anything.
        /// </summary>
        [Test]
        public async Task OnlyProjectFailsWhenNothingMatchesAsync()
        {
            ScriptResult result = await RunScriptAsync("full", onlyProject: "does/not/Exist.Tests.csproj")
                .ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("would report success without testing anything"));
        }

        /// <summary>
        /// A batch job is cancelled by GitHub the moment it passes
        /// timeout-minutes, and a cancelled job produces neither the executor's
        /// per-project annotation nor its results. The job budget therefore has
        /// to be an upper bound on everything the executor may spend: fixed
        /// setup cost plus one per-project ceiling for each project in the
        /// batch, never a clamped value.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task JobTimeoutBoundsEveryProjectsCombinedBudgetAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            string[] underBudget = matrix.Tests
                .Where(entry =>
                    entry.TimeoutMinutes <
                        entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length *
                        entry.PerProjectTimeout)
                .Select(entry => $"{entry.Id} allows {entry.TimeoutMinutes}min for " +
                    $"{entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length} x " +
                    $"{entry.PerProjectTimeout}min")
                .ToArray();

            Assert.That(underBudget, Is.Empty);
            Assert.That(matrix.Tests.All(entry => entry.PerProjectTimeout > 0), Is.True);
        }

        /// <summary>
        /// The per-project ceiling the matrix budgets once must also be spent
        /// once. Handing it separately to the build and to the test would let a
        /// project burn twice what its job was given, so both invocations share
        /// a single stopwatch.
        /// </summary>
        [Test]
        public async Task ExecutorSpendsOnePerProjectBudgetAcrossBuildAndTestAsync()
        {
            string executor = Path.Combine(FindRepositoryRoot(), ".github", "scripts", "run-dotnet-tests.ps1");
            string source = await File.ReadAllTextAsync(executor).ConfigureAwait(false);

            string[] invocations = source
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Contains("= Invoke-Dotnet ", StringComparison.Ordinal))
                .ToArray();

            Assert.That(invocations, Has.Length.EqualTo(2), "The executor builds once and tests once per project.");
            Assert.That(
                invocations.All(line => line.Contains("$projectBudget $PerProjectTimeoutMinutes", StringComparison.Ordinal)),
                Is.True,
                $"Both invocations must share the one stopwatch: {string.Join(" | ", invocations)}");
            Assert.That(
                source,
                Does.Contain("$projectBudget = [System.Diagnostics.Stopwatch]::StartNew()"),
                "The stopwatch has to be restarted for each project rather than spanning the batch.");
        }

        private static async Task<MatrixResult> RunMatrixAsync(
            string scope,
            bool excludeMacOs = false,
            string? onlyProject = null)
        {
            string outputPath = Path.Combine(Path.GetTempPath(), $"ci-matrix-{Guid.NewGuid():N}.txt");
            try
            {
                ScriptResult result = await RunScriptAsync(scope, excludeMacOs, onlyProject, outputPath)
                    .ConfigureAwait(false);
                Assert.That(result.ExitCode, Is.Zero, result.Output);

                string[] lines = await File.ReadAllLinesAsync(outputPath).ConfigureAwait(false);
                return new MatrixResult(
                    Deserialize<MatrixEntry>(lines, "tests="),
                    Deserialize<BuildEntry>(lines, "builds="),
                    int.Parse(Value(lines, "batch_size="), CultureInfo.InvariantCulture));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        private static List<T> Deserialize<T>(string[] lines, string prefix)
        {
            return JsonSerializer.Deserialize<List<T>>(Value(lines, prefix), s_json) ??
                throw new InvalidOperationException($"'{prefix}' did not carry a JSON array.");
        }

        private static string Value(string[] lines, string prefix)
        {
            string? line = lines.FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
            Assert.That(line, Is.Not.Null, $"The script emitted no '{prefix}' line.");
            return line![prefix.Length..].Trim();
        }

        private static async Task<ScriptResult> RunScriptAsync(
            string scope,
            bool excludeMacOs = false,
            string? onlyProject = null,
            string? githubOutputPath = null)
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
            process.StartInfo.ArgumentList.Add(Path.Combine(root, ".github", "scripts", "get-ci-matrix.ps1"));
            process.StartInfo.ArgumentList.Add("-Scope");
            process.StartInfo.ArgumentList.Add(scope);
            process.StartInfo.ArgumentList.Add("-RepositoryRoot");
            process.StartInfo.ArgumentList.Add(root);
            if (excludeMacOs)
            {
                process.StartInfo.ArgumentList.Add("-ExcludeMacOS");
            }
            if (!string.IsNullOrEmpty(onlyProject))
            {
                process.StartInfo.ArgumentList.Add("-OnlyProject");
                process.StartInfo.ArgumentList.Add(onlyProject);
            }

            // The matrices only reach GITHUB_OUTPUT, so pointing it at a
            // temporary file exercises the same path a workflow run takes.
            if (string.IsNullOrEmpty(githubOutputPath))
            {
                process.StartInfo.Environment.Remove("GITHUB_OUTPUT");
            }
            else
            {
                process.StartInfo.Environment["GITHUB_OUTPUT"] = githubOutputPath;
            }

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
                if (File.Exists(Path.Combine(current, ".github", "scripts", "get-ci-matrix.ps1")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("Could not find the repository root.");
        }

        private sealed record ScriptResult(int ExitCode, string Output);

        private sealed record MatrixResult(
            IReadOnlyList<MatrixEntry> Tests,
            IReadOnlyList<BuildEntry> Builds,
            int BatchSize);

        /// <summary>
        /// One expanded test matrix entry. Deserialized by reflection, hence public.
        /// </summary>
        public sealed record MatrixEntry(
            string Id,
            string Os,
            string Profile,
            string Tier,
            string Framework,
            string Filter,
            bool Coverage,
            string Projects,
            int PerProjectTimeout,
            int TimeoutMinutes);

        /// <summary>
        /// One expanded solution build matrix entry.
        /// </summary>
        public sealed record BuildEntry(string Id, string Os, string Solution, string CustomTestTarget);

        private static readonly JsonSerializerOptions s_json = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
#endif
