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
                () => requestManager.CancelRequests(requestHandle, out cancelCount));

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(
                context.OperationStatus.Code,
                Is.EqualTo(StatusCodes.BadRequestCancelledByRequest));
        }
    }
}
