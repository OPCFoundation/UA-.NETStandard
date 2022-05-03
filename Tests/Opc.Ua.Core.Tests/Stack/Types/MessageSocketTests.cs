using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("MessageSocketTests")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class MessageSocketTests
    {

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {

        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion

        #region Test Methods

        [Test]
        public void IMessageSocket_IPEndpoint_Returned()
        {
            var messageSocketMock = new Mock<IMessageSocket>();
            var endPoint = new IPEndPoint(IPAddress.Parse("192.168.0.1"), 55062);
            messageSocketMock.Setup(x => x.LocalEndpoint).Returns(endPoint);

            var messageSocket = messageSocketMock.Object;
            var gotEndpoint = messageSocket.LocalEndpoint;

            Assert.IsTrue(gotEndpoint.Equals(endPoint));
        }

        #endregion
    }
}
