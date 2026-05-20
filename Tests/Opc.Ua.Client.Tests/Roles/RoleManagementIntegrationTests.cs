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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Roles;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests.Roles
{
    /// <summary>
    /// End-to-end tests that exercise the client-side
    /// <see cref="RoleManagementClient"/> against a real
    /// <see cref="ReferenceServer"/> over an encrypted secure channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference server seeds <c>sysadmin/demo</c> with
    /// <c>Role.SecurityAdmin + Role.AuthenticatedUser</c> in its
    /// <see cref="LinqUserDatabase"/> bootstrap, so these tests can drive
    /// the SecurityAdmin-only RoleSet methods directly without further
    /// configuration. Tests use <c>SecurityPolicies.Basic256Sha256</c> so
    /// the channel SecurityMode is <c>SignAndEncrypt</c> as required by
    /// the <see cref="Opc.Ua.Server.RoleAuthorizationGate"/>.
    /// </para>
    /// <para>
    /// Each test is fully isolated — a fresh <c>ServerFixture</c> +
    /// <c>ClientFixture</c> is built in <c>[SetUp]</c> so role-manager
    /// state never leaks between tests.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("Client")]
    [Category("Roles")]
    [Category("Integration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class RoleManagementIntegrationTests
    {
        private const string SysAdminUser = "sysadmin";
        private const string SysAdminPassword = "demo";

        private ServerFixture<ReferenceServer> m_serverFixture = null!;
        private ClientFixture m_clientFixture = null!;
        private ReferenceServer m_server = null!;
        private ISession m_session = null!;
        private string m_pkiRoot = null!;
        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };

            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            // Add UserName token policies for the SignAndEncrypt channel so
            // the server accepts the sysadmin/demo USERNAME identity over a
            // secure channel. The default reference-server configuration
            // ships with no explicit user-token policies in some templates.
            m_serverFixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            m_serverFixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                };

            m_server = await m_serverFixture.StartAsync().ConfigureAwait(false);

            m_clientFixture = new ClientFixture(false, false, m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            string url = $"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}";
            var sysAdminIdentity = new UserIdentity(SysAdminUser, "demo"u8);

            m_session = await m_clientFixture
                .ConnectAsync(new Uri(url), SecurityPolicies.Basic256Sha256, userIdentity: sysAdminIdentity)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    await m_session.CloseAsync().ConfigureAwait(false);
                    m_session.Dispose();
                }
            }
            catch
            {
            }

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            m_clientFixture?.Dispose();

            TryDeleteDirectory(m_pkiRoot);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        // ----------------------------------------------------------------
        // Browse / read
        // ----------------------------------------------------------------

        [Test]
        public async Task ListRolesAsync_ReturnsAllWellKnownRolesAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            IReadOnlyList<RoleInfo> roles = await client.ListRolesAsync().ConfigureAwait(false);

            // Part 3 §4.9 defines nine well-known roles which the server's
            // default RoleManager pre-populates.
            string[] expectedNames =
            [
                BrowseNames.WellKnownRole_Anonymous,
                BrowseNames.WellKnownRole_AuthenticatedUser,
                BrowseNames.WellKnownRole_Observer,
                BrowseNames.WellKnownRole_Operator,
                BrowseNames.WellKnownRole_Engineer,
                BrowseNames.WellKnownRole_Supervisor,
                BrowseNames.WellKnownRole_ConfigureAdmin,
                BrowseNames.WellKnownRole_SecurityAdmin,
                BrowseNames.WellKnownRole_TrustedApplication
            ];

            foreach (string expected in expectedNames)
            {
                Assert.That(roles.Any(r => string.Equals(r.BrowseName.Name, expected, StringComparison.Ordinal)),
                    Is.True,
                    $"ListRoles must include the well-known role {expected}.");
            }
        }

        [Test]
        public async Task ReadRoleAsync_SecurityAdminRole_ReturnsExpectedNodeIdsAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            RoleInfo info = await client.ReadRoleAsync(ObjectIds.WellKnownRole_SecurityAdmin)
                .ConfigureAwait(false);

            Assert.That(info, Is.Not.Null);
            Assert.That(info.RoleId, Is.EqualTo(ObjectIds.WellKnownRole_SecurityAdmin));
            Assert.That(info.BrowseName.Name,
                Is.EqualTo(BrowseNames.WellKnownRole_SecurityAdmin));
        }

        // ----------------------------------------------------------------
        // Identity mutations under SecurityAdmin + SignAndEncrypt
        // ----------------------------------------------------------------

        [Test]
        public async Task AddIdentityAsync_Authorised_SucceedsAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            await client.AddIdentityAsync(
                ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "alice"
                }).ConfigureAwait(false);

            // No exception ⇒ the typed AddIdentity call succeeded over the
            // wire and the server's RoleManager state was mutated.
            RoleInfo refreshed = await client.ReadRoleAsync(ObjectIds.WellKnownRole_Observer)
                .ConfigureAwait(false);
            Assert.That(refreshed.Identities.Any(r =>
                r.CriteriaType == IdentityCriteriaType.UserName &&
                string.Equals(r.Criteria, "alice", StringComparison.Ordinal)),
                Is.True,
                "The persisted IdentityMappingRule should be visible on subsequent reads.");
        }

        [Test]
        public async Task RemoveIdentityAsync_Authorised_DropsMappingAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            var rule = new IdentityMappingRuleType
            {
                CriteriaType = IdentityCriteriaType.UserName,
                Criteria = "bob"
            };

            await client.AddIdentityAsync(ObjectIds.WellKnownRole_Operator, rule)
                .ConfigureAwait(false);

            await client.RemoveIdentityAsync(ObjectIds.WellKnownRole_Operator, rule)
                .ConfigureAwait(false);

            RoleInfo refreshed = await client.ReadRoleAsync(ObjectIds.WellKnownRole_Operator)
                .ConfigureAwait(false);
            Assert.That(refreshed.Identities.Any(r =>
                r.CriteriaType == IdentityCriteriaType.UserName &&
                string.Equals(r.Criteria, "bob", StringComparison.Ordinal)),
                Is.False,
                "RemoveIdentity should drop the rule from the role manager.");
        }

        // ----------------------------------------------------------------
        // Application / exclude flag
        // ----------------------------------------------------------------

        [Test]
        public async Task AddApplicationAsync_Authorised_AddsAppUriAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            await client.AddApplicationAsync(
                ObjectIds.WellKnownRole_Observer,
                "urn:test:role-management:app").ConfigureAwait(false);

            RoleInfo refreshed = await client.ReadRoleAsync(ObjectIds.WellKnownRole_Observer)
                .ConfigureAwait(false);

            // Applications is an ArrayOf<string> snapshot; convert to a
            // local list for assertion clarity.
            var apps = new List<string>();
            foreach (string app in refreshed.Applications)
            {
                apps.Add(app);
            }
            Assert.That(apps, Has.Member("urn:test:role-management:app"),
                "The added ApplicationUri should be visible on subsequent reads.");
        }

        [Test]
        public async Task SetApplicationsExcludeAsync_Authorised_PersistsAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);
            await client.SetApplicationsExcludeAsync(
                ObjectIds.WellKnownRole_Observer, true).ConfigureAwait(false);

            RoleInfo refreshed = await client.ReadRoleAsync(ObjectIds.WellKnownRole_Observer)
                .ConfigureAwait(false);
            Assert.That(refreshed.ApplicationsExclude, Is.True);

            // Toggle back and verify.
            await client.SetApplicationsExcludeAsync(
                ObjectIds.WellKnownRole_Observer, false).ConfigureAwait(false);
            refreshed = await client.ReadRoleAsync(ObjectIds.WellKnownRole_Observer)
                .ConfigureAwait(false);
            Assert.That(refreshed.ApplicationsExclude, Is.False);
        }

        // ----------------------------------------------------------------
        // Dynamic role materialization
        // ----------------------------------------------------------------

        [Test]
        public async Task AddRoleAsync_Authorised_MaterializesRoleAndIsBrowseableAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);

            NodeId newRoleId = await client.AddRoleAsync(
                "IntegrationTestRole_Add",
                namespaceUri: null).ConfigureAwait(false);

            Assert.That(newRoleId.IsNull, Is.False,
                "AddRole should return the allocated NodeId.");

            // The new role must show up in ListRoles immediately — proves
            // the dynamic materialization landed in the address space.
            IReadOnlyList<RoleInfo> roles = await client.ListRolesAsync()
                .ConfigureAwait(false);
            Assert.That(roles.Any(r => r.RoleId == newRoleId), Is.True,
                "ListRoles must include the newly added role.");

            // And we should be able to call AddIdentity on it via the same
            // typed proxy chain.
            await client.AddIdentityAsync(
                newRoleId,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.UserName,
                    Criteria = "carol"
                }).ConfigureAwait(false);

            RoleInfo refreshed = await client.ReadRoleAsync(newRoleId)
                .ConfigureAwait(false);
            Assert.That(refreshed.Identities.Any(r =>
                r.CriteriaType == IdentityCriteriaType.UserName &&
                string.Equals(r.Criteria, "carol", StringComparison.Ordinal)),
                Is.True,
                "AddIdentity on a dynamically-added role must persist via the role manager.");
        }

        [Test]
        public async Task RemoveRoleAsync_Authorised_DropsRoleFromAddressSpaceAsync()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);

            NodeId newRoleId = await client.AddRoleAsync(
                "IntegrationTestRole_Remove", namespaceUri: null).ConfigureAwait(false);
            Assume.That(newRoleId.IsNull, Is.False);

            await client.RemoveRoleAsync(newRoleId).ConfigureAwait(false);

            IReadOnlyList<RoleInfo> roles = await client.ListRolesAsync()
                .ConfigureAwait(false);
            Assert.That(roles.Any(r => r.RoleId == newRoleId), Is.False,
                "ListRoles must no longer include the removed role.");
        }

        // ----------------------------------------------------------------
        // Reserved role enforcement
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentityAsync_AnonymousReservedRole_RejectedByServer()
        {
            IRoleManagementClient client = new RoleManagementClient(m_session);

            // Per Part 3 §4.9 the Anonymous role's identity mapping is
            // immutable. The standard nodeset doesn't materialize an
            // AddIdentity method on Anonymous, so the client gets either
            // BadNoMatch when resolving the child or BadInvalidArgument /
            // BadInvalidState if a reserved-role guard hits first. Any of
            // these proves the reserved role can't be mutated.
            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.AddIdentityAsync(
                    ObjectIds.WellKnownRole_Anonymous,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.UserName,
                        Criteria = "should-not-stick"
                    }).ConfigureAwait(false));

            Assert.That((StatusCode)ex!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument)
                    .Or.EqualTo((StatusCode)StatusCodes.BadInvalidState)
                    .Or.EqualTo((StatusCode)StatusCodes.BadNoMatch),
                "Reserved-role AddIdentity must be rejected.");
        }
    }
}
