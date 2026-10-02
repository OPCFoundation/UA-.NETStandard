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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ChunkReassembly")]
    public class SessionlessBudgetTests
    {
        [Test]
        public async Task LiveMembershipFollowsActivationTransferAndLastSessionClosureAsync()
        {
            var fixture = new ServerFixture<StandardServer> { SecurityNone = true };
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                var manager = (SessionManager)server.CurrentInstance.SessionManager;
                Func<string, bool> membership = GetMembership(server);
                var activatedCallbacks = new List<bool>();
                manager.SessionActivated += (session, _) => activatedCallbacks.Add(membership(session.SecureChannelId));
                (RequestHeader first, SecureChannelContext channel) =
                    await server.CreateAndActivateSessionAsync("original").ConfigureAwait(false);
                (RequestHeader second, _) = await server.CreateAndActivateSessionAsync("original").ConfigureAwait(false);
                Assert.That(membership("original"), Is.True);
                Assert.That(activatedCallbacks, Is.Not.Empty);
                Assert.That(activatedCallbacks.All(value => value), Is.True, "Membership must precede activation callbacks.");

                var transferred = new SecureChannelContext("transferred", channel.EndpointDescription, RequestEncoding.Binary);
                var context = new OperationContext(first, transferred, RequestType.ActivateSession);
                int eventsBefore = activatedCallbacks.Count;
                (bool changed, _) = await manager.ActivateSessionAsync(context, first.AuthenticationToken, null, null, null, [])
                    .ConfigureAwait(false);
                Assert.That(changed, Is.False, "Transfer with unchanged identity must still refresh membership.");
                Assert.That(activatedCallbacks.Count, Is.EqualTo(eventsBefore), "Keep existing event semantics.");
                Assert.That(membership("original"), Is.True, "Second session still belongs to the original channel.");
                Assert.That(membership("transferred"), Is.True);

                bool? membershipDuringClose = null;
                manager.SessionClosing += (session, _) => membershipDuringClose = membership(session.SecureChannelId);
                manager.CloseSession(manager.GetSession(second.AuthenticationToken).Id);
                Assert.That(membershipDuringClose, Is.False, "Removal must precede closing callbacks.");
                Assert.That(membership("original"), Is.False);
                Assert.That(membership("transferred"), Is.True);
                manager.CloseSession(manager.GetSession(first.AuthenticationToken).Id);
                Assert.That(membership("transferred"), Is.False);
                Assert.That(manager.HasActivatedSession("original"), Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task UnactivatedFailedAndExpiredSessionsDoNotKeepFullBudgetAccessAsync()
        {
            var fixture = new ServerFixture<StandardServer> { SecurityNone = true };
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                var manager = (SessionManager)server.CurrentInstance.SessionManager;
                Func<string, bool> membership = GetMembership(server);
                EndpointDescription endpoint = server.GetEndpoints().First(e => e.SecurityPolicyUri == SecurityPolicies.None);
                var channel = new SecureChannelContext("unactivated", endpoint, RequestEncoding.Binary);
                CreateSessionResponse created = await server.CreateSessionAsync(channel, new RequestHeader(), null, null, null,
                    "pending", null, null, 120000, 128 * 1024, CancellationToken.None).ConfigureAwait(false);
                Assert.That(created.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(membership("unactivated"), Is.False);
                var header = new RequestHeader { AuthenticationToken = created.AuthenticationToken };
                var wrongEndpoint = new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.Sign,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                };
                var wrongChannel = new SecureChannelContext("unactivated", wrongEndpoint, RequestEncoding.Binary);
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.ActivateSessionAsync(new OperationContext(header, wrongChannel, RequestType.ActivateSession),
                        header.AuthenticationToken, null, null, null, []).ConfigureAwait(false));
                Assert.That(membership("unactivated"), Is.False);

                var context = new OperationContext(header, channel, RequestType.ActivateSession);
                await manager.ActivateSessionAsync(context, header.AuthenticationToken, null, null, null, []).ConfigureAwait(false);
                Assert.That(membership("unactivated"), Is.True);
                var session = (Session)manager.GetSession(header.AuthenticationToken);
                lock (session.DiagnosticsLock)
                {
                    session.SessionDiagnostics.ClientLastContactTime = DateTime.UtcNow.AddDays(-1);
                }
                // Exercise the existing expired-session cleanup, without adding/waiting for a new timer.
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.ActivateSessionAsync(context, header.AuthenticationToken, null, null, null, []).ConfigureAwait(false));
                Assert.That(manager.GetSession(header.AuthenticationToken), Is.Null);
                Assert.That(membership("unactivated"), Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MembershipCallbackSurvivesStopAndRestartWithoutOldSessionsAsync()
        {
            var fixture = new ServerFixture<StandardServer> { SecurityNone = true };
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                Func<string, bool> membership = GetMembership(server);
                await server.CreateAndActivateSessionAsync("old").ConfigureAwait(false);
                var oldManager = (SessionManager)server.CurrentInstance.SessionManager;
                Assert.That(membership("old"), Is.True);
                await server.StopAsync().ConfigureAwait(false);
                Assert.That(membership("old"), Is.False);
                Assert.That(oldManager.HasActivatedSession("old"), Is.False);
                await server.StartAsync(fixture.Config).ConfigureAwait(false);
                Assert.That(membership("old"), Is.False);
                await server.CreateAndActivateSessionAsync("new").ConfigureAwait(false);
                Assert.That(membership("new"), Is.True);
                Assert.That(GetMembership(server)("new"), Is.True);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomManagersUseExistingSessionContractIncludingSubclasses(bool subclass)
        {
            using var server = new StandardServer();
            using SessionManager derived = subclass ? new DerivedSessionManager(CreateServer(), Configuration()) : null;
            var session = new Mock<ISession>();
            bool activated = false;
            string channel = "first";
            session.SetupGet(s => s.Activated).Returns(() => activated);
            session.SetupGet(s => s.SecureChannelId).Returns(() => channel);
            var sessions = new List<ISession> { session.Object };
            var custom = new Mock<ISessionManager>();
            custom.Setup(m => m.GetSessions()).Returns(sessions);
            if (subclass)
            {
                Registry(derived).TryAdd(new NodeId(1), session.Object);
            }
            var membership = (Func<string, bool>)Delegate.CreateDelegate(
                typeof(Func<string, bool>), server, "ChannelHasActivatedSession");
            Assert.That(membership("first"), Is.False, "No manager is available before startup.");
            typeof(StandardServer).GetField("m_reassemblySessionManager", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(server, subclass ? derived : custom.Object);
            Assert.That(membership("first"), Is.False);
            activated = true;
            Assert.That(membership("first"), Is.True);
            channel = "second";
            Assert.That(membership("first"), Is.False);
            Assert.That(membership("second"), Is.True);
            if (subclass)
            {
                Registry(derived).Clear();
            }
            else
            {
                sessions.Clear();
            }
            Assert.That(membership("second"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ActivationFinishingAfterRemovalCannotRestoreMembershipAsync(bool shutdown)
        {
            using var manager = new SessionManager(CreateServer(), Configuration());
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(new NodeId(1));
            session.SetupGet(s => s.SecureChannelId).Returns("channel");
            bool activated = false;
            session.SetupGet(s => s.Activated).Returns(() => Volatile.Read(ref activated));
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            session.Setup(s => s.Activate(It.IsAny<OperationContext>(), It.IsAny<UserIdentityToken>(),
                It.IsAny<IUserIdentity>(), It.IsAny<IUserIdentity>(), It.IsAny<StringCollection>(), It.IsAny<Nonce>()))
                .Returns(() =>
                {
                    Volatile.Write(ref activated, true);
                    entered.Set();
                    if (!resume.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
                    return false;
                });
            var token = new NodeId(2);
            Registry(manager).TryAdd(token, session.Object);
            var context = new OperationContext(new RequestHeader(), new SecureChannelContext("channel",
                new EndpointDescription { SecurityPolicyUri = SecurityPolicies.None }, RequestEncoding.Binary), RequestType.ActivateSession);
            Task activation = Task.Run(async () => await manager.ActivateSessionAsync(context, token, null, null, null, []).ConfigureAwait(false));
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                if (shutdown) { manager.Shutdown(); }
                else { manager.CloseSession(session.Object.Id); }
                Assert.That(manager.HasActivatedSession("channel"), Is.False);
            }
            finally
            {
                resume.Set();
                await activation.ConfigureAwait(false);
            }
            Assert.That(manager.HasActivatedSession("channel"), Is.False);
        }

        [Test]
        public async Task ConcurrentRefreshCannotOverwriteNewerMembershipAsync()
        {
            using var manager = new SessionManager(CreateServer(), Configuration());
            var registry = Registry(manager);
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            string channel = "old";
            int reads = 0;
            var survivor = new Mock<ISession>();
            survivor.SetupGet(s => s.Activated).Returns(true);
            survivor.SetupGet(s => s.SecureChannelId).Returns(() =>
            {
                string captured = Volatile.Read(ref channel);
                if (Interlocked.Increment(ref reads) == 1)
                {
                    entered.Set();
                    if (!resume.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
                }
                return captured;
            });
            registry.TryAdd(new NodeId(1), survivor.Object);
            for (uint id = 2; id <= 3; id++)
            {
                var removable = new Mock<ISession>();
                removable.SetupGet(s => s.Id).Returns(new NodeId(id));
                registry.TryAdd(new NodeId(id), removable.Object);
            }
            Task first = Task.Run(() => manager.CloseSession(new NodeId(2)));
            Task second = Task.CompletedTask;
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Volatile.Write(ref channel, "new");
                second = Task.Run(() => manager.CloseSession(new NodeId(3)));
                Assert.That(SpinWait.SpinUntil(() => !registry.ContainsKey(new NodeId(3)), TimeSpan.FromSeconds(10)), Is.True);
            }
            finally
            {
                resume.Set();
                await Task.WhenAll(first, second).ConfigureAwait(false);
            }
            Assert.That(manager.HasActivatedSession("old"), Is.False);
            Assert.That(manager.HasActivatedSession("new"), Is.True);
            manager.Dispose();
            Assert.That(manager.HasActivatedSession("new"), Is.False);
        }

        private static Func<string, bool> GetMembership(StandardServer server) =>
            (Func<string, bool>)typeof(ServerBase).GetProperty("HasActivatedSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(server);

        private static NodeIdDictionary<ISession> Registry(SessionManager manager) =>
            (NodeIdDictionary<ISession>)typeof(SessionManager).GetField("m_sessions", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);

        private static ApplicationConfiguration Configuration() => new()
        {
            ServerConfiguration = new ServerConfiguration()
        };

        private static IServerInternal CreateServer()
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(s => s.DiagnosticsWriteLock).Returns(new object());
            server.SetupGet(s => s.ServerDiagnostics).Returns(new ServerDiagnosticsSummaryDataType { CurrentSessionCount = 100 });
            return server.Object;
        }

        private sealed class DerivedSessionManager : SessionManager
        {
            public DerivedSessionManager(IServerInternal server, ApplicationConfiguration configuration) : base(server, configuration) { }
        }
    }
}
