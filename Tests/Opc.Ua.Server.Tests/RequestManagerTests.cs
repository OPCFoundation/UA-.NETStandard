using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public class RequestManagerTests
    {
        [Test]
        public void CancelRequestsShouldCancelActivateSessionRequestWithoutSession()
        {
            const uint requestHandle = 1234;
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());

            using var requestManager = new RequestManager(serverMock.Object);
            var context = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.ActivateSession);

            requestManager.RequestReceived(context);

            uint cancelCount = 0;
            Assert.DoesNotThrow(
                () => requestManager.CancelRequests(context.SessionId, requestHandle, out cancelCount));

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(
                context.OperationStatus.Code,
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest));
        }

        [Test]
        public void CancelRequestsDoesNotCancelMatchingHandleFromDifferentSession()
        {
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());

            using var requestManager = new RequestManager(serverMock.Object);

            var cancellingSession = new Mock<ISession>();
            cancellingSession.Setup(s => s.Id).Returns(new NodeId(1));

            var otherSession = new Mock<ISession>();
            otherSession.Setup(s => s.Id).Returns(new NodeId(2));

            const uint requestHandle = 42;

            var ownContext = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Read,
                cancellingSession.Object);
            var otherContext = new OperationContext(
                new RequestHeader { RequestHandle = requestHandle },
                null,
                RequestType.Read,
                otherSession.Object);

            requestManager.RequestReceived(ownContext);
            requestManager.RequestReceived(otherContext);

            requestManager.CancelRequests(cancellingSession.Object.Id, requestHandle, out uint cancelCount);

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(
                ownContext.OperationStatus.Code,
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest));
            Assert.That(
                otherContext.OperationStatus.Code,
                Is.EqualTo(StatusCodes.Good));
        }
    }
}
