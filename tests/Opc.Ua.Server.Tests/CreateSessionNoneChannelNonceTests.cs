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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the client nonce sent with CreateSession on a None channel
    /// reaches the session, so an enhanced user token signature over it verifies.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class CreateSessionNoneChannelNonceTests
    {
        /// <summary>
        /// A client signs an X509 user token for an enhanced token policy on a
        /// None channel over ServerNonce | Hash(ServerCertificate) | ClientNonce.
        /// StandardServer used to drop the client nonce on a None channel, so the
        /// server verified over an empty nonce and every activation failed.
        /// </summary>
        [Test]
        public async Task EnhancedCertificateUserTokenOnNoneChannelVerifiesThroughCreateSessionAsync()
        {
            SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(SecurityPolicies.RSA_DH_AesGcm);
            if (policy == null)
            {
                Assert.Ignore("The RSA_DH_AesGcm security policy is not supported on this platform.");
            }

            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                EndpointDescription endpoint = server.GetEndpoints().Find(
                    candidate => candidate.SecurityPolicyUri == SecurityPolicies.None);
                Assert.That(endpoint, Is.Not.Null);
                endpoint = (EndpointDescription)endpoint.Clone();
                endpoint.UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "certificate",
                        TokenType = UserTokenType.Certificate,
                        SecurityPolicyUri = SecurityPolicies.RSA_DH_AesGcm
                    }
                ];
                var channel = new SecureChannelContext(
                    "none-nonce", endpoint, RequestEncoding.Binary, null, null);
                ByteString clientNonce = Nonce.CreateRandomNonceData(32).ToByteString();

                CreateSessionResponse response = await server.CreateSessionAsync(
                    channel, new RequestHeader(), null, null, null, "none-nonce",
                    clientNonce, default, 60000, 0, RequestLifetime.None).ConfigureAwait(false);

                ISession session = server.CurrentInstance.SessionManager.GetSessions()[0];
                Assert.That(session.ClientNonce, Is.EqualTo(clientNonce));

                using CertificateCollection serverChain = Utils.ParseCertificateChainBlob(
                    response.ServerCertificate, server.CurrentInstance.Telemetry);
                using Certificate userCertificate = CertificateBuilder.Create("CN=None Channel User")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                byte[] dataToSign = policy.GetUserTokenSignatureData(
                    null,
                    response.ServerNonce.ToArray(),
                    serverChain[0].RawData,
                    null,
                    userCertificate.RawData,
                    null,
                    clientNonce.ToArray(),
                    MessageSecurityMode.None);
                SignatureData userSignature = await SecurityPolicies.Default.CreateSignatureDataAsync(
                    policy,
                    userCertificate,
                    dataToSign,
                    CancellationToken.None).ConfigureAwait(false);
                var identity = new ExtensionObject(new X509IdentityToken
                {
                    PolicyId = "certificate",
                    CertificateData = userCertificate.RawData.ToByteString()
                });
                using var context = new OperationContext(
                    new RequestHeader(),
                    channel,
                    RequestType.ActivateSession,
                    RequestLifetime.None);

                (IUserIdentityTokenHandler token, UserTokenPolicy selected) =
                    await session.ValidateBeforeActivateAsync(
                        context, new SignatureData(), identity, userSignature, CancellationToken.None)
                        .ConfigureAwait(false);

                Assert.That(token.TokenType, Is.EqualTo(UserTokenType.Certificate));
                Assert.That(selected.PolicyId, Is.EqualTo("certificate"));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Part 4 5.7.2.3 (Table 16): a client nonce longer than 128 bytes is
        /// rejected with Bad_NonceInvalid on every channel, including None,
        /// instead of being dropped silently.
        /// </summary>
        [Test]
        public async Task OversizedClientNonceOnNoneChannelIsRejectedAsync()
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var channel = new SecureChannelContext(
                    "none-nonce-oversized", GetNoneEndpoint(server), RequestEncoding.Binary, null, null);
                ByteString clientNonce = Nonce.CreateRandomNonceData(129).ToByteString();

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.CreateSessionAsync(
                        channel, new RequestHeader(), null, null, null, "none-nonce-oversized",
                        clientNonce, default, 60000, 0, RequestLifetime.None).ConfigureAwait(false));
                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNonceInvalid));
                Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Is.Empty);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A None channel does not require a client nonce: an empty one is
        /// still accepted.
        /// </summary>
        [Test]
        public async Task EmptyClientNonceOnNoneChannelIsAcceptedAsync()
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var channel = new SecureChannelContext(
                    "none-nonce-empty", GetNoneEndpoint(server), RequestEncoding.Binary, null, null);

                CreateSessionResponse response = await server.CreateSessionAsync(
                    channel, new RequestHeader(), null, null, null, "none-nonce-empty",
                    default, default, 60000, 0, RequestLifetime.None).ConfigureAwait(false);

                Assert.That(response.SessionId.IsNull, Is.False);
                Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Has.Count.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static EndpointDescription GetNoneEndpoint(StandardServer server)
        {
            EndpointDescription endpoint = server.GetEndpoints().Find(
                candidate => candidate.SecurityPolicyUri == SecurityPolicies.None);
            Assert.That(endpoint, Is.Not.Null);
            return endpoint;
        }
    }
}
