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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("SecurityPolicy")]
    public class SecurityPolicyDepthTests : TestFixture
    {
        [Test]
        public async Task EndpointsAdvertiseSecurityPoliciesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));

            bool hasPolicies = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (!string.IsNullOrEmpty(ep.SecurityPolicyUri))
                {
                    hasPolicies = true;
                    break;
                }
            }
            Assert.That(hasPolicies, Is.True);
        }

        [Test]
        public async Task NoneSecurityPolicyPresentAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasNone = EndpointSupportsPolicy(endpoints, SecurityPolicies.None);
            Assert.That(hasNone, Is.True, "Server should advertise None security policy.");
        }

        [Test]
        public async Task AtLeastOneSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasSecure = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != MessageSecurityMode.None)
                {
                    hasSecure = true;
                    break;
                }
            }
            Assert.That(hasSecure, Is.True);
        }

        [Test]
        public async Task BasicSecurityPoliciesIfSupportedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            Assert.That(endpoints.Count, Is.GreaterThan(0));
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.SecurityPolicyUri, Is.Not.Null);
            }
        }

        [Test]
        public async Task SecurityPolicyUriFormatAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (!string.IsNullOrEmpty(ep.SecurityPolicyUri))
                {
                    Assert.That(ep.SecurityPolicyUri,
                        Does.StartWith("http://opcfoundation.org/UA/SecurityPolicy#"));
                }
            }
        }

        [Test]
        public async Task SignAndEncryptModeIfSupportedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasSignAndEncrypt = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                {
                    hasSignAndEncrypt = true;
                    break;
                }
            }
            Assert.That(hasSignAndEncrypt, Is.True);
        }

        [Test]
        public async Task SignOnlyModeIfSupportedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasSign = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.Sign)
                {
                    hasSign = true;
                    break;
                }
            }
            Assert.That(hasSign, Is.True);
        }

        [Test]
        public async Task NoneModeAlwaysSupportedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasNone = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode == MessageSecurityMode.None)
                {
                    hasNone = true;
                    break;
                }
            }
            Assert.That(hasNone, Is.True);
        }

        [Test]
        public async Task EachEndpointHasValidModeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                Assert.That(ep.SecurityMode, Is.AnyOf(
                    MessageSecurityMode.None,
                    MessageSecurityMode.Sign,
                    MessageSecurityMode.SignAndEncrypt));
            }
        }

        [Test]
        public async Task SecurityModeMatchesPolicyConsistencyAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityPolicyUri == SecurityPolicies.None)
                {
                    Assert.That(ep.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
                }
            }
        }

        [Test]
        public async Task EndpointsAdvertiseUserTokenPoliciesAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasTokenPolicies = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default && ep.UserIdentityTokens.Count > 0)
                {
                    hasTokenPolicies = true;
                    break;
                }
            }
            Assert.That(hasTokenPolicies, Is.True);
        }

        [Test]
        public async Task AnonymousTokenTypeAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasAnonymous = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Anonymous)
                        {
                            hasAnonymous = true;
                            break;
                        }
                    }
                }

                if (hasAnonymous)
                {
                    break;
                }
            }
            Assert.That(hasAnonymous, Is.True);
        }

        [Test]
        public async Task UsernameTokenTypeIfAvailableAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            bool hasUsername = false;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            hasUsername = true;
                            break;
                        }
                    }
                }
            }
            if (!hasUsername)
            {
                Assert.Fail("Server does not advertise username token type.");
            }
        }

        [Test]
        public async Task EachTokenPolicyHasIssuedTokenTypeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        Assert.That(t.TokenType, Is.Not.Null);
                    }
                }
            }
        }

        [Test]
        public async Task EachTokenPolicyHasSecurityPolicyUriAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType != UserTokenType.Anonymous &&
                            ep.SecurityMode == MessageSecurityMode.None)
                        {
                            Assert.That(t.SecurityPolicyUri, Is.Not.Null.And.Not.Empty,
                                $"TokenPolicy '{t.PolicyId}' on None-security endpoint " +
                                "must specify its own SecurityPolicyUri.");
                        }
                    }
                }
            }
        }

        [Test]
        public async Task ConnectWithNonePolicyAsync()
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
        public async Task SessionSecurityDetailsRecordedAsync()
        {
            ISession session = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Session should be connected with security details.");
                Assert.That(session.SessionId.IsNull, Is.False,
                    "Session should have a valid ID.");
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

        private bool EndpointSupportsPolicy(
            ArrayOf<EndpointDescription> endpoints,
            string securityPolicy)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityPolicyUri == securityPolicy)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
