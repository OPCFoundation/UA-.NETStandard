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
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.EventView;
using UaLens.Tests.Desktop;
using UaLens.Tests.Subscriptions;

namespace UaLens.Tests.Observe;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedEventViewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task EventSourceReceivesTypedFieldsAndPauseResumesInNewestFirstOrder(bool pause)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var wire = new ClassicEngineAdapterTests.ClassicProtocol();
            await using var context = new ConnectedProtocolContext();
            context.Session.Setup(session => session.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) => wire.Session.AddSubscription(subscription));
            await context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = new EventViewPlugin(context.Host);
            await plugin.SeedSourceAsync(new NodeId("Boiler", 2), "Boiler").ConfigureAwait(true);
            Assert.That(plugin.EventSources, Has.Count.EqualTo(1));
            Assert.That(plugin.EventSources[0].State, Does.Contain("created"));
            Subscription subscription = wire.Session.Subscriptions.Single();
            MonitoredItem monitored = subscription.MonitoredItems.Single();
            var filter = (EventFilter)monitored.Filter!;
            Assert.That(monitored.AttributeId, Is.EqualTo(Attributes.EventNotifier));
            Assert.That(monitored.QueueSize, Is.EqualTo(100));
            plugin.IsPaused = pause;
            Emit(subscription, monitored.ClientHandle, filter, 700, "First");
            Emit(subscription, monitored.ClientHandle, filter, 800, "Second");
            await FlushAsync().ConfigureAwait(true);
            if (pause)
            {
                Assert.That(plugin.Events, Is.Empty);
                plugin.IsPaused = false;
                await FlushAsync().ConfigureAwait(true);
            }
            Assert.That(plugin.Events, Has.Count.EqualTo(2));
            Assert.That(plugin.Events[0].Message, Is.EqualTo("Second"));
            Assert.That(plugin.Events[0].Severity, Is.EqualTo(800));
            Assert.That(plugin.Events[0].SourceName, Is.EqualTo("Boiler"));
            Assert.That(plugin.Events[1].Message, Is.EqualTo("First"));
            plugin.SelectedEntry = plugin.Events[0];
            plugin.ClearLogCommand.Execute(null);
            await FlushAsync().ConfigureAwait(true);
            Assert.That(plugin.Events, Is.Empty);
            Assert.That(plugin.SelectedEntry, Is.Null);
            await plugin.RemoveSourceCommand.ExecuteAsync(plugin.EventSources[0]).ConfigureAwait(true);
            Assert.That(plugin.EventSources, Is.Empty);
            Assert.That(
                wire.Requests.OfType<DeleteMonitoredItemsRequest>().Single().MonitoredItemIds[0],
                Is.EqualTo(700));
        });
    }

    [TestCase(false, 1999, 0)]
    [TestCase(false, 2000, 0)]
    [TestCase(false, 2001, 1)]
    [TestCase(true, 1999, 0)]
    [TestCase(true, 2000, 0)]
    [TestCase(true, 2001, 1)]
    public Task DisplayCapacityKeepsLatestEventsAndTracksLossWhileRunningOrPaused(bool pause, int count, int dropped)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var wire = new ClassicEngineAdapterTests.ClassicProtocol();
            await using var context = new ConnectedProtocolContext();
            context.Session.Setup(session => session.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) => wire.Session.AddSubscription(subscription));
            await context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = new EventViewPlugin(context.Host);
            await plugin.SeedSourceAsync(new NodeId("Boiler", 2), "Boiler").ConfigureAwait(true);
            Subscription subscription = wire.Session.Subscriptions.Single();
            MonitoredItem item = subscription.MonitoredItems.Single();
            var filter = (EventFilter)item.Filter!;
            plugin.IsPaused = pause;
            for (int i = 0; i < count; i++)
            {
                Emit(subscription, item.ClientHandle, filter, 700,
                    "Event" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            await FlushAsync().ConfigureAwait(true);
            plugin.IsPaused = false;
            await FlushAsync().ConfigureAwait(true);
            Assert.That(plugin.Events, Has.Count.EqualTo(Math.Min(2000, count)));
            Assert.That(plugin.Events[0].Message, Is.EqualTo($"Event{count - 1}"));
            Assert.That(plugin.Events[^1].Message, Is.EqualTo($"Event{dropped}"));
            Assert.That(plugin.Status.Contains("dropped", StringComparison.Ordinal), Is.EqualTo(dropped > 0));
        });
    }

    [Test]
    public Task ReleasedSubscriptionCannotPublishIntoTheDisconnectedDocument()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var wire = new ClassicEngineAdapterTests.ClassicProtocol();
            await using var context = new ConnectedProtocolContext();
            context.Session.Setup(session => session.AddSubscription(It.IsAny<Subscription>()))
                .Returns((Subscription subscription) => wire.Session.AddSubscription(subscription));
            await context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = new EventViewPlugin(context.Host);
            await plugin.SeedSourceAsync(new NodeId("Boiler", 2), "Boiler").ConfigureAwait(true);
            Subscription old = wire.Session.Subscriptions.Single();
            MonitoredItem item = old.MonitoredItems.Single();
            var filter = (EventFilter)item.Filter!;
            Emit(old, item.ClientHandle, filter, 800, "Known");
            await FlushAsync().ConfigureAwait(true);
            await context.Desktop.Connection.DisconnectAsync().ConfigureAwait(true);
            await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(true);
            Assert.That(plugin.EventSources[0].MonitoredItem, Is.Null);
            Emit(old, item.ClientHandle, filter, 900, "Stale");
            await FlushAsync().ConfigureAwait(true);
            Assert.That(plugin.Events.Select(entry => entry.Message), Is.EqualTo(s_known));
            Assert.That(plugin.EventSources[0].State, Does.Contain("offline"));
        });
    }

    private static void Emit(
        Subscription subscription,
        uint handle,
        EventFilter filter,
        ushort severity,
        string message)
    {
        Variant[] fields = new Variant[filter.SelectClauses.Count];
        for (int i = 0; i < fields.Length; i++)
        {
            string? name = filter.SelectClauses[i].BrowsePath.Count == 0
                ? null : filter.SelectClauses[i].BrowsePath[0].Name;
            fields[i] = name switch
            {
                BrowseNames.Time => Variant.From(s_time),
                BrowseNames.Severity => Variant.From(severity),
                BrowseNames.SourceName => Variant.From("Boiler"),
                BrowseNames.Message => Variant.From(new LocalizedText(message)),
                BrowseNames.EventType => Variant.From(ObjectTypeIds.BaseEventType),
                BrowseNames.EventId => Variant.From(ByteString.Empty),
                _ => Variant.Null
            };
        }
        subscription.FastEventCallback!(subscription,
            new EventNotificationList {
                Events = [new EventFieldList { ClientHandle = handle, EventFields = fields }] }, []);
    }

    private static async Task FlushAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static readonly DateTimeUtc s_time = new(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    private static readonly string[] s_known = ["Known"];
}
