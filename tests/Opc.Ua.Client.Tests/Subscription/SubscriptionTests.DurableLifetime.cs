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

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Durable lifetime provider tests: the lifetime passed to
    /// <c>SetSubscriptionDurable</c> is evaluated before every physical
    /// create and sent between <c>CreateSubscription</c> and the first
    /// <c>CreateMonitoredItems</c> of that create.
    /// </summary>
    public sealed partial class SubscriptionTests
    {
        [Test]
        [CancelAfter(30_000)]
        public async Task DurableLifetimeProviderIsEvaluatedOnRecreateAfterSubscriptionIdInvalidAsync(
            CancellationToken ct)
        {
            TimeSpan? lifetime = TimeSpan.FromHours(10);
            IReadOnlyList<string> calls = await RecreateAfterSubscriptionIdInvalidAsync(
                subscription => subscription.SetAsDurableAsync(() => lifetime, ct),
                () => lifetime = TimeSpan.FromHours(7),
                ct).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(s_durableRecreateWithCurrentLifetime));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DurableLifetimeProviderIsEvaluatedOnRecreateAfterFailedTransferAsync(
            CancellationToken ct)
        {
            var calls = new ConcurrentQueue<string>();
            SetupDurableLifetimeServer(calls);
            var context = new FakeSubscriptionManagerContext
            {
                CreateSubscriptionFactory = (handler, _, queue) => new TestSubscription(
                    m_session, handler, queue, m_options, m_telemetry),
                OnTransferSubscriptionsAsync = (_, ids, _, _) =>
                {
                    calls.Enqueue($"transfer:{ids[0]}");
                    return new ValueTask<TransferSubscriptionsResponse>(
                        new TransferSubscriptionsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new TransferResult
                                {
                                    StatusCode = StatusCodes.BadSubscriptionIdInvalid
                                }
                            ]
                        });
                }
            };
            m_options.Configure(_ => TestSubscription.SubscriptionOptions);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None)
            {
                TransferSubscriptionsOnRecreate = true
            };
            ISubscription subscription = manager.Add(m_mockNotificationDataHandler.Object, m_options);
            TimeSpan? lifetime = TimeSpan.FromHours(10);
            await MakeDurableWithItemAsync(subscription, calls,
                s => s.SetAsDurableAsync(() => lifetime, ct)).ConfigureAwait(false);

            lifetime = TimeSpan.FromHours(7);
            await manager.RecreateSubscriptionsAsync(new NodeId(99u), ct).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(s_durableRecreateAfterFailedTransfer));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DurableLifetimeProviderIsEvaluatedForNewPartitionAsync(
            CancellationToken ct)
        {
            var calls = new ConcurrentQueue<string>();
            var releaseSecondaryCreate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            // The secondary partition starts creating while it is being minted;
            // hold its CreateSubscription response like a network round trip.
            SetupDurableLifetimeServer(calls,
                id => id == 23 ? releaseSecondaryCreate.Task : Task.CompletedTask);
            var context = new FakeSubscriptionManagerContext
            {
                CreateSubscriptionFactory = (handler, _, queue) => new TestSubscription(
                    m_session, handler, queue, m_options, m_telemetry)
            };
            m_options.Configure(_ => TestSubscription.SubscriptionOptions with
            {
                MaxMonitoredItemsPerPartition = 1
            });
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            ISubscription subscription = manager.Add(m_mockNotificationDataHandler.Object, m_options);
            TimeSpan? lifetime = TimeSpan.FromHours(10);
            await MakeDurableWithItemAsync(subscription, calls,
                s => s.SetAsDurableAsync(() => lifetime, ct)).ConfigureAwait(false);

            lifetime = TimeSpan.FromHours(7);
            Assert.That(subscription.MonitoredItems.TryAdd("Second",
                OptionsFactory.Create(TestMonitoredItem.CreatedOptions), out _), Is.True);
            releaseSecondaryCreate.TrySetResult(true);
            await WaitForAsync(() => calls.Contains("items:23"), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.That(((IPartitionedSubscription)subscription).PartitionCount, Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(s_durableRecreateWithCurrentLifetime));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DurableLifetimeProviderReturningNullSendsNoSetSubscriptionDurableAsync(
            CancellationToken ct)
        {
            TimeSpan? lifetime = TimeSpan.FromHours(10);
            IReadOnlyList<string> calls = await RecreateAfterSubscriptionIdInvalidAsync(
                subscription => subscription.SetAsDurableAsync(() => lifetime, ct),
                () => lifetime = null,
                ct).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(s_durableRecreateWithoutDurable));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DurableLifetimeProviderThatThrowsLeavesRecreatedSubscriptionRunningAsync(
            CancellationToken ct)
        {
            bool fail = false;
            IReadOnlyList<string> calls = await RecreateAfterSubscriptionIdInvalidAsync(
                subscription => subscription.SetAsDurableAsync(
                    () => fail
                        ? throw new InvalidOperationException("Lifetime unavailable.")
                        : TimeSpan.FromHours(10),
                    ct),
                () => fail = true,
                ct).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(s_durableRecreateWithoutDurable));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DurableFixedLifetimeIsReappliedOnRecreateAsync(
            CancellationToken ct)
        {
            IReadOnlyList<string> calls = await RecreateAfterSubscriptionIdInvalidAsync(
                subscription => subscription.SetAsDurableAsync(TimeSpan.FromHours(10), ct),
                () => { },
                ct).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(s_durableRecreateWithFixedLifetime));
        }

        /// <summary>
        /// Creates a durable subscription with one item, lets the server
        /// report <c>BadSubscriptionIdInvalid</c> on the next
        /// <c>ModifySubscription</c> and returns the recorded calls once
        /// the recreated subscription has created its item.
        /// </summary>
        private async Task<IReadOnlyList<string>> RecreateAfterSubscriptionIdInvalidAsync(
            Func<ISubscription, ValueTask<TimeSpan>> makeDurable,
            Action beforeRecreate,
            CancellationToken ct)
        {
            var calls = new ConcurrentQueue<string>();
            SetupDurableLifetimeServer(calls);
            m_mockSubscriptionServices
                .Setup(s => s.ModifySubscriptionAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<uint>(), It.IsAny<double>(), It.IsAny<uint>(),
                    It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));
            var context = new FakeSubscriptionManagerContext
            {
                CreateSubscriptionFactory = (handler, _, queue) => new TestSubscription(
                    m_session, handler, queue, m_options, m_telemetry)
            };
            m_options.Configure(_ => TestSubscription.SubscriptionOptions);
            await using var manager = new SubscriptionManager(
                context, m_telemetry.LoggerFactory, DiagnosticsMasks.None);
            ISubscription subscription = manager.Add(m_mockNotificationDataHandler.Object, m_options);
            await MakeDurableWithItemAsync(subscription, calls, makeDurable).ConfigureAwait(false);

            beforeRecreate();
            m_options.Configure(o => o with { Priority = 4 });
            await WaitForAsync(() => calls.Contains("items:23"), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            Assert.That(subscription.Created, Is.True);
            return [.. calls];
        }

        private static async Task MakeDurableWithItemAsync(
            ISubscription subscription,
            ConcurrentQueue<string> calls,
            Func<ISubscription, ValueTask<TimeSpan>> makeDurable)
        {
            await WaitForAsync(() => subscription.Created, TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(subscription.Created, Is.True);

            TimeSpan revised = await makeDurable(subscription).ConfigureAwait(false);
            Assert.That(revised, Is.EqualTo(TimeSpan.FromHours(10)));

            Assert.That(subscription.MonitoredItems.TryAdd("First",
                OptionsFactory.Create(TestMonitoredItem.CreatedOptions), out _), Is.True);
            await WaitForAsync(() => calls.Contains("items:22"), TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(calls, Does.Contain("items:22"));
        }

        /// <summary>
        /// Server double that assigns subscription ids 22, 23, ... and
        /// records CreateSubscription, SetSubscriptionDurable (with the
        /// requested hours), CreateMonitoredItems and DeleteSubscriptions
        /// in call order.
        /// </summary>
        private void SetupDurableLifetimeServer(
            ConcurrentQueue<string> calls,
            Func<uint, Task>? beforeCreateResponse = null)
        {
            int nextSubscriptionId = 21;
            int nextItemId = 100;
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<uint>(),
                    It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<bool>(),
                    It.IsAny<byte>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double interval, uint lifetimeCount, uint keepAliveCount,
                    uint _, bool _, byte _, CancellationToken token) =>
                    new ValueTask<CreateSubscriptionResponse>(
                        RespondAsync(interval, lifetimeCount, keepAliveCount, token)));
            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<uint> ids, CancellationToken _) =>
                {
                    var results = new List<StatusCode>();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        calls.Enqueue($"delete:{ids[i]}");
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

            async Task<CreateSubscriptionResponse> RespondAsync(double interval,
                uint lifetimeCount, uint keepAliveCount, CancellationToken token)
            {
                uint id = (uint)Interlocked.Increment(ref nextSubscriptionId);
                calls.Enqueue($"create:{id}");
                if (beforeCreateResponse != null)
                {
                    await beforeCreateResponse(id).WaitAsync(token).ConfigureAwait(false);
                }
                return new CreateSubscriptionResponse
                {
                    SubscriptionId = id,
                    RevisedPublishingInterval = interval,
                    RevisedLifetimeCount = lifetimeCount,
                    RevisedMaxKeepAliveCount = keepAliveCount
                };
            }
        }

        private static readonly string[] s_durableRecreateWithCurrentLifetime =
        [
            "create:22", "durable:22:10h", "items:22",
            "create:23", "durable:23:7h", "items:23"
        ];

        private static readonly string[] s_durableRecreateWithFixedLifetime =
        [
            "create:22", "durable:22:10h", "items:22",
            "create:23", "durable:23:10h", "items:23"
        ];

        private static readonly string[] s_durableRecreateWithoutDurable =
        [
            "create:22", "durable:22:10h", "items:22",
            "create:23", "items:23"
        ];

        private static readonly string[] s_durableRecreateAfterFailedTransfer =
        [
            "create:22", "durable:22:10h", "items:22",
            "transfer:22", "delete:22",
            "create:23", "durable:23:7h", "items:23"
        ];
    }
}
