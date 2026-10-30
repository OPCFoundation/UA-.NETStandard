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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Measures the acknowledgement path of the <see cref="SubscriptionManager"/>
    /// Publish workers: many subscriptions and several workers acknowledge a burst of
    /// messages against a session that answers every Publish request immediately.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    public class AcknowledgementPathBenchmarks
    {
        /// <summary>
        /// The acknowledgement policy the session applies.
        /// </summary>
        public enum AcknowledgementPolicy
        {
            /// <summary>No provider and no legacy handler.</summary>
            None,

            /// <summary>A provider that sends everything.</summary>
            Provider,

            /// <summary>A provider that defers part of every other batch once.</summary>
            DeferringProvider,

            /// <summary>The obsolete deferral event handler.</summary>
            LegacyHandler
        }

        /// <summary>
        /// The acknowledgement policy of the benchmark run.
        /// </summary>
        [Params(
            AcknowledgementPolicy.None,
            AcknowledgementPolicy.Provider,
            AcknowledgementPolicy.DeferringProvider,
            AcknowledgementPolicy.LegacyHandler)]
        public AcknowledgementPolicy Policy { get; set; }

        /// <summary>
        /// Number of subscriptions the acknowledgements are spread over.
        /// </summary>
        public int SubscriptionCount { get; set; } = 100;

        /// <summary>
        /// Number of Publish workers.
        /// </summary>
        public int WorkerCount { get; set; } = 4;

        /// <summary>
        /// Number of acknowledgements per operation.
        /// </summary>
        public int AcknowledgementsPerOperation { get; set; } = 1000;

        /// <summary>
        /// Creates the manager, its subscriptions and starts the Publish workers.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_context = new BenchmarkSessionContext(Policy);
            m_manager = new SubscriptionManager(
                m_context, NullLoggerFactory.Instance, DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = WorkerCount,
                MaxPublishWorkerCount = WorkerCount
            };
            m_subscriptions.Clear();
            int next = 0;
            m_context.CreateSubscriptionFactory = () =>
            {
                var subscription = new FakeManagedSubscription
                {
                    Id = (uint)Interlocked.Increment(ref next),
                    Created = true,
                    CurrentPublishingInterval = TimeSpan.FromMilliseconds(100),
                    CurrentKeepAliveCount = 10
                };
                m_subscriptions.Add(subscription);
                return subscription;
            };
            ISubscriptionNotificationHandler handler = Mock.Of<ISubscriptionNotificationHandler>();
            for (int i = 0; i < SubscriptionCount; i++)
            {
                m_manager.Add(handler, new OptionsMonitor<SubscriptionOptions>(
                    new SubscriptionOptions { DisableUnboundedItemMode = true }));
            }
            m_manager.Resume();
        }

        /// <summary>
        /// Stops the Publish workers.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            TearDownAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Stops the Publish workers after a functional test.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_manager != null)
            {
                await m_manager.DisposeAsync().ConfigureAwait(false);
                m_manager = null;
            }
        }

        /// <summary>
        /// Queues a burst of acknowledgements and waits until the workers sent all of them.
        /// </summary>
        [Benchmark]
        public Task AcknowledgeBurstAsync()
        {
            return AcknowledgeAsync(CancellationToken.None);
        }

        /// <summary>
        /// Runs a few bursts with every policy, so the benchmark stays functional.
        /// </summary>
        /// <param name="policy">The acknowledgement policy.</param>
        [Test]
        [TestCase(AcknowledgementPolicy.None)]
        [TestCase(AcknowledgementPolicy.Provider)]
        [TestCase(AcknowledgementPolicy.DeferringProvider)]
        [TestCase(AcknowledgementPolicy.LegacyHandler)]
        public async Task AcknowledgeBurstSendsEveryAcknowledgementAsync(
            AcknowledgementPolicy policy)
        {
            Policy = policy;
            SubscriptionCount = 10;
            AcknowledgementsPerOperation = 200;
            GlobalSetup();
            for (int i = 0; i < 5; i++)
            {
                Task burst = AcknowledgeAsync(CancellationToken.None);
                Task first = await Task.WhenAny(burst, Task.Delay(30_000)).ConfigureAwait(false);
                Assert.That(first, Is.SameAs(burst), "Burst did not complete in time.");
                await burst.ConfigureAwait(false);
            }
            Assert.That(m_context!.Sent, Is.EqualTo(5L * AcknowledgementsPerOperation));
        }

        private readonly List<FakeManagedSubscription> m_subscriptions = [];

        /// <summary>
        /// Queues the acknowledgements of one operation round robin over all
        /// subscriptions and waits for the session to receive all of them.
        /// </summary>
        private async Task AcknowledgeAsync(CancellationToken ct)
        {
            BenchmarkSessionContext context = m_context!;
            SubscriptionManager manager = m_manager!;
            Task<bool> completed = context.Expect(AcknowledgementsPerOperation);
            for (int i = 0; i < AcknowledgementsPerOperation; i++)
            {
                FakeManagedSubscription owner = m_subscriptions[i % m_subscriptions.Count];
                uint sequenceNumber = unchecked(++m_sequenceNumber);
                if (sequenceNumber == 0)
                {
                    sequenceNumber = ++m_sequenceNumber;
                }
                await manager.QueueAsync(owner, owner.MessageGeneration,
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = owner.Id,
                        SequenceNumber = sequenceNumber
                    }, ct).ConfigureAwait(false);
            }
            await completed.ConfigureAwait(false);
        }

        private BenchmarkSessionContext? m_context;
        private SubscriptionManager? m_manager;
        private uint m_sequenceNumber;

        /// <summary>
        /// Session that answers every Publish request immediately with a keep-alive of a
        /// subscription the session owns, and counts the acknowledgements it received.
        /// </summary>
        private sealed class BenchmarkSessionContext : ISubscriptionManagerContext
        {
            public BenchmarkSessionContext(AcknowledgementPolicy policy)
            {
                m_policy = policy;
                AcknowledgementProvider = policy switch
                {
                    AcknowledgementPolicy.Provider => new PassThroughProvider(),
                    AcknowledgementPolicy.DeferringProvider => new DeferringProvider(),
                    _ => null
                };
            }

            public Func<IManagedSubscription> CreateSubscriptionFactory { get; set; }
                = () => throw new InvalidOperationException("No subscription factory.");

            public long Sent => Interlocked.Read(ref m_sent);

            public NodeId SessionId { get; } = new(1);

            public int SessionSubscriptionCount => 0;

            public ISubscriptionAcknowledgementProvider? AcknowledgementProvider { get; }

            public ISubscriptionAcknowledgementObserver? AcknowledgementObserver => null;

            public bool HasAcknowledgementPolicy => m_policy == AcknowledgementPolicy.LegacyHandler;

            /// <summary>
            /// Completes the returned task once <paramref name="count"/> more
            /// acknowledgements were sent.
            /// </summary>
            public Task<bool> Expect(int count)
            {
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref m_completion, completion);
                Volatile.Write(ref m_target, Interlocked.Read(ref m_sent) + count);
                return completion.Task;
            }

            public ValueTask<bool> OnPublishRecoveryErrorAsync(
                NodeId sessionId, ServiceResult error, Func<bool> isCurrent, CancellationToken ct)
            {
                return new ValueTask<bool>(false);
            }

            public IManagedSubscription CreateSubscription(
                ISubscriptionNotificationHandler handler,
                IOptionsMonitor<SubscriptionOptions> options,
                IMessageAckQueue queue,
                SubscriptionLoadState? loadState = null,
                Action<IManagedSubscription>? initialize = null)
            {
                IManagedSubscription subscription = CreateSubscriptionFactory();
                initialize?.Invoke(subscription);
                return subscription;
            }

            public AcknowledgementSelection PrepareAcknowledgementsToSend(
                List<SubscriptionAcknowledgement> acknowledgements)
            {
                return new AcknowledgementSelection(acknowledgements, []);
            }

            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader? requestHeader,
                ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
                CancellationToken ct = default)
            {
                int count = subscriptionAcknowledgements.Count;
                bool more = false;
                if (count != 0)
                {
                    long sent = Interlocked.Add(ref m_sent, count);
                    if (sent >= Volatile.Read(ref m_target))
                    {
                        Volatile.Read(ref m_completion)?.TrySetResult(true);
                    }
                }
                if (m_policy == AcknowledgementPolicy.DeferringProvider)
                {
                    // Deferred acknowledgements are not queued, so keep publishing until
                    // every acknowledgement of the burst was sent.
                    more = Interlocked.Read(ref m_sent) < Volatile.Read(ref m_target);
                }
                return new ValueTask<PublishResponse>(GetResponse(count, more));
            }

            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return new ValueTask<TransferSubscriptionsResponse>(new TransferSubscriptionsResponse());
            }

            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct = default)
            {
                return new ValueTask<DeleteSubscriptionsResponse>(new DeleteSubscriptionsResponse());
            }

            public bool TryDispatchToSessionSubscription(
                uint subscriptionId,
                NotificationMessage message,
                ArrayOf<uint> availableSequenceNumbers,
                ArrayOf<string> stringTable,
                bool moreNotifications)
            {
                return subscriptionId == kSessionSubscriptionId;
            }

            /// <summary>
            /// Responses are cached per acknowledgement count, so the session itself
            /// does not allocate per Publish request. The manager only reads them.
            /// </summary>
            private PublishResponse GetResponse(int count, bool more)
            {
                PublishResponse[] cache = more ? m_moreResponses : m_responses;
                if (count >= cache.Length)
                {
                    return CreateResponse(count, more);
                }
                PublishResponse? response = Volatile.Read(ref cache[count]);
                if (response == null)
                {
                    response = CreateResponse(count, more);
                    Volatile.Write(ref cache[count], response);
                }
                return response;
            }

            private static PublishResponse CreateResponse(int count, bool more)
            {
                var results = new StatusCode[count];
                for (int i = 0; i < results.Length; i++)
                {
                    results[i] = StatusCodes.Good;
                }
                return new PublishResponse
                {
                    SubscriptionId = kSessionSubscriptionId,
                    MoreNotifications = more,
                    NotificationMessage = new NotificationMessage(),
                    AvailableSequenceNumbers = [],
                    Results = results,
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                };
            }

            private const uint kSessionSubscriptionId = 1_000_000;
            private readonly AcknowledgementPolicy m_policy;
            private readonly PublishResponse[] m_responses = new PublishResponse[4096];
            private readonly PublishResponse[] m_moreResponses = new PublishResponse[4096];
            private TaskCompletionSource<bool>? m_completion;
            private long m_sent;
            private long m_target = long.MaxValue;
        }

        /// <summary>
        /// Provider that sends every acknowledgement.
        /// </summary>
        private sealed class PassThroughProvider : ISubscriptionAcknowledgementProvider
        {
            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                CancellationToken ct = default)
            {
                return new ValueTask<AcknowledgementDecision>(AcknowledgementDecision.SendAll);
            }
        }

        /// <summary>
        /// Provider that defers the first acknowledgement of every other batch. A
        /// deferred acknowledgement is presented again with a later batch.
        /// </summary>
        private sealed class DeferringProvider : ISubscriptionAcknowledgementProvider
        {
            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                CancellationToken ct = default)
            {
                if (acknowledgements.Count > 1 && (Interlocked.Increment(ref m_decisions) & 1) == 0)
                {
                    return new ValueTask<AcknowledgementDecision>(s_deferFirst);
                }
                return new ValueTask<AcknowledgementDecision>(AcknowledgementDecision.SendAll);
            }

            private static readonly AcknowledgementDecision s_deferFirst = new(new int[] { 0 });
            private int m_decisions;
        }
    }
}
