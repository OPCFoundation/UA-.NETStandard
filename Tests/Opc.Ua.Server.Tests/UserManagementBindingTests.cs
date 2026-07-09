/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.UserManagement;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UserManagementBinding"/> covering the static
    /// <c>Bind</c> guard clauses, dispose idempotency, and the
    /// <see cref="IUserManagement.UserDeactivated"/> session-closing logic
    /// (Part 18 §5.2.6 / §5.2.7).
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    public class UserManagementBindingTests
    {
        private Mock<IServerInternal> m_mockServer = null!;
        private ApplicationConfiguration m_configuration = null!;
        private Mock<ILogger> m_mockLogger = null!;
        private MonitoredItemQueueFactory m_monitoredItemQueueFactory = null!;
        private Mock<IUserManagement> m_userManagement = null!;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_mockLogger = new Mock<ILogger>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append("http://test.org/UA/UserManagement/");

            m_mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            m_mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            m_mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            m_monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            m_mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_monitoredItemQueueFactory);

            var serverSystemContext = new ServerSystemContext(m_mockServer.Object);
            m_mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            m_userManagement = new Mock<IUserManagement>();
        }

        [TearDown]
        public void TearDown()
        {
            m_monitoredItemQueueFactory?.Dispose();
        }

        private TestableAsyncCustomNodeManager CreateNodeManager()
        {
            return new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_mockLogger.Object,
                "http://test.org/UA/UserManagement/");
        }

        private TestableAsyncCustomNodeManager CreateNodeManagerWithUserManagementNode()
        {
            TestableAsyncCustomNodeManager manager = CreateNodeManager();
            var state = new UserManagementState(null)
            {
                NodeId = new NodeId(Objects.UserManagement)
            };
            manager.PredefinedNodes[state.NodeId] = state;
            return manager;
        }

        private static Mock<ISession> CreateSessionWithUser(string? userName)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.DisplayName).Returns(userName!);

            var session = new Mock<ISession>();
            session.Setup(s => s.Identity).Returns(identity.Object);
            session.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            return session;
        }

        [Test]
        public void BindRejectsNullNodeManager()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => UserManagementBinding.Bind(null!, m_userManagement.Object, null));
            Assert.That(ex.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void BindRejectsNullUserManagement()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManager();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => UserManagementBinding.Bind(manager, null!, null));
            Assert.That(ex.ParamName, Is.EqualTo("userManagement"));
        }

        [Test]
        public void BindReturnsNullWhenUserManagementNodeMissing()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManager();
            UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Null);
        }

        [Test]
        public void BindReturnsBindingWhenUserManagementNodePresent()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Not.Null);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Not.Null);

            binding!.Dispose();
            Assert.DoesNotThrow(() => binding.Dispose());
        }

        [Test]
        public void UserDeactivatedClosesMatchingSession()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("bob");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            Assert.That(binding, Is.Not.Null);

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));

            session.Verify(s => s.Dispose(), Times.Once);
        }

        [Test]
        public void UserDeactivatedIgnoresNonMatchingSession()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("alice");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));

            session.Verify(s => s.Dispose(), Times.Never);
        }

        [Test]
        public void UserDeactivatedSwallowsSessionDisposeException()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("bob");
            session.Setup(s => s.Dispose()).Throws(new InvalidOperationException("boom"));
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            Assert.DoesNotThrow(() =>
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")));
            session.Verify(s => s.Dispose(), Times.Once);
        }

        [Test]
        public void UserDeactivatedSwallowsGetSessionsException()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Throws(new InvalidOperationException("boom"));

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            Assert.DoesNotThrow(() =>
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")));
        }

        [Test]
        public void UserDeactivatedWithEmptyUserNameDoesNothing()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs(string.Empty));

            sessionManager.Verify(m => m.GetSessions(), Times.Never);
        }

        [Test]
        public void UserDeactivatedWithoutSessionManagerDoesNothing()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();

            using UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);

            Assert.DoesNotThrow(() =>
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")));
        }

        [Test]
        public void DisposeUnsubscribesFromUserDeactivated()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns(new List<ISession>());

            UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            Assert.That(binding, Is.Not.Null);

            binding!.Dispose();

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));

            sessionManager.Verify(m => m.GetSessions(), Times.Never);
        }
    }
}
