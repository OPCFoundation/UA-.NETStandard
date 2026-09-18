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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionIdentityReviewTests : ClientTestFramework
    {
        public SessionIdentityReviewTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UserNameOnSignUsesEffectiveTokenPolicyAsync(bool explicitNone)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ArrayOf<EndpointDescription> endpoints = await ClientFixture
                .GetEndpointsAsync(ServerUrl, timeout.Token).ConfigureAwait(false);
            EndpointDescription description = endpoints.ToList().First(endpoint =>
                endpoint.SecurityMode == MessageSecurityMode.Sign &&
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            UserTokenPolicy policy = description.UserIdentityTokens.ToList().First(token =>
                token.TokenType == UserTokenType.UserName &&
                string.IsNullOrEmpty(token.SecurityPolicyUri));
            if (explicitNone)
            {
                policy.SecurityPolicyUri = SecurityPolicies.None;
            }
            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(ClientFixture.Config));
            var identity = new UserIdentity("user1", "password"u8) { PolicyId = policy.PolicyId };
            var factory = new DefaultSessionFactory(Telemetry);

            if (explicitNone)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await factory.CreateAsync(
                        ClientFixture.Config, endpoint, false, false, "unencrypted-token",
                        60000, identity, default, timeout.Token).ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                return;
            }

            using ISession session = await factory.CreateAsync(
                ClientFixture.Config, endpoint, false, false, "inherited-token",
                60000, identity, default, timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }
    }
}
