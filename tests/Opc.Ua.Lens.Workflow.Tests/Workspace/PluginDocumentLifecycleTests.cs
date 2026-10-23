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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.SubscriptionBench;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;
using UaLens.Tests.Observe;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Workspace;

[TestFixture]
[NonParallelizable]
public sealed partial class PluginDocumentLifecycleTests
{

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [Platform("Win,Linux")]
    [Category("LensDesktopWorkflow")]
    public Task SubscriptionStateApplyAndCapturePreserveSettingsItemsAndModesWithoutServerIds(int count)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var document = new SubscriptionViewModel("Before", null, NullLogger.Instance);
            MonitoredItemConfig previous = Item(999, "Old");
            document.Items.Add(previous);
            MonitoredItemConfig[] items = new[]
            {
                Item(21, "Temperature"),
                Item(22, "Pressure") with { MonitoringMode = MonitoringMode.Disabled },
                Item(23, "Events") with { AttributeId = Attributes.EventNotifier, IsEvent = true }
            }.Take(count).ToArray();
            var subscription = new SubscriptionConfig
            {
                PublishingInterval = TimeSpan.FromMilliseconds(123.5),
                LifetimeCount = 37,
                KeepAliveCount = 9,
                Priority = 6,
                PublishingEnabled = false,
                MaxNotificationsPerPublish = 43,
                MinPublishRequestCount = 3,
                MaxPublishRequestCount = 17
            };
            var state = new SubscriptionDocumentState(
                "Restored monitor", subscription, items, AnimationMode.Histogram, 2, true,
                ShowItemStatusGrid: false, ShowLegend: true, ShowXAxis: true, ShowYAxis: false, DisplayModeIndex: 5);

            await state.ApplyToAsync(document).ConfigureAwait(true);
            SubscriptionDocumentState captured = SubscriptionDocumentState.Capture(document);
            UaLens.Connection.SessionFile.TabSnapshot exported = captured.Export();

            Assert.That(document.Title, Is.EqualTo("Restored monitor"));
            Assert.That(document.IsBound, Is.False);
            Assert.That(document.Items.Select(item => item.Id),
                Is.EqualTo(Enumerable.Range(previous.Id + 1, count)));
            Assert.That(document.Items.Select(item => item.Id), Does.Not.Contain(previous.Id));
            Assert.That(captured.Items.ToList().Select(item => item.Id),
                Is.EqualTo(document.Items.Select(item => item.Id)));
            Assert.That(document.ItemStatuses.Select(row => row.NodeId),
                Is.EqualTo(s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected.Take(count)));
            Assert.That(exported.Items.Select(item => item.NodeId),
                Is.EqualTo(s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected.Take(count)));
            Assert.That(exported.Items.Select(item => item.QueueSize), Is.All.EqualTo(13));
            Assert.That(exported.Items.Select(item => item.MonitoringMode),
                Is.EqualTo(new byte[] { 2, 0, 2 }.Take(count)));
            Assert.That(exported.PublishingInterval.Milliseconds, Is.EqualTo(123.5));
            Assert.That(exported.KeepAliveCount, Is.EqualTo(9));
            Assert.That(exported.LifetimeCount, Is.EqualTo(37));
            Assert.That(exported.Priority, Is.EqualTo(6));
            Assert.That(exported.MaxNotificationsPerPublish, Is.EqualTo(43));
            Assert.That(exported.PublishingEnabled, Is.False);
            Assert.That(exported.MinPublishRequestCount, Is.EqualTo(3));
            Assert.That(exported.MaxPublishRequestCount, Is.EqualTo(17));
            Assert.That(exported.AnimationMode, Is.EqualTo("Histogram"));
            Assert.That(exported.DisplayModeIndex, Is.EqualTo(5));
            Assert.That(exported.AnimationTimeScale, Is.EqualTo(2));
            Assert.That(exported.ShowResourceOverlay, Is.True);
            Assert.That(exported.ShowItemStatusGrid, Is.False);
            Assert.That(exported.ShowLegend, Is.True);
            Assert.That(exported.ShowXAxis, Is.True);
            Assert.That(exported.ShowYAxis, Is.False);
            Assert.That(captured.Items.Count, Is.EqualTo(count));
            document.Items.Clear();
            Assert.That(captured.Items.Count, Is.EqualTo(count));
            Assert.That(document.ItemStatuses, Is.Empty);
        });
    }

    private static readonly string[] s_subscriptionStateApplyAndCapturePreserveSettingsItemsAndModesExpected =
    [
        "ns=2;s=Temperature",
        "ns=2;s=Pressure",
        "ns=2;s=Events",
    ];
}
