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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the session AuthenticationToken and the user
    /// identity token decryption errors returned by ActivateSession.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SessionSecurityRegressionTests
    {
        private ServerFixture<StandardServer> m_fixture;
        private string m_pkiRoot;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            m_fixture = new ServerFixture<StandardServer>();
            await m_fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_fixture.Config.ServerConfiguration.UserTokenPolicies
                .Add(new UserTokenPolicy(UserTokenType.UserName));
            await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
            try
            {
                if (Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        /// <summary>
        /// Part 6 7.4.1: every HTTPS client of a listener shares one SecureChannelId,
        /// so the AuthenticationToken must be a random value of at least 32 bytes.
        /// A sequential token let a second client derive the token of the session
        /// created just before its own and use that session.
        /// </summary>
        [Test]
        public async Task SharedSecuredChannelGetsRandomTokenAsync()
        {
            // one channel id for both clients, as with the HTTPS ListenerId.
            SecureChannelContext sharedChannel = CreateSecuredContext("https-listener");

            NodeId victim = (await CreateSessionAsync(sharedChannel, "Victim")
                .ConfigureAwait(false)).AuthenticationToken;
            NodeId attacker = (await CreateSessionAsync(sharedChannel, "Attacker")
                .ConfigureAwait(false)).AuthenticationToken;

            foreach (NodeId token in new[] { victim, attacker })
            {
                Assert.That(token.IdType, Is.EqualTo(IdType.Opaque),
                    "The token on a shared secured channel must be a random opaque value.");
                Assert.That(((byte[])token.Identifier).Length, Is.GreaterThanOrEqualTo(32));
            }
            Assert.That(victim, Is.Not.EqualTo(attacker));
        }

        /// <summary>
        /// OPC 10000-4 7.40.2.1: a UserName token whose password fails RSA decryption
        /// returns Bad_IdentityTokenInvalid without an inner result, whether the
        /// padding is broken or conforming with an invalid length prefix. Otherwise
        /// the status (or the inner status a client requests with ReturnDiagnostics)
        /// is a padding oracle on the server key.
        /// </summary>
        [Test]
        public async Task PasswordDecryptionFailuresAreIndistinguishableAsync()
        {
            SecureChannelContext channel = CreateSecuredContext("oracle-channel");
            CreateSessionResponse created = await CreateSessionAsync(channel, "Oracle")
                .ConfigureAwait(false);

            X509Certificate2 serverCertificate = Utils.ParseCertificateChainBlob(
                channel.EndpointDescription.ServerCertificate,
                m_fixture.Server.MessageContext.Telemetry)[0];
            using RSA rsa = serverCertificate.GetRSAPublicKey();

            // conforming OAEP padding, length prefix larger than the block.
            byte[] block = new byte[16];
            BitConverter.GetBytes(0x7FFFFFFF).CopyTo(block, 0);
            byte[] badLength = rsa.Encrypt(block, RSAEncryptionPadding.OaepSHA1);

            // non conforming padding.
            byte[] badPadding = new byte[badLength.Length];
            badPadding[0] = 0x01;
            badPadding[^1] = 0x01;

            ServiceResultException lengthError = await ActivateWithPasswordAsync(
                channel, created, badLength).ConfigureAwait(false);
            ServiceResultException paddingError = await ActivateWithPasswordAsync(
                channel, created, badPadding).ConfigureAwait(false);

            foreach (ServiceResultException error in new[] { lengthError, paddingError })
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
                Assert.That(
                    error.InnerResult == null || StatusCode.IsGood(error.InnerResult.StatusCode),
                    Is.True,
                    "The cause of a token decryption failure must not reach the client.");
            }
            Assert.That(lengthError.Message, Is.EqualTo(paddingError.Message));
        }

        private SecureChannelContext CreateSecuredContext(string channelId)
        {
            EndpointDescription endpoint = m_fixture.Server.GetEndpoints().First(e =>
                e.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                e.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            return new SecureChannelContext(channelId, endpoint, RequestEncoding.Binary);
        }

        private async Task<CreateSessionResponse> CreateSessionAsync(
            SecureChannelContext context,
            string sessionName)
        {
            CreateSessionResponse response = await m_fixture.Server.CreateSessionAsync(
                context,
                new RequestHeader(),
                null,
                null,
                null,
                sessionName,
                null,
                null,
                ServerFixtureUtils.DefaultSessionTimeout,
                ServerFixtureUtils.DefaultMaxResponseMessageSize,
                CancellationToken.None).ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
            return response;
        }

        private async Task<ServiceResultException> ActivateWithPasswordAsync(
            SecureChannelContext context,
            CreateSessionResponse created,
            byte[] password)
        {
            UserTokenPolicy policy = context.EndpointDescription.UserIdentityTokens
                .First(p => p.TokenType == UserTokenType.UserName);
            var token = new UserNameIdentityToken
            {
                PolicyId = policy.PolicyId,
                UserName = "user1",
                Password = password,
                EncryptionAlgorithm = SecurityAlgorithms.RsaOaep
            };
            var requestHeader = new RequestHeader
            {
                AuthenticationToken = created.AuthenticationToken,
                ReturnDiagnostics = (uint)DiagnosticsMasks.All
            };
            try
            {
                await m_fixture.Server.ActivateSessionAsync(
                    context,
                    requestHeader,
                    null,
                    [],
                    [],
                    new ExtensionObject(token),
                    null,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                return sre;
            }
            Assert.Fail("ActivateSession with an undecryptable password must fail.");
            return null;
        }
    }
}
