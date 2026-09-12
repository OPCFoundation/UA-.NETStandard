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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.EventView;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Observe;

[TestFixture]
[NonParallelizable]
public sealed class EventViewWorkflowTests
{
    [TestCase(0, "#ff94a3b8")]
    [TestCase(199, "#ff94a3b8")]
    [TestCase(200, "#ff60a5fa")]
    [TestCase(201, "#ff60a5fa")]
    [TestCase(399, "#ff60a5fa")]
    [TestCase(400, "#fff59e0b")]
    [TestCase(401, "#fff59e0b")]
    [TestCase(599, "#fff59e0b")]
    [TestCase(600, "#fff87171")]
    [TestCase(601, "#fff87171")]
    [TestCase(799, "#fff87171")]
    [TestCase(800, "#ffc084fc")]
    [TestCase(801, "#ffc084fc")]
    [TestCase(1000, "#ffc084fc")]
    public void EventLogEntryPreservesFieldOrderMissingValuesAndSeverity(int severity, string color)
    {
        DateTime time = new(2026, 3, 4, 5, 6, 7, 123, DateTimeKind.Utc);
        var entry = new EventLogEntry(time.ToLocalTime(), (ushort)severity, "Boiler", "BoilerAlarm", "High pressure",
            [("/Missing", null), ("/Message", "校正済み"), ("/Pressure", 1250.75), ("/Id", new Uri("urn:event:71"))]);

        Assert.That(entry.DisplayTime, Is.EqualTo("05:06:07.123"));
        Assert.That(((ISolidColorBrush)entry.SeverityBrush).Color, Is.EqualTo(Color.Parse(color)));
        Assert.That(entry.FieldRows, Is.EqualTo(new[]
        {
            new EventFieldDisplay("/Missing", string.Empty), new EventFieldDisplay("/Message", "校正済み"),
            new EventFieldDisplay("/Pressure", "1250.75"), new EventFieldDisplay("/Id", "urn:event:71")
        }));
        Assert.That(entry.Message, Is.EqualTo("High pressure"));
        Assert.That(entry.SourceName, Is.EqualTo("Boiler"));
    }

