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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Issue #4627 RS-8: a session restored on another replica of a non-transparent
    /// redundant server set accepts a client signature over the certificate of the server
    /// that created the session, since every replica has its own ApplicationUri and
    /// certificate (OPC 10000-4 6.6.2.4.1), as well as one over its own certificate.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public class SessionOriginalServerCertificateTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        private ITelemetryContext m_telemetry = null!;
        private Mock<IServerInternal> m_serverMock = null!;
        private Certificate m_localServerCertificate = null!;
        private Certificate m_originalServerCertificate = null!;
        private Certificate m_clientCertificate = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_localServerCertificate = s_factory.CreateCertificate("CN=StandbyReplica").CreateForRSA();
            m_originalServerCertificate = s_factory.CreateCertificate("CN=ActiveReplica").CreateForRSA();
            m_clientCertificate = s_factory.CreateCertificate("CN=FailoverClient").CreateForRSA();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_localServerCertificate?.Dispose();
            m_originalServerCertificate?.Dispose();
            m_clientCertificate?.Dispose();
        }

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_serverMock.Setup(s => s.SessionManager).Returns(new Mock<ISessionManager>().Object);
        }

        [Test]
        public async Task SignatureOverOriginalServerCertificateIsAcceptedForRestoredSessionAsync()
        {
            using ServerSession session = CreateSession(out byte[] serverNonce, out OperationContext context);
            session.OriginalServerCertificate = m_originalServerCertificate.RawData.ToByteString();

            SignatureData signature = Sign(m_originalServerCertificate, serverNonce);

            (IUserIdentityTokenHandler handler, _) = await session.ValidateBeforeActivateAsync(
                context, signature, default, new SignatureData(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(handler.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public async Task SignatureOverOwnServerCertificateIsStillAcceptedForRestoredSessionAsync()
        {
            using ServerSession session = CreateSession(out byte[] serverNonce, out OperationContext context);
            session.OriginalServerCertificate = m_originalServerCertificate.RawData.ToByteString();

            SignatureData signature = Sign(m_localServerCertificate, serverNonce);

            (IUserIdentityTokenHandler handler, _) = await session.ValidateBeforeActivateAsync(
                context, signature, default, new SignatureData(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(handler.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void SignatureOverAnotherServerCertificateIsRejectedWithoutOriginalCertificate()
        {
            using ServerSession session = CreateSession(out byte[] serverNonce, out OperationContext context);

            SignatureData signature = Sign(m_originalServerCertificate, serverNonce);

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.ValidateBeforeActivateAsync(
                    context, signature, default, new SignatureData(), CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadApplicationSignatureInvalid));
        }

        private ServerSession CreateSession(out byte[] serverNonce, out OperationContext context)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/Standby",
                SecurityMode = MessageSecurityMode.Sign,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>(new[]
                {
                    new UserTokenPolicy { PolicyId = "anon", TokenType = UserTokenType.Anonymous }
                })
            };
            context = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext("channel-1", endpoint, RequestEncoding.Binary),
                RequestType.ActivateSession,
                RequestLifetime.None);

            serverNonce = Nonce.CreateRandomNonceData(32);
            return new ServerSession(
                context,
                m_serverMock.Object,
                m_localServerCertificate,
                new NodeId(Guid.NewGuid()),
                ByteString.Empty,
                Nonce.CreateNonce(SecurityPolicies.None, serverNonce),
                "RestoredSession",
                new ApplicationDescription { ApplicationUri = "urn:failover:client" },
                endpoint.EndpointUrl,
                m_clientCertificate.AddRef(),
                [],
                60_000,
                10,
                10);
        }

        private SignatureData Sign(Certificate serverCertificate, byte[] serverNonce)
        {
            SecurityPolicyInfo policy = SecurityPolicies.Default.GetInfo(SecurityPolicies.Basic256Sha256)!;
            byte[] dataToSign = policy.GetClientSignatureData(
                null,
                serverNonce,
                serverCertificate.RawData,
                null,
                null,
                []);
            return SecurityPolicies.Default.CreateSignatureData(policy, m_clientCertificate, dataToSign);
        }
    }
}
