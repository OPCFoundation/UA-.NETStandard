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
