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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

// Test code exercises RequestManager.RequestCompleted, which is obsolete for callers
// because requests are completed by disposing the OperationContext.
#pragma warning disable CS0618

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    public class RequestManagerTests
    {
        private Mock<IServerInternal> m_mockServer;
        private RequestManager m_requestManager;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockServer.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_requestManager = new RequestManager(m_mockServer.Object);
        }

        [TearDown]
        public void TearDown()
        {
            m_requestManager?.Dispose();
        }

        [Test]
        public void ConstructorThrowsArgumentNullExceptionWhenServerNull()
        {
            Assert.That(() => new RequestManager(null), Throws.ArgumentNullException);
        }

        [Test]
        public void RequestReceivedThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(() => m_requestManager.RequestReceived(null), Throws.ArgumentNullException);
        }

        [Test]
        public void RequestCompletedThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(() => m_requestManager.RequestCompleted(null), Throws.ArgumentNullException);
        }

        [Test]
        public void CancelRequestsCancelsMatchingRequestsAndFiresEvent()
        {
            // Arrange
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));

            var requestHeader = new RequestHeader { RequestHandle = 42, TimeoutHint = 0 };
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                requestHeader,
                null,
                RequestType.Read,
                requestLifetime,
                mockSession.Object);

            m_requestManager.RequestReceived(context);

            bool eventFired = false;
            uint cancelledRequestId = 0;
            StatusCode cancelledStatus = StatusCodes.Good;
            m_requestManager.RequestCancelled += (sender, reqId, status) =>
            {
                eventFired = true;
                cancelledRequestId = reqId;
                cancelledStatus = status;
            };

            // Act
            m_requestManager.CancelRequests(context.SessionId, 42, out uint cancelCount);

            // Assert
            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(eventFired, Is.True);
            Assert.That(cancelledRequestId, Is.EqualTo(context.RequestId));
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.True);
            // OPC 10000-4 5.7.5.1: cancelled requests respond with Bad_RequestCancelledByClient.
            Assert.That(cancelledStatus, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(context.OperationStatus.Code, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
        }

        [Test]
        public void CancelRequestsWithContextReportsOneAuditEventPerCancelCall()
        {
            // OPC 10000-5 6.4.11: one AuditCancelEvent per Cancel call, whether it matched
            // no request or several, carrying the Cancel request's ClientAuditEntryId.
            var auditEvents = new System.Collections.Generic.List<AuditEventState>();
            m_mockServer.Setup(s => s.Auditing).Returns(true);
            m_mockServer.Setup(s => s.DefaultAuditContext).Returns(CreateAuditContext());
            m_mockServer
                .Setup(s => s.ReportAuditEvent(It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()))
                .Callback<ISystemContext, AuditEventState>((_, e) => auditEvents.Add(e));

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));
            using var lifetime1 = new RequestLifetime();
            using var lifetime2 = new RequestLifetime();
            m_requestManager.RequestReceived(new OperationContext(
                new RequestHeader { RequestHandle = 42 }, null, RequestType.Read, lifetime1, mockSession.Object));
            m_requestManager.RequestReceived(new OperationContext(
                new RequestHeader { RequestHandle = 42 }, null, RequestType.Browse, lifetime2, mockSession.Object));
            using var cancelLifetime = new RequestLifetime();
            var cancelContext = new OperationContext(
                new RequestHeader { RequestHandle = 43, AuditEntryId = "op-42" },
                null,
                RequestType.Cancel,
                cancelLifetime,
                mockSession.Object);

            m_requestManager.CancelRequests(cancelContext, 42, out uint cancelCount);

            Assert.That(cancelCount, Is.EqualTo(2));
            Assert.That(auditEvents, Has.Count.EqualTo(1));
            var cancelEvent = (AuditCancelEventState)auditEvents[0];
            Assert.That(cancelEvent.ClientAuditEntryId.Value, Is.EqualTo("op-42"));
            Assert.That(cancelEvent.RequestHandle.Value, Is.EqualTo(42u));

            m_requestManager.CancelRequests(cancelContext, 99, out cancelCount);

            Assert.That(cancelCount, Is.Zero);
            Assert.That(auditEvents, Has.Count.EqualTo(2), "A Cancel matching nothing is still audited.");
        }

        [Test]
        public void CancelSessionRequestsAbortsOutstandingRequestsExceptTheExcludedOne()
        {
            // OPC 10000-4 5.7.2.1: closing a Session aborts its outstanding requests with
            // Bad_SessionClosed; the CloseSession request itself completes normally.
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            var otherSession = new Mock<ISession>();
            otherSession.Setup(s => s.Id).Returns(new NodeId(2));
            using var callLifetime = new RequestLifetime();
            using var closeLifetime = new RequestLifetime();
            using var otherLifetime = new RequestLifetime();
            var call = new OperationContext(
                new RequestHeader { RequestHandle = 1 }, null, RequestType.Call, callLifetime, session.Object);
            var close = new OperationContext(
                new RequestHeader { RequestHandle = 2 }, null, RequestType.CloseSession, closeLifetime, session.Object);
            var other = new OperationContext(
                new RequestHeader { RequestHandle = 1 }, null, RequestType.Read, otherLifetime, otherSession.Object);
            m_requestManager.RequestReceived(call);
            m_requestManager.RequestReceived(close);
            m_requestManager.RequestReceived(other);

            uint aborted = m_requestManager.CancelSessionRequests(
                session.Object.Id,
                close.RequestId,
                StatusCodes.BadSessionClosed);

            Assert.That(aborted, Is.EqualTo(1));
            Assert.That(call.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(callLifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(closeLifetime.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(otherLifetime.CancellationToken.IsCancellationRequested, Is.False);
        }

        [Test]
        public void CancelSessionRequestsCancelsEveryRequestWhenACancellationCallbackFails()
        {
            // A cancellation callback runs inline on the closing thread. One that throws, or
            // that registers another request while the requests are enumerated, must neither
            // escape to the Session close nor keep the other requests from being aborted.
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            using var failingLifetime = new RequestLifetime();
            using var reentrantLifetime = new RequestLifetime();
            using var otherLifetime = new RequestLifetime();
            using var lateLifetime = new RequestLifetime();
            var failing = new OperationContext(
                new RequestHeader { RequestHandle = 1 }, null, RequestType.Call, failingLifetime, session.Object);
            var reentrant = new OperationContext(
                new RequestHeader { RequestHandle = 2 }, null, RequestType.Read, reentrantLifetime, session.Object);
            var other = new OperationContext(
                new RequestHeader { RequestHandle = 3 }, null, RequestType.Read, otherLifetime, session.Object);
            var late = new OperationContext(
                new RequestHeader { RequestHandle = 4 }, null, RequestType.Read, lateLifetime);
            failingLifetime.CancellationToken.Register(() => throw new InvalidOperationException("callback"));
            reentrantLifetime.CancellationToken.Register(() => m_requestManager.RequestReceived(late));
            m_requestManager.RequestReceived(failing);
            m_requestManager.RequestReceived(reentrant);
            m_requestManager.RequestReceived(other);

            uint aborted = 0;
            Assert.DoesNotThrow(() => aborted = m_requestManager.CancelSessionRequests(
                session.Object.Id,
                0,
                StatusCodes.BadSessionClosed));

            Assert.That(aborted, Is.EqualTo(3));
            Assert.That(failing.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(reentrant.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(other.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            m_requestManager.RequestCompleted(late);
        }

        [Test]
        public void CancelRequestsCancelsEveryMatchingRequestWhenACancellationCallbackFails()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            using var failingLifetime = new RequestLifetime();
            using var otherLifetime = new RequestLifetime();
            using var lateLifetime = new RequestLifetime();
            var failing = new OperationContext(
                new RequestHeader { RequestHandle = 7 }, null, RequestType.Call, failingLifetime, session.Object);
            var other = new OperationContext(
                new RequestHeader { RequestHandle = 7 }, null, RequestType.Read, otherLifetime, session.Object);
            var late = new OperationContext(
                new RequestHeader { RequestHandle = 8 }, null, RequestType.Read, lateLifetime);
            failingLifetime.CancellationToken.Register(() =>
            {
                m_requestManager.RequestReceived(late);
                throw new InvalidOperationException("callback");
            });
            m_requestManager.RequestReceived(failing);
            m_requestManager.RequestReceived(other);

            uint cancelCount = 0;
            Assert.DoesNotThrow(() => m_requestManager.CancelRequests(session.Object.Id, 7, out cancelCount));

            Assert.That(cancelCount, Is.EqualTo(2));
            Assert.That(otherLifetime.CancellationToken.IsCancellationRequested, Is.True);
            m_requestManager.RequestCompleted(late);
        }

        [Test]
        public void CancelAppliesToMatchingRequestsThatWereStillQueued()
        {
            // OPC 10000-4 5.7.5.2: every outstanding request with the handle is cancelled,
            // including one that was still queued (not registered) when Cancel ran.
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            var otherSession = new Mock<ISession>();
            otherSession.Setup(s => s.Id).Returns(new NodeId(2));
            var sentAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            using var cancelLifetime = new RequestLifetime();
            var cancel = new OperationContext(
                new RequestHeader { RequestHandle = 10, Timestamp = sentAt.AddSeconds(1) },
                null,
                RequestType.Cancel,
                cancelLifetime,
                session.Object);

            m_requestManager.CancelRequests(cancel, 9, out uint cancelCount);
            Assert.That(cancelCount, Is.Zero);

            OperationContext Queued(ISession owner, uint handle, DateTime timestamp)
            {
                return new OperationContext(
                    new RequestHeader { RequestHandle = handle, Timestamp = timestamp },
                    null,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    owner);
            }

            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(Queued(session.Object, 9, sentAt)),
                Is.True,
                "A queued request sent before the Cancel is cancelled.");
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(Queued(session.Object, 9, sentAt.AddSeconds(2))),
                Is.False,
                "A request sent after the Cancel is not affected.");
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(Queued(session.Object, 8, sentAt)),
                Is.False);
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(Queued(otherSession.Object, 9, sentAt)),
                Is.False);
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(Queued(session.Object, 9, DateTime.MinValue)),
                Is.False,
                "A request without a timestamp cannot be proven to precede the Cancel.");
        }

        [Test]
        public void CancelWithoutTimestampDoesNotCancelLaterRequestsReusingTheHandle()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            using var cancelLifetime = new RequestLifetime();
            var cancel = new OperationContext(
                new RequestHeader { RequestHandle = 10 },
                null,
                RequestType.Cancel,
                cancelLifetime,
                session.Object);

            m_requestManager.CancelRequests(cancel, 0, out _);

            var later = new OperationContext(
                new RequestHeader { RequestHandle = 0 },
                null,
                RequestType.Read,
                RequestLifetime.None,
                session.Object);
            Assert.That(m_requestManager.IsCancelledBeforeAdmission(later), Is.False);
        }

        [Test]
        public void CancelDoesNotApplyToAQueuedRequestWithTheSameTimestamp()
        {
            // Only a request strictly older than the Cancel was provably sent before it; with
            // a coarse client clock an equal timestamp may belong to a later request.
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            DateTime sentAt = DateTime.UtcNow;
            CancelWithTimestamp(session.Object, 9, sentAt);

            Assert.That(m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(session.Object, 9, sentAt)), Is.False);
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(session.Object, 9, sentAt.AddTicks(-1))),
                Is.True);
        }

        [Test]
        public void CancelFloodOfOneSessionDoesNotEvictThePendingCancelsOfAnother()
        {
            var flooding = new Mock<ISession>();
            flooding.Setup(s => s.Id).Returns(new NodeId(1));
            var victim = new Mock<ISession>();
            victim.Setup(s => s.Id).Returns(new NodeId(2));
            DateTime sentAt = DateTime.UtcNow;
            CancelWithTimestamp(victim.Object, 9, sentAt.AddSeconds(1));

            for (uint handle = 1000; handle < 2100; handle++)
            {
                CancelWithTimestamp(flooding.Object, handle, sentAt.AddSeconds(1));
            }

            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(victim.Object, 9, sentAt)),
                Is.True,
                "Another session's Cancel flood must not evict this session's pending Cancel.");
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(flooding.Object, 2099, sentAt)),
                Is.True,
                "The flooding session keeps its newest pending Cancels.");
            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(flooding.Object, 1000, sentAt)),
                Is.False,
                "The flooding session evicts its own oldest pending Cancels.");
        }

        [Test]
        public void CancelsWithoutTimestampAreNotRememberedAndEvictNothing()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            DateTime sentAt = DateTime.UtcNow;
            CancelWithTimestamp(session.Object, 9, sentAt.AddSeconds(1));

            for (uint handle = 1000; handle < 2100; handle++)
            {
                CancelWithTimestamp(session.Object, handle, DateTime.MinValue);
                m_requestManager.CancelRequests(session.Object.Id, handle, out _);
            }

            Assert.That(
                m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(session.Object, 9, sentAt)),
                Is.True,
                "Cancels that can never match a queued request must not evict one that can.");
        }

        [Test]
        public void ClosingASessionForgetsItsPendingCancels()
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(new NodeId(1));
            DateTime sentAt = DateTime.UtcNow;
            CancelWithTimestamp(session.Object, 9, sentAt.AddSeconds(1));
            Assert.That(m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(session.Object, 9, sentAt)), Is.True);

            m_requestManager.CancelSessionRequests(session.Object.Id, 0, StatusCodes.BadSessionClosed);

            Assert.That(m_requestManager.IsCancelledBeforeAdmission(QueuedRequest(session.Object, 9, sentAt)), Is.False);
        }

        private void CancelWithTimestamp(ISession session, uint requestHandle, DateTime timestamp)
        {
            var cancel = new OperationContext(
                new RequestHeader { RequestHandle = 1, Timestamp = timestamp },
                null,
                RequestType.Cancel,
                RequestLifetime.None,
                session);
            m_requestManager.CancelRequests(cancel, requestHandle, out _);
        }

        private static OperationContext QueuedRequest(ISession session, uint requestHandle, DateTime timestamp)
        {
            return new OperationContext(
                new RequestHeader { RequestHandle = requestHandle, Timestamp = timestamp },
                null,
                RequestType.Read,
                RequestLifetime.None,
                session);
        }

        private static ServerSystemContext CreateAuditContext()
        {
            NamespaceTable namespaceUris = new();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            return new ServerSystemContext(server.Object);
        }

        [Test]
        public void CancelRequestsShouldCancelActivateSessionRequestWithoutSession()
        {
            const uint requestHandle = 1234;
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.ActivateSession,
                requestLifetime);

            m_requestManager.RequestReceived(context);

            uint cancelCount = 0;
            Assert.DoesNotThrow(
                () => m_requestManager.CancelRequests(context.SessionId, requestHandle, out cancelCount));

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(
                context.OperationStatus.Code,
                Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
        }

        [Test]
        public void CancelRequestsDoesNotCancelMatchingHandleFromDifferentSession()
        {
            var cancellingSession = new Mock<ISession>();
            cancellingSession.Setup(s => s.Id).Returns(new NodeId(1));

            var otherSession = new Mock<ISession>();
            otherSession.Setup(s => s.Id).Returns(new NodeId(2));

            const uint requestHandle = 42;
            using var ownRequestLifetime = new RequestLifetime();
            using var otherRequestLifetime = new RequestLifetime();

            var ownContext = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Read,
                ownRequestLifetime,
                cancellingSession.Object);
            var otherContext = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Read,
                otherRequestLifetime,
                otherSession.Object);

            m_requestManager.RequestReceived(ownContext);
            m_requestManager.RequestReceived(otherContext);

            m_requestManager.CancelRequests(cancellingSession.Object.Id, requestHandle, out uint cancelCount);

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(ownRequestLifetime.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(otherRequestLifetime.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(
                otherContext.OperationStatus.Code,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void RequestCompletedRemovesRequestAndCompletesLifetime()
        {
            // Arrange
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));

            var requestHeader = new RequestHeader { RequestHandle = 42, TimeoutHint = 0 };
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                requestHeader,
                null,
                RequestType.Read,
                requestLifetime,
                mockSession.Object);

            m_requestManager.RequestReceived(context);

            // Act
            m_requestManager.RequestCompleted(context);

            // Assert
            // To ensure it is removed, cancelling it will yield 0 count
            m_requestManager.CancelRequests(context.SessionId, 42, out uint cancelCount);
            Assert.That(cancelCount, Is.Zero);
            // Assert that lifetime is completed (disposed), which means TryCancel returns false
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public async Task TimerCancelsExpiredRequestsAndFiresEventAsync()
        {
            // Arrange
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));

            // TimeoutHint is small to ensure it expires quickly
            var requestHeader = new RequestHeader { RequestHandle = 43, TimeoutHint = 100 };
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                requestHeader,
                null,
                RequestType.Read,
                requestLifetime,
                mockSession.Object);

            bool eventFired = false;
            m_requestManager.RequestCancelled += (sender, reqId, status) =>
            {
                if (reqId == context.RequestId && status == StatusCodes.BadTimeout)
                {
                    eventFired = true;
                }
            };

            m_requestManager.RequestReceived(context);

            // Act
            // Wait for timer to expire since TimeoutHint = 100ms. The timer runs every 1000ms;
            // poll instead of a fixed delay so a loaded runner cannot miss the first tick.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!eventFired && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            // Assert
            Assert.That(eventFired, Is.True);
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void DisposeCancelsPendingRequests()
        {
            // Arrange
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));

            var requestHeader = new RequestHeader { RequestHandle = 44, TimeoutHint = 0 };
            using var requestLifetime = new RequestLifetime();
            var context = new OperationContext(
                requestHeader,
                null,
                RequestType.Read,
                requestLifetime,
                mockSession.Object);

            m_requestManager.RequestReceived(context);

            // Act
            m_requestManager.Dispose();

            // Assert
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void RegisterLifecycleExtensionTwiceReturnsSameExtension()
        {
            RequestManagerLifecycleExtension first = RegisterLifecycleExtension();
            RequestManagerLifecycleExtension second = RegisterLifecycleExtension();

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void RegisterLifecycleExtensionAfterDisposeThrowsObjectDisposedException()
        {
            m_requestManager.Dispose();

            Assert.That(
                () => m_requestManager.RegisterLifecycleExtension(),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void RequestManagerWithoutLifecycleExtensionAdmitsValidationAndRequests()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(15, requestLifetime);

            using (m_requestManager.EnterValidationScope())
            {
                Assert.DoesNotThrow(() => m_requestManager.RequestReceived(context));
            }

            m_requestManager.RequestCompleted(context);
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task RequestManagerWithoutLifecycleExtensionDoesNotRepeatDrainAsync()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(16, requestLifetime);
            IDisposable validationScope = m_requestManager.EnterValidationScope();

            Task drain = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);

            m_requestManager.RequestReceived(context);
            validationScope.Dispose();

            await AssertCompletesWithinTimeoutAsync(drain).ConfigureAwait(false);
            Assert.That(
                requestLifetime.TryCancel(StatusCodes.BadTimeout),
                Is.True,
                "Without the lifecycle extension, the drain must not resnapshot promoted requests.");
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncCompletesAfterAllSnapshotRequestsCompleteAsync()
        {
            using var requestLifetimeA = new RequestLifetime();
            using var requestLifetimeB = new RequestLifetime();
            OperationContext contextA = CreateOperationContext(1, requestLifetimeA);
            OperationContext contextB = CreateOperationContext(2, requestLifetimeB);

            m_requestManager.RequestReceived(contextA);
            m_requestManager.RequestReceived(contextB);

            Task waiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();

            Assert.That(waiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(contextA);

            Assert.That(waiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(contextB);

            await AssertCompletesWithinTimeoutAsync(waiter).ConfigureAwait(false);
            Assert.That(waiter.IsCompleted, Is.True);
            Assert.That(waiter.IsCanceled, Is.False);
            Assert.That(waiter.IsFaulted, Is.False);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncDoesNotWaitForParkedPublishAsync()
        {
            using var readLifetime = new RequestLifetime();
            using var publishLifetime = new RequestLifetime();
            OperationContext read = CreateOperationContext(1, readLifetime);
            var publish = new OperationContext(
                new RequestHeader { RequestHandle = 2 },
                null,
                RequestType.Publish,
                publishLifetime);

            m_requestManager.RequestReceived(read);
            m_requestManager.RequestReceived(publish);

            Task waiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(waiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(read);

            await AssertCompletesWithinTimeoutAsync(waiter).ConfigureAwait(false);
            Assert.That(publishLifetime.CancellationToken.IsCancellationRequested, Is.False);
            m_requestManager.RequestCompleted(publish);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncExcludesRequestsReceivedAfterSnapshotAsync()
        {
            using var requestLifetimeA = new RequestLifetime();
            using var requestLifetimeB = new RequestLifetime();
            OperationContext contextA = CreateOperationContext(1, requestLifetimeA);
            OperationContext contextB = CreateOperationContext(2, requestLifetimeB);

            m_requestManager.RequestReceived(contextA);
            Task snapshotWaiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            m_requestManager.RequestReceived(contextB);

            m_requestManager.RequestCompleted(contextA);

            await AssertCompletesWithinTimeoutAsync(snapshotWaiter).ConfigureAwait(false);
            Assert.That(requestLifetimeB.CancellationToken.IsCancellationRequested, Is.False);

            Task remainingRequestWaiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(remainingRequestWaiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(contextB);

            await AssertCompletesWithinTimeoutAsync(remainingRequestWaiter).ConfigureAwait(false);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncCancellationCancelsOnlyTheWaiterAsync()
        {
            using var requestLifetime = new RequestLifetime();
            using var cancellationTokenSource = new CancellationTokenSource();
            OperationContext context = CreateOperationContext(1, requestLifetime);
            m_requestManager.RequestReceived(context);

            Task canceledWaiter = m_requestManager
                .WaitForCurrentRequestsAsync(cancellationTokenSource.Token)
                .AsTask();

            cancellationTokenSource.Cancel();

            Assert.That(
                async () => await canceledWaiter.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.False);

            Task remainingRequestWaiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(remainingRequestWaiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(context);

            await AssertCompletesWithinTimeoutAsync(remainingRequestWaiter).ConfigureAwait(false);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncWithNoRequestsCompletesImmediatelyAsync()
        {
            Task waiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();

            Assert.That(waiter.IsCompleted, Is.True);
            Assert.That(waiter.IsCanceled, Is.False);
            Assert.That(waiter.IsFaulted, Is.False);

            await waiter.ConfigureAwait(false);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncExcludesEveryLifecycleWaiterAsync()
        {
            RequestManagerLifecycleExtension extension = RegisterLifecycleExtension();
            using var requestLifetimeA = new RequestLifetime();
            using var requestLifetimeB = new RequestLifetime();
            using var requestLifetimeC = new RequestLifetime();
            OperationContext contextA = CreateOperationContext(10, requestLifetimeA);
            OperationContext contextB = CreateOperationContext(11, requestLifetimeB);
            OperationContext contextC = CreateOperationContext(12, requestLifetimeC);

            using IDisposable requestScopeA = m_requestManager.EnterRequestScope(contextA);
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope waiterScopeA =
                extension.EnterLifecycleWaiter();
            waiterScopeA.MarkSemaphoreWaitStarted();
            using IDisposable requestScopeB = m_requestManager.EnterRequestScope(contextB);
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope waiterScopeB =
                extension.EnterLifecycleWaiter();
            waiterScopeB.MarkSemaphoreWaitStarted();
            m_requestManager.RequestReceived(contextC);

            Task drain = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(contextC);

            await AssertCompletesWithinTimeoutAsync(drain).ConfigureAwait(false);
            Assert.That(
                requestLifetimeA.TryCancel(StatusCodes.BadTimeout),
                Is.True,
                "The first excluded request must still be executing.");
            Assert.That(
                requestLifetimeB.TryCancel(StatusCodes.BadTimeout),
                Is.True,
                "The second excluded request must still be executing.");
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task SemaphoreWaitStartReleasesAlreadyActiveDrainAsync()
        {
            RequestManagerLifecycleExtension extension = RegisterLifecycleExtension();
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(13, requestLifetime);
            using IDisposable requestScope = m_requestManager.EnterRequestScope(context);

            Task drain = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);

            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope waiterScope =
                extension.EnterLifecycleWaiter();

            Assert.That(
                drain.IsCompleted,
                Is.False,
                "Registration must not release a drain before the semaphore wait is queued.");

            waiterScope.MarkSemaphoreWaitStarted();

            await AssertCompletesWithinTimeoutAsync(drain).ConfigureAwait(false);
            Assert.That(
                requestLifetime.TryCancel(StatusCodes.BadTimeout),
                Is.True,
                "Waiting for the lifecycle semaphore must not complete the request itself.");
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task DisposedLifecycleWaiterIsIncludedInNextDrainAsync()
        {
            RequestManagerLifecycleExtension extension = RegisterLifecycleExtension();
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(14, requestLifetime);
            IDisposable requestScope = m_requestManager.EnterRequestScope(context);
            using (extension.EnterLifecycleWaiter())
            {
            }

            Task drain = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
            Assert.That(drain.IsCompleted, Is.False);

            requestScope.Dispose();

            await AssertCompletesWithinTimeoutAsync(drain).ConfigureAwait(false);
        }

        [Test]
        public void RequestReceivedCalledTwiceWithSameContextIsIdempotent()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(45, requestLifetime);

            m_requestManager.RequestReceived(context);

            Assert.DoesNotThrow(() => m_requestManager.RequestReceived(context));

            m_requestManager.RequestCompleted(context);
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public void RequestCompletedForUnknownContextDoesNotThrowAndLeavesLifetimeActive()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(50, requestLifetime);
            // The context was never passed to RequestReceived.

            Assert.DoesNotThrow(() => m_requestManager.RequestCompleted(context));

            // The lifetime was not marked completed, so it can still be cancelled.
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.True);
        }

        [Test]
        public void IsExecutingRequestIsFalseForANullContext()
        {
            Assert.That(m_requestManager.IsExecutingRequest(null), Is.False);
        }

        [Test]
        public void IsExecutingRequestIsFalseForAContextThatWasNeverRegistered()
        {
            // An internal operation creates a context of its own without enrolling it as a
            // request. Such a context is not executing, so a lifecycle operation may proceed.
            using var requestLifetime = new RequestLifetime();
            using OperationContext context = CreateOperationContext(80, requestLifetime);

            Assert.That(m_requestManager.IsExecutingRequest(context), Is.False);
        }

        [Test]
        public void IsExecutingRequestIsTrueOnlyWhileTheRequestScopeIsOpen()
        {
            using var requestLifetime = new RequestLifetime();
            using OperationContext context = CreateOperationContext(81, requestLifetime);

            Assert.That(m_requestManager.IsExecutingRequest(context), Is.False);

            using (m_requestManager.EnterRequestScope(context))
            {
                Assert.That(m_requestManager.IsExecutingRequest(context), Is.True);
            }

            Assert.That(m_requestManager.IsExecutingRequest(context), Is.False);
        }

        [Test]
        public async Task IsExecutingRequestIsVisibleToTheHandlerTheRequestIsDispatchedToAsync()
        {
            // The context is handed to the handler explicitly, so the guard works across await
            // boundaries and background tasks without relying on ambient state.
            using var requestLifetime = new RequestLifetime();
            using OperationContext context = CreateOperationContext(82, requestLifetime);
            bool observedInCallee = false;

            async Task DispatchAsync()
            {
                using (m_requestManager.EnterRequestScope(context))
                {
                    await HandleAsync(context).ConfigureAwait(false);
                }
            }

            async Task HandleAsync(OperationContext callerContext)
            {
                await Task.Yield();
                observedInCallee = await Task.Run(
                    () => m_requestManager.IsExecutingRequest(callerContext))
                    .ConfigureAwait(false);
            }

            await DispatchAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observedInCallee, Is.True);
                Assert.That(m_requestManager.IsExecutingRequest(context), Is.False);
            });
        }

        [Test]
        public void IsExecutingRequestDistinguishesConcurrentlyExecutingRequests()
        {
            // Identity decides, so a request that is executing never makes another context look
            // like the one the caller is serving.
            using var outerLifetime = new RequestLifetime();
            using var innerLifetime = new RequestLifetime();
            using OperationContext outerContext = CreateOperationContext(83, outerLifetime);
            using OperationContext innerContext = CreateOperationContext(84, innerLifetime);

            using (m_requestManager.EnterRequestScope(outerContext))
            {
                using (m_requestManager.EnterRequestScope(innerContext))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(m_requestManager.IsExecutingRequest(outerContext), Is.True);
                        Assert.That(m_requestManager.IsExecutingRequest(innerContext), Is.True);
                    });
                }

                Assert.Multiple(() =>
                {
                    Assert.That(m_requestManager.IsExecutingRequest(outerContext), Is.True);
                    Assert.That(m_requestManager.IsExecutingRequest(innerContext), Is.False);
                });
            }
        }

        [Test]
        public void EnterRequestScopeThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(() => m_requestManager.EnterRequestScope(null), Throws.ArgumentNullException);
        }

        [Test]
        public void EnterRequestScopeDisposeCompletesTheRequest()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(46, requestLifetime);

            IDisposable scope = m_requestManager.EnterRequestScope(context);
            scope.Dispose();

            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public void NestedRequestScopesOnlyCompleteTheirOwnRequestOnDispose()
        {
            using var outerLifetime = new RequestLifetime();
            using var innerLifetime = new RequestLifetime();
            OperationContext outerContext = CreateOperationContext(47, outerLifetime);
            OperationContext innerContext = CreateOperationContext(48, innerLifetime);

            using (m_requestManager.EnterRequestScope(outerContext))
            {
                using (m_requestManager.EnterRequestScope(innerContext))
                {
                }

                // Disposing the inner scope must complete only innerContext.
                Assert.That(innerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
            }

            Assert.That(outerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }


        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task DrainWaitsForBothTheValidationScopeAndTheRequestAsync()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(70, requestLifetime);
            Task waiter;

            using (m_requestManager.EnterValidationScope())
            {
                m_requestManager.RequestReceived(context);

                waiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
                Assert.That(waiter.IsCompleted, Is.False);
            }

            // The validation scope no longer owns the request, so closing it is not enough.
            Assert.That(waiter.IsCompleted, Is.False);

            m_requestManager.RequestCompleted(context);
            await AssertCompletesWithinTimeoutAsync(waiter).ConfigureAwait(false);
        }

        [Test]
        public void ValidationScopeDoesNotCompleteRequestsRegisteredWhileItIsOpen()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(71, requestLifetime);

            using (m_requestManager.EnterValidationScope())
            {
                m_requestManager.RequestReceived(context);
            }

            // The promoted request was handed off to ordinary request-scope ownership,
            // so disposing the validation scope must not have completed it.
            m_requestManager.CancelRequests(
                context.SessionId,
                71,
                out uint cancelCount);
            Assert.That(cancelCount, Is.EqualTo(1));

            // Clean up explicitly since the validation scope no longer owns it.
            m_requestManager.RequestCompleted(context);
        }

        [Test]
        public void NestedValidationScopesLeaveRegisteredRequestsToTheirOwnScope()
        {
            using var outerLifetime = new RequestLifetime();
            using var innerLifetime = new RequestLifetime();
            OperationContext outerContext = CreateOperationContext(80, outerLifetime);
            OperationContext innerContext = CreateOperationContext(81, innerLifetime);

            using (m_requestManager.EnterValidationScope())
            {
                m_requestManager.RequestReceived(outerContext);

                using (m_requestManager.EnterValidationScope())
                {
                    m_requestManager.RequestReceived(innerContext);
                }
            }

            m_requestManager.CancelRequests(outerContext.SessionId, 80, out uint outerCancelled);
            m_requestManager.CancelRequests(innerContext.SessionId, 81, out uint innerCancelled);
            Assert.Multiple(() =>
            {
                Assert.That(outerCancelled, Is.EqualTo(1));
                Assert.That(innerCancelled, Is.EqualTo(1));
            });

            m_requestManager.RequestCompleted(outerContext);
            m_requestManager.RequestCompleted(innerContext);
        }

        private static OperationContext CreateOperationContext(
            uint requestHandle,
            RequestLifetime requestLifetime)
        {
            return CreateOperationContext(requestHandle, requestLifetime, 0);
        }

        private RequestManagerLifecycleExtension RegisterLifecycleExtension()
        {
            return m_requestManager.RegisterLifecycleExtension();
        }

        private static OperationContext CreateOperationContext(
            uint requestHandle,
            RequestLifetime requestLifetime,
            uint timeoutHint)
        {
            return new OperationContext(
                new RequestHeader
                {
                    RequestHandle = requestHandle,
                    TimeoutHint = timeoutHint
                },
                null,
                RequestType.Read,
                requestLifetime);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void WaitForCurrentRequestsAsyncGivesUpWhenARequestNeverCompletes()
        {
            // A lifecycle operation holds its semaphore across the drain, so a request that is
            // never completed would otherwise wedge every later lifecycle operation.
            m_requestManager.RequestDrainTimeout = TimeSpan.FromMilliseconds(200);

            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(90, requestLifetime);
            m_requestManager.RequestReceived(context);

            Assert.That(
                async () => await m_requestManager.WaitForCurrentRequestsAsync().ConfigureAwait(false),
                Throws.TypeOf<TimeoutException>());

            m_requestManager.RequestCompleted(context);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task WaitForCurrentRequestsAsyncIgnoresRequestsAbandonedPastTheirDeadlineAsync()
        {
            m_requestManager.RequestDrainTimeout = TimeSpan.FromMilliseconds(10);

            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(91, requestLifetime, timeoutHint: 1);
            m_requestManager.RequestReceived(context);

            // Once a request is well past its deadline it is not going to complete, so waiting for
            // it would make every later lifecycle operation pay the full budget before failing.
            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);

            await AssertCompletesWithinTimeoutAsync(
                m_requestManager.WaitForCurrentRequestsAsync().AsTask()).ConfigureAwait(false);

            m_requestManager.RequestCompleted(context);
        }

        private static async Task AssertCompletesWithinTimeoutAsync(Task task)
        {
            Task completedTask = await Task.WhenAny(
                task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            Assert.That(completedTask, Is.SameAs(task));
            await task.ConfigureAwait(false);
        }
    }
}
