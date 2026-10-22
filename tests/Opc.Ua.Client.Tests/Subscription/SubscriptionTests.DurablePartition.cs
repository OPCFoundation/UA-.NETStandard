/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    public sealed partial class SubscriptionTests
    {
        [Test]
        [CancelAfter(30_000)]
        public async Task DurableIsSetOnNewPartitionCreatedBeforeFactoryReturnsAsync(
            CancellationToken ct)
        {
            var calls = new ConcurrentQueue<string>();
            SetupDurablePartitionServer(calls);
            int created = 0;
            var context = new FakeSubscriptionManagerContext
            {
                CreateInitializedSubscriptionFactory = (handler, _, queue, initialize) =>
                {
                    var subscription = new TestSubscription(
                        m_session, handler, queue, m_options, m_telemetry, initialize: initialize);
                    if (Interlocked.Increment(ref created) == 2)
                    {
                        // The server answers CreateSubscription of the new
                        // partition before the partition factory returns.
                        Assert.That(SpinWait.SpinUntil(() => subscription.Created,
                            TimeSpan.FromSeconds(10)), Is.True);
                    }
                    return subscription;
                }
            };
            m_options.Configure(_ => TestSubscription.SubscriptionOptions with
            {
                MaxMonitoredItemsPerPartition = 1
            });
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            ISubscription subscription = manager.Add(m_mockNotificationDataHandler.Object, m_options);
            await WaitForAsync(() => subscription.Created, TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(subscription.Created, Is.True);

            TimeSpan revised = await subscription.SetAsDurableAsync(TimeSpan.FromHours(10), ct)
                .ConfigureAwait(false);
            Assert.That(revised, Is.EqualTo(TimeSpan.FromHours(10)));
            Assert.That(subscription.MonitoredItems.TryAdd("First",
                OptionsFactory.Create(TestMonitoredItem.CreatedOptions), out _), Is.True);
            await WaitForAsync(() => calls.Contains("items:22"), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.That(subscription.MonitoredItems.TryAdd("Second",
                OptionsFactory.Create(TestMonitoredItem.CreatedOptions), out _), Is.True);
            await WaitForAsync(() => calls.Contains("items:23"), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.That(((IPartitionedSubscription)subscription).PartitionCount, Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(s_durableOnBothPartitions));
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task InitializeRunsBeforeTheFirstCreatePassAsync(CancellationToken ct)
        {
            var calls = new ConcurrentQueue<string>();
            SetupDurablePartitionServer(calls);
            m_options.Configure(_ => TestSubscription.SubscriptionOptions);
            bool createdWhenInitialized = true;
            var afterCreate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var sut = new TestSubscription(
                m_session, m_mockNotificationDataHandler.Object, m_completion, m_options, m_telemetry,
                initialize: subscription =>
                {
                    createdWhenInitialized = subscription.Created;
                    ((Subscription)subscription).OnAfterCreateAsync = _ =>
                    {
                        afterCreate.TrySetResult(true);
                        return default;
                    };
                });

            await afterCreate.Task.WaitAsync(ct).ConfigureAwait(false);
            Assert.That(createdWhenInitialized, Is.False);
            Assert.That(sut.Id, Is.EqualTo(22));
            Assert.That(calls, Is.EqualTo(s_createOnly));
        }

        /// <summary>
        /// Server double that answers every call immediately, assigns
        /// subscription ids 22, 23, ... and records CreateSubscription,
        /// SetSubscriptionDurable (with the requested hours) and
        /// CreateMonitoredItems in call order.
        /// </summary>
        private void SetupDurablePartitionServer(ConcurrentQueue<string> calls)
        {
            int nextSubscriptionId = 21;
            int nextItemId = 100;
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<uint>(),
                    It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<bool>(),
                    It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double interval, uint lifetimeCount, uint keepAliveCount,
                    uint _, bool _, byte _, CancellationToken _) =>
                {
                    uint id = (uint)Interlocked.Increment(ref nextSubscriptionId);
                    calls.Enqueue($"create:{id}");
                    return new ValueTask<CreateSubscriptionResponse>(new CreateSubscriptionResponse
                    {
                        SubscriptionId = id,
                        RevisedPublishingInterval = interval,
                        RevisedLifetimeCount = lifetimeCount,
                        RevisedMaxKeepAliveCount = keepAliveCount
                    });
                });
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<uint> ids, CancellationToken _) =>
                {
                    var results = new List<StatusCode>();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        results.Add(StatusCodes.Good);
                    }
                    return new ValueTask<DeleteSubscriptionsResponse>(
                        new DeleteSubscriptionsResponse { Results = results.ToArrayOf() });
                });
            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                        r.Count == 1 && r[0].MethodId == MethodIds.Server_SetSubscriptionDurable),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    Assert.That(requests[0].InputArguments[0].TryGetValue(out uint id), Is.True);
                    Assert.That(requests[0].InputArguments[1].TryGetValue(out uint hours), Is.True);
                    calls.Enqueue($"durable:{id}:{hours}h");
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        Results =
                        [
                            new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = [new Variant(hours)]
                            }
                        ]
                    });
                });
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<uint>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, uint id, TimestampsToReturn _,
                    ArrayOf<MonitoredItemCreateRequest> requests, CancellationToken _) =>
                {
                    calls.Enqueue($"items:{id}");
                    var results = new List<MonitoredItemCreateResult>();
                    for (int i = 0; i < requests.Count; i++)
                    {
                        results.Add(new MonitoredItemCreateResult
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = (uint)Interlocked.Increment(ref nextItemId),
                            RevisedSamplingInterval = 1000,
                            RevisedQueueSize = 5
                        });
                    }
                    return new ValueTask<CreateMonitoredItemsResponse>(
                        new CreateMonitoredItemsResponse { Results = results.ToArrayOf() });
                });
        }

        private static readonly string[] s_durableOnBothPartitions =
        [
            "create:22", "durable:22:10h", "items:22",
            "create:23", "durable:23:10h", "items:23"
        ];

        private static readonly string[] s_createOnly = ["create:22"];
    }
}
