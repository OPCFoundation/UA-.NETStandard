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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Length of the serverNonce returned by CreateSession and ActivateSession.
    /// </summary>
    /// <remarks>
    /// CTT Interoperability CreateSession with SecurityPolicy Basic128Rsa15:
    /// the Server returned a 16 byte nonce. OPC 10000-4 5.7.2.2 (CreateSession)
    /// and 5.7.3.2 (ActivateSession) require the serverNonce to "have a length
    /// between 32 and 128 bytes inclusive", and the Client shall check it.
    /// The SecureChannel nonce is a different value whose length is the
    /// SecureChannelNonceLength of the SecurityPolicy (OPC 10000-4 5.6.2.2);
    /// for Basic128Rsa15 that is 16 bytes and it is not changed.
    /// </remarks>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SessionNonceLengthTests
    {
        private const int kMinSessionNonceLength = 32;
        private const int kMaxSessionNonceLength = 128;

        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>();
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [TestCase(SecurityPolicies.Basic128Rsa15, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.Basic128Rsa15, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.Basic256, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt)]
        [TestCase(SecurityPolicies.None, MessageSecurityMode.None)]
        public async Task CreateAndActivateSessionReturnServerNonceOfAtLeast32BytesAsync(
            string securityPolicyUri,
            MessageSecurityMode securityMode)
        {
            EndpointDescription endpoint = m_server.GetEndpoints().First(e =>
                e.TransportProfileUri.Equals(Profiles.UaTcpTransport, StringComparison.Ordinal));
            endpoint.SecurityPolicyUri = securityPolicyUri;
            endpoint.SecurityMode = securityMode;

            var secureChannelContext = new SecureChannelContext(
                TestContext.CurrentContext.Test.Name,
                endpoint,
                RequestEncoding.Binary);
            var requestHeader = new RequestHeader { Timestamp = DateTime.UtcNow };

            CreateSessionResponse createResponse = await m_server.CreateSessionAsync(
                secureChannelContext,
                requestHeader,
                null,
                null,
                null,
                TestContext.CurrentContext.Test.Name,
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);

            requestHeader.AuthenticationToken = createResponse.AuthenticationToken;
            try
            {
                AssertSessionNonceLength(createResponse.ServerNonce, "CreateSession");

                requestHeader.Timestamp = DateTime.UtcNow;
                ActivateSessionResponse activateResponse = await m_server.ActivateSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    createResponse.ServerSignature,
                    [],
                    [],
                    default,
                    null,
                    CancellationToken.None).ConfigureAwait(false);

                AssertSessionNonceLength(activateResponse.ServerNonce, "ActivateSession");
                Assert.IsFalse(
                    Utils.IsEqual(createResponse.ServerNonce, activateResponse.ServerNonce),
                    "ActivateSession must return a new serverNonce.");
            }
            finally
            {
                requestHeader.Timestamp = DateTime.UtcNow;
                await m_server.CloseSessionAsync(
                    secureChannelContext,
                    requestHeader,
                    true,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static void AssertSessionNonceLength(byte[] serverNonce, string service)
        {
            Assert.NotNull(serverNonce, $"{service} returned no serverNonce.");
            NUnit.Framework.Assert.That(
                serverNonce.Length,
                Is.InRange(kMinSessionNonceLength, kMaxSessionNonceLength),
                $"{service} serverNonce length (OPC 10000-4 5.7.2.2 / 5.7.3.2).");
        }
    }
}
