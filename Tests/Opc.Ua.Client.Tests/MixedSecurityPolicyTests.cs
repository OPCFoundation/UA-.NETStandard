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
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class MixedSecurityPolicyTests : ClientTestFramework
    {
        public MixedSecurityPolicyTests()
            : base(Utils.UriSchemeOpcTcp)
        {
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

        [Test]
        public Task Connect_ECCEndpoint_RSAUserTokenAsync()
        {
            return Connect_Endpoint_UserECCRSATokenAsync(true);
        }

        [Test]
        public Task Connect_RsaEndpoint_EccUserTokenAsync()
        {
            return Connect_Endpoint_UserECCRSATokenAsync(false);
        }

        private async Task Connect_Endpoint_UserECCRSATokenAsync(bool useEccEndpoint)
        {
            // 1. Get endpoints to find the PolicyId
            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry).ConfigureAwait(false);

            EndpointDescriptionCollection endpoints = await discoveryClient.GetEndpointsAsync(null).ConfigureAwait(false);

            // 2. Find RSA endpoint
            EndpointDescription rsaEndpoint = endpoints.FirstOrDefault(e =>
                (useEccEndpoint ^ !EccUtils.IsEccPolicy(e.SecurityPolicyUri)) &&
                e.SecurityMode == MessageSecurityMode.SignAndEncrypt);

            Assert.That(rsaEndpoint, Is.Not.Null, "RSA Endpoint with RSA Policy and SignAndEncrypt not found");

            var configuredEndpoint = new ConfiguredEndpoint(null, rsaEndpoint, endpointConfiguration);

            // 3. Find ECC UserTokenPolicy
            UserTokenPolicy userTokenPolicy = rsaEndpoint.UserIdentityTokens.FirstOrDefault(p =>
                p.TokenType == UserTokenType.UserName &&
                (useEccEndpoint ^ EccUtils.IsEccPolicy(p.SecurityPolicyUri)));

            Assert.That(userTokenPolicy, Is.Not.Null, $"UserTokenPolicy with ECC SecurityPolicy not found on RSA Endpoint.");
            TestContext.WriteLine($"Using UserTokenPolicy: {userTokenPolicy.PolicyId} ({userTokenPolicy.SecurityPolicyUri})");

            // 4. Create UserIdentity and enforce PolicyId
            var identity = new UserIdentity("user1", System.Text.Encoding.UTF8.GetBytes("password"));
            identity.PolicyId = userTokenPolicy.PolicyId;

            // 5. Connect
            ISession session = await ClientFixture.ConnectAsync(
                configuredEndpoint,
                identity).ConfigureAwait(false);

            try
            {
                Assert.That(session, Is.Not.Null);
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
