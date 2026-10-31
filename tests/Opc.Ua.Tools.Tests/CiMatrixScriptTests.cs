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
            Assert.That(matrix.Tests.Select(entry => entry.Id), Is.Unique);
            Assert.That(matrix.Tests.Select(entry => entry.Name), Is.Unique);
            Assert.That(
                matrix.Tests.Count + matrix.Builds.Count,
                Is.LessThanOrEqualTo(256),
                "The expanded matrix must fit GitHub's per-run job ceiling.");
        }

        /// <summary>
        /// The batch target is computed from the whole profile table so that
        /// turning macOS off does not reshuffle which projects run together on
        /// the remaining runners, which would make two runs incomparable.
        /// </summary>
        [Test]
        public async Task ExcludingMacOsKeepsTheRemainingBatchesIdenticalAsync()
        {
            MatrixResult withMac = await RunMatrixAsync("pr").ConfigureAwait(false);
            MatrixResult withoutMac = await RunMatrixAsync("pr", excludeMacOs: true).ConfigureAwait(false);

            Assert.That(withoutMac.BatchTargetMinutes, Is.EqualTo(withMac.BatchTargetMinutes));
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
        /// Packing by duration and filter sharding must never drop a project:
        /// each profile schedules every project in its tier, with repeated
        /// projects limited to their declared, disjoint shards.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task EveryProfileRunsEachOfItsProjectsExactlyOnceAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            HashSet<string> mainline = matrix.Tests
                .Where(entry => entry.Tier == "mainline")
                .SelectMany(entry => entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);

            foreach (IGrouping<string, MatrixEntry> profile in matrix.Tests.GroupBy(entry => entry.Profile))
            {
                string[] projects = profile
                    .SelectMany(entry => entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    .ToArray();

                if (profile.First().Tier is "mainline" or "long-running")
                {
                    Assert.That(
                        projects.Distinct(StringComparer.Ordinal),
                        Is.EquivalentTo(mainline),
                        $"Profile '{profile.Key}' does not cover the mainline project set.");
                }

                foreach (IGrouping<string, MatrixEntry> project in profile
                    .SelectMany(entry => entry.Projects
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(path => (Path: path, Entry: entry)))
                    .GroupBy(item => item.Path, item => item.Entry, StringComparer.Ordinal))
                {
                    Assert.That(
                        project.Select(entry => entry.Shard),
                        Is.Unique,
                        $"Profile '{profile.Key}' schedules a shard of '{project.Key}' more than once.");
                }
            }
        }

        /// <summary>
        /// Batches are packed up to the target from the duration table. Only a
        /// project that is heavier than the target on its own may exceed it,
        /// and then it has to run alone rather than drag others past it.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task BatchesStayWithinTheTargetUnlessOneProjectExceedsItAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            Assert.That(matrix.BatchTargetMinutes, Is.Positive);
            string[] overweight = matrix.Tests
                .Where(entry => entry.PackedMinutes > matrix.BatchTargetMinutes &&
                    entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length > 1)
                .Select(entry => $"{entry.Id}: {entry.PackedMinutes}min")
                .ToArray();

            Assert.That(overweight, Is.Empty);
            Assert.That(matrix.Tests.All(entry => entry.PackedMinutes > 0), Is.True);
        }

        /// <summary>
        /// The point of packing by duration is to stop paying the per-batch
        /// cold build for every second project. Guard against a regression to
        /// one or two projects per job, which nearly doubles the pull-request
        /// runner minutes without testing anything more.
        /// </summary>
        [Test]
        public async Task PullRequestBatchesAmortizeTheirFixedCostAsync()
        {
            MatrixResult matrix = await RunMatrixAsync("pr").ConfigureAwait(false);

            string[] isolatedProjects =
            [
                "Opc.Ua.Server.Tests",
                "Opc.Ua.Types.Tests",
                "Opc.Ua.WotCon.Samples.Tests",
                "Opc.Ua.WotCon.Tests"
            ];
            foreach (IGrouping<string, MatrixEntry> profile in matrix.Tests.GroupBy(entry => entry.Profile))
            {
                MatrixEntry[] packed = profile
                    .Where(entry => entry.Shard == "all" &&
                        !isolatedProjects.Any(project => entry.Projects.Contains(project, StringComparison.Ordinal)))
                    .ToArray();
                int projects = packed.Sum(entry =>
                    entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.That(
                    packed,
                    Has.Length.LessThanOrEqualTo(projects / 3),
                    $"Profile '{profile.Key}' spreads {projects} packable projects over {packed.Length} jobs.");
            }
        }

        /// <summary>
        /// A weight for a project that no longer exists is harmless to the
        /// matrix but means the table is not being maintained; every weight has
        /// to name a test project that is on disk.
        /// </summary>
        [Test]
        public async Task DurationTableNamesOnlyExistingProjectsAsync()
        {
            string root = FindRepositoryRoot();
            string table = await File.ReadAllTextAsync(Path.Combine(root, ".github", "ci-test-durations.json"))
                .ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(table);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };
            HashSet<string> onDisk = Directory.EnumerateFiles(root, "*.Tests.csproj", options)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(document.RootElement.GetProperty("defaultMinutes").GetInt32(), Is.Positive);
            string[] stale = document.RootElement.GetProperty("projects").EnumerateObject()
                .Where(project => !onDisk.Contains(project.Name))
                .Select(project => project.Name)
                .ToArray();
            Assert.That(stale, Is.Empty, "Remove these from .github/ci-test-durations.json.");
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
                full.Tests.Where(entry => entry.Tier != "mainline").All(entry =>
                    !entry.Filter.Contains("TestCategory!=LongRunning", StringComparison.Ordinal) &&
                    !entry.Filter.Contains("TestCategory!=Stress", StringComparison.Ordinal)),
                Is.True,
                "A tier that exists to lift category exclusions must not restore them.");
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
        public async Task JobTimeoutIncludesEveryTestBudgetAndBuildOverheadAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            string[] underBudget = matrix.Tests
                .Where(entry =>
                    entry.TimeoutMinutes <
                        20 + entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length *
                        entry.PerProjectTimeout)
                .Select(entry => $"{entry.Id} allows {entry.TimeoutMinutes}min for " +
                    $"20 + {entry.Projects.Split(';', StringSplitOptions.RemoveEmptyEntries).Length} x " +
                    $"{entry.PerProjectTimeout}min")
                .ToArray();

            Assert.That(underBudget, Is.Empty);
            Assert.That(matrix.Tests.All(entry => entry.PerProjectTimeout > 0), Is.True);
        }

        /// <summary>
        /// Every workload, including formerly extended sample and integration
        /// projects, uses the same 45-minute test-execution ceiling.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task EveryProjectUsesTheStandardFortyFiveMinuteCeilingAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);

            string[] exceptions = matrix.Tests
                .Where(entry => entry.PerProjectTimeout != 45)
                .Select(entry => $"{entry.Id}: {entry.PerProjectTimeout}min")
                .ToArray();

            Assert.That(exceptions, Is.Empty);
        }

        /// <summary>
        /// The long sample scenarios run as separate jobs with the standard
        /// budget, and the filters cover distinct parts of the suite.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task SampleScenariosAreSplitIntoBoundedWorkloadsAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);
            MatrixEntry[] samples = matrix.Tests.Where(entry =>
                entry.Projects.Contains("Opc.Ua.WotCon.Samples.Tests", StringComparison.Ordinal)).ToArray();

            Assert.That(samples, Is.Not.Empty);
            foreach (IGrouping<string, MatrixEntry> profile in samples.GroupBy(entry => entry.Profile))
            {
                string[] expectedShards = profile.First().Framework == "net48"
                    ? ["all"]
                    : ["sample-core", "sample-generation", "sample-native-comparisons"];
                Assert.That(profile.Select(entry => entry.Shard), Is.EquivalentTo(expectedShards));
                if (profile.First().Framework != "net48")
                {
                    MatrixEntry generation = profile.Single(entry => entry.Shard == "sample-generation");
                    MatrixEntry nativeComparisons = profile.Single(entry => entry.Shard == "sample-native-comparisons");
                    MatrixEntry core = profile.Single(entry => entry.Shard == "sample-core");
                    Assert.That(
                        generation.Filter,
                        Does.Contain("RealSamplesAggregateSubscribeAndReplaceGenerationAsync"));
                    Assert.That(nativeComparisons.Filter, Does.Contain("WotPumpInstanceMatchesNativePumpSubsetAsync"));
                    Assert.That(
                        nativeComparisons.Filter,
                        Does.Contain("CompanionTypeDefinitionsMatchNativePumpServerAsync"));
                    Assert.That(core.Filter, Does.Contain("FullyQualifiedName!~"));
                }
                foreach (MatrixEntry sample in profile)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(sample.Projects, Does.Not.Contain(";"));
                        Assert.That(sample.PerProjectTimeout, Is.EqualTo(45));
                        Assert.That(sample.TimeoutMinutes, Is.EqualTo(65));
                        if (sample.Framework != "net48")
                        {
                            Assert.That(sample.Filter, Does.Contain("FullyQualifiedName"));
                        }
                    });
                }
            }
        }

        /// <summary>
        /// The oversized net48 server suite is split into exclusive filters,
        /// while the other frameworks retain their existing single workload.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task ServerNet48FiltersPartitionEligibleTestsExactlyOnceAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);
            MatrixEntry[] server = matrix.Tests.Where(entry =>
                entry.Projects.Contains("Opc.Ua.Server.Tests.csproj", StringComparison.Ordinal)).ToArray();
            Assert.That(server, Is.Not.Empty);

            string[] serverShards =
            [
                "server-node-lifecycle-prepared",
                "server-node-lifecycle",
                "server-node-management",
                "server-historian",
                "server-subscriptions",
                "server-sessions",
                "server-configuration",
                "server-runtime",
                "server-transport",
                "server-other"
            ];
            foreach (IGrouping<string, MatrixEntry> profile in server.GroupBy(entry => entry.Profile))
            {
                string[] expectedShards = profile.First().Framework == "net48" ? serverShards : ["all"];
                Assert.That(profile.Select(entry => entry.Shard), Is.EquivalentTo(expectedShards));
            }

            (string TestName, string ExpectedShard)[] testCases =
            [
                ("Opc.Ua.Server.Tests.NodeManager.NodeManagerLifecycleTests.PreparedBatchDecisionAsync",
                    "server-node-lifecycle-prepared"),
                ("Opc.Ua.Server.Tests.NodeManager.NodeManagerLifecycleTests.TestAsync", "server-node-lifecycle"),
                ("Opc.Ua.Server.Tests.NodeManager.NodeManagerLifecycleTests.RejectsPreparedBatchAsync",
                    "server-node-lifecycle"),
                ("Opc.Ua.Server.Tests.Hosting.HostedNodeManagerLifecycleTests.PreparedBatchAsync",
                    "server-node-management"),
                ("Opc.Ua.Server.Tests.NodeManager.NodeManagerLifecycleTestsExtra.PreparedBatchAsync",
                    "server-node-management"),
                ("Opc.Ua.Server.Tests.Fluent.FluentNodeManagerBasePipelineTests.TestAsync", "server-node-management"),
                ("Opc.Ua.Server.Tests.Historian.HistorianDispatcherTests.TestAsync", "server-historian"),
                ("Opc.Ua.Server.Tests.SubscriptionTests.TransferSubscriptionAsync", "server-subscriptions"),
                ("Opc.Ua.Server.Tests.Session.SessionTests.TestAsync", "server-sessions"),
                ("Opc.Ua.Server.Tests.Configuration.TrustListTests.TestAsync", "server-configuration"),
                ("Opc.Ua.Server.Tests.RuntimeNodeSet.RuntimeNodeSetTests.TestAsync", "server-runtime"),
                ("Opc.Ua.Server.Tests.ReverseConnectServerTests.TestAsync", "server-transport"),
                ("Opc.Ua.Server.Tests.RequestTableTests.TestAsync", "server-other")
            ];
            foreach (IGrouping<string, MatrixEntry> profile in server
                .Where(entry => entry.Framework == "net48")
                .GroupBy(entry => entry.Profile))
            {
                foreach ((string testName, string expectedShard) in testCases)
                {
                    string[] matches = profile
                        .Where(entry => MatchesServerFilter(entry.Filter, testName))
                        .Select(entry => entry.Shard)
                        .ToArray();
                    Assert.That(
                        matches,
                        Is.EqualTo(new[] { expectedShard }),
                        $"Profile '{profile.Key}', test '{testName}'.");
                }
            }
        }

        /// <summary>
        /// Each integration project stays within the standard workload budget,
        /// and the WoT suite is partitioned by namespace and integration
        /// category where the framework run is the longest.
        /// </summary>
        [TestCase("pr")]
        [TestCase("full")]
        public async Task IntegrationProjectsUseBoundedWorkloadsAsync(string scope)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);
            MatrixEntry[] integration = matrix.Tests.Where(entry =>
                entry.Projects.Contains("Opc.Ua.Server.Tests", StringComparison.Ordinal) ||
                entry.Projects.Contains("Opc.Ua.WotCon.Tests", StringComparison.Ordinal) ||
                entry.Projects.Contains("Opc.Ua.Types.Tests", StringComparison.Ordinal)).ToArray();
            Assert.That(integration, Is.Not.Empty);
            foreach (MatrixEntry entry in integration)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(entry.Projects, Does.Not.Contain(";"));
                    Assert.That(entry.PerProjectTimeout, Is.EqualTo(45));
                    Assert.That(entry.TimeoutMinutes, Is.EqualTo(65));
                    Assert.That(entry.HangTimeout, Is.EqualTo(
                        entry.Tier == "mainline" ? "10m" : "30m"));
                });
            }

            foreach (IGrouping<string, MatrixEntry> profile in integration
                .Where(entry => entry.Projects.Contains("Opc.Ua.WotCon.Tests", StringComparison.Ordinal))
                .GroupBy(entry => entry.Profile))
            {
                string[] expectedShards = profile.First().Framework == "net48"
                    ? [
                        "wot-materialization-core",
                        "wot-materialization-integration",
                        "wot-other-core",
                        "wot-other-integration"
                    ]
                    : ["wot-materialization", "wot-other"];
                Assert.That(
                    profile.Select(entry => entry.Shard),
                    Is.EquivalentTo(expectedShards));
            }
        }

        /// <summary>
        /// WoT shard filters must select every eligible test exactly once on
        /// both framework branches, preserving each profile's category exclusions.
        /// </summary>
        [TestCase("pr", false, false)]
        [TestCase("pr", false, true)]
        [TestCase("pr", true, false)]
        [TestCase("pr", true, true)]
        [TestCase("full", false, false)]
        [TestCase("full", false, true)]
        [TestCase("full", true, false)]
        [TestCase("full", true, true)]
        public async Task WotFiltersPartitionEligibleTestsExactlyOnceAsync(
            string scope,
            bool materialization,
            bool integration)
        {
            MatrixResult matrix = await RunMatrixAsync(scope).ConfigureAwait(false);
            MatrixEntry[] wot = matrix.Tests.Where(entry =>
                entry.Projects.Contains("Opc.Ua.WotCon.Tests.csproj", StringComparison.Ordinal)).ToArray();
            Assert.That(wot.Any(entry => entry.Framework == "net48"), Is.True);
            Assert.That(wot.Any(entry => entry.Framework != "net48"), Is.True);

            string testName = materialization
                ? "Opc.Ua.WotCon.Tests.Materialization.ExampleTests.ExampleAsync"
                : "Opc.Ua.WotCon.Tests.Registry.ExampleTests.ExampleAsync";
            foreach (IGrouping<string, MatrixEntry> profile in wot.GroupBy(entry => entry.Profile))
            {
                foreach (string category in new[] { string.Empty, "LongRunning", "Stress" })
                {
                    string[] categories = integration ? [category, "Integration"] : [category];
                    bool allowed = profile.First().Tier != "mainline" || category.Length == 0;
                    string[] matches = profile
                        .Where(entry => MatchesWotFilter(entry.Filter, testName, categories))
                        .Select(entry => entry.Shard)
                        .ToArray();
                    string expectedShard = !materialization
                        ? profile.First().Framework != "net48"
                            ? "wot-other"
                            : integration ? "wot-other-integration" : "wot-other-core"
                        : profile.First().Framework != "net48"
                            ? "wot-materialization"
                            : integration ? "wot-materialization-integration" : "wot-materialization-core";

                    Assert.That(
                        matches,
                        Is.EqualTo(allowed ? new[] { expectedShard } : Array.Empty<string>()),
                        $"Profile '{profile.Key}', test '{testName}', categories '{string.Join(",", categories)}'.");
                }
            }
        }

        /// <summary>
        /// Each test invocation gets its full budget after the build completes;
        /// build time is instead covered by the fixed overhead in the job
        /// timeout.
        /// </summary>
        [Test]
        public async Task ExecutorBudgetsTestExecutionSeparatelyFromBuildAsync()
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
                invocations[0],
                Does.Contain("$null 0"),
                "The build is bounded by the job timeout rather than consuming the test budget.");
            Assert.That(
                invocations[1],
                Does.Contain("$testBudget $PerProjectTimeoutMinutes"),
                "The full project ceiling is applied to test execution.");
            Assert.That(
                source,
                Does.Contain("$testBudget = [System.Diagnostics.Stopwatch]::StartNew()"),
                "The test stopwatch starts after the build completes.");
            Assert.That(
                source,
                Does.Contain("$buildBudget = [System.Diagnostics.Stopwatch]::StartNew()"),
                "Build duration remains available for reporting.");
            Assert.That(invocations[1], Does.Contain("$logPath $properties.TargetDir"));
            Assert.That(source, Does.Contain("$startInfo = New-DotnetStartInfo $arguments $IsLinux"));
            Assert.That(source, Does.Contain("$process.StartInfo = $startInfo"));
            Assert.That(source, Does.Contain("$process.Kill($true)"));
        }

        /// <summary>
        /// Evaluates the conjunctions emitted for WoT tests; unsupported terms
        /// fail rather than silently weakening the partition assertions.
        /// </summary>
        private static bool MatchesWotFilter(string filter, string testName, string[] categories)
        {
            return filter.Split('&').All(term => term switch
            {
                "" => true,
                _ when term.StartsWith("FullyQualifiedName!~", StringComparison.Ordinal) =>
                    !testName.Contains(term["FullyQualifiedName!~".Length..], StringComparison.OrdinalIgnoreCase),
                _ when term.StartsWith("FullyQualifiedName~", StringComparison.Ordinal) =>
                    testName.Contains(term["FullyQualifiedName~".Length..], StringComparison.OrdinalIgnoreCase),
                _ when term.StartsWith("TestCategory!=", StringComparison.Ordinal) =>
                    !categories.Contains(term["TestCategory!=".Length..], StringComparer.OrdinalIgnoreCase),
                _ when term.StartsWith("TestCategory=", StringComparison.Ordinal) =>
                    categories.Contains(term["TestCategory=".Length..], StringComparer.OrdinalIgnoreCase),
                _ => throw new InvalidOperationException($"Unsupported WoT filter term '{term}'.")
            });
        }

        /// <summary>
        /// Evaluates the disjoint name-filter expressions emitted for the
        /// oversized server suite.
        /// </summary>
        private static bool MatchesServerFilter(string filter, string testName)
        {
            string[] categories = [];
            return filter.Split('&').All(term =>
            {
                string expression = term.Trim('(', ')');
                if (expression.StartsWith("TestCategory!=", StringComparison.Ordinal))
                {
                    return !categories.Contains(expression["TestCategory!=".Length..], StringComparer.OrdinalIgnoreCase);
                }
                if (expression.StartsWith("TestCategory=", StringComparison.Ordinal))
                {
                    return categories.Contains(expression["TestCategory=".Length..], StringComparer.OrdinalIgnoreCase);
                }

                string[] alternatives = expression.Split('|');
                bool matches = alternatives.Any(alternative =>
                {
                    const string includePrefix = "FullyQualifiedName~";
                    const string excludePrefix = "FullyQualifiedName!~";
                    bool excluded;
                    string needle;
                    if (alternative.StartsWith(excludePrefix, StringComparison.Ordinal))
                    {
                        excluded = true;
                        needle = alternative[excludePrefix.Length..];
                    }
                    else if (alternative.StartsWith(includePrefix, StringComparison.Ordinal))
                    {
                        excluded = false;
                        needle = alternative[includePrefix.Length..];
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unsupported server filter term '{alternative}'.");
                    }

                    bool contains = testName.Contains(needle, StringComparison.OrdinalIgnoreCase);
                    return excluded ? !contains : contains;
                });
                return matches;
            });
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
                    int.Parse(Value(lines, "batch_target_minutes="), CultureInfo.InvariantCulture));
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
            string? configuredRoot = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot))
            {
                if (!File.Exists(Path.Combine(configuredRoot, ".github", "scripts", "get-ci-matrix.ps1")))
                {
                    throw new DirectoryNotFoundException("The configured root has no CI matrix script.");
                }
                return configuredRoot;
            }
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
            int BatchTargetMinutes);

        /// <summary>
        /// One expanded test matrix entry. Deserialized by reflection, hence public.
        /// </summary>
        /// <param name="PerProjectTimeout">The per project timeout.</param>
        /// <param name="TimeoutMinutes">The timeout minutes.</param>
        /// <param name="PackedMinutes">The packed minutes.</param>
        /// <param name="HangTimeout">The hang timeout.</param>
        /// <param name="Id">The matrix entry identifier.</param>
        /// <param name="Name">The display name of the matrix entry.</param>
        /// <param name="Os">The operating system of the runner.</param>
        /// <param name="Profile">The test profile of the entry.</param>
        /// <param name="Tier">The test tier of the entry.</param>
        /// <param name="Framework">The target framework of the entry.</param>
        /// <param name="Filter">The test filter expression of the entry.</param>
        /// <param name="Coverage">The coverage flag supplied by the caller.</param>
        /// <param name="Projects">The test projects run by the entry.</param>
        /// <param name="Shard">The workload shard name.</param>
        public sealed record MatrixEntry(
            string Id,
            string Name,
            string Os,
            string Profile,
            string Tier,
            string Framework,
            string Filter,
            bool Coverage,
            string Projects,
            string Shard,
            int PerProjectTimeout,
            int TimeoutMinutes,
            int PackedMinutes,
            string HangTimeout);

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
