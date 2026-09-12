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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.UserManagement;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Features.Tests
{
    [TestFixture]
    [Category("Roles")]
    [Category("Integration")]
    public sealed class UserRevocationIntegrationTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            m_password = Guid.NewGuid().ToString("N");
            m_serverFixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                };
            await m_serverFixture.StartAsync().ConfigureAwait(false);

            m_clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_admin = await ConnectAsync(new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
            m_users = new UserManagementClient(m_admin);
            await m_users.AddUserAsync("revoked-user", m_password).ConfigureAwait(false);
            await m_users.AddUserAsync("unrelated-user", m_password).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            foreach (ISession session in m_sessions)
            {
                try
                {
                    await session.CloseAsync(5_000, true).ConfigureAwait(false);
                }
                catch (ServiceResultException ex) when (
                    ex.StatusCode == StatusCodes.BadSessionIdInvalid ||
                    ex.StatusCode == StatusCodes.BadSessionClosed)
                {
                    TestContext.Out.WriteLine("The revoked session was already closed by the server.");
                }
                finally
                {
                    session.Dispose();
                }
            }
            m_sessions.Clear();

            if (m_clientFixture != null)
            {
                await m_clientFixture.DisposeAsync().ConfigureAwait(false);
            }
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [Test]
        public Task DisableUserClosesSessionsAndSubscriptionsAsync()
        {
            return AssertUserRevokedAsync(remove: false);
        }

        [Test]
        public Task RemoveUserClosesSessionsAndSubscriptionsAsync()
        {
            return AssertUserRevokedAsync(remove: true);
        }

        private async Task AssertUserRevokedAsync(bool remove)
        {
            ISession first = await ConnectAsync(
                new UserIdentity("revoked-user", Encoding.UTF8.GetBytes(m_password))).ConfigureAwait(false);
            ISession second = await ConnectAsync(
                new UserIdentity("revoked-user", Encoding.UTF8.GetBytes(m_password))).ConfigureAwait(false);
            ISession unrelated = await ConnectAsync(
                new UserIdentity("unrelated-user", Encoding.UTF8.GetBytes(m_password))).ConfigureAwait(false);
            uint firstSubscription = await CreateSubscriptionAsync(first).ConfigureAwait(false);
            uint secondSubscription = await CreateSubscriptionAsync(second).ConfigureAwait(false);
            uint unrelatedSubscription = await CreateSubscriptionAsync(unrelated).ConfigureAwait(false);
            Server.IServerInternal server = m_serverFixture.Server.CurrentInstance;

            Assert.That(server.SessionManager.GetSessions().Select(session => session.Id),
                Is.EquivalentTo(new[] { m_admin.SessionId, first.SessionId, second.SessionId, unrelated.SessionId }));
            Assert.That(server.SubscriptionManager.GetSubscriptions().Select(subscription => subscription.Id),
                Is.EquivalentTo(new[] { firstSubscription, secondSubscription, unrelatedSubscription }));
            Assert.That((await first.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));

            if (remove)
            {
                await m_users.RemoveUserAsync("revoked-user").ConfigureAwait(false);
            }
            else
            {
                await m_users.ModifyUserAsync("revoked-user", userConfiguration: UserConfigurationMask.Disabled)
                    .ConfigureAwait(false);
            }

            Assert.That(server.SessionManager.GetSessions().Select(session => session.Id),
                Is.EquivalentTo(new[] { m_admin.SessionId, unrelated.SessionId }),
                "The user-management response must await removal of every affected session.");
            Assert.That(server.SubscriptionManager.GetSubscriptions().Select(subscription => subscription.Id),
                Is.EqualTo(new[] { unrelatedSubscription }),
                "Revocation must delete subscriptions, not leave them available for transfer.");
            foreach (ISession revoked in new[] { first, second })
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await revoked.ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            }
            Assert.That((await unrelated.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await m_admin.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                .ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(server.UserManagement!.IsUserActive("revoked-user"), Is.False);
            Assert.That(server.UserManagement.IsUserActive("unrelated-user"), Is.True);
        }

        private async Task<ISession> ConnectAsync(IUserIdentity identity)
        {
            ISession session = await m_clientFixture.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_serverFixture.Port}"),
                SecurityPolicies.Basic256Sha256,
                userIdentity: identity).ConfigureAwait(false);
            m_sessions.Add(session);
            Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            return session;
        }

        private static async Task<uint> CreateSubscriptionAsync(ISession session)
        {
            CreateSubscriptionResponse response = await session.CreateSubscriptionAsync(
                null, 1_000, 1_000, 10, 0, false, 0, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.SubscriptionId, Is.Not.Zero);
            return response.SubscriptionId;
        }

        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ISession m_admin = null!;
        private UserManagementClient m_users = null!;
        private string m_pkiRoot = null!;
        private string m_password = null!;
        private readonly List<ISession> m_sessions = [];
    }
}
