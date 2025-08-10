using System.Net;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("MessageSocketTests")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class MessageSocketTests
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp() { }

        [OneTimeTearDown]
        protected void OneTimeTearDown() { }

        [SetUp]
        protected void SetUp() { }

        [TearDown]
        protected void TearDown() { }

        [Test]
        public void IMessageSocketIPEndpointReturned()
        {
            var messageSocketMock = new Mock<IMessageSocket>();
            var endPoint = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 55062);
            messageSocketMock.Setup(x => x.LocalEndpoint).Returns(endPoint);

            IMessageSocket messageSocket = messageSocketMock.Object;
            EndPoint gotEndpoint = messageSocket.LocalEndpoint;

            Assert.IsTrue(gotEndpoint.Equals(endPoint));
        }
    }
}
