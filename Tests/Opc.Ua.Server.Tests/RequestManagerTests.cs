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
            m_requestManager.CancelRequests(42, out uint cancelCount);

            // Assert
            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(eventFired, Is.True);
            Assert.That(cancelledRequestId, Is.EqualTo(context.RequestId));
            Assert.That(requestLifetime.CancellationToken.IsCancellationRequested, Is.True);
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
            m_requestManager.CancelRequests(42, out uint cancelCount);
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
    }
}
