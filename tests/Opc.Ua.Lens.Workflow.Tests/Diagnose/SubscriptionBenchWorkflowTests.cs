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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using V2ItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
using V2SubscriptionState = Opc.Ua.Client.Subscriptions.SubscriptionState;

namespace UaLens.Tests.Diagnose;

[TestFixture]
[NonParallelizable]
public sealed partial class SubscriptionBenchWorkflowTests
{

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task SubscriptionSettingsCommandCommitsOnlyAcceptedIntentWithoutStartingTopology(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new SubscriptionBenchPlugin(host);
            DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
            Task command = Task.CompletedTask;
            SubscriptionSettingsDialog dialog = await DesktopInteraction.OpenedAsync<SubscriptionSettingsDialog>(
                () => command = plugin.EditSubscriptionCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "PubMs").Text = "375";
                DesktopInteraction.Control<TextBox>(dialog, "KeepAlive").Text = "7";
                DesktopInteraction.Control<TextBox>(dialog, "Lifetime").Text = "31";
                DesktopInteraction.Control<TextBox>(dialog, "MaxNotifs").Text = "47";
                DesktopInteraction.Control<TextBox>(dialog, "Priority").Text = "19";
                DesktopInteraction.Control<CheckBox>(dialog, "PublishingEnabled").IsChecked = false;
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);

                JsonElement config = plugin.CaptureState().GetProperty("subscription");
                Assert.That(config.GetProperty("publishingIntervalMs").GetDouble(), Is.EqualTo(accept ? 375 : 1000));
                Assert.That(config.GetProperty("keepAliveCount").GetUInt32(), Is.EqualTo(accept ? 7 : 10));
                Assert.That(config.GetProperty("lifetimeCount").GetUInt32(), Is.EqualTo(accept ? 31 : 1000));
                Assert.That(config.GetProperty("maxNotificationsPerPublish").GetUInt32(), Is.EqualTo(accept ? 47 : 0));
                Assert.That(config.GetProperty("priority").GetByte(), Is.EqualTo(accept ? 19 : 0));
                Assert.That(config.GetProperty("publishingEnabled").GetBoolean(), Is.EqualTo(!accept));
                Assert.That(plugin.Status, Is.EqualTo(accept
                    ? "Subscription parameters saved — they apply when connected." : "Not connected."));
                Assert.That(plugin.SubsSliderValue, Is.Zero);
                Assert.That(plugin.ItemsSliderValue, Is.Zero);
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

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task ItemSettingsCommandCommitsAcceptedSamplingQueueModeAndFilterWithoutStartingTopology(bool accept)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            await using PluginHost host = HistorianWorkflowTests.Host(context);
            await using var plugin = new SubscriptionBenchPlugin(host);
            DesktopInteraction.Owner.Content = ((IPlugin)plugin).View;
            Task command = Task.CompletedTask;
            MonitoredItemSettingsDialog dialog = await DesktopInteraction.OpenedAsync<MonitoredItemSettingsDialog>(
                () => command = plugin.EditItemSettingsCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "SamplingMs").Text = "45";
                DesktopInteraction.Control<TextBox>(dialog, "QueueSizeBox").Text = "13";
                DesktopInteraction.Control<CheckBox>(dialog, "DiscardOldestBox").IsChecked = false;
                DesktopInteraction.Control<ComboBox>(dialog, "MonitoringModeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<ComboBox>(dialog, "TriggerCombo").SelectedIndex = 2;
                DesktopInteraction.Control<ComboBox>(dialog, "DeadbandTypeCombo").SelectedIndex = 1;
                DesktopInteraction.Control<TextBox>(dialog, "DeadbandValueBox").Text = "2";
                DesktopInteraction.Click(
                    DesktopInteraction.Control<Button>(dialog, accept ? "OkButton" : "CancelButton"));
                await command.ConfigureAwait(true);

                JsonElement item = plugin.CaptureState().GetProperty("item");
                Assert.That(item.GetProperty("samplingIntervalMs").GetDouble(), Is.EqualTo(accept ? 45 : 0));
                Assert.That(item.GetProperty("queueSize").GetUInt32(), Is.EqualTo(accept ? 13 : 1));
                Assert.That(item.GetProperty("discardOldest").GetBoolean(), Is.EqualTo(!accept));
                Assert.That(item.GetProperty("monitoringMode").GetInt32(), Is.EqualTo(accept ? 1 : 2));
                if (accept)
                {
                    Assert.That(item.GetProperty("filter").GetProperty("trigger").GetInt32(), Is.EqualTo(2));
                    Assert.That(item.GetProperty("filter").GetProperty("deadbandType").GetUInt32(), Is.EqualTo(1));
                    Assert.That(item.GetProperty("filter").GetProperty("deadbandValue").GetDouble(), Is.EqualTo(2));
                    Assert.That(plugin.Status, Is.EqualTo("Item settings saved — they apply when connected."));
                }
                Assert.That(plugin.SubsSliderValue, Is.Zero);
                Assert.That(plugin.ItemsSliderValue, Is.Zero);
                Assert.That(plugin.TryDequeueChartSample(out _), Is.False);
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
}
