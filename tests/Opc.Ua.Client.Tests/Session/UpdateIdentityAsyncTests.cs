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
                endpoint.Description.SecurityPolicyUri);
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
