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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("ClassicSubscriptionEngine")]
    public class ClassicSubscriptionEngineTests
    {
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionEngineContext> m_mockContext;
        private SemaphoreSlim m_lock;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_lock = new SemaphoreSlim(1, 1);
            m_mockContext = new Mock<ISubscriptionEngineContext>();
            m_mockContext.Setup(c => c.SessionId)
                .Returns(new NodeId(1));
            m_mockContext.Setup(c => c.Disposed)
                .Returns(false);
            m_mockContext.Setup(c => c.Reconnecting)
                .Returns(false);
            m_mockContext.Setup(c => c.Connected)
                .Returns(false);
            m_mockContext.Setup(c => c.Closing)
                .Returns(false);
            m_mockContext.Setup(c => c.Telemetry)
                .Returns(m_telemetry);
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns([]);
            m_mockContext.Setup(c => c.OperationTimeout)
                .Returns(60000);
            m_mockContext.Setup(c => c.ReturnDiagnostics)
                .Returns(DiagnosticsMasks.None);
            m_mockContext.Setup(c => c.ReconnectLock)
                .Returns(m_lock);
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(0);
        }

        [TearDown]
        public void TearDown()
        {
            m_lock.Dispose();
        }

        [Test]
        public void FactoryCreateReturnsClassicEngine()
        {
            ISubscriptionEngine engine =
                ClassicSubscriptionEngineFactory.Instance.Create(
                    m_mockContext.Object);

            Assert.That(engine, Is.Not.Null);
            Assert.That(
                engine,
                Is.InstanceOf<ClassicSubscriptionEngine>());

            engine.Dispose();
        }

        [Test]
        public void PublishResponsePreservesServerSequenceAvailability()
        {
            using var subscription = new Subscription(m_telemetry);
            m_mockContext.Setup(context => context.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            ArrayOf<uint> available = new uint[] { 9, 10, 11 }.ToArrayOf();

            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTimeUtc.Now },
                subscription.Id,
                available,
                false,
                new NotificationMessage
                {
                    SequenceNumber = 11,
                    PublishTime = DateTimeUtc.Now,
                    NotificationData = [new ExtensionObject(new DataChangeNotification())]
                });

            Assert.That(subscription.AvailableSequenceNumbers.ToArray(), Is.EquivalentTo(available.ToArray()!));
        }

        /// <summary>
        /// A Republish response has no available sequence numbers. It must
        /// neither wipe the subscription's list (so other missing messages are
        /// still republished) nor drop pending acknowledgements (L7-1).
        /// </summary>
        [Test]
        public void RepublishResponseKeepsAvailableSequenceNumbersAndAcknowledgements()
        {
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            using var subscription = new Subscription(m_telemetry);
            m_mockContext.Setup(context => context.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            ArrayOf<uint> available = new uint[] { 7, 8, 9 }.ToArrayOf();

            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTimeUtc.Now },
                subscription.Id,
                available,
                false,
                new NotificationMessage
                {
                    SequenceNumber = 9,
                    PublishTime = DateTimeUtc.Now,
                    NotificationData = [new ExtensionObject(new DataChangeNotification())]
                });
            engine.AddPendingAcknowledgement(subscription.Id, 30);

            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTimeUtc.Now },
                subscription.Id,
                default,
                false,
                new NotificationMessage
                {
                    SequenceNumber = 7,
                    PublishTime = DateTimeUtc.Now,
                    NotificationData = [new ExtensionObject(new DataChangeNotification())]
                },
                republished: true);

            Assert.That(subscription.AvailableSequenceNumbers.ToArray(), Is.EquivalentTo(available.ToArray()!));
            Assert.That(engine.RemoveAcknowledgementsForSubscription(subscription.Id), Is.EqualTo(3),
                "The acks for 9 and 30 are kept and the republished 7 is added.");
        }

        [Test]
        public void StartPublishingWithNoSubscriptionsDoesNothing()
        {
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns([]);

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.StartPublishing(timeout: 5000, fullQueue: false);

            m_mockContext.Verify(
                c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void NotifySubscriptionsChangedTriggersPublishReEvaluation()
        {
            m_mockContext.Setup(c => c.Subscriptions)
                .Returns([]);
            m_mockContext.Setup(c => c.Disposed)
                .Returns(false);

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            // With no subscriptions, NotifySubscriptionsChanged
            // should not trigger a publish request.
            engine.NotifySubscriptionsChanged();

            m_mockContext.Verify(
                c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void MinPublishRequestCountDefaultsCorrectly()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.MinPublishRequestCount, Is.EqualTo(1));
            Assert.That(
                engine.MaxPublishRequestCount,
                Is.EqualTo(ushort.MaxValue));
        }

        [Test]
        public void MinMaxPublishRequestCountCanBeSet()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.MinPublishRequestCount = 5;
            engine.MaxPublishRequestCount = 50;

            Assert.That(
                engine.MinPublishRequestCount, Is.EqualTo(5));
            Assert.That(
                engine.MaxPublishRequestCount, Is.EqualTo(50));
        }

        [Test]
        public void MinPublishRequestCountRejectsInvalidValues()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.MinPublishRequestCount = 0,
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => engine.MinPublishRequestCount = 101,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void MaxPublishRequestCountRejectsInvalidValues()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.MaxPublishRequestCount = 0,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void PauseAndResumePublishing()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(engine.PausePublishing, Throws.Nothing);
            Assert.That(engine.ResumePublishing, Throws.Nothing);
        }

        [Test]
        public async Task StopPublishingAsyncDisposesCleanlyAsync()
        {
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);

            await engine.StopPublishingAsync().ConfigureAwait(false);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);
