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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ServerSystemContext")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerSystemContextTests
    {
        private Mock<IServerInternal> m_mockServer;
        private NamespaceTable m_namespaceUris;
        private StringTable m_serverUris;
        private TypeTable m_typeTable;
        private Mock<IEncodeableFactory> m_mockFactory;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_namespaceUris = new NamespaceTable();
            m_serverUris = new StringTable();
            m_typeTable = new TypeTable(m_namespaceUris);
            m_mockFactory = new Mock<IEncodeableFactory>();

            m_mockServer.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceUris);
            m_mockServer.Setup(s => s.ServerUris).Returns(m_serverUris);
            m_mockServer.Setup(s => s.TypeTree).Returns(m_typeTable);
            m_mockServer.Setup(s => s.Factory).Returns(m_mockFactory.Object);
        }

        [Test]
        public void ConstructorWithServerInitializesProperties()
        {
            var context = new ServerSystemContext(m_mockServer.Object);

            Assert.That(context.Server, Is.SameAs(m_mockServer.Object));
            Assert.That(context.NamespaceUris, Is.SameAs(m_namespaceUris));
            Assert.That(context.ServerUris, Is.SameAs(m_serverUris));
            Assert.That(context.TypeTable, Is.SameAs(m_typeTable));
            Assert.That(context.EncodeableFactory, Is.SameAs(m_mockFactory.Object));
            Assert.That(context.OperationContext, Is.Null);
        }

        [Test]
        public void ConstructorWithServerThrowsWhenServerNull()
        {
            Assert.That(() => new ServerSystemContext(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithOperationContextInitializesProperties()
        {
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var context = new ServerSystemContext(m_mockServer.Object, opContext);

            Assert.That(context.Server, Is.SameAs(m_mockServer.Object));
            Assert.That(context.OperationContext, Is.SameAs(opContext));
            Assert.That(context.NamespaceUris, Is.SameAs(m_namespaceUris));
        }

        [Test]
        public void ConstructorWithOperationContextThrowsWhenServerNull()
        {
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            Assert.That(() => new ServerSystemContext(null!, opContext), Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithSessionInitializesProperties()
        {
            var sessionId = new NodeId(42);
            var mockIdentity = new Mock<IUserIdentity>();
            var preferredLocales = new[] { "en-US", "de-DE" };

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(sessionId);
            mockSession.Setup(s => s.Identity).Returns(mockIdentity.Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(preferredLocales);

            var context = new ServerSystemContext(m_mockServer.Object, mockSession.Object);

            Assert.That(context.Server, Is.SameAs(m_mockServer.Object));
            Assert.That(context.SessionId, Is.EqualTo(sessionId));
            Assert.That(context.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(context.PreferredLocales.ToArray(), Is.EqualTo(preferredLocales));
            Assert.That(context.OperationContext, Is.Null);
        }

        [Test]
        public void ConstructorWithSessionThrowsWhenServerNull()
        {
            var mockSession = new Mock<ISession>();
            Assert.That(() => new ServerSystemContext(null!, mockSession.Object), Throws.ArgumentNullException);
        }

        [Test]
        public void CopyReturnsShallowClone()
        {
            var context = new ServerSystemContext(m_mockServer.Object);
            var copy = context.Copy();

            Assert.That(copy, Is.Not.SameAs(context));
            Assert.That(copy.Server, Is.SameAs(context.Server));
            Assert.That(copy.NamespaceUris, Is.SameAs(context.NamespaceUris));
        }

        [Test]
        public void CopyWithOperationContextSetsContext()
        {
            var context = new ServerSystemContext(m_mockServer.Object);
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None);

            var copy = context.Copy(opContext);

            Assert.That(copy.OperationContext, Is.SameAs(opContext));
            Assert.That(copy.Server, Is.SameAs(m_mockServer.Object));
        }

        [Test]
        public void CopyWithNullOperationContextPreservesExisting()
        {
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var context = new ServerSystemContext(m_mockServer.Object, opContext);

            var copy = context.Copy((OperationContext)null!);

            Assert.That(copy.OperationContext, Is.SameAs(opContext));
        }

        [Test]
        public void CopyWithSessionSetsSessionProperties()
        {
            var sessionId = new NodeId(99);
            var mockIdentity = new Mock<IUserIdentity>();
            var preferredLocales = new[] { "fr-FR" };

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(sessionId);
            mockSession.Setup(s => s.Identity).Returns(mockIdentity.Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(preferredLocales);

            var context = new ServerSystemContext(m_mockServer.Object);
            var copy = context.Copy(mockSession.Object);

            Assert.That(copy.SessionId, Is.EqualTo(sessionId));
            Assert.That(copy.UserIdentity, Is.SameAs(mockIdentity.Object));
            Assert.That(copy.PreferredLocales.ToArray(), Is.EqualTo(preferredLocales));
            Assert.That(copy.OperationContext, Is.Null);
        }

        [Test]
        public void CopyWithNullSessionClearsSessionProperties()
        {
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(1));
            mockSession.Setup(s => s.Identity).Returns(new Mock<IUserIdentity>().Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(new[] { "en-US" });

            var context = new ServerSystemContext(m_mockServer.Object, mockSession.Object);
            var copy = context.Copy((ISession)null!);

            Assert.That(copy.SessionId, Is.Null);
            Assert.That(copy.UserIdentity, Is.Null);
            Assert.That(copy.PreferredLocales.ToArray(), Is.Null.Or.Empty);
        }

        [Test]
        public void CopyWithServerSystemContextCopiesAllProperties()
        {
            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.Id).Returns(new NodeId(77));
            mockSession.Setup(s => s.Identity).Returns(new Mock<IUserIdentity>().Object);
            mockSession.Setup(s => s.PreferredLocales).Returns(new[] { "fr-FR" });

            var source = new ServerSystemContext(m_mockServer.Object, mockSession.Object);

            var target = new ServerSystemContext(m_mockServer.Object);
            var copy = target.Copy(source);

            Assert.That(copy.SessionId, Is.EqualTo(new NodeId(77)));
            Assert.That(copy.NamespaceUris, Is.SameAs(m_namespaceUris));
        }

        [Test]
        public void CopyWithNullServerSystemContextPreservesExisting()
        {
            var opContext = new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var context = new ServerSystemContext(m_mockServer.Object, opContext);

            var copy = context.Copy((ServerSystemContext)null!);

            Assert.That(copy.OperationContext, Is.SameAs(opContext));
            Assert.That(copy.Server, Is.SameAs(m_mockServer.Object));
        }
    }
}
