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
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public sealed class UserDeactivationShutdownReviewTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ShutdownDrainsDeactivationOutsideTheSessionClosingGateAsync(
            bool removeUser,
            bool disposeMaster)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:shutdown-review",
                ApplicationName = "Shutdown review",
                ServerConfiguration = new ServerConfiguration(),
                SecurityConfiguration = new SecurityConfiguration(),
                TransportQuotas = new TransportQuotas()
            };
            using var data = new ServerInternalData(
                new ServerProperties(), configuration, ServiceMessageContext.Create(telemetry));
            var sessions = new Mock<ISessionManager>();
            var subscriptions = new Mock<ISubscriptionManager>();
            data.SetSessionManager(sessions.Object, subscriptions.Object);
            data.SetMonitoredItemQueueFactory(new MonitoredItemQueueFactory(telemetry));
            var users = new Mock<IUserManagement>();
            users.Setup(value => value.SnapshotUsers()).Returns([]);
            users.Setup(value => value.RemoveUser("bob", "admin"))
                .Callback(() => users.Raise(value => value.UserDeactivated += null,
                    new UserDeactivatedEventArgs("bob")))
                .Returns(ServiceResult.Good);
            data.SetUserManagement(users.Object);

            var configManager = new ClosingConfigurationNodeManager(data, configuration);
            var coreManager = new Mock<ICoreNodeManager>();
            coreManager.SetupGet(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(configManager);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(coreManager.Object);
            data.SetMainNodeManagerFactory(factory.Object);
            using var abortDeadlock = new CancellationTokenSource();
            var master = new ClosingMasterNodeManager(data, configuration, configManager, abortDeadlock.Token);
            data.SetNodeManager(master);
            using var allowClose = new ManualResetEventSlim();
            var closeReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.DisplayName).Returns("bob");
            var session = new Mock<ISession>();
            var sessionId = new NodeId("revoked-session", 1);
            session.SetupGet(value => value.Id).Returns(sessionId);
            session.SetupGet(value => value.Identity).Returns(() =>
            {
                closeReady.TrySetResult(true);
                if (!allowClose.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the deactivation callback.");
                }
                return identity.Object;
            });
            Task shutdown = Task.CompletedTask;
            Task repeatedShutdown = Task.CompletedTask;
            Task<ServiceResult> change = Task.FromResult(ServiceResult.Good);
            try
            {
                await master.StartupAsync(CancellationToken.None).ConfigureAwait(false);
                configManager.CreateServerConfiguration(data.DefaultSystemContext, configuration);
                var state = (UserManagementState)configManager.Find(ObjectIds.UserManagement);
                Assert.That(state, Is.Not.Null);
                int snapshots = 0;
                sessions.Setup(value => value.GetSessions())
                    .Returns(() => Interlocked.Increment(ref snapshots) == 1 ? [session.Object] : []);
                if (removeUser)
                {
                    change = RemoveUserAsync(state, telemetry);
                }
                else
                {
                    users.Raise(value => value.UserDeactivated += null, new UserDeactivatedEventArgs("bob"));
                }
                await closeReady.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                shutdown = disposeMaster
                    ? master.DisposeAsync().AsTask()
                    : master.ShutdownAsync(CancellationToken.None).AsTask();
                ServiceResult rejected = await RemoveUserAsync(state, telemetry).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
                Assert.That(shutdown.IsCompleted, Is.False, "Accepted session revocation must still be drained.");
                repeatedShutdown = disposeMaster
                    ? master.DisposeAsync().AsTask()
                    : master.ShutdownAsync(CancellationToken.None).AsTask();
                Assert.That(repeatedShutdown.IsCompleted, Is.False,
                    "Concurrent shutdown callers must join the pending drain, not discard its binding.");

                allowClose.Set();
                bool reachedConfiguration = await master.ReentryReachedConfiguration.Task
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(reachedConfiguration, Is.True,
                    "Shutdown must release the gate needed by the pending deactivation's SessionClosingAsync.");
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await repeatedShutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That((await change.ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));

                Assert.That(abortDeadlock.IsCancellationRequested, Is.False);
                Assert.That(master.DeactivationToken.CanBeCanceled, Is.False);
                Assert.That(configManager.SessionClosingCount, Is.EqualTo(1));
                Assert.That(configManager.HadAddressSpaceDuringSessionClosing, Is.True);
                Assert.That(configManager.RetainedNodes, Is.Zero);
                subscriptions.Verify(value => value.SessionClosingAsync(
                    It.IsAny<OperationContext>(), sessionId, true, CancellationToken.None), Times.Once);
                sessions.Verify(value => value.CloseSessionAsync(sessionId, CancellationToken.None), Times.Once);
                session.Verify(value => value.Dispose(), Times.Never);
                coreManager.Verify(value => value.DeleteAddressSpaceAsync(CancellationToken.None),
                    disposeMaster ? Times.Never() : Times.Once());
            }
            finally
            {
                allowClose.Set();
                // Rescue only a failing test; normal shutdown and the caller use CancellationToken.None.
                abortDeadlock.Cancel();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await repeatedShutdown.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await change.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await master.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ServiceResult> RemoveUserAsync(UserManagementState state, ITelemetryContext telemetry)
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.DisplayName).Returns("admin");
            identity.SetupGet(value => value.GrantedRoleIds).Returns([ObjectIds.WellKnownRole_SecurityAdmin]);
            var channel = new SecureChannelContext("shutdown-review",
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt }, RequestEncoding.Binary);
            using var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            var context = new SessionSystemContext(operation, telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            RemoveUserMethodStateResult result = await state.RemoveUser.OnCallAsync(
                context, state.RemoveUser, state.NodeId, "bob", CancellationToken.None).ConfigureAwait(false);
            return result.ServiceResult;
        }

        private sealed class ClosingConfigurationNodeManager : ConfigurationNodeManager
        {
            public ClosingConfigurationNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public int SessionClosingCount { get; private set; }
            public bool HadAddressSpaceDuringSessionClosing { get; private set; }
            public int RetainedNodes => PredefinedNodes.Count;

            public override async ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                SessionClosingCount++;
                HadAddressSpaceDuringSessionClosing = Find(ObjectIds.UserManagement) != null;
                await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private sealed class ClosingMasterNodeManager : MasterNodeManager
        {
            public ClosingMasterNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ClosingConfigurationNodeManager configManager,
                CancellationToken abortDeadlock)
                : base(server, configuration, null, Array.Empty<IAsyncNodeManager>())
            {
                m_configManager = configManager;
                m_abortDeadlock = abortDeadlock;
            }

            public TaskCompletionSource<bool> ReentryReachedConfiguration { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public CancellationToken DeactivationToken { get; private set; }

            public override async ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                DeactivationToken = cancellationToken;
                ValueTask closing = base.SessionClosingAsync(context, sessionId, deleteSubscriptions, m_abortDeadlock);
                ReentryReachedConfiguration.TrySetResult(m_configManager.SessionClosingCount != 0);
                await closing.ConfigureAwait(false);
            }

            private readonly ClosingConfigurationNodeManager m_configManager;
            private readonly CancellationToken m_abortDeadlock;
        }
    }
}