    [Test]
    public async Task EventSourceStateShowsOfflinePendingCreatedBadAndStickyFailure()
    {
        await using var context = new DesktopConnectionContext();
        var source = new EventSourceVm(new NodeId("Boiler", 2), "Boiler events");
        var states = new List<string>();
        source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EventSourceVm.State))
            {
                states.Add(source.State);
            }
        };
        source.RefreshState();
        var item = new MonitoredItem(73, context.Telemetry, new MonitoredItemOptions
        {
            StartNodeId = source.NodeId, AttributeId = Attributes.EventNotifier,
            QueueSize = 100, SamplingInterval = 0, MonitoringMode = MonitoringMode.Reporting
        });
        source.MonitoredItem = item;
        source.RefreshState();
        item.ServerId = 19;
        source.RefreshState();
        item.SetError(new ServiceResult(StatusCodes.BadFilterNotAllowed));
        source.RefreshState();
        source.CreationFailure = "FAILED: event subscription lost";
        item.SetError(ServiceResult.Good);
        source.RefreshState();
        source.MonitoredItem = null;
        source.RefreshState();

        Assert.That(states, Is.EqualTo(s_eventSourceStateShowsOfflinePendingCreatedBadAndStickyFailureExpected));
        Assert.That(source.State, Is.EqualTo("FAILED: event subscription lost"));
        Assert.That(source.Name, Is.EqualTo("Boiler events"));
        Assert.That(source.NodeId, Is.EqualTo(new NodeId("Boiler", 2)));
        Assert.That(item.ClientHandle, Is.EqualTo(73));
        Assert.That(item.ServerId, Is.EqualTo(19));
        source.CreationFailure = null;
        source.RefreshState();
        Assert.That(source.State, Is.EqualTo("○ offline"));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task FilterCommandUsesOwnedDialogAndCommitsOnlyAcceptedSelections(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new EventViewPlugin(host);
            DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
            EventFilterConfig original = plugin.Filter;
            Task command = Task.CompletedTask;
            EventFilterDialog dialog = await DesktopInteraction.OpenedAsync<EventFilterDialog>(
                () => command = plugin.EditFilterCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<Slider>(dialog, "SeveritySlider").Value = 775;
                foreach (CheckBox field in DesktopInteraction.Control<StackPanel>(dialog, "FieldsPanel")
                    .Children.OfType<CheckBox>())
                {
                    field.IsChecked = Equals(field.Content, "Message");
                }
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);
                if (accept)
                {
                    Assert.That(
                        plugin.Filter.Fields,
                        Is.EqualTo(s_filterCommandUsesOwnedDialogAndCommitsOnlyAcceptedSelectionsExpected));
                    Assert.That(plugin.Filter.SeverityThreshold, Is.EqualTo(775));
                    Assert.That(plugin.FilterSummary,
                        Is.EqualTo("Severity ≥ 775 · 1 field · i=2041 · no where clause"));
                }
                else
                {
                    Assert.That(plugin.Filter, Is.SameAs(original));
                    Assert.That(plugin.FilterSummary,
                        Is.EqualTo("Severity ≥ 0 · 6 fields · BaseEventType · no where clause"));
                }
                Assert.That(plugin.Events, Is.Empty);
                Assert.That(plugin.EventSources, Is.Empty);
                Assert.That(plugin.SubscriptionStatus, Is.EqualTo("○ Subscription: not created"));
                Assert.That(context.ConfigurationsCreated, Is.Zero);
            }
            finally
            {
                dialog.Close();
                await command.ConfigureAwait(true);
                DesktopInteraction.Owner.Content = null;
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task SourceRemovalAndClearCommandsPreserveNeighborConfigurationAndResetSelection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new EventViewPlugin(host);
            var first = new EventSourceVm(new NodeId("Boiler", 2), "Boiler");
            var second = new EventSourceVm(new NodeId("Pump", 2), "Pump");
            plugin.EventSources.Add(first);
            plugin.EventSources.Add(second);
            var entry = new EventLogEntry(
                new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), 500, "Boiler", "Alarm", "Cached", []);
            plugin.Events.Add(entry);
            plugin.SelectedEntry = entry;
            await plugin.RemoveSourceCommand.ExecuteAsync(first).ConfigureAwait(true);
            Assert.That(plugin.EventSources.Single(), Is.SameAs(second));
            Assert.That(plugin.Events.Single(), Is.SameAs(entry));
            plugin.ClearLogCommand.Execute(null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.That(plugin.Events, Is.Empty);
            Assert.That(plugin.SelectedEntry, Is.Null);
            Assert.That(plugin.EventSources.Single(), Is.SameAs(second));
            Assert.That(plugin.Status, Is.EqualTo("● 1 source · 0 events · waiting for the server to emit events…"));
            MenuItem pause = plugin.ContributeMenuItems().Single(item => Equals(item.Header, "_Pause Display"));
            pause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.That(plugin.IsPaused, Is.True);
            Assert.That(pause.IsChecked, Is.True);
            Assert.That(plugin.Status, Does.Contain("display paused (0 buffered)"));
            plugin.PauseStreamCommand.Execute(null);
            Assert.That(plugin.IsPaused, Is.False);
            Assert.That(plugin.Status, Does.Not.Contain("display paused"));
        });
    }

    private static readonly string[] s_eventSourceStateShowsOfflinePendingCreatedBadAndStickyFailureExpected =
    [
        "○ offline",
        "pending",
        "✓ created",
        "BAD: BadFilterNotAllowed [0x80450000]",
        "FAILED: event subscription lost",
    ];
    private static readonly string[] s_filterCommandUsesOwnedDialogAndCommitsOnlyAcceptedSelectionsExpected =
    [
        "Message",
    ];
}
