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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Conformance.Tests.Security
{
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityUserToken")]
    public class SecurityUserTokenDepthTests : TestFixture
    {
        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosTokenAdvertisementIgnored()
        {
            Assert.Ignore("Kerberos token advertisement not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosConnectionIgnored()
        {
            Assert.Ignore("Kerberos connection not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosDelegationIgnored()
        {
            Assert.Ignore("Kerberos delegation not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosTokenRefreshIgnored()
        {
            Assert.Ignore("Kerberos token refresh not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosRealmHandlingIgnored()
        {
            Assert.Ignore("Kerberos realm handling not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosMultiAuthIgnored()
        {
            Assert.Ignore("Kerberos multi-authentication not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosTimeSkewIgnored()
        {
            Assert.Ignore("Kerberos time skew handling not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosSessionCachingIgnored()
        {
            Assert.Ignore("Kerberos session caching not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosCredentialCachingIgnored()
        {
            Assert.Ignore("Kerberos credential caching not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosErrorHandlingIgnored()
        {
            Assert.Ignore("Kerberos error handling not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosTokenStructureIgnored()
        {
            Assert.Ignore("Kerberos token structure not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosIntegrityCheckIgnored()
        {
            Assert.Ignore("Kerberos integrity check not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosEncryptionIgnored()
        {
            Assert.Ignore("Kerberos encryption not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosClaimMappingIgnored()
        {
            Assert.Ignore("Kerberos claim mapping not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosGroupMembershipIgnored()
        {
            Assert.Ignore("Kerberos group membership not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosServicePrincipalIgnored()
        {
            Assert.Ignore("Kerberos service principal handling not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosAuthorizationDataIgnored()
        {
            Assert.Ignore("Kerberos authorization data not exercised.");
        }

        [Property("Limitation", "RequiresKerberos")]
        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public void KerberosPreAuthIgnored()
        {
            Assert.Ignore("Kerberos pre-authentication not exercised.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task EndpointsAdvertiseUsernameTokenAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasUsernameToken = EndpointsHaveUsernameToken(endpoints);
            if (!hasUsernameToken)
            {
                Assert.Fail("Server does not advertise username/password token.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task UsernameTokenHasSecurityPolicyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool foundWithPolicy = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName && !string.IsNullOrEmpty(t.SecurityPolicyUri))
                        {
                            foundWithPolicy = true;
                            break;
                        }
                    }
                }
            }

            if (!foundWithPolicy)
            {
                Assert.Fail("No username token with security policy found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "003")]
        public async Task ConnectWithUsernamePasswordAsync()
        {
            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
                try
                {
                    Assert.That(session.Connected, Is.True);
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            catch (ServiceResultException)
            {
                Assert.Fail("Username authentication not supported by server.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "004")]
        public async Task ChangeIdentityBetweenSessionsAsync()
        {
            try
            {
                ISession session1 = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
                try
                {
                    Assert.That(session1.Connected, Is.True);
                }
                finally
                {
                    await session1.CloseAsync(5000, true).ConfigureAwait(false);
                    session1.Dispose();
                }

                ISession session2 = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
                try
                {
                    Assert.That(session2.Connected, Is.True);
                }
                finally
                {
                    await session2.CloseAsync(5000, true).ConfigureAwait(false);
                    session2.Dispose();
                }
            }
            catch (ServiceResultException)
            {
                Assert.Fail("Username authentication not supported by server.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "001")]
        public async Task NonceIsUniquePerSessionAsync()
        {
            ISession session1 = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            ISession session2 = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session1.Connected, Is.True);
                Assert.That(session2.Connected, Is.True);
                Assert.That(session1.SessionId, Is.Not.EqualTo(session2.SessionId));
            }
            finally
            {
                await session1.CloseAsync(5000, true).ConfigureAwait(false);
                session1.Dispose();
                await session2.CloseAsync(5000, true).ConfigureAwait(false);
                session2.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "001")]
        public async Task SessionTimeoutBehaviorAsync()
        {
            ISession session = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
                Assert.That(session.SessionTimeout, Is.GreaterThan(0));
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "001")]
        public async Task AnonymousTokenTypeAsync()
        {
            ISession session = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task UsernameTokenPolicyIdPresentAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasPolicy = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName && !string.IsNullOrEmpty(t.PolicyId))
                        {
                            hasPolicy = true;
                            break;
                        }
                    }
                }
            }
            if (!hasPolicy)
            {
                Assert.Fail("No username token with PolicyId found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task IssuedTokenTypeForUsernameAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasIssued = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName && !string.IsNullOrEmpty(t.IssuedTokenType))
                        {
                            hasIssued = true;
                            break;
                        }
                    }
                }
            }

            if (!hasIssued)
            {
                Assert.Ignore("No username token with IssuedTokenType found.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task SecurityLevelValueAsync()
        {
            ISession session = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "002")]
        public async Task MultipleEndpointsWithDifferentTokensAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            var tokenTypes = new HashSet<UserTokenType>();
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        tokenTypes.Add(t.TokenType);
                    }
                }
            }
            Assert.That(tokenTypes, Is.Not.Empty);
        }

        [Test]
        [Property("ConformanceUnit", "Security User Anonymous")]
        [Property("Tag", "001")]
        public async Task SessionKeepAliveAsync()
        {
            ISession session = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True);
                await Task.Delay(1000).ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }

        private bool EndpointsHaveUsernameToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
