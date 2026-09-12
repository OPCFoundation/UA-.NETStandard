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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

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
            m_userManagement.Setup(m => m.PasswordLength).Returns(new Range { Low = 8, High = 64 });
            m_userManagement.Setup(m => m.PasswordOptions).Returns(PasswordOptionsMask.None);
            m_userManagement.Setup(m => m.PasswordRestrictions).Returns(new LocalizedText("restrictions"));
            m_userManagement.Setup(m => m.SnapshotUsers()).Returns([]);
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

        private (TestableAsyncCustomNodeManager Manager, UserManagementState State)
            CreateNodeManagerWithCreatedUserManagementNode()
        {
            TestableAsyncCustomNodeManager manager = CreateNodeManager();
            var state = new UserManagementState(null);
            state.Create(
                manager.SystemContext,
                new NodeId(Objects.UserManagement),
                new QualifiedName(BrowseNames.UserManagement),
                new LocalizedText(BrowseNames.UserManagement),
                false);
            manager.PredefinedNodes[state.NodeId] = state;
            return (manager, state);
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

        private static SessionSystemContext BuildContext(
            MessageSecurityMode securityMode,
            IUserIdentity identity)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test-channel", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                identity);
            return new SessionSystemContext(operationContext, NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private static IUserIdentity BuildIdentity(
            UserTokenType tokenType,
            string displayName,
            params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.DisplayName).Returns(displayName);
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            return identity.Object;
        }

        private static SessionSystemContext BuildAdminContext()
        {
            return BuildContext(
                MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(
                    UserTokenType.UserName,
                    "admin",
                    ObjectIds.WellKnownRole_SecurityAdmin));
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
            var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Null);
        }

        [Test]
        public void BindReturnsBindingWhenUserManagementNodePresent()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Not.Null);
        }

        [Test]
        public async Task AddUserRejectsNonAdminAndDoesNotDelegateAsync()
        {
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();
            using (manager)
            using (UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null))
            {
                Assert.That(binding, Is.Not.Null);
                SessionSystemContext context = BuildContext(
                    MessageSecurityMode.SignAndEncrypt,
                    BuildIdentity(UserTokenType.UserName, "operator", ObjectIds.WellKnownRole_Operator));

                AddUserMethodStateResult result = await state.AddUser!.OnCallAsync!(
                    context,
                    state.AddUser,
                    state.NodeId,
                    "alice",
                    "password",
                    (uint)UserConfigurationMask.None,
                    "Alice",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                m_userManagement.Verify(
                    m => m.AddUser(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<UserConfigurationMask>(),
                        It.IsAny<string>()),
                    Times.Never);
            }
        }

        [Test]
        public async Task AdminMethodsDelegateToUserManagementAndRefreshPropertiesAsync()
        {
            UserManagementDataType[] initialUsers = [];
            var addedUsers = new[]
            {
                new UserManagementDataType
                {
                    UserName = "alice",
                    UserConfiguration = (uint)UserConfigurationMask.None,
                    Description = "Alice"
                }
            };
            var modifiedUsers = new[]
            {
                new UserManagementDataType
                {
                    UserName = "alice",
                    UserConfiguration = (uint)UserConfigurationMask.Disabled,
                    Description = "Disabled"
                }
            };
            UserManagementDataType[] removedUsers = [];
            m_userManagement
                .SetupSequence(m => m.SnapshotUsers())
                .Returns(initialUsers)
                .Returns(addedUsers)
                .Returns(modifiedUsers)
                .Returns(removedUsers);
            m_userManagement.Setup(m => m.AddUser("alice", "password", UserConfigurationMask.None, "Alice"))
                .Returns(ServiceResult.Good);
            m_userManagement
                .Setup(m => m.ModifyUser(
                    "alice", true, "new", true, UserConfigurationMask.Disabled, true, "Disabled", "admin"))
                .Returns(ServiceResult.Good);
            m_userManagement.Setup(m => m.RemoveUser("alice", "admin")).Returns(ServiceResult.Good);
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();

            using (manager)
            using (UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null))
            {
                Assert.That(binding, Is.Not.Null);
                SessionSystemContext context = BuildAdminContext();

                AddUserMethodStateResult addResult = await state.AddUser!.OnCallAsync!(
                    context,
                    state.AddUser,
                    state.NodeId,
                    "alice",
                    "password",
                    (uint)UserConfigurationMask.None,
                    "Alice",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(addResult.ServiceResult), Is.True);
                Assert.That(state.Users!.Value, Has.Count.EqualTo(1));
                Assert.That(state.Users.Value[0].UserName, Is.EqualTo("alice"));
                Assert.That(state.Users.Value[0].UserConfiguration, Is.EqualTo((uint)UserConfigurationMask.None));
                Assert.That(state.Users.Value[0].Description, Is.EqualTo("Alice"));

                ModifyUserMethodStateResult modifyResult = await state.ModifyUser!.OnCallAsync!(
                    context,
                    state.ModifyUser,
                    state.NodeId,
                    "alice",
                    true,
                    "new",
                    true,
                    (uint)UserConfigurationMask.Disabled,
                    true,
                    "Disabled",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(modifyResult.ServiceResult), Is.True);
                Assert.That(state.Users!.Value, Has.Count.EqualTo(1));
                Assert.That(state.Users.Value[0].UserName, Is.EqualTo("alice"));
                Assert.That(
                    state.Users.Value[0].UserConfiguration,
                    Is.EqualTo((uint)UserConfigurationMask.Disabled));
                Assert.That(state.Users.Value[0].Description, Is.EqualTo("Disabled"));

                RemoveUserMethodStateResult removeResult = await state.RemoveUser!.OnCallAsync!(
                    context,
                    state.RemoveUser,
                    state.NodeId,
                    "alice",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(removeResult.ServiceResult), Is.True);
                Assert.That(state.Users!.Value, Is.Empty);
                Assert.That(state.PasswordLength!.Value.High, Is.EqualTo(64));

                m_userManagement.Verify(
                    m => m.AddUser("alice", "password", UserConfigurationMask.None, "Alice"),
                    Times.Once);
                m_userManagement.Verify(
                    m => m.ModifyUser(
                        "alice",
                        true,
                        "new",
                        true,
                        UserConfigurationMask.Disabled,
                        true,
                        "Disabled",
                        "admin"),
                    Times.Once);
                m_userManagement.Verify(m => m.RemoveUser("alice", "admin"), Times.Once);
                m_userManagement.Verify(m => m.SnapshotUsers(), Times.Exactly(4));
            }
        }

        [Test]
        public async Task ChangePasswordRequiresUserNameIdentityAsync()
        {
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();

            using (manager)
            using (UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null))
            {
                Assert.That(binding, Is.Not.Null);
                SessionSystemContext context = BuildContext(
                    MessageSecurityMode.SignAndEncrypt,
                    BuildIdentity(UserTokenType.IssuedToken, "alice", ObjectIds.WellKnownRole_SecurityAdmin));

                ChangePasswordMethodStateResult result = await state.ChangePassword!.OnCallAsync!(
                    context,
                    state.ChangePassword,
                    state.NodeId,
                    "old",
                    "new",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                m_userManagement.Verify(
                    m => m.ChangePassword(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                    Times.Never);
            }
        }

        [Test]
        public async Task ChangePasswordDelegatesForSelfUserNameAsync()
        {
            m_userManagement.Setup(m => m.ChangePassword("alice", "old", "new"))
                .Returns(ServiceResult.Good);
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();

            using (manager)
            using (UserManagementBinding? binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null))
            {
                Assert.That(binding, Is.Not.Null);
                SessionSystemContext context = BuildContext(
                    MessageSecurityMode.SignAndEncrypt,
                    BuildIdentity(UserTokenType.UserName, "alice", ObjectIds.WellKnownRole_Observer));

                ChangePasswordMethodStateResult result = await state.ChangePassword!.OnCallAsync!(
                    context,
                    state.ChangePassword,
                    state.NodeId,
                    "old",
                    "new",
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                m_userManagement.Verify(m => m.ChangePassword("alice", "old", "new"), Times.Once);
            }
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);
            Assert.That(binding, Is.Not.Null);

            binding!.Dispose();
            Assert.DoesNotThrow(binding.Dispose);
        }

        [Test]
        public async Task UserDeactivatedClosesMatchingSession()
        {
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();
            using var managerLifetime = manager;
            Mock<ISession> session = CreateSessionWithUser("bob");
            Mock<ISession> unrelated = CreateSessionWithUser("alice");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object, unrelated.Object]);
            m_userManagement.Setup(m => m.RemoveUser("bob", "admin"))
                .Callback(() => m_userManagement.Raise(
                    u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")))
                .Returns(ServiceResult.Good);

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            Assert.That(binding, Is.Not.Null);

            RemoveUserMethodStateResult result = await state.RemoveUser!.OnCallAsync!(
                BuildAdminContext(),
                state.RemoveUser,
                state.NodeId,
                "bob",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            m_mockServer.Verify(s => s.CloseSessionAsync(
                It.IsAny<OperationContext>(), session.Object.Id, true, CancellationToken.None), Times.Once);
            m_mockServer.Verify(s => s.CloseSessionAsync(
                It.IsAny<OperationContext>(), unrelated.Object.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
                Times.Never);
            session.Verify(s => s.Dispose(), Times.Never);
            sessionManager.Verify(m => m.CloseSessionAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void UserDeactivatedIgnoresNonMatchingSession()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("alice");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));

            session.Verify(s => s.Dispose(), Times.Never);
        }

        [Test]
        public async Task UserDeactivatedLogsCloseFailureAndContinuesAsync()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(m_mockLogger.Object);
            Mock.Get(m_mockServer.Object.Telemetry).Setup(t => t.LoggerFactory).Returns(loggerFactory.Object);
            m_mockLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();
            using var managerLifetime = manager;
            Mock<ISession> session = CreateSessionWithUser("bob");
            Mock<ISession> anotherSession = CreateSessionWithUser("bob");
            var failure = new InvalidOperationException("close failed");
            m_mockServer.Setup(s => s.CloseSessionAsync(
                    It.IsAny<OperationContext>(), session.Object.Id, true, It.IsAny<CancellationToken>()))
                .Throws(failure);
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object, anotherSession.Object]);
            m_userManagement.Setup(m => m.RemoveUser("bob", "admin"))
                .Callback(() => m_userManagement.Raise(
                    u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")))
                .Returns(ServiceResult.Good);

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            RemoveUserMethodStateResult result = await state.RemoveUser!.OnCallAsync!(
                BuildAdminContext(),
                state.RemoveUser,
                state.NodeId,
                "bob",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            m_mockServer.Verify(s => s.CloseSessionAsync(
                It.IsAny<OperationContext>(), anotherSession.Object.Id, true, CancellationToken.None), Times.Once);
            m_mockLogger.Verify(l => l.Log(
                LogLevel.Warning,
                It.Is<EventId>(id => id.Name == "FailedToCloseSessionSessionIdForDeactivated"),
                It.IsAny<It.IsAnyType>(),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            session.Verify(s => s.Dispose(), Times.Never);
        }

        [Test]
        public void UserDeactivatedSwallowsGetSessionsException()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Throws(new InvalidOperationException("boom"));

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            Assert.DoesNotThrow(() =>
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")));
        }

        [Test]
        public void UserDeactivatedWithEmptyUserNameDoesNothing()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs(string.Empty));

            sessionManager.Verify(m => m.GetSessions(), Times.Never);
        }

        [Test]
        public void UserDeactivatedWithoutSessionManagerDoesNothing()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();

            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null);

            Assert.DoesNotThrow(() =>
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")));
        }

        [Test]
        public async Task LateBoundServerSessionManagerIsResolvedAtDeactivationAsync()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("bob");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(value => value.GetSessions()).Returns([session.Object]);
            await using UserManagementBinding binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, null)!;
            m_mockServer.SetupGet(value => value.SessionManager).Returns(sessionManager.Object);

            m_userManagement.Raise(value => value.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));
            await binding.DisposeAsync().ConfigureAwait(false);

            m_mockServer.Verify(value => value.CloseSessionAsync(
                It.IsAny<OperationContext>(), session.Object.Id, true, CancellationToken.None), Times.Once);
            session.Verify(value => value.Dispose(), Times.Never);
        }

        [Test]
        public void DisposeUnsubscribesFromUserDeactivated()
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([]);

            var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            Assert.That(binding, Is.Not.Null);

            binding!.Dispose();

            m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));

            sessionManager.Verify(m => m.GetSessions(), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UserChangeWaitsForCoordinatedTeardownAsync(bool remove)
        {
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();
            using var managerLifetime = manager;
            Mock<ISession> session = CreateSessionWithUser("bob");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);
            SetupUserDeactivation();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockServer.Setup(s => s.CloseSessionAsync(
                    It.IsAny<OperationContext>(), session.Object.Id, true, CancellationToken.None))
                .Callback(() => entered.SetResult(true))
                .Returns(new ValueTask(release.Task));
            using var binding = UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            using var cancellation = new CancellationTokenSource();
            Task<ServiceResult> change = InvokeUserChangeAsync(state, BuildAdminContext(), remove, cancellation.Token);

            try
            {
                Task first = await Task.WhenAny(entered.Task, change).WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task), "Revocation must enter the full server teardown.");
                cancellation.Cancel();
                Assert.That(change.IsCompleted, Is.False,
                    "Cancelling the request after the user is revoked must not abandon its teardown.");
            }
            finally
            {
                release.TrySetResult(true);
            }

            Assert.That(ServiceResult.IsGood(await change.ConfigureAwait(false)), Is.True);
            session.Verify(s => s.Dispose(), Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposeAsyncDrainsExternalDeactivationAsync(bool disposeFirst)
        {
            using TestableAsyncCustomNodeManager manager = CreateNodeManagerWithUserManagementNode();
            Mock<ISession> session = CreateSessionWithUser("bob");
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(m => m.GetSessions()).Returns([session.Object]);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockServer.Setup(s => s.CloseSessionAsync(
                    It.IsAny<OperationContext>(), session.Object.Id, true, CancellationToken.None))
                .Callback(() => entered.SetResult(true))
                .Returns(new ValueTask(release.Task));
            using var binding =
                UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object)!;

            try
            {
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));
                if (disposeFirst)
                {
                    binding.Dispose();
                }
                Assert.That(binding, Is.InstanceOf<IAsyncDisposable>());
                Task disposal = binding.DisposeAsync().AsTask();
                Task first = await Task.WhenAny(entered.Task, disposal).WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(first, Is.SameAs(entered.Task));
                Assert.That(disposal.IsCompleted, Is.False, "Disposal must drain an external notification.");
                m_userManagement.Raise(u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));
                release.SetResult(true);
                await disposal.ConfigureAwait(false);
                await binding.DisposeAsync().ConfigureAwait(false);
                sessionManager.Verify(m => m.GetSessions(), Times.Once);
                m_mockServer.Verify(s => s.CloseSessionAsync(
                    It.IsAny<OperationContext>(), session.Object.Id, true, CancellationToken.None), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        [TestCase(false, MessageSecurityMode.None, true)]
        [TestCase(true, MessageSecurityMode.None, true)]
        [TestCase(false, MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(true, MessageSecurityMode.SignAndEncrypt, false)]
        public async Task UserChangeAuthorizationPrecedesMutationAsync(
            bool remove,
            MessageSecurityMode securityMode,
            bool isAdmin)
        {
            (TestableAsyncCustomNodeManager manager, UserManagementState state) =
                CreateNodeManagerWithCreatedUserManagementNode();
            using var managerLifetime = manager;
            var sessionManager = new Mock<ISessionManager>();
            using var binding = UserManagementBinding.Bind(manager, m_userManagement.Object, sessionManager.Object);
            SessionSystemContext context = BuildContext(
                securityMode,
                BuildIdentity(UserTokenType.UserName, "admin",
                    isAdmin ? ObjectIds.WellKnownRole_SecurityAdmin : ObjectIds.WellKnownRole_Observer));

            ServiceResult result = await InvokeUserChangeAsync(state, context, remove, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(
                isAdmin ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadUserAccessDenied));
            m_userManagement.Verify(m => m.RemoveUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            m_userManagement.Verify(m => m.ModifyUser(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<UserConfigurationMask>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            sessionManager.Verify(m => m.GetSessions(), Times.Never);
        }

        private void SetupUserDeactivation()
        {
            m_userManagement.Setup(m => m.RemoveUser("bob", "admin"))
                .Callback(() => m_userManagement.Raise(
                    u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")))
                .Returns(ServiceResult.Good);
            m_userManagement.Setup(m => m.ModifyUser(
                    "bob", false, string.Empty, true, UserConfigurationMask.Disabled, false, string.Empty, "admin"))
                .Callback(() => m_userManagement.Raise(
                    u => u.UserDeactivated += null, new UserDeactivatedEventArgs("bob")))
                .Returns(ServiceResult.Good);
        }

        private static async Task<ServiceResult> InvokeUserChangeAsync(
            UserManagementState state,
            SessionSystemContext context,
            bool remove,
            CancellationToken cancellationToken)
        {
            if (remove)
            {
                RemoveUserMethodStateResult result = await state.RemoveUser!.OnCallAsync!(
                    context, state.RemoveUser, state.NodeId, "bob", cancellationToken).ConfigureAwait(false);
                return result.ServiceResult;
            }
            ModifyUserMethodStateResult modified = await state.ModifyUser!.OnCallAsync!(
                context, state.ModifyUser, state.NodeId, "bob", false, string.Empty, true,
                (uint)UserConfigurationMask.Disabled, false, string.Empty, cancellationToken).ConfigureAwait(false);
            return modified.ServiceResult;
        }
    }
}
