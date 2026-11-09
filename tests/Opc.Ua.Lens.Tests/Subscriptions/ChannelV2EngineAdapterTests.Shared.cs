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
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Diagnostics;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using V2ItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2Options = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Tests.Subscriptions;

public sealed partial class ChannelV2EngineAdapterTests
{

    private static MonitoredItemConfig Item(int id, bool events = false)
    {
        return new MonitoredItemConfig
        {
            Id = id,
            NodeId = new NodeId("Temperature", 2),
            DisplayName = "Temperature",
            SamplingInterval = TimeSpan.FromMilliseconds(13),
            QueueSize = 4,
            DiscardOldest = false,
            IsEvent = events,
            AttributeId = events ? Attributes.EventNotifier : Attributes.Value
        };
    }

    internal sealed class AdapterContext
    {
        public AdapterContext(bool supported = true)
        {
            Subscription.As<IPartitionedSubscription>().SetupGet(subscription => subscription.PartitionIds)
                .Returns(s_singlePartition);
            Subscription.SetupGet(subscription => subscription.MonitoredItems).Returns(Items.Object);
            Subscription.SetupGet(subscription => subscription.CurrentPublishingInterval)
                .Returns(TimeSpan.FromMilliseconds(12));
            Subscription.SetupGet(subscription => subscription.CurrentKeepAliveCount).Returns(10u);
            Subscription.SetupGet(subscription => subscription.CurrentLifetimeCount).Returns(300u);
            Subscription.Setup(subscription => subscription.DisposeAsync()).Returns(ValueTask.CompletedTask);
            ISubscriptionManager? manager = supported ? Manager.Object : null;
            Session.Setup(session => session.TryGetSubscriptionManager(out manager)).Returns(supported);
            Session.SetupGet(session => session.GoodPublishRequestCount).Returns(7);
            Session.SetupGet(session => session.MinPublishRequestCount).Returns(8);
            Session.SetupGet(session => session.MaxPublishRequestCount).Returns(19);
            Manager.SetupGet(value => value.PublishWorkerCount).Returns(3);
            Manager.SetupGet(value => value.BadPublishRequestCount).Returns(4);
            Manager.SetupGet(value => value.MissingMessageCount).Returns(5L);
            Manager.SetupGet(value => value.RepublishMessageCount).Returns(6L);
            Manager.SetupGet(value => value.MinPublishWorkerCount).Returns(2);
            Manager.SetupGet(value => value.MaxPublishWorkerCount).Returns(9);
            Manager.Setup(value => value.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<V2Options>>()))
                .Callback((ISubscriptionNotificationHandler handler, IOptionsMonitor<V2Options> options) =>
                {
                    Adds++;
                    Handler = handler;
                    Options = options;
                }).Returns(Subscription.Object);
            Items.Setup(value => value.TryAdd(It.IsAny<string>(), It.IsAny<IOptionsMonitor<V2ItemOptions>>(),
                out It.Ref<IMonitoredItem?>.IsAny))
                .Callback(new CaptureItem((string name, IOptionsMonitor<V2ItemOptions> options, out IMonitoredItem? item) =>
                {
                    ItemNames.Add(name);
                    ItemOptions.Add(options);
                    var created = new Mock<IMonitoredItem>();
                    created.As<IMonitoredItemApplyState>().SetupGet(value => value.HasPendingChanges).Returns(true);
                    created.SetupGet(value => value.ClientHandle).Returns((uint)(101 + Monitored.Count));
                    created.SetupGet(value => value.Created).Returns(true);
                    created.SetupGet(value => value.Error).Returns(new ServiceResult(StatusCodes.Good));
                    created.SetupGet(value => value.CurrentSamplingInterval).Returns(TimeSpan.FromMilliseconds(27));
                    created.SetupGet(value => value.CurrentQueueSize).Returns(7u);
                    created.SetupGet(value => value.CurrentMonitoringMode).Returns(MonitoringMode.Sampling);
                    Monitored.Add(created);
                    item = created.Object;
                    ItemAdded?.Invoke();
                })).Returns(true);
            Items.Setup(value => value.TryRemove(It.IsAny<uint>())).Returns(true);
        }

        public Mock<ISession> Session { get; } = new();
        public Mock<ISubscriptionManager> Manager { get; } = new();
        public Mock<ISubscription> Subscription { get; } = new();
        public Mock<IMonitoredItemCollection> Items { get; } = new();
        public List<string> ItemNames { get; } = [];
        public List<IOptionsMonitor<V2ItemOptions>> ItemOptions { get; } = [];
        public List<Mock<IMonitoredItem>> Monitored { get; } = [];
        public IOptionsMonitor<V2Options>? Options { get; private set; }
        public ISubscriptionNotificationHandler? Handler { get; private set; }
        public int Adds { get; private set; }
        public Action? ItemAdded { get; set; }

        public ChannelV2EngineAdapter CreateAdapter(PublishLogObserver? log = null)
        {
            return new ChannelV2EngineAdapter(Session.Object, new AppTelemetryContext(new LogRingBuffer(32)), log);
        }
    }

    private delegate void CaptureItem(string name, IOptionsMonitor<V2ItemOptions> options, out IMonitoredItem? item);

    private static readonly uint[] s_singlePartition = [500];
}
