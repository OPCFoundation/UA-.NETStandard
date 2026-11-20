/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
public sealed partial class EventViewWorkflowTests
{

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
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
    [Category("LensDesktopWorkflow")]
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
    private static readonly string[] s_filterCommandUsesOwnedDialogAndCommitsOnlyAcceptedSelectionsExpected =
    [
        "Message",
    ];
}
