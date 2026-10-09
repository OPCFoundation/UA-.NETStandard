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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests.Identity
{
    [TestFixture]
    [Category("Session")]
    [Category("Identity")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class UpdateIdentityAsyncTests : ClientTestFramework
    {
        public UpdateIdentityAsyncTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            m_secretStore = new InMemorySecretStore();
            m_secretRegistry = new SecretRegistry(m_secretStore);
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SingleSession = false;
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [Test]
        public async Task UpdateIdentityAsyncSwitchesUserNameIdentity()
        {
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            var userIdentity = new UserIdentity("user1", "password"u8);
            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri!)!;
            if (identityPolicy == null)
            {
                Assert.Ignore("The test server endpoint does not advertise UserName tokens.");
            }

            using ISession session = await ClientFixture
                .ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            var rawSession = (Session)session;
            SecretIdentifier passwordId = await CreatePasswordAsync("password1"u8.ToArray())
                .ConfigureAwait(false);
            var provider = new UserNamePasswordIdentityProvider(
                "user2",
                m_secretRegistry,
                passwordId);

            await rawSession.UpdateIdentityAsync(provider).ConfigureAwait(false);

            Assert.That(session.Identity.DisplayName, Is.EqualTo("user2"));
            ServerStatusDataType status = await session
                .ReadValueAsync<ServerStatusDataType>(VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(status, Is.Not.Null);
        }

        /// <summary>
        /// L1-4: switching a UserName identity to an ECC user-token policy
        /// cleared the ephemeral key before the activation that needed it,
        /// so the token could never be encrypted. The key for the new policy
        /// must be requested first (Part 6 6.8.2).
        /// </summary>
        [Test]
        public async Task UpdateIdentityAsyncSwitchesToEccUserTokenPolicy()
        {
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            var userIdentity = new UserIdentity("user1", "password"u8);
            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri!)!;
            string? eccPolicyUri = null;
            foreach (UserTokenPolicy policy in endpoint.Description.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.UserName &&
                    policy.SecurityPolicyUri != null &&
                    policy.SecurityPolicyUri.StartsWith(
                        SecurityPolicies.ECC_nistP256,
                        System.StringComparison.Ordinal))
                {
                    eccPolicyUri = policy.SecurityPolicyUri;
                    break;
                }
            }
            if (identityPolicy == null || eccPolicyUri == null)
            {
                Assert.Ignore("The test server endpoint does not advertise UserName tokens with an ECC policy.");
            }

            using ISession session = await ClientFixture
                .ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            var rawSession = (Session)session;
            SecretIdentifier passwordId = await CreatePasswordAsync("password1"u8.ToArray())
                .ConfigureAwait(false);
            var provider = new UserNamePasswordIdentityProvider(
                "user2",
                m_secretRegistry,
                passwordId);

            // The reference server only honours ECDHPolicyUri on CreateSession,
            // so it may not hand out the key for the new policy; and with an RSA
            // instance certificate the ECC token policy cannot be satisfied at
            // all. Either way the switch must fail cleanly (not with a null
            // ephemeral key, nor by silently encrypting under the channel
            // policy) and leave the current identity active.
            string expectedUser = "user2";
            try
            {
                await rawSession.UpdateIdentityAsync(provider, eccPolicyUri).ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadSecurityPolicyRejected ||
                sre.StatusCode == StatusCodes.BadIdentityTokenRejected)
            {
                expectedUser = "user1";
            }

            Assert.That(session.Identity.DisplayName, Is.EqualTo(expectedUser));
            ServerStatusDataType status = await session
                .ReadValueAsync<ServerStatusDataType>(VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(status, Is.Not.Null);
        }

        /// <summary>
        /// G4 (review of L1-4): an ECC-to-ECC override fetches the ephemeral
        /// key for the new policy by reactivating the current identity, which
        /// replaces its key. When the new identity is then rejected, the
        /// current identity must keep an ephemeral key: without one every
        /// later reconnect fails before it reaches the server.
        /// </summary>
        [Test]
        public async Task FailedEccToEccOverrideKeepsSessionReconnectable()
        {
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.ECC_nistP256, Endpoints)
                .ConfigureAwait(false);
            var userIdentity = new UserIdentity("user1", "password"u8);
            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri!)!;
            string currentPolicyUri = (string.IsNullOrEmpty(identityPolicy?.SecurityPolicyUri)
                ? endpoint.Description.SecurityPolicyUri
                : identityPolicy.SecurityPolicyUri)!;
            string? eccPolicyUri = null;
            foreach (UserTokenPolicy policy in endpoint.Description.UserIdentityTokens)
            {
                if (policy.TokenType == UserTokenType.UserName &&
                    policy.SecurityPolicyUri != null &&
                    CryptoUtils.IsEccPolicy(policy.SecurityPolicyUri) &&
                    policy.SecurityPolicyUri != currentPolicyUri)
                {
                    eccPolicyUri = policy.SecurityPolicyUri;
                    break;
                }
            }
            if (identityPolicy == null || eccPolicyUri == null)
            {
                Assert.Ignore("The test server endpoint does not advertise two ECC UserName token policies.");
            }
            TestContext.Out.WriteLine($"Token policy {currentPolicyUri}, override {eccPolicyUri}.");

            using ISession session = await ClientFixture
                .ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            var rawSession = (Session)session;
            SecretIdentifier passwordId = await CreatePasswordAsync("wrong-password"u8.ToArray())
                .ConfigureAwait(false);
            var provider = new UserNamePasswordIdentityProvider(
                "user2",
                m_secretRegistry,
                passwordId);

            Assert.That(
                async () => await rawSession.UpdateIdentityAsync(provider, eccPolicyUri).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>());
            Assert.That(session.Identity.DisplayName, Is.EqualTo("user1"));

            await session.ReconnectAsync(null, null, default).ConfigureAwait(false);

            ServerStatusDataType status = await session
                .ReadValueAsync<ServerStatusDataType>(VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(status, Is.Not.Null);
            Assert.That(session.Identity.DisplayName, Is.EqualTo("user1"));
        }

        /// <summary>
        /// An override to an ECC policy on another curve than the client's
        /// P-256 instance certificate cannot be satisfied. It must be refused
        /// up front rather than fall back to a policy that inherits the
        /// channel policy, which would encrypt the token for the wrong curve.
        /// </summary>
        [Test]
        public async Task UnsatisfiableEccOverrideIsRejectedWithoutChangingTheSession()
        {
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.ECC_nistP256, Endpoints)
                .ConfigureAwait(false);
            const string overridePolicyUri = SecurityPolicies.ECC_nistP384;
            bool offered = false;
            foreach (UserTokenPolicy policy in endpoint.Description.UserIdentityTokens)
            {
                offered |= policy.TokenType == UserTokenType.UserName &&
                    policy.SecurityPolicyUri == overridePolicyUri;
            }
            if (!offered)
            {
                Assert.Ignore("The test server endpoint does not advertise an ECC_nistP384 UserName token policy.");
            }

            using ISession session = await ClientFixture
                .ConnectAsync(endpoint, new UserIdentity("user1", "password"u8))
                .ConfigureAwait(false);
            var rawSession = (Session)session;
            SecretIdentifier passwordId = await CreatePasswordAsync("password"u8.ToArray())
                .ConfigureAwait(false);
            var provider = new UserNamePasswordIdentityProvider("user2", m_secretRegistry, passwordId);

            Assert.That(
                async () => await rawSession.UpdateIdentityAsync(provider, overridePolicyUri).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadIdentityTokenRejected));
            Assert.That(session.Identity.DisplayName, Is.EqualTo("user1"));

            await session.ReconnectAsync(null, null, default).ConfigureAwait(false);

            ServerStatusDataType status = await session
                .ReadValueAsync<ServerStatusDataType>(VariableIds.Server_ServerStatus)
                .ConfigureAwait(false);
            Assert.That(status, Is.Not.Null);
            Assert.That(session.Identity.DisplayName, Is.EqualTo("user1"));
        }

        private async Task<SecretIdentifier> CreatePasswordAsync(byte[] password)
        {
            var id = new SecretIdentifier(
                "password-" + m_secretCounter++,
                InMemorySecretStore.DefaultStoreType);
            await m_secretStore.SetAsync(id, password).ConfigureAwait(false);
            return id;
        }

        private readonly InMemorySecretStore m_secretStore;
        private readonly SecretRegistry m_secretRegistry;
        private int m_secretCounter;
    }
}