#pragma warning restore CA2000 // Dispose objects before losing scope

            Assert.That(engine.Dispose, Throws.Nothing);
            Assert.That(engine.Dispose, Throws.Nothing);
        }

        [Test]
        public void ConstructorThrowsOnNullContext()
        {
            Assert.That(
                () => new ClassicSubscriptionEngine(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void GoodPublishRequestCountDelegatesToContext()
        {
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(42);

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.GoodPublishRequestCount, Is.EqualTo(42));
        }

        [Test]
        public void BadPublishRequestCountIsZero()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.BadPublishRequestCount, Is.Zero);
        }

        [Test]
        public void BeginPublishSkipsWhenSessionKeepAliveStopped()
        {
            // Arrange — session looks healthy except KeepAliveStopped is true.
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.Reconnecting).Returns(false);
            m_mockContext.Setup(c => c.Closing).Returns(false);
            m_mockContext.Setup(c => c.KeepAliveStopped).Returns(true);
            m_mockContext.Setup(c => c.LastKeepAliveTime)
                .Returns(DateTime.UtcNow.AddSeconds(-30));

            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            // Act
            bool result = engine.BeginPublish(timeout: 5000);

            // Assert — guard short-circuited; no PublishAsync was invoked.
            Assert.That(result, Is.False);
            m_mockContext.Verify(
                c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void MaxPublishRequestCountRespectsMinimum()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.MinPublishRequestCount = 10;
            engine.MaxPublishRequestCount = 5;

            // MaxPublishRequestCount getter returns the larger of
            // min and max to ensure min is always respected.
            Assert.That(
                engine.MaxPublishRequestCount,
                Is.GreaterThanOrEqualTo(
                    engine.MinPublishRequestCount));
        }

        /// <summary>
        /// Regression coverage for the V1 ack-pruning helper added by
        /// #3540. <see cref="ClassicSubscriptionEngine.RemoveAcknowledgementsForSubscription"/>
        /// drops every queued acknowledgement targeting the given
        /// subscription id while leaving acks for other subscriptions
        /// untouched. The recovery path relies on this before recreating
        /// a subscription so stale acks for the dead id cannot produce
        /// <c>BadSubscriptionIdInvalid</c> responses on servers that
        /// re-use subscription identifiers (e.g. Kepware always
        /// starting at <c>1</c>).
        /// </summary>
        [Test]
        public void RemoveAcknowledgementsForSubscriptionDropsOnlyMatchingAcks()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.AddPendingAcknowledgement(1u, 10u);
            engine.AddPendingAcknowledgement(2u, 11u);
            engine.AddPendingAcknowledgement(1u, 12u);
            engine.AddPendingAcknowledgement(1u, 13u);
            engine.AddPendingAcknowledgement(3u, 14u);

            int dropped = engine.RemoveAcknowledgementsForSubscription(1u);

            Assert.That(dropped, Is.EqualTo(3),
                "All queued acks for the dead subscription id must be dropped.");

            int droppedAgain = engine.RemoveAcknowledgementsForSubscription(1u);
            Assert.That(droppedAgain, Is.Zero,
                "Second call must drop nothing (idempotent).");

            int droppedSecond = engine.RemoveAcknowledgementsForSubscription(2u);
            Assert.That(droppedSecond, Is.EqualTo(1),
                "Survivors must still be in the queue and drainable per-id.");

            int droppedThird = engine.RemoveAcknowledgementsForSubscription(3u);
            Assert.That(droppedThird, Is.EqualTo(1));
        }

        /// <summary>
        /// Calling
        /// <see cref="ClassicSubscriptionEngine.RemoveAcknowledgementsForSubscription"/>
        /// on an empty pending-ack list is a no-op and must not throw.
        /// </summary>
        [Test]
        public void RemoveAcknowledgementsForSubscriptionOnEmptyQueueReturnsZero()
        {
            using var engine =
                new ClassicSubscriptionEngine(m_mockContext.Object);

            int dropped = engine.RemoveAcknowledgementsForSubscription(42u);
            Assert.That(dropped, Is.Zero);
        }

        [Test]
        public void ProcessPublishResponseHandlesKeepAliveUnknownAndAcknowledgements()
        {
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            m_mockContext.Setup(c => c.SessionId).Returns(new NodeId(1));
            m_mockContext.Setup(c => c.Subscriptions).Returns([]);
            m_mockContext.Setup(c => c.DeleteSubscriptionsOnClose).Returns(false);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            engine.AddPendingAcknowledgement(7, 10);
            engine.AddPendingAcknowledgement(7, 200);

            engine.ProcessPublishResponse(
                new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    StringTable = ["s"]
                },
                7,
                [10u, 200u],
                false,
                new NotificationMessage
                {
                    SequenceNumber = 201,
                    PublishTime = DateTime.UtcNow,
                    NotificationData = []
                });

            m_mockContext.Verify(c => c.OnKeepAlive(ServerState.Running, It.IsAny<DateTime>()), Times.Once);
            m_mockContext.Verify(c => c.OnPublishNotification(
                It.IsAny<Subscription>(),
                It.IsAny<NotificationEventArgs>()), Times.Never);
        }

        /// <summary>
        /// A pending acknowledgement exactly 2^31 away from the latest
        /// sequence number must not throw from Math.Abs(int.MinValue) and
        /// abort the publish response (L4-1).
        /// </summary>
        [Test]
        public void ProcessPublishResponseToleratesAcknowledgementHalfTheSequenceSpaceAway()
        {
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            m_mockContext.Setup(c => c.Subscriptions).Returns([]);
            m_mockContext.Setup(c => c.DeleteSubscriptionsOnClose).Returns(false);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            engine.AddPendingAcknowledgement(7, 0x80000000u);

            Assert.That(() => engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTime.UtcNow },
                7,
                [],
                false,
                new NotificationMessage
                {
                    SequenceNumber = 1,
                    PublishTime = DateTime.UtcNow,
                    NotificationData = []
                }), Throws.Nothing);

            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero,
                "An acknowledgement far outside the tolerance is dropped.");
        }

        [TestCase(10u, 1u, true)]
        [TestCase(1u, 10u, true)]
        [TestCase(1u, uint.MaxValue, true)]
        [TestCase(20u, 1u, false)]
        [TestCase(0x80000000u, 0u, false)]
        [TestCase(0u, 0x80000000u, false)]
        public void OutOfOrderThresholdIsWrapAwareAndNeverThrows(
            uint sequenceNumber, uint latest, bool expected)
        {
            Assert.That(
                ClassicSubscriptionEngine.IsWithinOutOfOrderThreshold(sequenceNumber, latest),
                Is.EqualTo(expected));
        }

        [Test]
        public async Task ProcessPublishResponseDispatchesKnownSubscriptionNotificationAsync()
        {
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            var session = CreateSubscriptionSession(7);
            using var subscription = new Subscription(
                m_telemetry,
                new SubscriptionOptions
                {
                    PublishingEnabled = false
                })
            {
                Session = session.Object
            };
            await subscription.CreateAsync().ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            var dataChange = new DataChangeNotification
            {
                MonitoredItems = [],
                DiagnosticInfos = []
            };

            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTime.UtcNow, StringTable = ["ns"] },
                subscription.Id,
                [2u],
                true,
                new NotificationMessage
                {
                    SequenceNumber = 2,
                    PublishTime = DateTime.UtcNow,
                    NotificationData = [new ExtensionObject(dataChange)]
                });

            m_mockContext.Verify(c => c.OnPublishNotification(
                subscription,
                It.Is<NotificationEventArgs>(args => args.NotificationMessage.SequenceNumber == 2)),
                Times.Once);
        }

        [Test]
        public void ProcessRepublishResponseErrorClassifiesServiceResults()
        {
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            (uint statusCode, bool expectedHandled)[] cases =
            [
                (StatusCodes.BadSubscriptionIdInvalid.Code, true),
                (StatusCodes.BadMessageNotAvailable.Code, true),
                (StatusCodes.BadEncodingLimitsExceeded.Code, true),
                (StatusCodes.BadUnexpectedError.Code, false)
            ];

            foreach ((uint statusCode, bool expectedHandled) in cases)
            {
                (bool handled, ServiceResult error) = engine.ProcessRepublishResponseError(
                    new ServiceResultException(statusCode),
                    11,
                    22);

                Assert.That(handled, Is.EqualTo(expectedHandled));
                Assert.That(error.StatusCode, Is.EqualTo(statusCode));
            }

            m_mockContext.Verify(c => c.OnPublishError(
                It.IsAny<ServiceResult>(),
                11,
                22), Times.Exactly(cases.Length));
        }

        [Test]
        public async Task OnPublishCompleteProcessesSuccessfulResponseAndRequeuesAsync()
        {
            var sessionId = new NodeId(1);
            m_mockContext.Setup(c => c.SessionId).Returns(sessionId);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            var session = CreateSubscriptionSession(1);
            using var subscription = new Subscription(
                m_telemetry,
                new SubscriptionOptions
                {
                    PublishingEnabled = false
                })
            {
                Session = session.Object
            };
            await subscription.CreateAsync().ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements =>
                    (new List<SubscriptionAcknowledgement>(acknowledgements), []));
            var followUpResponse = new TaskCompletionSource<PublishResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var publishCalled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var followUpAcknowledgements = ArrayOf<SubscriptionAcknowledgement>.Empty;
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>(
                    (_, acknowledgements, _) =>
                    {
                        followUpAcknowledgements = acknowledgements;
                        publishCalled.TrySetResult(true);
                    })
                .Returns(new ValueTask<PublishResponse>(followUpResponse.Task));
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            Task<PublishResponse> task = Task.FromResult(new PublishResponse
            {
                ResponseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    StringTable = []
                },
                SubscriptionId = 1,
                AvailableSequenceNumbers = [],
                MoreNotifications = false,
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = 1,
                    PublishTime = DateTime.UtcNow,
                    NotificationData =
                    [
                        new ExtensionObject(new DataChangeNotification
                        {
                            MonitoredItems = [],
                            DiagnosticInfos = []
                        })
                    ]
                },
                Results = [StatusCodes.Good]
            });
            var sentAcknowledgements = new List<SubscriptionAcknowledgement>
            {
                new() { SubscriptionId = subscription.Id, SequenceNumber = 5 }
            };

            InvokeOnPublishComplete(engine, task, sessionId, sentAcknowledgements);
            Task completed = await Task.WhenAny(
                publishCalled.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(publishCalled.Task));
            Assert.That(followUpAcknowledgements, Has.Count.EqualTo(1));
            Assert.That(followUpAcknowledgements[0].SubscriptionId, Is.EqualTo(subscription.Id));
            Assert.That(followUpAcknowledgements[0].SequenceNumber, Is.EqualTo(1u));
            m_mockContext.Verify(c => c.PrepareAcknowledgementsToSend(
                It.IsAny<List<SubscriptionAcknowledgement>>()), Times.Once);
            m_mockContext.Verify(c => c.PublishAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_mockContext.Verify(c => c.AsyncRequestCompleted(
                task,
                99,
                DataTypes.PublishRequest), Times.Once);
            m_mockContext.Verify(c => c.AsyncRequestStarted(
                followUpResponse.Task,
                It.IsAny<System.Diagnostics.Activity>(),
                It.IsAny<uint>(),
                DataTypes.PublishRequest), Times.Once);
            m_mockContext.Verify(c => c.OnKeepAlive(ServerState.Running, It.IsAny<DateTime>()), Times.Once);
        }

        [Test]
        public void OnPublishCompleteRaisesPublishErrorForFaultedResponse()
        {
            m_mockContext.Setup(c => c.SessionId).Returns(new NodeId(1));
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.Subscriptions).Returns([
                new Subscription(m_telemetry)
                {
                    CurrentPublishingInterval = 100,
                    CurrentLifetimeCount = 10
                }
            ]);
            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);
            Task<PublishResponse> task = Task.FromException<PublishResponse>(
                new ServiceResultException(StatusCodes.BadSessionIdInvalid));

            InvokeOnPublishComplete(
                engine,
                task,
                new NodeId(1),
                [new SubscriptionAcknowledgement { SubscriptionId = 1, SequenceNumber = 2 }]);

            m_mockContext.Verify(c => c.OnPublishError(
                It.Is<ServiceResult>(result => result.StatusCode == StatusCodes.BadSessionIdInvalid),
                0,
                0), Times.Once);
            m_mockContext.Verify(c => c.OnKeepAliveError(
                It.Is<ServiceResult>(result => result.StatusCode == StatusCodes.BadSessionIdInvalid)), Times.Once);
        }

        [Test]
        public void ConcurrentPublishReEvaluationDoesNotExceedDesiredRequestCount()
        {
            const int subscriptionCount = 5;
            const int concurrentCompletions = 64;

            var subscriptions = new List<Subscription>();
            for (int ii = 0; ii < subscriptionCount; ii++)
            {
                subscriptions.Add(
                    new Subscription(m_telemetry)
                    {
                        CurrentPublishingInterval = 100,
                        CurrentLifetimeCount = 10
                    });
            }

            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.Subscriptions).Returns(subscriptions);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns(([], []));

            // Mirrors the session: a publish request only becomes visible to
            // GoodPublishRequestCount once AsyncRequestStarted has recorded it,
            // which the engine does after the request has already been issued.
            int outstanding = 0;
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(() => Volatile.Read(ref outstanding));
            m_mockContext.Setup(c => c.AsyncRequestStarted(
                    It.IsAny<Task>(),
                    It.IsAny<Activity>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Callback(() => Interlocked.Increment(ref outstanding));

            // Requests never complete, so nothing drains the outstanding count.
            // RunContinuationsAsynchronously keeps the completion below off
            // this thread, so cancelling cannot re-enter the engine inline and
            // issue further publishes while the assertion is evaluated.
            var pending = new TaskCompletionSource<PublishResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int issued = 0;
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref issued);
                    return new ValueTask<PublishResponse>(pending.Task);
                });

            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);

            using var barrier = new Barrier(concurrentCompletions);
            var threads = new Task[concurrentCompletions];
            for (int ii = 0; ii < concurrentCompletions; ii++)
            {
                threads[ii] = Task.Factory.StartNew(
                    () =>
                    {
                        barrier.SignalAndWait();
                        engine.NotifySubscriptionsChanged();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            Task.WaitAll(threads);

            // Sample before completing the pending requests so the result is
            // fixed by the re-evaluation alone.
            int issuedByReEvaluation = Volatile.Read(ref issued);
            pending.TrySetCanceled();

            Assert.That(
                issuedByReEvaluation,
                Is.LessThanOrEqualTo(subscriptionCount),
                "Concurrent publish re-evaluation must not issue more requests " +
                "than the desired publish request count.");
        }

        [Test]
        public void StartPublishingRefillsPipelineAfterRequestsAreWrittenOff()
        {
            const int subscriptionCount = 5;

            var subscriptions = new List<Subscription>();
            for (int ii = 0; ii < subscriptionCount; ii++)
            {
                var subscription = new Subscription(m_telemetry)
                {
                    CurrentPublishingInterval = 100,
                    CurrentLifetimeCount = 10
                };

                // Created is Id != 0, and the server assigns the id, so the
                // desired publish count only counts subscriptions the server
                // has acknowledged.
                typeof(Subscription).GetProperty(
                        nameof(Subscription.Id),
                        BindingFlags.Instance | BindingFlags.Public)!
                    .SetValue(subscription, (uint)(ii + 1));

                subscriptions.Add(subscription);
            }

            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.Subscriptions).Returns(subscriptions);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns(([], []));

            // Mirrors Session.GoodPublishRequestCount: a request is recorded by
            // AsyncRequestStarted and stops counting once it is written off.
            int outstanding = 0;
            int writtenOff = 0;
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(() => Volatile.Read(ref outstanding) - Volatile.Read(ref writtenOff));
            m_mockContext.Setup(c => c.AsyncRequestStarted(
                    It.IsAny<Task>(),
                    It.IsAny<Activity>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Callback(() => Interlocked.Increment(ref outstanding));

            // The requests never complete: this is the state Session.OnKeepAlive
            // recovers from, where the responses are outstanding but are no
            // longer expected to return. RunContinuationsAsynchronously keeps
            // the completion below off the test thread: the engine hangs its
            // continuation on this task with OnCompleted, so an inline
            // completion would re-enter the engine synchronously and issue
            // further publishes while the assertion is being evaluated.
            var pending = new TaskCompletionSource<PublishResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int issued = 0;
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref issued);
                    return new ValueTask<PublishResponse>(pending.Task);
                });

            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);

            engine.StartPublishing(timeout: 5000, fullQueue: true);
            int afterFill = Volatile.Read(ref issued);
            Assert.That(afterFill, Is.EqualTo(subscriptionCount));

            // Session.OnKeepAlive marks every outstanding publish request
            // defunct and then calls StartPublishing to refill the pipeline.
            Volatile.Write(ref writtenOff, afterFill);
            Assert.That(m_mockContext.Object.GoodPublishRequestCount, Is.Zero);

            engine.StartPublishing(timeout: 5000, fullQueue: false);

            // Sample before completing the pending requests so the result is
            // fixed by StartPublishing alone.
            int afterRefill = Volatile.Read(ref issued);
            pending.TrySetCanceled();

            Assert.That(
                afterRefill - afterFill,
                Is.EqualTo(subscriptionCount),
                "StartPublishing must refill a pipeline whose requests were " +
                "written off, otherwise the session never publishes again.");
        }

        [Test]
        public void BeginPublishIsBoundedByTheDesiredRequestCount()
        {
            const int subscriptionCount = 3;

            var subscriptions = new List<Subscription>();
            for (int ii = 0; ii < subscriptionCount; ii++)
            {
                subscriptions.Add(
                    new Subscription(m_telemetry)
                    {
                        CurrentPublishingInterval = 100,
                        CurrentLifetimeCount = 10
                    });
            }

            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.Subscriptions).Returns(subscriptions);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns(([], []));

            int outstanding = 0;
            int writtenOff = 0;
            m_mockContext.Setup(c => c.GoodPublishRequestCount)
                .Returns(() => Volatile.Read(ref outstanding) - Volatile.Read(ref writtenOff));
            m_mockContext.Setup(c => c.AsyncRequestStarted(
                    It.IsAny<Task>(),
                    It.IsAny<Activity>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .Callback(() => Interlocked.Increment(ref outstanding));

            var pending = new TaskCompletionSource<PublishResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int issued = 0;
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref issued);
                    return new ValueTask<PublishResponse>(pending.Task);
                });

            using var engine = new ClassicSubscriptionEngine(m_mockContext.Object);

            // Every subscription nudges the pipeline when it sees no
            // notification, which is what Subscription.HandleOnKeepAliveStopped
            // does. The nudges must not multiply the pipeline.
            for (int ii = 0; ii < subscriptionCount * 4; ii++)
            {
                engine.BeginPublish(timeout: 5000);
            }

            int afterNudges = Volatile.Read(ref issued);
            Assert.That(
                afterNudges,
                Is.EqualTo(subscriptionCount),
                "The keep alive nudge must not send past the desired publish " +
                "request count.");

            // Once the outstanding requests are written off the pipeline is
            // drained, so the nudge has to get through again.
            Volatile.Write(ref writtenOff, afterNudges);

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            int afterRecovery = Volatile.Read(ref issued);
            pending.TrySetCanceled();

            Assert.That(afterRecovery, Is.EqualTo(afterNudges + 1));
        }

        /// <summary>
        /// An acknowledgement approved by an asynchronous decision is not sent when its
        /// subscription was recreated with the same id while the provider decided: it is
        /// retired instead.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task AnApprovedAcknowledgementOfARecreatedSubscriptionIsRetiredAsync(CancellationToken ct)
        {
            var decision = new TaskCompletionSource<AcknowledgementDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new PendingDecisionProvider(decision.Task);
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, provider, observer);

            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription original = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            using Subscription replacement = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            List<Subscription> subscriptions = [original];
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => [.. subscriptions]);
            var sent = new System.Collections.Concurrent.ConcurrentQueue<uint>();
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>(
                    (_, acknowledgements, _) =>
                    {
                        foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                        {
                            sent.Enqueue(acknowledgement.SequenceNumber);
                        }
                    })
                .Returns(new ValueTask<PublishResponse>(new TaskCompletionSource<PublishResponse>().Task));
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            var handle = new AcknowledgementHandle(original.GetAcknowledgementScope(), 3);

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(provider.Calls, Is.EqualTo(1), "The decision is pending.");
            subscriptions[0] = replacement;
            decision.SetResult(AcknowledgementDecision.SendAll);
            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            await WaitForAsync(() => observer.Outcomes.Count == 1, ct).ConfigureAwait(false);
            Assert.That(sent, Is.Empty, "The acknowledgement would confirm a message of the replacement.");
            Assert.That(observer.Outcomes.Single().Handle, Is.EqualTo(handle));
            Assert.That(observer.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
        }

        /// <summary>
        /// Disposing the engine ends in-flight acknowledgements as well as pending ones.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task DisposeEndsInFlightAcknowledgementsAsync(CancellationToken ct)
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);

            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements =>
                    ([.. acknowledgements], []));
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PublishResponse>(new TaskCompletionSource<PublishResponse>().Task));
            var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            engine.AddPendingAcknowledgement(7, 4);

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            engine.AddPendingAcknowledgement(7, 5);
            engine.Dispose();

            await WaitForAsync(() => observer.Outcomes.Count == 3, ct).ConfigureAwait(false);
            uint[] ended = [.. observer.Outcomes.Select(outcome => outcome.Handle.SequenceNumber)];
            Array.Sort(ended);
            Assert.That(ended,
                Is.EqualTo(s_inFlightAndPending));
            Assert.That(observer.Outcomes.All(outcome => outcome.Disposition == AcknowledgementDisposition.Abandoned),
                Is.True);
        }

        /// <summary>
        /// With an acknowledgement configuration, an acknowledgement that is still pending is
        /// queued once and tracked for confirmation; without one, duplicates are kept as before.
        /// </summary>
        [Test]
        public async Task TrackedAcknowledgementsAreQueuedOnceAsync()
        {
            var acknowledgements = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            var confirmations = (ConfirmedAcknowledgementProvider)acknowledgements.Provider!;
            SessionAcknowledgements? configured = null;
            var context = new AcknowledgingContext(m_mockContext.Object, () => configured);

            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using (var untracked = new ClassicSubscriptionEngine(context))
            {
                untracked.AddPendingAcknowledgement(7, 3);
                untracked.AddPendingAcknowledgement(7, 3);
                Assert.That(untracked.RemoveAcknowledgementsForSubscription(7), Is.EqualTo(2));
            }
            configured = acknowledgements;
            using var engine = new ClassicSubscriptionEngine(context);

            engine.AddPendingAcknowledgement(7, 3);
            engine.AddPendingAcknowledgement(7, 3);

            Assert.That(confirmations.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.EqualTo(1));
        }

        /// <summary>
        /// Deleting a classic subscription ends its pending acknowledgements right away, without
        /// waiting for another Publish request.
        /// </summary>
        [Test]
        public async Task DeletingASubscriptionRetiresItsPendingAcknowledgementsAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            AcknowledgementScope before = subscription.GetAcknowledgementScope();

            subscription.SupersedeIdClaim();
            engine.RetireAcknowledgementsOf(subscription);

            await WaitForAsync(() => observer.Outcomes.Count == 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(observer.Outcomes.Single().Handle, Is.EqualTo(new AcknowledgementHandle(before, 3)));
            Assert.That(observer.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero, "Nothing is left to send.");
            engine.RetireAcknowledgementsOf(subscription);
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1), "Retiring again reports nothing new.");
        }

        /// <summary>
        /// Removing a classic subscription from its session ends its current lifetime too, so its
        /// pending acknowledgements end without another Publish request.
        /// </summary>
        [Test]
        public async Task RemovingASubscriptionRetiresItsCurrentLifetimeAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 4);

            engine.RetireAcknowledgementsOf(subscription);
            Assert.That(observer.Outcomes, Is.Empty, "The current lifetime is kept when only older ones ended.");
            engine.RetireAcknowledgementsOf(subscription, includeCurrentLifetime: true);

            await WaitForAsync(() => observer.Outcomes.Count == 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(observer.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
        }

        /// <summary>
        /// A subscription transferred to another session hands its tracked acknowledgements over:
        /// the previous engine reports nothing, the new one tracks those the server still holds
        /// and ends the others as not retained, so each acknowledgement ends exactly once.
        /// </summary>
        [Test]
        public async Task TransferredAcknowledgementsEndExactlyOnceAsync()
        {
            var oldObserver = new CollectingObserver();
            var newObserver = new CollectingObserver();
            var oldConfiguration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, oldObserver);
            var newConfiguration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, newObserver);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            var oldEngine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => oldConfiguration));
            using var newEngine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => newConfiguration));
            oldEngine.AddPendingAcknowledgement(7, 3);
            oldEngine.AddPendingAcknowledgement(7, 4);
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            oldConfiguration.Confirm(new AcknowledgementHandle(scope, 3));

            var released = oldEngine.ReleaseAcknowledgementsOf(subscription);
            newEngine.AdoptAcknowledgements(
                subscription,
                released,
                ArrayOf.Create<uint>([3u]),
                ArrayOf.Create<uint>([3u]));
            oldEngine.Dispose();

            Assert.That(released, Has.Count.EqualTo(2));
            Assert.That(oldObserver.Outcomes, Is.Empty, "The previous session reports nothing.");
            Assert.That(oldEngine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
            await WaitForAsync(() => newObserver.Outcomes.Count == 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(newObserver.Outcomes.Single().Handle, Is.EqualTo(new AcknowledgementHandle(scope, 4)));
            Assert.That(newObserver.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.NotRetained));
            Assert.That(
                ((ConfirmedAcknowledgementProvider)newConfiguration.Provider!).TrackedAcknowledgements,
                Is.EqualTo(1),
                "Only the retained acknowledgement stays tracked.");
            Assert.That(newEngine.RemoveAcknowledgementsForSubscription(7), Is.EqualTo(1),
                "The retained acknowledgement is queued in the new engine.");
        }

        /// <summary>
        /// A transferred acknowledgement reserved for republish remains tracked but cannot be
        /// sent until the republished notification is received.
        /// </summary>
        [Test]
        public async Task RepublishReservedAcknowledgementIsQueuedAfterReceiptAsync()
        {
            var oldConfiguration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            var newConfiguration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using var oldEngine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => oldConfiguration));
            using var newEngine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => newConfiguration));
            var requests = new List<ArrayOf<SubscriptionAcknowledgement>>();
            var pendingResponse = new TaskCompletionSource<PublishResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>(
                    (_, acknowledgements, _) => requests.Add(acknowledgements))
                .Returns(() => new ValueTask<PublishResponse>(pendingResponse.Task));
            oldEngine.AddPendingAcknowledgement(7, 3);
            AcknowledgementHandle handle = new(subscription.GetAcknowledgementScope(), 3);
            oldConfiguration.Confirm(handle);

            List<AcknowledgementHandover> released = oldEngine.ReleaseAcknowledgementsOf(subscription);
            newEngine.AdoptAcknowledgements(subscription, released, [3u], []);

            Assert.That(
                ((ConfirmedAcknowledgementProvider)newConfiguration.Provider!).PendingConfirmations,
                Is.EqualTo(1),
                "The confirmation stays associated with the tracked acknowledgement.");

            Assert.That(newEngine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(requests.Single().IsEmpty, Is.True,
                "A reserved acknowledgement cannot be sent before the republished notification arrives.");

            newEngine.AddPendingAcknowledgement(7, 3);

            Assert.That(newEngine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].ToArray()!.Select(acknowledgement => acknowledgement.SequenceNumber),
                Is.EqualTo([3u]),
                "Receiving the republished message makes its acknowledgement eligible.");
            Assert.That(
                ((ConfirmedAcknowledgementProvider)newConfiguration.Provider!).TrackedAcknowledgements,
                Is.EqualTo(1),
                "The acknowledgement remains tracked until its Publish result arrives.");
        }

        /// <summary>
        /// An acknowledgement retired while its Publish request is in flight is not requeued when
        /// the request then fails, so it cannot be sent later under a reused id.
        /// </summary>
        [Test]
        public async Task AnAcknowledgementRetiredInFlightIsNotRequeuedWhenThePublishFailsAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            var response = new TaskCompletionSource<PublishResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            int publishCalls = 0;
            var resent = new System.Collections.Concurrent.ConcurrentQueue<uint>();
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>((_, acknowledgements, _) =>
                {
                    if (Interlocked.Increment(ref publishCalls) == 1)
                    {
                        return new ValueTask<PublishResponse>(response.Task);
                    }
                    foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                    {
                        resent.Enqueue(acknowledgement.SequenceNumber);
                    }
                    return new ValueTask<PublishResponse>(new TaskCompletionSource<PublishResponse>().Task);
                });
            var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockContext.Setup(c => c.OnPublishError(It.IsAny<ServiceResult>(), It.IsAny<uint>(), It.IsAny<uint>()))
                .Callback(() => failed.TrySetResult(true));
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            subscription.SupersedeIdClaim();
            engine.RetireAcknowledgementsOf(subscription);
            response.SetException(new ServiceResultException(StatusCodes.BadTimeout));
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            engine.BeginPublish(timeout: 5000);

            Assert.That(resent, Is.Empty, "The retired entry is neither requeued nor sent again.");
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// When the session is recreated in place while a Publish request is in flight, its tracked
        /// acknowledgements are not retired but sent again on the new session, so a subscription
        /// transferred to it keeps its handles and each acknowledgement ends once.
        /// </summary>
        [Test]
        public async Task InFlightAcknowledgementsSurviveAnInPlaceSessionRecreateAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            List<SubscriptionAcknowledgement>? inFlight = null;
            var sent = new System.Collections.Concurrent.ConcurrentQueue<uint>();
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>((_, acknowledgements, _) =>
                {
                    if (inFlight == null)
                    {
                        inFlight = [.. acknowledgements];
                    }
                    else
                    {
                        foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                        {
                            sent.Enqueue(acknowledgement.SequenceNumber);
                        }
                    }
                    return new ValueTask<PublishResponse>(new TaskCompletionSource<PublishResponse>().Task);
                });
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(inFlight, Has.Count.EqualTo(1));

            // The request fails for the previous session after the session was recreated in place.
            InvokeOnPublishComplete(
                engine,
                Task.FromException<PublishResponse>(new ServiceResultException(StatusCodes.BadSessionIdInvalid)),
                new NodeId(9),
                inFlight);
            engine.AddPendingAcknowledgement(7, 3);
            engine.BeginPublish(timeout: 5000);

            Assert.That(observer.Outcomes, Is.Empty, "Nothing ended: the subscription lives on.");
            Assert.That(sent, Is.EqualTo(s_resentOnce), "Sent once on the new session; the transfer adds no duplicate.");
        }
        private static readonly uint[] s_resentOnce = [3u];

        private static readonly uint[] s_inFlightAndPending = [3u, 4u, 5u];

        /// <summary>
        /// With an observer but no provider, an acknowledgement whose subscription was recreated
        /// with the same id is retired instead of being sent, whether the subscription changes
        /// before or while the legacy handler runs.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyPathRetiresAcknowledgementsOfARecreatedSubscriptionAsync(bool duringHandler)
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription original = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            using Subscription replacement = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            List<Subscription> subscriptions = [original];
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => [.. subscriptions]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements =>
                {
                    subscriptions[0] = replacement;
                    return ([.. acknowledgements], []);
                });
            System.Collections.Concurrent.ConcurrentQueue<uint> sent = CapturePublishedAcknowledgements();
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            if (!duringHandler)
            {
                subscriptions[0] = replacement;
            }

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            await WaitForAsync(() => observer.Outcomes.Count == 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(sent, Is.Empty);
            Assert.That(observer.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
        }

        /// <summary>
        /// With an observer but no provider, an acknowledgement the legacy handler drops from both
        /// lists ends as abandoned right away.
        /// </summary>
        [Test]
        public async Task LegacyPathAbandonsAcknowledgementsTheHandlerDropsAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements =>
                    ([], [.. acknowledgements.Where(acknowledgement => acknowledgement.SequenceNumber != 3)]));
            CapturePublishedAcknowledgements();
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            engine.AddPendingAcknowledgement(7, 4);

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            await WaitForAsync(() => observer.Outcomes.Count == 1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(observer.Outcomes.Single().Handle.SequenceNumber, Is.EqualTo(3u));
            Assert.That(observer.Outcomes.Single().Disposition, Is.EqualTo(AcknowledgementDisposition.Abandoned));
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.EqualTo(1), "The retained one is kept.");
        }

        /// <summary>
        /// An older subscription still registered under an id the server gave to a newer one is
        /// superseded: its acknowledgements are retired and new acknowledgements of the id belong
        /// to the newer one, and it stays superseded after the newer one is gone.
        /// </summary>
        [Test]
        public async Task AnOlderOwnerOfAReusedIdIsSupersededAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, null, observer);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription older = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            using Subscription newer = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            List<Subscription> subscriptions = [older];
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => [.. subscriptions]);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            System.Collections.Concurrent.ConcurrentQueue<uint> sent = CapturePublishedAcknowledgements();
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            subscriptions = [older, newer];
            engine.AddPendingAcknowledgement(7, 5);
            Assert.That(older.IsIdClaimSuperseded, Is.True);
            Assert.That(newer.IsIdClaimSuperseded, Is.False);
            subscriptions = [older];

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);

            await WaitForAsync(() => observer.Outcomes.Count == 2, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(sent, Is.Empty, "Neither the superseded owner nor the retired newer one may acknowledge.");
            Assert.That(observer.Outcomes.All(outcome => outcome.Disposition == AcknowledgementDisposition.OwnerRetired),
                Is.True);
            Assert.That(observer.Outcomes.Single(outcome => outcome.Handle.SequenceNumber == 5).Handle.Scope!.Owner,
                Is.SameAs(newer), "The acknowledgement of the reused id belongs to the newer subscription.");
        }

        /// <summary>
        /// The classic engine passes its shutdown token to the provider, so a pending decision is
        /// cancelled when the engine is disposed.
        /// </summary>
        [Test]
        public async Task ProviderDecisionsAreCancelledWhenTheEngineIsDisposedAsync()
        {
            var decision = new TaskCompletionSource<AcknowledgementDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new PendingDecisionProvider(decision.Task);
            var configuration = new SessionAcknowledgements(AcknowledgementMode.Immediate, provider, null);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            CapturePublishedAcknowledgements();
            var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);

            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(provider.LastToken.CanBeCanceled, Is.True);
            Assert.That(provider.LastToken.IsCancellationRequested, Is.False);
            engine.Dispose();

            Assert.That(provider.LastToken.IsCancellationRequested, Is.True);
        }

        /// <summary>
        /// Concurrent Publish calls cannot invoke the provider twice while the first call
        /// has not yet returned its asynchronous decision.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task ProviderDecisionIsReservedBeforeInvokingApplicationCodeAsync(CancellationToken ct)
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decision = new TaskCompletionSource<AcknowledgementDecision>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            var provider = new Mock<ISubscriptionAcknowledgementProvider>();
            provider.Setup(value => value.DecideAsync(
                    It.IsAny<ArrayOf<AcknowledgementHandle>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<AcknowledgementHandle> _, CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        entered.TrySetResult(true);
                        if (!release.Wait(TimeSpan.FromSeconds(5), token))
                        {
                            throw new TimeoutException("The test did not release the provider invocation.");
                        }
                    }
                    return new ValueTask<AcknowledgementDecision>(decision.Task);
                });
            var configuration = new SessionAcknowledgements(
                AcknowledgementMode.Immediate, provider.Object, null);
            m_mockContext.Setup(value => value.Connected).Returns(true);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(value => value.Subscriptions).Returns([subscription]);
            System.Collections.Concurrent.ConcurrentQueue<uint> sent = CapturePublishedAcknowledgements();
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);

            engine.MinPublishRequestCount = 2;
            Task<bool> first = Task.Run(() => engine.BeginPublish(5000), ct);
            try
            {
                await entered.Task.WaitAsync(ct).ConfigureAwait(false);
                Assert.That(engine.BeginPublish(5000), Is.True);
                Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
                Assert.That(sent, Is.Empty);
            }
            finally
            {
                release.Set();
                await first.ConfigureAwait(false);
                decision.TrySetResult(AcknowledgementDecision.SendAll);
            }
            engine.BeginPublish(5000);
            await WaitForAsync(() => sent.Contains(3u), ct).ConfigureAwait(false);
        }

        private System.Collections.Concurrent.ConcurrentQueue<uint> CapturePublishedAcknowledgements()
        {
            var sent = new System.Collections.Concurrent.ConcurrentQueue<uint>();
            m_mockContext.Setup(c => c.PublishAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader, ArrayOf<SubscriptionAcknowledgement>, CancellationToken>(
                    (_, acknowledgements, _) =>
                    {
                        foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                        {
                            sent.Enqueue(acknowledgement.SequenceNumber);
                        }
                    })
                .Returns(new ValueTask<PublishResponse>(new TaskCompletionSource<PublishResponse>().Task));
            return sent;
        }

        private async Task<Subscription> CreateSubscriptionAsync(uint subscriptionId)
        {
            Mock<ISession> session = CreateSubscriptionSession(subscriptionId);
            var subscription = new Subscription(
                m_telemetry,
                new SubscriptionOptions
                {
                    PublishingEnabled = false
                })
            {
                Session = session.Object
            };
            await subscription.CreateAsync().ConfigureAwait(false);
            return subscription;
        }

        private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Forwards the engine context to a mock and adds the acknowledgement configuration,
        /// which the mock cannot provide because its type is internal.
        /// </summary>
        /// <param name="context">The mocked context.</param>
        /// <param name="configuration">Returns the acknowledgement configuration.</param>
        private sealed class AcknowledgingContext(
            ISubscriptionEngineContext context,
            Func<SessionAcknowledgements?> configuration) :
            ISubscriptionEngineContext,
            IAcknowledgementPolicyCapability
        {
            /// <inheritdoc/>
            public NodeId SessionId => context.SessionId;

            /// <inheritdoc/>
            public bool Reconnecting => context.Reconnecting;

            /// <inheritdoc/>
            public bool Connected => context.Connected;

            /// <inheritdoc/>
            public bool Closing => context.Closing;

            /// <inheritdoc/>
            public bool KeepAliveStopped => context.KeepAliveStopped;

            /// <inheritdoc/>
            public DateTime LastKeepAliveTime => context.LastKeepAliveTime;

            /// <inheritdoc/>
            public bool Disposed => context.Disposed;

            /// <inheritdoc/>
            public bool DeleteSubscriptionsOnClose => context.DeleteSubscriptionsOnClose;

            /// <inheritdoc/>
            public int OperationTimeout => context.OperationTimeout;

            /// <inheritdoc/>
            public double SessionTimeout => context.SessionTimeout;

            /// <inheritdoc/>
            public ServerState ServerState => context.ServerState;

            /// <inheritdoc/>
            public DiagnosticsMasks ReturnDiagnostics => context.ReturnDiagnostics;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry => context.Telemetry;

            /// <inheritdoc/>
            public IReadOnlyList<Subscription> Subscriptions => context.Subscriptions;

            /// <inheritdoc/>
            public SemaphoreSlim ReconnectLock => context.ReconnectLock;

            /// <inheritdoc/>
            public int GoodPublishRequestCount => context.GoodPublishRequestCount;

            /// <inheritdoc/>
            public ISubscriptionServiceSetClientMethods SubscriptionServiceSet => context.SubscriptionServiceSet;

            /// <inheritdoc/>
            public IMonitoredItemServiceSetClientMethods MonitoredItemServiceSet => context.MonitoredItemServiceSet;

            /// <inheritdoc/>
            public IMethodServiceSetClientMethods MethodServiceSet => context.MethodServiceSet;

            /// <inheritdoc/>
            public bool HasAcknowledgementPolicy => false;

            /// <inheritdoc/>
            public SessionAcknowledgements? Acknowledgements => configuration();

            /// <inheritdoc/>
            public ValueTask<PublishResponse> PublishAsync(
                RequestHeader requestHeader,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                CancellationToken ct = default)
            {
                return context.PublishAsync(requestHeader, acknowledgements, ct);
            }

            /// <inheritdoc/>
            public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                bool sendInitialValues,
                CancellationToken ct = default)
            {
                return context.TransferSubscriptionsAsync(requestHeader, subscriptionIds, sendInitialValues, ct);
            }

            /// <inheritdoc/>
            public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                RequestHeader? requestHeader,
                ArrayOf<uint> subscriptionIds,
                CancellationToken ct = default)
            {
                return context.DeleteSubscriptionsAsync(requestHeader, subscriptionIds, ct);
            }

            /// <inheritdoc/>
            public void OnKeepAlive(ServerState serverState, DateTime timestamp)
            {
                context.OnKeepAlive(serverState, timestamp);
            }

            /// <inheritdoc/>
            public void OnPublishError(ServiceResult error, uint subscriptionId, uint sequenceNumber)
            {
                context.OnPublishError(error, subscriptionId, sequenceNumber);
            }

            /// <inheritdoc/>
            public void OnPublishNotification(Subscription subscription, NotificationEventArgs notification)
            {
                context.OnPublishNotification(subscription, notification);
            }

            /// <inheritdoc/>
            public void OnKeepAliveError(ServiceResult error)
            {
                context.OnKeepAliveError(error);
            }

            /// <inheritdoc/>
            public void AsyncRequestStarted(Task task, Activity? activity, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestStarted(task, activity, requestHandle, requestTypeId);
            }

            /// <inheritdoc/>
            public void AsyncRequestCompleted(Task task, uint requestHandle, uint requestTypeId)
            {
                context.AsyncRequestCompleted(task, requestHandle, requestTypeId);
            }

            /// <inheritdoc/>
            public (List<SubscriptionAcknowledgement> toSend, List<SubscriptionAcknowledgement> updatedPending)
                PrepareAcknowledgementsToSend(List<SubscriptionAcknowledgement> currentAcknowledgements)
            {
                return context.PrepareAcknowledgementsToSend(currentAcknowledgements);
            }

            /// <inheritdoc/>
            public ValueTask DeleteOrphanedSubscriptionAsync(uint subscriptionId)
            {
                return context.DeleteOrphanedSubscriptionAsync(subscriptionId);
            }

            /// <inheritdoc/>
            public bool TryDispatchToSessionSubscription(
                uint subscriptionId,
                NotificationMessage message,
                ArrayOf<uint> availableSequenceNumbers,
                ArrayOf<string> stringTable,
                bool moreNotifications)
            {
                return context.TryDispatchToSessionSubscription(
                    subscriptionId, message, availableSequenceNumbers, stringTable, moreNotifications);
            }
        }

        /// <summary>
        /// Returns a decision that completes when the test says so.
        /// </summary>
        private sealed class PendingDecisionProvider(Task<AcknowledgementDecision> decision) :
            ISubscriptionAcknowledgementProvider
        {
            public int Calls => Volatile.Read(ref m_calls);

            public CancellationToken LastToken { get; private set; }

            public ValueTask<AcknowledgementDecision> DecideAsync(
                ArrayOf<AcknowledgementHandle> acknowledgements,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_calls);
                LastToken = ct;
                return new ValueTask<AcknowledgementDecision>(decision);
            }

            private int m_calls;
        }

        /// <summary>
        /// Collects the reported outcomes.
        /// </summary>
        private sealed class CollectingObserver : ISubscriptionAcknowledgementObserver
        {
            public IReadOnlyList<AcknowledgementOutcome> Outcomes => [.. m_outcomes];

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    m_outcomes.Enqueue(outcome);
                }
                return default;
            }

            private readonly System.Collections.Concurrent.ConcurrentQueue<AcknowledgementOutcome> m_outcomes = new();
        }

        private static void InvokeOnPublishComplete(
            ClassicSubscriptionEngine engine,
            Task<PublishResponse> task,
            NodeId sessionId,
            List<SubscriptionAcknowledgement>? acknowledgements)
        {
            MethodInfo method = typeof(ClassicSubscriptionEngine).GetMethod(
                "OnPublishComplete",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(
                engine,
                [
                    task,
                    sessionId,
                    acknowledgements,
                    new RequestHeader { RequestHandle = 99 }
                ]);
        }

        private static Mock<ISession> CreateSubscriptionSession(uint subscriptionId)
        {
            var session = new Mock<ISession>(MockBehavior.Loose);
            session.SetupGet(s => s.SessionTimeout).Returns(60000);
            session.Setup(s => s.CreateSubscriptionAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<bool>(),
                    It.IsAny<byte>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CreateSubscriptionResponse>(new CreateSubscriptionResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    SubscriptionId = subscriptionId,
                    RevisedPublishingInterval = 100,
                    RevisedMaxKeepAliveCount = 10,
                    RevisedLifetimeCount = 30
                }));
            return session;
        }

        [TestCase(true, true, true, TestName = "ReleaseAcknowledgementRemovesTrackingAndReturnsConfirmation_TrackedConfirmed")]
        [TestCase(true, false, false, TestName = "ReleaseAcknowledgementRemovesTrackingAndReturnsConfirmation_TrackedUnconfirmed")]
        [TestCase(false, false, false, TestName = "ReleaseAcknowledgementRemovesTrackingAndReturnsConfirmation_NotTracked")]
        public async Task ReleaseAcknowledgementRemovesTrackingAndReturnsConfirmationAsync(
            bool tracked,
            bool confirmed,
            bool expectedConfirmed)
        {
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            var provider = new ConfirmedAcknowledgementProvider();
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var handle = new AcknowledgementHandle(scope, 3);

            if (tracked)
            {
                provider.Track(handle);
                if (confirmed)
                {
                    provider.Confirm(handle);
                }
            }

            Assert.That(provider.Release(handle), Is.EqualTo(expectedConfirmed));
            Assert.That(provider.TrackedAcknowledgements, Is.Zero);
            Assert.That(provider.PendingConfirmations, Is.Zero);
            Assert.That(provider.Release(handle), Is.False, "A removed or absent handle cannot be released twice.");

            provider.Confirm(handle);
            Assert.That(provider.TrackedAcknowledgements, Is.Zero, "A late confirmation cannot recreate tracking.");
            Assert.That(provider.PendingConfirmations, Is.Zero);

            var controlHandle = new AcknowledgementHandle(scope, 4);
            AcknowledgementDecision removedDecision = await provider.DecideAsync(
                new[] { handle }.ToArrayOf()).ConfigureAwait(false);
            Assert.That(removedDecision.Deferred.ToArray(), Is.EqualTo(s_deferredFirstIndex));

            var isolationProvider = new ConfirmedAcknowledgementProvider();
            isolationProvider.Track(handle);
            isolationProvider.Confirm(handle);
            isolationProvider.Track(controlHandle);
            isolationProvider.Confirm(controlHandle);
            Assert.That(isolationProvider.Release(handle), Is.True);
            Assert.That(isolationProvider.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(isolationProvider.PendingConfirmations, Is.EqualTo(1));
            AcknowledgementDecision decision = await isolationProvider.DecideAsync(
                new[] { handle, controlHandle }.ToArrayOf()).ConfigureAwait(false);

            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_deferredFirstIndex),
                "The released handle is deferred while the independently tracked control remains eligible.");
            AcknowledgementDecision controlDecision = await isolationProvider.DecideAsync(
                new[] { controlHandle }.ToArrayOf()).ConfigureAwait(false);
            Assert.That(controlDecision.Deferred.Count, Is.Zero);
        }

        [Test]
        public async Task ReleaseAcknowledgementsDoesNotChangeStateWhenDetachFailsAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(subscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var handle = new AcknowledgementHandle(scope, 3);
            var provider = (ConfirmedAcknowledgementProvider)configuration.Provider!;
            provider.Confirm(handle);
            int detachCalls = 0;
            List<AcknowledgementHandover> released;

            bool detached = engine.ReleaseAcknowledgementsOf(
                subscription,
                () =>
                {
                    detachCalls++;
                    return false;
                },
                out released);

            Assert.That(detachCalls, Is.EqualTo(1));
            Assert.That(detached, Is.False);
            Assert.That(released, Is.Empty);
            Assert.That(owners, Is.EqualTo(new[] { subscription }));
            Assert.That(provider.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(provider.PendingConfirmations, Is.EqualTo(1));
            Assert.That(observer.Outcomes, Is.Empty);

            System.Collections.Concurrent.ConcurrentQueue<uint> sent = CapturePublishedAcknowledgements();
            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(sent.ToArray(), Is.EqualTo(s_sequenceThree),
                "The queued acknowledgement remains available after detach fails.");
            Assert.That(provider.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(provider.PendingConfirmations, Is.EqualTo(1));
            Assert.That(observer.Outcomes, Is.Empty);

            bool detachedOnRetry = engine.ReleaseAcknowledgementsOf(
                subscription,
                () =>
                {
                    detachCalls++;
                    owners.Remove(subscription);
                    return true;
                },
                out released);

            Assert.That(detachedOnRetry, Is.True);
            Assert.That(detachCalls, Is.EqualTo(2));
            Assert.That(owners, Is.Empty);
            Assert.That(released, Has.Count.EqualTo(1));
            Assert.That(released[0].Handle, Is.EqualTo(handle));
            Assert.That(released[0].Confirmed, Is.True);
            Assert.That(observer.Outcomes, Is.Empty);
        }

        [TestCase(false, TestName = "ReleaseAcknowledgementsSeparatesOldAndCurrentLifetimes_CurrentUnconfirmed")]
        [TestCase(true, TestName = "ReleaseAcknowledgementsSeparatesOldAndCurrentLifetimes_CurrentConfirmed")]
        public async Task ReleaseAcknowledgementsSeparatesOldAndCurrentLifetimesAsync(bool currentConfirmed)
        {
            var observer = new SignalingObserver(expectedOutcomeCount: 1);
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription oldSubscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(oldSubscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));

            engine.AddPendingAcknowledgement(7, 3);
            AcknowledgementScope oldScope = oldSubscription.GetAcknowledgementScope();
            var oldHandle = new AcknowledgementHandle(oldScope, 3);
            using Subscription currentSubscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(currentSubscription);
            engine.AddPendingAcknowledgement(7, 4);
            AcknowledgementScope currentScope = currentSubscription.GetAcknowledgementScope();
            var currentHandle = new AcknowledgementHandle(currentScope, 4);
            Assert.That(oldSubscription.IsIdClaimSuperseded, Is.True);
            Assert.That(currentSubscription.IsIdClaimSuperseded, Is.False);
            var provider = (ConfirmedAcknowledgementProvider)configuration.Provider!;
            if (currentConfirmed)
            {
                provider.Confirm(currentHandle);
            }

            bool detached = engine.ReleaseAcknowledgementsOf(
                oldSubscription,
                () =>
                {
                    owners.Remove(oldSubscription);
                    return true;
                },
                out List<AcknowledgementHandover> released);

            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(detached, Is.True);
            Assert.That(owners, Has.Count.EqualTo(1));
            Assert.That(owners[0], Is.SameAs(currentSubscription));
            Assert.That(released, Is.Empty, "Releasing the old owner cannot hand off the current owner's acknowledgement.");
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1));
            Assert.That(observer.Outcomes[0].Handle, Is.EqualTo(oldHandle));
            Assert.That(observer.Outcomes[0].Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
            Assert.That(observer.Outcomes[0].Status, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            Assert.That(provider.TrackedAcknowledgements, Is.EqualTo(1));
            Assert.That(provider.PendingConfirmations, Is.EqualTo(currentConfirmed ? 1 : 0));
            AcknowledgementDecision currentDecision = await provider.DecideAsync(
                new[] { currentHandle }.ToArrayOf()).ConfigureAwait(false);
            Assert.That(currentDecision.Deferred.Count, Is.EqualTo(currentConfirmed ? 0 : 1),
                "The current owner's confirmation state survives retirement of the superseded owner.");

            bool currentDetached = engine.ReleaseAcknowledgementsOf(
                currentSubscription,
                () =>
                {
                    owners.Remove(currentSubscription);
                    return true;
                },
                out List<AcknowledgementHandover> currentReleased);

            Assert.That(currentDetached, Is.True);
            Assert.That(owners, Is.Empty);
            Assert.That(currentReleased, Has.Count.EqualTo(1));
            Assert.That(currentReleased[0].Handle, Is.EqualTo(currentHandle));
            Assert.That(currentReleased[0].Confirmed, Is.EqualTo(currentConfirmed));
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1),
                "Only the superseded old handle retires; the current handle is released for handover.");
            Assert.That(provider.TrackedAcknowledgements, Is.Zero);
            Assert.That(provider.PendingConfirmations, Is.Zero);
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
        }

        [Test]
        public async Task RestoreAcknowledgementsPreservesConfirmationAsync()
        {
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, null);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(subscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            engine.AddPendingAcknowledgement(7, 4);
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var confirmedHandle = new AcknowledgementHandle(scope, 3);
            var unconfirmedHandle = new AcknowledgementHandle(scope, 4);
            var provider = (ConfirmedAcknowledgementProvider)configuration.Provider!;
            provider.Confirm(confirmedHandle);

            bool detached = engine.ReleaseAcknowledgementsOf(
                subscription,
                () =>
                {
                    owners.Remove(subscription);
                    return true;
                },
                out List<AcknowledgementHandover> released);
            int attachCalls = 0;
            bool restored = engine.RestoreAcknowledgementsOf(
                subscription,
                released,
                () =>
                {
                    attachCalls++;
                    owners.Add(subscription);
                    return true;
                });

            Assert.That(detached, Is.True);
            Assert.That(released.Select(item => item.Handle), Is.EquivalentTo(new[] { confirmedHandle, unconfirmedHandle }));
            Assert.That(restored, Is.True);
            Assert.That(attachCalls, Is.EqualTo(1));
            Assert.That(owners, Is.EqualTo(new[] { subscription }));
            Assert.That(provider.TrackedAcknowledgements, Is.EqualTo(2));
            Assert.That(provider.PendingConfirmations, Is.EqualTo(1));
            AcknowledgementDecision decision = await provider.DecideAsync(
                released.Select(item => item.Handle).ToArrayOf()).ConfigureAwait(false);
            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_deferredSecondIndex),
                "Only the previously confirmed acknowledgement is eligible.");
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.EqualTo(2),
                "Restore queues each released handle exactly once.");
            Assert.That(provider.TrackedAcknowledgements, Is.Zero);
            Assert.That(provider.PendingConfirmations, Is.Zero);
        }

        [Test]
        public async Task RestoreAcknowledgementsDoesNotQueueWhenAttachFailsAsync()
        {
            var observer = new SignalingObserver(expectedOutcomeCount: 1);
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(subscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var handle = new AcknowledgementHandle(scope, 3);
            ((ConfirmedAcknowledgementProvider)configuration.Provider!).Confirm(handle);
            bool detached = engine.ReleaseAcknowledgementsOf(
                subscription,
                () =>
                {
                    owners.Remove(subscription);
                    return true;
                },
                out List<AcknowledgementHandover> released);
            int attachCalls = 0;

            bool restored = engine.RestoreAcknowledgementsOf(
                subscription,
                released,
                () =>
                {
                    attachCalls++;
                    return false;
                });

            Assert.That(detached, Is.True);
            Assert.That(released, Has.Count.EqualTo(1));
            Assert.That(released[0].Handle, Is.EqualTo(handle));
            Assert.That(released[0].Confirmed, Is.True);
            Assert.That(restored, Is.False);
            Assert.That(attachCalls, Is.EqualTo(1));
            Assert.That(owners, Is.Empty);
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).TrackedAcknowledgements, Is.Zero);
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
            Assert.That(observer.Outcomes, Is.Empty);

            engine.RetireReleasedAcknowledgements(released);
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1));
            Assert.That(observer.Outcomes[0].Handle, Is.EqualTo(handle));
            Assert.That(observer.Outcomes[0].Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
            Assert.That(observer.Outcomes[0].Status, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task RestoreAcknowledgementsHandlesEmptyReleasedListAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            int attachCalls = 0;

            bool restored = engine.RestoreAcknowledgementsOf(
                subscription,
                [],
                () =>
                {
                    attachCalls++;
                    owners.Add(subscription);
                    return true;
                });

            Assert.That(restored, Is.True);
            Assert.That(attachCalls, Is.EqualTo(1));
            Assert.That(owners, Is.EqualTo(new[] { subscription }));
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).TrackedAcknowledgements, Is.Zero);
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).PendingConfirmations, Is.Zero);
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
            Assert.That(observer.Outcomes, Is.Empty);
        }

        [Test]
        public async Task RestoreAcknowledgementsQueuesLegacyHandlesWithoutTrackingAsync()
        {
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            m_mockContext.Setup(c => c.Connected).Returns(true);
            m_mockContext.Setup(c => c.PrepareAcknowledgementsToSend(
                    It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns<List<SubscriptionAcknowledgement>>(acknowledgements => ([.. acknowledgements], []));
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => null));
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var confirmedHandle = new AcknowledgementHandle(scope, 3);
            var unconfirmedHandle = new AcknowledgementHandle(scope, 4);
            List<AcknowledgementHandover> released =
            [
                new(confirmedHandle, true),
                new(unconfirmedHandle, false)
            ];
            int attachCalls = 0;

            bool restored = engine.RestoreAcknowledgementsOf(
                subscription,
                released,
                () =>
                {
                    attachCalls++;
                    owners.Add(subscription);
                    return true;
                });

            Assert.That(restored, Is.True);
            Assert.That(attachCalls, Is.EqualTo(1));
            Assert.That(owners, Is.EqualTo(new[] { subscription }));
            System.Collections.Concurrent.ConcurrentQueue<uint> sent = CapturePublishedAcknowledgements();
            Assert.That(engine.BeginPublish(timeout: 5000), Is.True);
            Assert.That(sent.ToArray(), Is.EqualTo(new uint[] { 3, 4 }),
                "Legacy restore publishes the original sequence numbers, including the unconfirmed handle.");
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero,
                "Both restored legacy acknowledgements have left the pending queue for Publish.");
        }

        [Test]
        public async Task RetireReleasedAcknowledgementsReportsOriginalHandlesAsync()
        {
            var observer = new SignalingObserver(expectedOutcomeCount: 2);
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(subscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            engine.AddPendingAcknowledgement(7, 3);
            engine.AddPendingAcknowledgement(7, 4);
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var confirmedHandle = new AcknowledgementHandle(scope, 3);
            var unconfirmedHandle = new AcknowledgementHandle(scope, 4);
            ((ConfirmedAcknowledgementProvider)configuration.Provider!).Confirm(confirmedHandle);
            bool detached = engine.ReleaseAcknowledgementsOf(
                subscription,
                () =>
                {
                    owners.Remove(subscription);
                    return true;
                },
                out List<AcknowledgementHandover> released);

            engine.RetireReleasedAcknowledgements(released);
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(detached, Is.True);
            Assert.That(released.Select(item => item.Handle), Is.EquivalentTo(new[] { confirmedHandle, unconfirmedHandle }));
            Assert.That(observer.Outcomes.Select(outcome => outcome.Handle),
                Is.EquivalentTo(new[] { confirmedHandle, unconfirmedHandle }));
            Assert.That(observer.Outcomes.All(outcome =>
                    outcome.Disposition == AcknowledgementDisposition.OwnerRetired &&
                    outcome.Status == StatusCodes.BadSubscriptionIdInvalid),
                Is.True);
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).TrackedAcknowledgements, Is.Zero);
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);

            engine.Dispose();
            Assert.That(observer.Outcomes, Has.Count.EqualTo(2),
                "Disposal after the handover list was retired adds no terminal outcomes.");
        }

        [Test]
        public async Task LateRepublishDoesNotReopenAnEndedAcknowledgementAsync()
        {
            m_mockContext.Setup(c => c.ServerState).Returns(ServerState.Running);
            var observer = new SignalingObserver(expectedOutcomeCount: 1);
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            List<Subscription> owners = [];
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            owners.Add(subscription);
            m_mockContext.Setup(c => c.Subscriptions).Returns(() => owners);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));
            AcknowledgementScope scope = subscription.GetAcknowledgementScope();
            var handle = new AcknowledgementHandle(scope, 3);

            engine.AdoptAcknowledgements(
                subscription,
                [new AcknowledgementHandover(handle, Confirmed: true)],
                [3u],
                []);
            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTimeUtc.Now },
                7,
                [],
                false,
                new NotificationMessage());
            await observer.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            engine.ProcessPublishResponse(
                new ResponseHeader { Timestamp = DateTimeUtc.Now },
                7,
                [],
                false,
                new NotificationMessage
                {
                    SequenceNumber = 3,
                    PublishTime = DateTimeUtc.Now,
                    NotificationData = [new ExtensionObject(new DataChangeNotification())]
                },
                republished: true);

            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero,
                "A successful late replay cannot recreate the terminally ended acknowledgement.");
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).TrackedAcknowledgements, Is.Zero);
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).PendingConfirmations, Is.Zero);
            Assert.That(observer.Outcomes, Has.Count.EqualTo(1));
            Assert.That(observer.Outcomes[0].Handle, Is.EqualTo(handle));
            Assert.That(observer.Outcomes[0].Disposition, Is.EqualTo(AcknowledgementDisposition.NotRetained));
            Assert.That(observer.Outcomes[0].Status, Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
        }

        [Test]
        public async Task RetireReleasedAcknowledgementsDoesNothingForEmptyListAsync()
        {
            var observer = new CollectingObserver();
            var configuration = new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, observer);
            using Subscription subscription = await CreateSubscriptionAsync(7).ConfigureAwait(false);
            m_mockContext.Setup(c => c.Subscriptions).Returns([subscription]);
            using var engine = new ClassicSubscriptionEngine(
                new AcknowledgingContext(m_mockContext.Object, () => configuration));

            engine.RetireReleasedAcknowledgements([]);

            Assert.That(observer.Outcomes, Is.Empty);
            Assert.That(((ConfirmedAcknowledgementProvider)configuration.Provider!).TrackedAcknowledgements, Is.Zero);
            Assert.That(engine.RemoveAcknowledgementsForSubscription(7), Is.Zero);
        }

        /// <summary>
        /// Signals after the expected asynchronous observer outcomes arrive.
        /// </summary>
        private sealed class SignalingObserver(int expectedOutcomeCount) : ISubscriptionAcknowledgementObserver
        {
            public IReadOnlyList<AcknowledgementOutcome> Outcomes => [.. m_outcomes];

            public Task Completion => m_completion.Task;

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                foreach (AcknowledgementOutcome outcome in outcomes)
                {
                    m_outcomes.Enqueue(outcome);
                    if (Interlocked.Increment(ref m_outcomeCount) >= expectedOutcomeCount)
                    {
                        m_completion.TrySetResult(true);
                    }
                }
                return default;
            }

            private readonly System.Collections.Concurrent.ConcurrentQueue<AcknowledgementOutcome> m_outcomes = new();
            private readonly TaskCompletionSource<bool> m_completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_outcomeCount;
        }

        private static readonly int[] s_deferredFirstIndex = [0];
        private static readonly int[] s_deferredSecondIndex = [1];
        private static readonly uint[] s_sequenceThree = [3u];
    }
}
