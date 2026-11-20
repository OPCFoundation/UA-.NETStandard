// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
public sealed partial class HistorianWorkflowTests
{

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
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
    [Category("LensDesktopWorkflow")]
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
}
