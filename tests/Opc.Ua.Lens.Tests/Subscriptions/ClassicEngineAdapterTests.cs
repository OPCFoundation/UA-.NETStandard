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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Diagnostics;
using UaLens.Subscriptions;
using UaLens.Telemetry;

namespace UaLens.Tests.Subscriptions;

[TestFixture]
public sealed class ClassicEngineAdapterTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RealClassicSubscriptionPreservesRevisedSettingsAndTargetOwnership(bool events)
    {
        using var context = new ClassicProtocol();
        await using var adapter = new ClassicEngineAdapter(context.Session, context.Telemetry);
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig
        {
            PublishingInterval = TimeSpan.FromMilliseconds(10), KeepAliveCount = 3, LifetimeCount = 1000,
            MaxNotificationsPerPublish = 88, Priority = 5
        }, CancellationToken.None).ConfigureAwait(false);
        var create = context.Requests.OfType<CreateSubscriptionRequest>().Single();
        Assert.That(create.RequestedPublishingInterval, Is.EqualTo(10));
        Assert.That(create.Priority, Is.EqualTo(5));
        Assert.That(create.MaxNotificationsPerPublish, Is.EqualTo(88));
        Assert.That(adapter.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
        Assert.That(adapter.CurrentKeepAliveCount, Is.EqualTo(5));
        var config = new MonitoredItemConfig
        {
            NodeId = new NodeId("Temperature", 2), DisplayName = "Temperature",
            SamplingInterval = TimeSpan.FromMilliseconds(7), QueueSize = 3,
            IsEvent = events, AttributeId = events ? Attributes.EventNotifier : Attributes.Value
        };
        int id = await adapter.AddItemAsync(config, CancellationToken.None).ConfigureAwait(false);
        Assert.That(id, Is.EqualTo(1));
        CreateMonitoredItemsRequest itemRequest = context.Requests.OfType<CreateMonitoredItemsRequest>().Single();
        Assert.That(itemRequest.ItemsToCreate[0].ItemToMonitor.NodeId, Is.EqualTo(config.NodeId));
        Assert.That(itemRequest.ItemsToCreate[0].ItemToMonitor.AttributeId, Is.EqualTo(config.AttributeId));
        Assert.That(itemRequest.ItemsToCreate[0].RequestedParameters.SamplingInterval, Is.EqualTo(7));
        Assert.That(adapter.Items[0].SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(9)));
        Assert.That(adapter.Items[0].QueueSize, Is.EqualTo(8));
        Assert.That(adapter.TryGetItemResult(id, out MonitoredItemResult result), Is.True);
        Assert.That(result.Created, Is.True);
        Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
        await adapter.ConfigureItemAsync(config with
        {
            Id = id, SamplingInterval = TimeSpan.FromMilliseconds(11), QueueSize = 12,
            DiscardOldest = false, MonitoringMode = MonitoringMode.Sampling, DisplayName = "Updated"
        }, CancellationToken.None).ConfigureAwait(false);
        Assert.That(context.Requests.OfType<ModifyMonitoredItemsRequest>().Single()
            .ItemsToModify[0].RequestedParameters.QueueSize, Is.EqualTo(12));
        Assert.That(adapter.Items[0].MonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
        Assert.That(adapter.Items[0].DisplayName, Is.EqualTo("Updated"));
        await adapter.RemoveItemAsync(id, CancellationToken.None).ConfigureAwait(false);
        Assert.That(adapter.Items.IsEmpty, Is.True);
        Assert.That(adapter.TryGetItemStats(id, out _), Is.False);
        Assert.That(adapter.TryGetItemResult(id, out _), Is.False);
        Assert.That(context.Requests.OfType<DeleteMonitoredItemsRequest>().Single().MonitoredItemIds[0],
            Is.EqualTo(700));
        await adapter.DisposeAsync().ConfigureAwait(false);
        Assert.That(await adapter.Events.WaitToReadAsync().ConfigureAwait(false), Is.False);
        Assert.That(context.Requests.OfType<DeleteSubscriptionsRequest>().Single().SubscriptionIds[0], Is.EqualTo(500));
    }

    [TestCase(8191, 0)]
    [TestCase(8192, 0)]
    [TestCase(8193, 1)]
    public async Task ClassicDropCountEqualsActualEvictionsRatherThanQueueSaturation(int count, int dropped)
    {
        using var context = new ClassicProtocol();
        await using var adapter = new ClassicEngineAdapter(context.Session, context.Telemetry);
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        Opc.Ua.Client.Subscription subscription = context.Session.Subscriptions.Single();
        var keepAlive = new DataChangeNotification();
        for (int i = 0; i < count; i++)
        {
            subscription.FastKeepAliveCallback!(subscription, keepAlive);
        }
        Assert.That(adapter.Counters.KeepAlives, Is.EqualTo(count));
        Assert.That(adapter.Events.Count, Is.EqualTo(Math.Min(count, 8192)));
        Assert.That(adapter.DroppedNotificationCount, Is.EqualTo(dropped));
    }

    [Test]
    public async Task ClassicCallbacksPreserveItemIdentityValuesAndEventCounters()
    {
        using var context = new ClassicProtocol();
        var observer = new PublishLogObserver(action => action());
        await using var adapter = new ClassicEngineAdapter(context.Session, context.Telemetry, observer);
        await adapter.ApplySubscriptionAsync(new SubscriptionConfig(), CancellationToken.None).ConfigureAwait(false);
        int id = await adapter.AddItemAsync(new MonitoredItemConfig
        {
            NodeId = new NodeId("Temperature", 2), DisplayName = "Temperature"
        }, CancellationToken.None).ConfigureAwait(false);
        Opc.Ua.Client.Subscription subscription = context.Session.Subscriptions.Single();
        uint handle = subscription.MonitoredItems.Single().ClientHandle;
        subscription.FastDataChangeCallback!(subscription, new DataChangeNotification
        {
            MonitoredItems =
            [
                new MonitoredItemNotification { ClientHandle = handle, Value = new DataValue(Variant.From(42)) },
                new MonitoredItemNotification {
                    ClientHandle = uint.MaxValue,
                    Value = new DataValue(Variant.From("text")) }
            ]
        }, []);
        subscription.FastEventCallback!(subscription, new EventNotificationList
        {
            Events = [new EventFieldList { ClientHandle = handle, EventFields = [Variant.From("message")] }]
        }, []);
        var received = new List<NotificationEvent>();
        while (adapter.Events.TryRead(out NotificationEvent notification))
        {
            received.Add(notification);
        }
        Assert.That(received, Has.Count.EqualTo(3));
        Assert.That(received[0].ItemId, Is.EqualTo(id));
        Assert.That(received[0].Value, Is.EqualTo(42));
        Assert.That(received[1].ItemId, Is.Zero);
        Assert.That(received[1].Value, Is.Null);
        Assert.That(received[2].ItemId, Is.EqualTo(id));
        Assert.That(received[2].Kind, Is.EqualTo(NotificationKind.Event));
        Assert.That(adapter.TryGetItemStats(id, out MonitoredItemLiveStats? stats), Is.True);
        Assert.That(stats!.Samples, Is.EqualTo(2));
        Assert.That(stats.LastValueText, Is.EqualTo("42"));
        Assert.That(adapter.Counters.DataMessages, Is.EqualTo(1));
        Assert.That(adapter.Counters.DataValues, Is.EqualTo(2));
        Assert.That(adapter.Counters.EventValues, Is.EqualTo(1));
        Assert.That(observer.CaptureSnapshot().Count, Is.EqualTo(2));
    }

    internal sealed class ClassicProtocol : IDisposable
    {
        public ClassicProtocol()
        {
            var channel = new Mock<ITransportChannel>();
            ServiceMessageContext messages = ServiceMessageContext.Create(Telemetry);
            channel.SetupGet(value => value.MessageContext).Returns(messages);
            channel.Setup(value => value.SendRequestAsync(It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns((IServiceRequest request, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Requests.Add(request);
                    IServiceResponse response = request switch
                    {
                        CreateSubscriptionRequest => new CreateSubscriptionResponse
                        {
                            SubscriptionId = 500, RevisedPublishingInterval = 20,
                            RevisedMaxKeepAliveCount = 5, RevisedLifetimeCount = 1000
                        },
                        SetPublishingModeRequest => new SetPublishingModeResponse { Results = [StatusCodes.Good] },
                        CreateMonitoredItemsRequest => new CreateMonitoredItemsResponse
                        {
                            Results = [new MonitoredItemCreateResult
                            {
                                StatusCode = StatusCodes.Good, MonitoredItemId = 700,
                                RevisedSamplingInterval = 9, RevisedQueueSize = 8
                            }]
                        },
                        ModifyMonitoredItemsRequest => new ModifyMonitoredItemsResponse
                        {
                            Results = [new MonitoredItemModifyResult
                            {
                                StatusCode = StatusCodes.Good, RevisedSamplingInterval = 13, RevisedQueueSize = 15
                            }]
                        },
                        SetMonitoringModeRequest => new SetMonitoringModeResponse { Results = [StatusCodes.Good] },
                        DeleteMonitoredItemsRequest => new DeleteMonitoredItemsResponse {
                            Results = [StatusCodes.Good] },
                        DeleteSubscriptionsRequest => new DeleteSubscriptionsResponse { Results = [StatusCodes.Good] },
                        _ => throw new AssertionException("Unexpected classic request: " + request.GetType().Name)
                    };
                    response.ResponseHeader.ServiceResult = StatusCodes.Good;
                    response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                    return ValueTask.FromResult(response);
                });
            var configuration = new ApplicationConfiguration(Telemetry)
            {
                ClientConfiguration = new ClientConfiguration()
            };
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://classic.example.test:4840",
                SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None
            }, new EndpointConfiguration());
            Session = new Session(channel.Object, configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance);
        }

        public AppTelemetryContext Telemetry { get; } = new(new LogRingBuffer(32));
        public Session Session { get; }
        public List<IServiceRequest> Requests { get; } = [];
        public void Dispose() => Session.Dispose();
    }
}
