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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Observe;

[TestFixture]
[NonParallelizable]
public sealed class HistorianWorkflowTests
{
    private static readonly DateTime s_time = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    [TestCase(NodeClass.Variable, true)]
    [TestCase(NodeClass.Object, false)]
    [TestCase(NodeClass.Method, false)]
    [TestCase(NodeClass.VariableType, false)]
    public async Task VariableSelectionSeedsTargetButOtherClassesDoNot(NodeClass nodeClass, bool seeded)
    {
        await using var context = new DesktopConnectionContext();
        var selected = new NodeViewModel(context.Browser, new NodeId("Boiler", 2),
            new NodeId("Temperature", 2), "Boiler temperature", nodeClass);
        await using PluginHost host = Host(context, selected);
        await using var plugin = new HistorianPlugin(host);
        plugin.CustomStart = s_time;
        plugin.CustomEnd = s_time.AddHours(1);

        Assert.That(plugin.HasTarget, Is.EqualTo(seeded));
        Assert.That(plugin.TargetNodeId, Is.EqualTo(seeded ? selected.NodeId : NodeId.Null));
        Assert.That(plugin.TargetDisplayName, Is.EqualTo(seeded ? "Boiler temperature" : string.Empty));
        Assert.That(plugin.TargetDescription, Is.EqualTo(seeded
            ? "Boiler temperature  •  ns=2;s=Temperature  ·  Range: 2026-03-04T05:06:07Z → 2026-03-04T06:06:07Z"
            : "(no target — Pick Variable… or select one in the address-space tree)"));
        Assert.That(plugin.Rows, Is.Empty);
        Assert.That(plugin.IsReading, Is.False);
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TimestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReadUpdateLists(bool update)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = Host(context);
        await using var plugin = new HistorianPlugin(host);
        var rows = update ? plugin.UpdateAtTimes : plugin.AtTimes;
        var other = update ? plugin.AtTimes : plugin.UpdateAtTimes;
        AtTimeRow sentinel = rows.Single();
        sentinel.RemoveCommand.Execute(null);
        AtTimeRow first = rows[0];
        first.Timestamp = s_time;
        first.ConfirmCommand.Execute(null);
        sentinel.RemoveCommand.Execute(null);
        AtTimeRow second = rows[1];
        second.Timestamp = s_time.AddMinutes(2);
        second.ConfirmCommand.Execute(null);
        sentinel.RemoveCommand.Execute(null);

        Assert.That(rows.Select(row => row.IsAddButton), Is.EqualTo(s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected));
        Assert.That(rows.Select(row => row.IsEditor), Is.EqualTo(s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected2));
        Assert.That(rows.Select(row => row.IsLabel), Is.EqualTo(s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected3));
        Assert.That(rows.Where(row => row.IsLabel).Select(row => row.Timestamp),
            Is.EqualTo(new[] { s_time, s_time.AddMinutes(2) }));
        Assert.That(other.Single().IsAddButton, Is.True);
        first.RemoveCommand.Execute(null);
        first.RemoveCommand.Execute(null);
        Assert.That(rows[0], Is.SameAs(second));
        Assert.That(rows[^1], Is.SameAs(sentinel));
        rows[1].RemoveCommand.Execute(null);
        second.RemoveCommand.Execute(null);
        Assert.That(rows.Single(), Is.SameAs(sentinel));
        Assert.That(plugin.Rows, Is.Empty);
    }

    [Test]
    public async Task AddTimestampRepairsAnEmptyEditableListWithoutDuplicatingItsSentinel()
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = Host(context);
        await using var plugin = new HistorianPlugin(host);
        plugin.AtTimes.Clear();
        plugin.AddTimestampCommand.Execute(null);
        plugin.AddTimestampCommand.Execute(null);
        plugin.AtTimes[0].ConfirmCommand.Execute(null);

