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
            m_requestManager.RequestCancelled += (sender, reqId, status) =>
            {
                eventFired = true;
                cancelledRequestId = reqId;
            };

            // Act
            m_requestManager.CancelRequests(context.SessionId, 42, out uint cancelCount);

            // Assert
            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(eventFired, Is.True);
            Assert.That(cancelledRequestId, Is.EqualTo(context.RequestId));
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.True);
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
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest));
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
            // Wait for timer to expire since TimeoutHint = 100ms. Note the original timer runs every 1000ms.
            // We need to wait a bit more than 1000ms.
            await Task.Delay(1200).ConfigureAwait(false);

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
        public void IsExecutingRequestIsFalseInitiallyAndTrueWithinRequestScope()
        {
            Assert.That(m_requestManager.IsExecutingRequest, Is.False);

            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(60, requestLifetime);

            using (m_requestManager.EnterRequestScope(context))
            {
                Assert.That(m_requestManager.IsExecutingRequest, Is.True);
            }

            Assert.That(m_requestManager.IsExecutingRequest, Is.False);
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
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
                    Assert.That(m_requestManager.IsExecutingRequest, Is.True);
                }

                // Disposing the inner scope must complete only innerContext.
                Assert.That(innerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
                Assert.That(m_requestManager.IsExecutingRequest, Is.True);
            }

            Assert.That(m_requestManager.IsExecutingRequest, Is.False);
            Assert.That(outerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public void PromoteValidatedRequestThrowsArgumentNullExceptionWhenContextNull()
        {
            Assert.That(() => m_requestManager.PromoteValidatedRequest(null), Throws.ArgumentNullException);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task EnterValidationScopeCompletesRegisteredRequestsOnDisposeAsync()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(70, requestLifetime);
            Task waiter;

            using (m_requestManager.EnterValidationScope())
            {
                Assert.That(m_requestManager.IsExecutingRequest, Is.True);
                m_requestManager.RequestReceived(context);

                waiter = m_requestManager.WaitForCurrentRequestsAsync().AsTask();
                Assert.That(waiter.IsCompleted, Is.False);
            }

            // Disposing the validation scope completes every request it registered
            // and unblocks waiters that were tracking the active validation scope.
            await AssertCompletesWithinTimeoutAsync(waiter).ConfigureAwait(false);
            Assert.That(requestLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        [Test]
        public void PromoteValidatedRequestExcludesContextFromValidationScopeAutoCompletion()
        {
            using var requestLifetime = new RequestLifetime();
            OperationContext context = CreateOperationContext(71, requestLifetime);

            using (m_requestManager.EnterValidationScope())
            {
                m_requestManager.RequestReceived(context);
                m_requestManager.PromoteValidatedRequest(context);
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
        public void NestedValidationScopesOnlyCompleteRequestsRegisteredWithinThem()
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

                // The inner scope disposed and completed only innerContext.
                Assert.That(innerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
            }

            // The outer scope disposal then completes outerContext too.
            Assert.That(outerLifetime.TryCancel(StatusCodes.BadTimeout), Is.False);
        }

        private static OperationContext CreateOperationContext(
            uint requestHandle,
            RequestLifetime requestLifetime)
        {
            return new OperationContext(
                new RequestHeader
                {
                    RequestHandle = requestHandle,
                    TimeoutHint = 0
                },
                null,
                RequestType.Read,
                requestLifetime);
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
