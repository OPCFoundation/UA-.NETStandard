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

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;
using UaLens.Tests.Observe;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class PerformanceComparisonTests
{
    [Test]
    public void BaselineSelectionIsIndependentOfNewestThreeHighlighting()
    {
        var history = new BenchmarkHistory();
        for (int i = 0; i < 5; i++)
        {
            history.Add(BenchmarkComparisonTestData.Captured(i));
        }
        history.SelectedRun = history.Runs[^1];
        history.UseSelectedAsBaselineCommand.Execute(null);
        BenchmarkRun baseline = history.BaselineRun!.Run;
        history.SelectedRun = history.Runs[1];
        history.HighlightLatestThree = true;

        Assert.That(history.Runs.Count(row => row.IsHighlighted), Is.EqualTo(3));
        Assert.That(history.BaselineRun.Run.Id, Is.EqualTo(baseline.Id));
        Assert.That(history.BaselineRun.IsHighlighted, Is.False);
        Assert.That(history.BaselineRun.IsBaseline, Is.True);
        Assert.That(history.Comparison!.Baseline, Is.SameAs(baseline));
        Assert.That(history.Comparison.Selected, Is.SameAs(history.Runs[1].Run));
        Assert.That(history.Comparison.TotalOps.AbsoluteDelta, Is.Zero);
        history.HighlightLatestThree = false;
        Assert.That(history.Runs.Any(row => row.IsHighlighted), Is.False);
        Assert.That(history.HasComparison, Is.True);
    }

    [Test]
    public void CapacityEvictsTheChosenBaselineWithoutSilentlyReplacingIt()
    {
        var history = new BenchmarkHistory();
        history.Add(BenchmarkComparisonTestData.Captured());
        history.UseSelectedAsBaselineCommand.Execute(null);
        Guid baseline = history.BaselineRun!.Run.Id;
        for (int i = 1; i < BenchmarkHistory.Capacity; i++)
        {
            history.Add(BenchmarkComparisonTestData.Captured(i));
        }
        Assert.That(history.BaselineRun!.Run.Id, Is.EqualTo(baseline));
        Assert.That(history.Runs, Has.Count.EqualTo(64));
        BenchmarkRun newest = BenchmarkComparisonTestData.Captured(64);
        history.Add(newest);

        Assert.That(history.Runs, Has.Count.EqualTo(64));
        Assert.That(history.Runs[0].Run, Is.SameAs(newest));
        Assert.That(history.SelectedRun!.Run, Is.SameAs(newest));
        Assert.That(history.BaselineRun, Is.Null);
        Assert.That(history.Comparison, Is.Null);
        Assert.That(history.Status, Does.Contain("baseline was evicted"));
        Assert.That(history.Runs.Any(row => row.Run.Id == baseline), Is.False);
    }

    [Test]
    public void ArchiveRestoreRetainsExactSelectionsAndDisclosesEvictedSelections()
    {
        var runs = new BenchmarkRun[65];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = BenchmarkComparisonTestData.Captured(i);
        }
        var history = new BenchmarkHistory();
        history.Replace(new BenchmarkArchive(runs, runs[1].Id, runs[15].Id));
        Assert.That(history.BaselineRun!.Run, Is.SameAs(runs[1]));
        Assert.That(history.SelectedRun!.Run, Is.SameAs(runs[15]));
        Assert.That(history.DiscardedOnImport, Is.EqualTo(1));
        BenchmarkArchive captured = history.Capture();
        Assert.That(captured.Runs.Count, Is.EqualTo(64));
        Assert.That(captured.BaselineId, Is.EqualTo(runs[1].Id));
        Assert.That(captured.SelectedId, Is.EqualTo(runs[15].Id));

        history.Replace(new BenchmarkArchive(runs, runs[0].Id, runs[0].Id));
        Assert.That(history.HasBaseline, Is.False);
        Assert.That(history.HasComparison, Is.False);
        Assert.That(history.SelectedRun!.Run, Is.SameAs(runs[64]));
        Assert.That(history.Status,
            Does.Contain("baseline was not retained").And.Contain("selected run was not retained"));
    }

    [Test]
    public void RejectedReplacementPreservesHistoryAndCommandSelection()
    {
        BenchmarkRun run = BenchmarkComparisonTestData.Captured();
        var history = new BenchmarkHistory();
        history.Add(run);
        history.UseSelectedAsBaselineCommand.Execute(null);
        Assert.That(() => history.Replace(new BenchmarkArchive([run, run])), Throws.TypeOf<FormatException>());
        Assert.That(() => history.Add(run), Throws.TypeOf<ArgumentException>());
        Assert.That(() => history.SelectedRun = new BenchmarkRunRow(BenchmarkComparisonTestData.Captured(1)),
            Throws.TypeOf<ArgumentException>());
        Assert.That(history.Runs, Has.Count.EqualTo(1));
        Assert.That(history.SelectedRun, Is.SameAs(history.BaselineRun));
        Assert.That(history.SelectedRun!.Run, Is.SameAs(run));
        Assert.That(history.Status, Does.Contain("all deltas are zero"));
        Assert.That(history.UseSelectedAsBaselineCommand.CanExecute(null), Is.False);

        history.ClearBaselineCommand.Execute(null);
        Assert.That(history.Comparison, Is.Null);
        Assert.That(history.ClearBaselineCommand.CanExecute(null), Is.False);
        Assert.That(history.UseSelectedAsBaselineCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void EmptyArchiveClearsSelectionsWithoutStartingAnyReplacement()
    {
        var history = new BenchmarkHistory();
        history.Add(BenchmarkComparisonTestData.Captured());
        history.UseSelectedAsBaselineCommand.Execute(null);
        history.Replace(new BenchmarkArchive(ArrayOf<BenchmarkRun>.Empty));

        Assert.That(history.Runs, Is.Empty);
        Assert.That(history.SelectedRun, Is.Null);
        Assert.That(history.BaselineRun, Is.Null);
        Assert.That(history.HasComparison, Is.False);
        Assert.That(history.UseSelectedAsBaselineCommand.CanExecute(null), Is.False);
        Assert.That(history.Status, Does.Contain("Select a run"));
    }

    [Test]
    public async Task ImportAndWorkspaceRestoreNeverStartWorkOrOverwriteTheSelectedWorkloadAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var target = new BenchmarkTarget(
                BenchmarkMode.Write, new NodeId(100u), NodeId.Null, BuiltInType.Double,
                ValueRanks.Scalar, [], "Configured target");
            var plugin = new PerformancePlugin(host.Host)
            {
                Target = target, Generator = ValueGenerator.Sequential, TargetRate = 250,
                DurationSeconds = 2, DurationUnit = DurationUnit.Seconds
            };
            await using (plugin.ConfigureAwait(false))
            {
                BenchmarkRun run = BenchmarkComparisonTestData.Captured();
                plugin.History.Add(run);
                plugin.History.UseSelectedAsBaselineCommand.Execute(null);
                JsonElement configuration = plugin.CaptureState();
                Assert.That(configuration.GetRawText(), Does.Not.Contain(run.Id.ToString("D")));
                string exported = plugin.ExportResults();
                plugin.ImportResults(exported);
                Assert.That(plugin.History.BaselineRun!.Run.Id, Is.EqualTo(run.Id));
                Assert.That(plugin.Target, Is.SameAs(target));
                Assert.That(plugin.TargetRate, Is.EqualTo(250));
                Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Sequential));
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.RunCommand.CanExecute(null), Is.False);
                Assert.That(plugin.Status, Does.Contain("no run was started"));

                using var canceled = new CancellationTokenSource();
                await canceled.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(() => plugin.RestoreStateAsync(configuration, canceled.Token),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(plugin.Target, Is.SameAs(target));
                await plugin.RestoreStateAsync(configuration).ConfigureAwait(false);
                Assert.That(plugin.Target!.NodeId, Is.EqualTo(target.NodeId));
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.Status, Does.Contain("Explicit Run is required"));
                Assert.That(() => plugin.ImportResults("corrupt,row"), Throws.TypeOf<FormatException>());
                Assert.That(plugin.History.Runs, Has.Count.EqualTo(1));
                Assert.That(plugin.History.BaselineRun!.Run.Id, Is.EqualTo(run.Id));
                Assert.That(plugin.Status, Does.Contain("Load failed"));
            }
        }
    }
}
