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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Performance;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[NonParallelizable]
public sealed class PerformanceWorkflowTests
{
    private const string kHeader =
        "timestamp_utc,target_rate,achieved_rate,total_ops,mean_latency_ms,p50_ms,p90_ms,p99_ms,errors,notes\r\n";
    private const string kFirst =
        "2026-03-04T05:06:07.0000000Z,200,150,900,2.5,1,3,7,4,\"Boiler, calibration\"\r\n";
    private const string kSecond =
        "2026-03-04T05:07:07.0000000Z,250,200,1200,3,2,4,9,5,Pressure\r\n";

    [Test]
    public async Task LegacyImportPublishesOrderedRowsAndDisclosesMissingEvidenceWithoutChangingWorkload()
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        var history = new BenchmarkHistory();
        await using var plugin = new PerformancePlugin(host, history);
        var target = new BenchmarkTarget(
            BenchmarkMode.Write, new NodeId("Temperature", 2), NodeId.Null,
            BuiltInType.Double, ValueRanks.Scalar, null, "Current boiler workload");
        plugin.Target = target;
        plugin.TargetRate = 321;
        plugin.DurationUnit = DurationUnit.Minutes;
        plugin.DurationSeconds = 7;
        plugin.Generator = ValueGenerator.Fixed;
        var changes = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)plugin.RunHistory).CollectionChanged += (_, args) => changes.Add(args.Action);

        plugin.ImportResults(kHeader + kFirst + kSecond);

        Assert.That(plugin.History, Is.SameAs(history));
        Assert.That(changes, Is.EqualTo(new[]
        {
            NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add
        }));
        Assert.That(plugin.RunHistory.Select(row => row.Run.TotalOps), Is.EqualTo(new long[] { 1200, 900 }));
        Assert.That(plugin.RunHistory.Select(row => row.Run.ErrorCount), Is.EqualTo(new long[] { 5, 4 }));
        Assert.That(plugin.RunHistory.Select(row => row.Run.Notes),
            Is.EqualTo(s_legacyImportPublishesOrderedRowsAndDisclosesMissingEvidenceWiExpected));
        Assert.That(plugin.RunHistory[1].Run.P99Ms, Is.EqualTo(7));
        Assert.That(plugin.Status,
            Does.StartWith("● Loaded 2 run(s). Legacy CSV: configuration, timing and distributions are missing."));
        Assert.That(history.SelectedRun, Is.SameAs(plugin.RunHistory[0]));
        Assert.That(history.BaselineRun, Is.Null);
        Assert.That(plugin.Target, Is.SameAs(target));
        Assert.That(plugin.TargetRate, Is.EqualTo(321));
        Assert.That(plugin.DurationSeconds, Is.EqualTo(7));
        Assert.That(plugin.EffectiveDuration, Is.EqualTo(TimeSpan.FromMinutes(7)));
        Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Fixed));
        Assert.That(plugin.IsRunning, Is.False);
        Assert.That(plugin.HistogramTotal, Is.Zero);
        Assert.That(plugin.TryDequeueThroughput(out double seconds, out double rate), Is.False);
        Assert.That(seconds, Is.Zero);
        Assert.That(rate, Is.Zero);
        using JsonDocument exported = JsonDocument.Parse(plugin.ExportResults());
        JsonElement runs = exported.RootElement.GetProperty("runs");
        Assert.That(runs.GetArrayLength(), Is.EqualTo(2));
        Assert.That(runs[0].GetProperty("totalOps").GetInt64(), Is.EqualTo(1200));
        Assert.That(runs[1].GetProperty("notes").GetString(), Is.EqualTo("Boiler, calibration"));
        Assert.That(runs[0].GetProperty("distribution").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [TestCase("partialCsv")]
    [TestCase("unsupportedJson")]
    [TestCase("empty")]
    public async Task MalformedImportIsAtomicAcrossHistorySelectionAndConfiguredIntent(string malformed)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        await using var plugin = new PerformancePlugin(host);
        plugin.ImportResults(kHeader + kFirst);
        BenchmarkRunRow original = plugin.RunHistory.Single();
        plugin.History.UseSelectedAsBaselineCommand.Execute(null);
        plugin.TargetRate = 433;
        plugin.Generator = ValueGenerator.Sequential;
        string input = malformed switch
        {
            "partialCsv" => kHeader + kSecond + "broken,row\r\n",
            "unsupportedJson" => "{\"format\":\"unknown\",\"version\":99,\"runs\":[]}",
            _ => string.Empty
        };

        Assert.That(() => plugin.ImportResults(input), Throws.TypeOf<FormatException>());

        Assert.That(plugin.RunHistory.Single(), Is.SameAs(original));
        Assert.That(plugin.History.SelectedRun, Is.SameAs(original));
        Assert.That(plugin.History.BaselineRun, Is.SameAs(original));
        Assert.That(plugin.TargetRate, Is.EqualTo(433));
        Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Sequential));
        Assert.That(plugin.Status, Does.StartWith("● Load failed:"));
        if (malformed == "partialCsv")
        {
            Assert.That(plugin.Status, Is.EqualTo("● Load failed: CSV record 3 is malformed; no history was loaded."));
        }
        Assert.That(plugin.RunCommand.CanExecute(null), Is.False);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [Test]
    public async Task ResetPreservesCompletedHistoryAndWorkloadButDisposalPreventsFurtherMutationCommands()
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = HistorianWorkflowTests.Host(context);
        var plugin = new PerformancePlugin(host);
        plugin.ImportResults(kHeader + kFirst + kSecond);
        plugin.TargetRate = 750;
        plugin.DurationSeconds = 5;
        plugin.DurationUnit = DurationUnit.Minutes;
        plugin.Generator = ValueGenerator.Sequential;
        BenchmarkRunRow selected = plugin.RunHistory[1];
        plugin.History.SelectedRun = selected;
        plugin.History.UseSelectedAsBaselineCommand.Execute(null);
        plugin.WasReset = false;

        plugin.ResetCommand.Execute(null);

        Assert.That(plugin.WasReset, Is.True);
        Assert.That(plugin.RunHistory, Has.Count.EqualTo(2));
        Assert.That(plugin.History.SelectedRun, Is.SameAs(selected));
        Assert.That(plugin.History.BaselineRun, Is.SameAs(selected));
        Assert.That(plugin.TargetRate, Is.EqualTo(750));
        Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Sequential));
        Assert.That(plugin.EffectiveDuration, Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(plugin.TotalOpsText, Is.EqualTo("0"));
        Assert.That(plugin.MeanRateText, Is.EqualTo("0"));
        Assert.That(plugin.ErrorsText, Is.EqualTo("0"));
        Assert.That(new[] { plugin.P50Text, plugin.P95Text, plugin.P99Text, plugin.MaxLatencyText },
            Is.All.EqualTo("—"));
        Assert.That(plugin.GetHistogramSnapshot().ToArray(), Has.Length.EqualTo(71).And.All.Zero);
        await plugin.DisposeAsync().ConfigureAwait(false);
        await plugin.DisposeAsync().ConfigureAwait(false);
        Assert.That(plugin.ConfigurationEditable, Is.False);
        Assert.That(plugin.ResetCommand.CanExecute(null), Is.False);
        Assert.That(plugin.LoadResultsCommand.CanExecute(null), Is.False);
        Assert.That(plugin.PickTargetCommand.CanExecute(null), Is.False);
        Assert.That(() => plugin.ImportResults(kHeader + kSecond), Throws.InvalidOperationException
            .With.Message.EqualTo("Stop the workload before importing benchmark history."));
        Assert.That(plugin.RunHistory[1], Is.SameAs(selected));
    }

    [TestCase(0, 59, "59 s")]
    [TestCase(0, 60, "1 min")]
    [TestCase(1, 59, "59 min")]
    [TestCase(1, 60, "1 h")]
    [TestCase(2, 2, "2 h")]
    [Platform("Win,Linux")]
    public Task SettingsCommandChangesActualBoundWorkloadAndCloseRetainsIntentWithoutStartingARun(
        int unit, int duration, string display)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new PerformancePlugin(host);
            Task command = Task.CompletedTask;
            PerformanceSettingsDialog dialog = await DesktopInteraction.OpenedAsync<PerformanceSettingsDialog>(
                () => command = plugin.OpenSettingsDialogCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(dialog.DataContext, Is.SameAs(plugin));
                Slider rate = DesktopInteraction.Control<Slider>(dialog, "RateSlider");
                rate.Value = 450;
                var durationInput = dialog.GetLogicalDescendants().OfType<NumericUpDown>().Single();
                durationInput.Value = duration;
                ComboBox units = dialog.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(combo => ReferenceEquals(combo.ItemsSource, plugin.DurationUnitOptions));
                units.SelectedItem = (DurationUnit)unit;
                ComboBox generator = dialog.GetLogicalDescendants().OfType<ComboBox>()
                    .Single(combo => ReferenceEquals(combo.ItemsSource, plugin.GeneratorOptions));
                generator.SelectedItem = ValueGenerator.Fixed;
                CheckBox burst = dialog.GetLogicalDescendants().OfType<CheckBox>()
                    .Single(box => Equals(box.Content, "Unbounded burst"));
                burst.IsChecked = true;
                Assert.That(rate.IsEnabled, Is.False);
                Assert.That(plugin.RateEditable, Is.False);
                burst.IsChecked = false;
                Assert.That(rate.IsEnabled, Is.True);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CloseButton"));
                await command.ConfigureAwait(true);

                Assert.That(plugin.TargetRate, Is.EqualTo(450));
                Assert.That(plugin.TargetRateText, Is.EqualTo("450"));
                Assert.That(plugin.Generator, Is.EqualTo(ValueGenerator.Fixed));
                Assert.That(plugin.DurationSeconds, Is.EqualTo(duration));
                Assert.That(plugin.DurationUnit, Is.EqualTo((DurationUnit)unit));
                Assert.That(plugin.EffectiveDurationText, Is.EqualTo(display));
                Assert.That(plugin.UnboundedBurst, Is.False);
                Assert.That(plugin.IsRunning, Is.False);
                Assert.That(plugin.RunHistory, Is.Empty);
                Assert.That(plugin.StopCommand.CanExecute(null), Is.False);
                Assert.That(context.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
            }
        });
    }

    private static readonly string[] s_legacyImportPublishesOrderedRowsAndDisclosesMissingEvidenceWiExpected =
    [
        "Pressure",
        "Boiler, calibration",
    ];
}