        Assert.That(plugin.AtTimes.Select(row => row.IsAddButton), Is.EqualTo(s_addTimestampRepairsAnEmptyEditableListWithoutDuplicatingItsSeExpected));
        Assert.That(plugin.AtTimes[0].IsLabel, Is.True);
        Assert.That(plugin.AtTimes[1].IsEditor, Is.True);
        Assert.That(plugin.UpdateAtTimes.Single().IsAddButton, Is.True);
        Assert.That(plugin.ReadCommand.CanExecute(null), Is.False);
    }

    [TestCase(-5, 0)]
    [TestCase(0, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 0)]
    [TestCase(6, 2)]
    [TestCase(10, 2)]
    [TestCase(25, 2)]
    public async Task NearestNumericIgnoresTextAndKeepsFirstEquidistantRow(int seconds, int expectedIndex)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = Host(context);
        await using var plugin = new HistorianPlugin(host);
        HistoryRow first = Row(0, new Variant(12));
        HistoryRow text = Row(5, new Variant("warming"));
        HistoryRow last = Row(10, new Variant(-4));
        plugin.Rows.Add(first);
        plugin.Rows.Add(text);
        plugin.Rows.Add(last);
        plugin.Rows.Add(Row(5, Variant.Null));
        plugin.Rows.Add(Row(5, new Variant(double.NaN)));

        HistoryRow? nearest = plugin.FindNearestNumeric(s_time.AddSeconds(seconds).ToLocalTime());

        Assert.That(nearest, Is.SameAs(plugin.Rows[expectedIndex]));
        Assert.That(nearest!.Numeric, Is.EqualTo(expectedIndex == 0 ? 12 : -4));
        Assert.That(plugin.HasAnyNumericRow, Is.True);
        Assert.That(plugin.SelectedRow, Is.Null);
        plugin.Rows.Remove(first);
        plugin.Rows.Remove(last);
        Assert.That(plugin.HasAnyNumericRow, Is.False);
        Assert.That(plugin.FindNearestNumeric(s_time), Is.Null);
    }

    [TestCase(0, true, false, false)]
    [TestCase(1, false, true, false)]
    [TestCase(2, false, false, true)]
    public async Task InvalidModeIndicesPreserveValidReadIntent(int index, bool raw, bool processed, bool atTime)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = Host(context);
        await using var plugin = new HistorianPlugin(host);
        plugin.ReturnBounds = true;
        plugin.ProcessingIntervalMs = 2500;
        plugin.SelectedReadModeIndex = index;
        plugin.SelectedReadModeIndex = -1;
        plugin.SelectedReadModeIndex = 3;

        Assert.That(plugin.SelectedReadModeIndex, Is.EqualTo(index));
        Assert.That(plugin.IsRawMode, Is.EqualTo(raw));
        Assert.That(plugin.IsProcessedMode, Is.EqualTo(processed));
        Assert.That(plugin.IsAtTimeMode, Is.EqualTo(atTime));
        Assert.That(plugin.ReturnBounds, Is.True);
        Assert.That(plugin.ProcessingIntervalMs, Is.EqualTo(2500));
        Assert.That(plugin.IsReading, Is.False);
    }

    [TestCase(0, true, false, false, false)]
    [TestCase(1, true, false, false, false)]
    [TestCase(2, true, false, false, false)]
    [TestCase(3, false, true, false, false)]
    [TestCase(4, false, false, true, false)]
    [TestCase(5, false, false, true, false)]
    [TestCase(6, false, false, false, true)]
    public async Task UpdateOperationSelectionShowsOnlyItsOwnInputFamily(
        int index, bool value, bool remove, bool range, bool times)
    {
        await using var context = new DesktopConnectionContext();
        await using PluginHost host = Host(context);
        await using var plugin = new HistorianPlugin(host);
        plugin.UpdateValueText = "17.25";
        plugin.UpdateStart = s_time;
        plugin.SelectedUpdateOpIndex = index;
        plugin.SelectedUpdateOpIndex = -1;
        plugin.SelectedUpdateOpIndex = 7;

        Assert.That(plugin.SelectedUpdateOpIndex, Is.EqualTo(index));
        Assert.That(plugin.IsSingleValueOp, Is.EqualTo(value));
        Assert.That(plugin.IsRemoveOp, Is.EqualTo(remove));
        Assert.That(plugin.IsRangeOp, Is.EqualTo(range));
        Assert.That(plugin.IsDeleteAtTimeOp, Is.EqualTo(times));
        Assert.That(plugin.UpdateValueText, Is.EqualTo("17.25"));
        Assert.That(plugin.UpdateStart, Is.EqualTo(s_time));
        Assert.That(plugin.UpdateResult, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task PickRangeCommandAppliesOnlyAcceptedFixedUtcRange(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = Host(context);
            await using var plugin = new HistorianPlugin(host);
            plugin.CustomStart = s_time;
            plugin.CustomEnd = s_time.AddHours(1);
            Task command = Task.CompletedTask;
            RangeDialog dialog = await DesktopInteraction.OpenedAsync<RangeDialog>(
                () => command = plugin.PickRangeCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(DesktopInteraction.Control<UtcDateTimePicker>(dialog, "StartPicker").Value,
                    Is.EqualTo(s_time));
                DesktopInteraction.Control<UtcDateTimePicker>(dialog, "StartPicker").Value = s_time.AddDays(1);
                DesktopInteraction.Control<UtcDateTimePicker>(dialog, "EndPicker").Value =
                    s_time.AddDays(1).AddMinutes(45);
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);
                Assert.That(plugin.CustomStart, Is.EqualTo(accept ? s_time.AddDays(1) : s_time));
                Assert.That(plugin.CustomEnd,
                    Is.EqualTo(accept ? s_time.AddDays(1).AddMinutes(45) : s_time.AddHours(1)));
                Assert.That(plugin.Rows, Is.Empty);
                Assert.That(plugin.IsReading, Is.False);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task AdvancedDialogIsSingleInstanceUntilClosedAndDoesNotExecuteAnUpdate()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = Host(context);
            await using var plugin = new HistorianPlugin(host);
            Task first = Task.CompletedTask;
            HistoryUpdateDialog dialog = await DesktopInteraction.OpenedAsync<HistoryUpdateDialog>(
                () => first = plugin.OpenHistoryUpdateDialogAsync()).ConfigureAwait(true);
            try
            {
                await plugin.OpenHistoryUpdateDialogAsync().ConfigureAwait(true);
                Assert.That(DesktopInteraction.Owner.OwnedWindows.OfType<HistoryUpdateDialog>().ToArray(),
                    Has.Length.EqualTo(1));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(dialog.DataContext, Is.SameAs(plugin));
                Assert.That(plugin.UpdateResult, Is.Empty);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CloseButton"));
                await first.ConfigureAwait(true);
                Task second = Task.CompletedTask;
                HistoryUpdateDialog reopened = await DesktopInteraction.OpenedAsync<HistoryUpdateDialog>(
                    () => second = plugin.OpenHistoryUpdateDialogAsync()).ConfigureAwait(true);
                try
                {
                    Assert.That(reopened, Is.Not.SameAs(dialog));
                    Assert.That(plugin.Rows, Is.Empty);
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(reopened, "CloseButton"));
                    await second.ConfigureAwait(true);
                }
                finally
                {
                    reopened.Close();
                    await second.ConfigureAwait(true);
                }
            }
            finally
            {
                dialog.Close();
                await first.ConfigureAwait(true);
            }
        });
    }

    private static HistoryRow Row(int seconds, Variant value)
    {
        return new HistoryRow(s_time.AddSeconds(seconds), s_time.AddSeconds(seconds + 1), value, StatusCodes.Good);
    }

    internal static PluginHost Host(DesktopConnectionContext context, NodeViewModel? selection = null)
    {
        var workspace = new Mock<IPluginWorkspace>();
        workspace.SetupGet(w => w.SelectedNode).Returns(selection);
        return new PluginHost(workspace.Object, context.Connection, context.Browser, context.Telemetry);
    }

    private static readonly bool[] s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected =
    [
        false,
        false,
        false,
        true,
    ];
    private static readonly bool[] s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected2 =
    [
        false,
        false,
        true,
        false,
    ];
    private static readonly bool[] s_timestampEditingKeepsExactlyOneTrailingSentinelAndSeparateReaExpected3 =
    [
        true,
        true,
        false,
        false,
    ];
    private static readonly bool[] s_addTimestampRepairsAnEmptyEditableListWithoutDuplicatingItsSeExpected =
    [
        false,
        false,
        true,
    ];
}
