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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using UaLens.Subscriptions;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;
using V2MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using V2SubscriptionOptions = Opc.Ua.Client.Subscriptions.SubscriptionOptions;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class MonitorIdentityTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task BothAdaptersExportRemovedItemsAcrossOfflineEditsAndRepeatedEngineSwitches(bool classic)
    {
        await using var first = new EngineContext(classic);
        await using var second = new EngineContext(!classic);
        await using var third = new EngineContext(classic);
        var model = new SubscriptionViewModel(
            "Monitor", null, NullLogger.Instance, InlineWorkspaceDispatcher.Instance);
        await using var lifetime = model.ConfigureAwait(false);
        await model.ApplySubscriptionAsync(new SubscriptionConfig { PublishingEnabled = false })
            .ConfigureAwait(false);
        await model.AddItemAsync(Item("A")).ConfigureAwait(false);
        await model.AddItemAsync(Item("B")).ConfigureAwait(false);
        int a = model.Items[0].Id;
        int b = model.Items[1].Id;
        ChannelReader<NotificationEvent> cursor = model.Recorder.CreateReader();
        await model.AttachAdapterAsync(first.Adapter).ConfigureAwait(false);
        await RecordAsync(first, cursor, "A", 11).ConfigureAwait(false);
        await RecordAsync(first, cursor, "B", 22).ConfigureAwait(false);
        await model.RemoveItemAsync(model.Items[0]).ConfigureAwait(false);
        await Assert.ThatAsync(() => first.Adapter.AddItemAsync(Item("Not A") with { Id = a }, CancellationToken.None),
            Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
        // A callback already in flight still carries A's attribution after removal.
        await RecordAsync(first, cursor, "A", 12).ConfigureAwait(false);
        await model.DetachAdapterAsync().ConfigureAwait(false);

        await model.ConfigureItemAsync(model.Items.Single(), new MonitoredItemSettings
        {
            SamplingInterval = TimeSpan.FromMilliseconds(25)
        }).ConfigureAwait(false);
        await model.AddItemAsync(Item("C")).ConfigureAwait(false);
        int c = model.Items[1].Id;
        await model.AttachAdapterAsync(second.Adapter).ConfigureAwait(false);
        Assert.That(model.Items[0].Id, Is.EqualTo(b));
        await RecordAsync(second, cursor, "B", 23).ConfigureAwait(false);
        await RecordAsync(second, cursor, "C", 33).ConfigureAwait(false);
        await model.DetachAdapterAsync().ConfigureAwait(false);

        await model.RemoveItemAsync(model.Items[1]).ConfigureAwait(false);
        await model.AttachAdapterAsync(third.Adapter).ConfigureAwait(false);
        await RecordAsync(third, cursor, "B", 24).ConfigureAwait(false);
        Assert.That(third.FirstServerItemId, Is.EqualTo(1), "New adapters reuse native server/client handles.");
        Assert.That(model.Items.Single().Id, Is.EqualTo(b));
        Assert.That(model.Recorder.TotalWritten, Is.EqualTo(6));
        Assert.That(model.Recorder.Snapshot().ToList().Select(value => value.ItemId),
            Is.EqualTo(new[] { a, b, a, b, c, b }));

        string path = Path.Combine(Environment.CurrentDirectory, $"lens-monitor-{Guid.NewGuid():N}");
        string csvPath = path + ".csv";
        string jsonPath = path + ".json";
        try
        {
            await model.Recorder.ExportCsvAsync(csvPath).ConfigureAwait(false);
            await model.Recorder.ExportJsonAsync(jsonPath).ConfigureAwait(false);
            string csv = await File.ReadAllTextAsync(csvPath).ConfigureAwait(false);
            string[][] rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(line => line.TrimEnd('\r').Split(',')).ToArray();
            Assert.That(rows.Select(row => row[3]), Is.EqualTo(s_expectedNames));
            Assert.That(rows.Select(row => row[2]), Is.EqualTo(s_expectedCsvItemIds));
            Assert.That(rows.Select(row => row[6]), Is.EqualTo(s_expectedCsvValues));
            Assert.That(rows.Select(row => row[7]),
                Is.EqualTo(s_expectedNames.Select(name => new NodeId(name, 0).ToString())));
            using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false));
            JsonElement[] records = json.RootElement.EnumerateArray().ToArray();
            Assert.That(records.Select(record => record.GetProperty("displayName").GetString()),
                Is.EqualTo(s_expectedNames));
            Assert.That(records.Select(record => record.GetProperty("itemId").GetInt32()),
                Is.EqualTo(new[] { a, b, a, b, c, b }));
            Assert.That(records.Select(record => record.GetProperty("value").GetDouble()),
                Is.EqualTo(s_expectedValues));
            Assert.That(records.Select(record => record.GetProperty("nodeId").GetString()),
                Is.EqualTo(s_expectedNames.Select(name => new NodeId(name, 0).ToString())));
        }
        finally
        {
            File.Delete(csvPath);
            File.Delete(jsonPath);
        }
    }

    private static MonitoredItemConfig Item(string name)
    {
        return new MonitoredItemConfig { DisplayName = name, NodeId = new NodeId(name, 0) };
    }

    private static async Task RecordAsync(
        EngineContext engine, ChannelReader<NotificationEvent> cursor, string name, int value)
    {
        await engine.EmitAsync(name, value).ConfigureAwait(false);
        NotificationEvent notification = await cursor.ReadAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(notification.DisplayName, Is.EqualTo(name));
        Assert.That(notification.Value, Is.EqualTo(value));
    }

    private sealed class EngineContext : IAsyncDisposable
    {
        public EngineContext(bool classic)
        {
            var telemetry = new AppTelemetryContext(new LogRingBuffer(32));
            if (classic)
            {
                var channel = new Mock<ITransportChannel>();
                channel.SetupGet(value => value.MessageContext).Returns(ServiceMessageContext.Create(telemetry));
                channel.SetupGet(value => value.SupportedFeatures).Returns(TransportChannelFeatures.Reconnect);
                channel.Setup(value => value.SendRequestAsync(
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((IServiceRequest request, CancellationToken _) => ValueTask.FromResult(Respond(request)));
                var configuration = new ApplicationConfiguration(telemetry)
                {
                    ClientConfiguration = new ClientConfiguration()
                };
                m_classicSession = new Session(channel.Object, configuration, new ConfiguredEndpoint(null,
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None
                    }, configuration: null));
                Adapter = new ClassicEngineAdapter(m_classicSession, telemetry);
                return;
            }
            var session = new Mock<ISession>();
            var manager = new Mock<ISubscriptionManager>();
            ISubscriptionManager? resolved = manager.Object;
            session.Setup(value => value.TryGetSubscriptionManager(out resolved)).Returns(true);
            var items = new Mock<IMonitoredItemCollection>();
            IMonitoredItem? created = null;
            items.Setup(value => value.TryAdd(
                It.IsAny<string>(), It.IsAny<IOptionsMonitor<V2MonitoredItemOptions>>(), out created))
                .Callback(new TryAddCallback((string _, IOptionsMonitor<V2MonitoredItemOptions> options,
                    out IMonitoredItem? item) =>
                {
                    var monitored = new Mock<IMonitoredItem>();
                    uint handle = ++m_nextServerItemId;
                    FirstServerItemId = FirstServerItemId == 0 ? handle : FirstServerItemId;
                    monitored.SetupGet(value => value.ClientHandle).Returns(handle);
                    monitored.SetupGet(value => value.CurrentSamplingInterval)
                        .Returns(() => options.CurrentValue.SamplingInterval);
                    monitored.SetupGet(value => value.CurrentQueueSize).Returns(() => options.CurrentValue.QueueSize);
                    monitored.SetupGet(value => value.CurrentMonitoringMode)
                        .Returns(() => options.CurrentValue.MonitoringMode);
                    item = monitored.Object;
                    m_v2Items[options.CurrentValue.StartNodeId.ToString()] = item;
                })).Returns(true);
            items.Setup(value => value.TryRemove(It.IsAny<uint>())).Returns(true);
            var subscription = new Mock<IPartitionedSubscription>();
            subscription.SetupGet(value => value.MonitoredItems).Returns(items.Object);
            subscription.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            m_v2Subscription = subscription.Object;
            manager.Setup(value => value.Add(
                It.IsAny<ISubscriptionNotificationHandler>(), It.IsAny<IOptionsMonitor<V2SubscriptionOptions>>()))
                .Callback<ISubscriptionNotificationHandler, IOptionsMonitor<V2SubscriptionOptions>>(
                    (handler, _) => m_handler = handler)
                .Returns(subscription.Object);
            Adapter = new ChannelV2EngineAdapter(session.Object, telemetry);
        }

        public ISubscriptionAdapter Adapter { get; }
        public uint FirstServerItemId { get; private set; }

        public ValueTask EmitAsync(string name, int value)
        {
            var data = new DataValue(Variant.From(value));
            string nodeId = new NodeId(name, 0).ToString();
            if (m_classicSession is not null)
            {
                Opc.Ua.Client.Subscription subscription = m_classicSession.Subscriptions.Single();
                subscription.FastDataChangeCallback!(subscription, new DataChangeNotification
                {
                    MonitoredItems = [new MonitoredItemNotification { ClientHandle = m_handles[nodeId], Value = data }]
                }, []);
                return ValueTask.CompletedTask;
            }
            return m_handler!.OnDataChangeNotificationAsync(m_v2Subscription!, 1, DateTime.UtcNow,
                new DataValueChange[] { new(m_v2Items[nodeId], data, null) }, PublishState.None, []);
        }

        public async ValueTask DisposeAsync()
        {
            if (m_classicSession is not null)
            {
                await m_classicSession.DisposeAsync().ConfigureAwait(false);
            }
        }

        private IServiceResponse Respond(IServiceRequest request)
        {
            return request switch
            {
                CreateSubscriptionRequest create => new CreateSubscriptionResponse
                {
                    SubscriptionId = 1,
                    RevisedPublishingInterval = create.RequestedPublishingInterval,
                    RevisedLifetimeCount = create.RequestedLifetimeCount,
                    RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                },
                CreateMonitoredItemsRequest create => new CreateMonitoredItemsResponse
                {
                    Results = create.ItemsToCreate.ToList().Select(item =>
                    {
                        m_handles[item.ItemToMonitor.NodeId.ToString()] = item.RequestedParameters.ClientHandle;
                        uint id = ++m_nextServerItemId;
                        FirstServerItemId = FirstServerItemId == 0 ? id : FirstServerItemId;
                        return new MonitoredItemCreateResult
                        {
                            MonitoredItemId = id,
                            RevisedSamplingInterval = item.RequestedParameters.SamplingInterval,
                            RevisedQueueSize = item.RequestedParameters.QueueSize
                        };
                    }).ToArray()
                },
                DeleteMonitoredItemsRequest delete => new DeleteMonitoredItemsResponse
                {
                    Results = delete.MonitoredItemIds.ToList().Select(_ => (StatusCode)StatusCodes.Good).ToArray()
                },
                DeleteSubscriptionsRequest delete => new DeleteSubscriptionsResponse
                {
                    Results = delete.SubscriptionIds.ToList().Select(_ => (StatusCode)StatusCodes.Good).ToArray()
                },
                _ => throw new InvalidOperationException($"Unexpected service: {request.GetType().Name}")
            };
        }

        private delegate void TryAddCallback(
            string name, IOptionsMonitor<V2MonitoredItemOptions> options, out IMonitoredItem? item);

        private readonly Session? m_classicSession;
        private readonly ISubscription? m_v2Subscription;
        private readonly Dictionary<string, uint> m_handles = [];
        private readonly Dictionary<string, IMonitoredItem> m_v2Items = [];
        private ISubscriptionNotificationHandler? m_handler;
        private uint m_nextServerItemId;
    }

    private static readonly string[] s_expectedNames = ["A", "B", "A", "B", "C", "B"];
    private static readonly string[] s_expectedCsvItemIds = ["1", "2", "1", "2", "3", "2"];
    private static readonly string[] s_expectedCsvValues = ["11", "22", "12", "23", "33", "24"];
    private static readonly double[] s_expectedValues = [11, 22, 12, 23, 33, 24];
}
