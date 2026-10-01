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

#nullable enable

using System;
using System.Reflection;
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
    /// Regression tests for the identity token and signature checks that
    /// ActivateSession runs in <see cref="ServerSession.ValidateBeforeActivateAsync"/>
    /// and for the security diagnostics it records (OPC 10000-4 5.7.3, OPC 10000-5 12.12).
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Parallelizable]
    public sealed class ActivateSessionValidationRegressionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            m_serverMock.Setup(s => s.SessionManager).Returns(new Mock<ISessionManager>().Object);
            m_serverCertificate = CertificateBuilder.Create("CN=ActivateSessionValidationServer").CreateForRSA();
        }

        [TearDown]
        public void TearDown()
        {
            m_serverCertificate?.Dispose();
        }

        /// <summary>
        /// AS-4: an AnonymousIdentityToken that names a policy must conform to an
        /// anonymous UserTokenPolicy of the endpoint (Part 4 7.40.3); an unknown or
        /// non-anonymous PolicyId is Bad_IdentityTokenInvalid. Only a token without
        /// PolicyId falls back to the first anonymous policy.
        /// </summary>
        [TestCase("does-not-exist", null)]
        [TestCase("user", null)]
        [TestCase("anon-2", "anon-2")]
        [TestCase("", "anon-1")]
        [TestCase(null, "anon-1")]
        public async Task AnonymousTokenPolicyIdMustNameAnAnonymousPolicyAsync(
            string? policyId,
            string? expectedPolicyId)
        {
            EndpointDescription endpoint = CreateEndpoint(
                tokens:
                [
                    new UserTokenPolicy { PolicyId = "anon-1", TokenType = UserTokenType.Anonymous },
                    new UserTokenPolicy
                    {
                        PolicyId = "user",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.None
                    },
                    new UserTokenPolicy { PolicyId = "anon-2", TokenType = UserTokenType.Anonymous }
                ]);
            using ServerSession session = CreateSession(endpoint);
            using OperationContext context = CreateContext(endpoint);
            var token = new ExtensionObject(new AnonymousIdentityToken { PolicyId = policyId! });

            if (expectedPolicyId == null)
            {
                ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await session.ValidateBeforeActivateAsync(
                        context, new SignatureData(), token, new SignatureData(),
                        CancellationToken.None).ConfigureAwait(false));
                Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
                return;
            }

            (IUserIdentityTokenHandler handler, UserTokenPolicy? policy) =
                await session.ValidateBeforeActivateAsync(
                    context, new SignatureData(), token, new SignatureData(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(handler.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(policy!.PolicyId, Is.EqualTo(expectedPolicyId));
        }

        /// <summary>
        /// AS-6: ClientUserIdOfSession names the user authenticated when the session
        /// was created (Part 5 12.12); a later identity change only extends
        /// ClientUserIdHistory.
        /// </summary>
        [Test]
        public async Task ClientUserIdOfSessionKeepsTheFirstActivatedUserAsync()
        {
            EndpointDescription endpoint = CreateEndpoint(
                tokens:
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "user",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]);
            using ServerSession session = CreateSession(endpoint);
            using OperationContext context = CreateContext(endpoint);

            await ActivateAsUserAsync(session, context, "alice").ConfigureAwait(false);
            await ActivateAsUserAsync(session, context, "bob").ConfigureAwait(false);

            SessionSecurityDiagnosticsDataType diagnostics = GetSecurityDiagnostics(session);
            Assert.That(diagnostics.ClientUserIdOfSession, Is.EqualTo("alice"));
            Assert.That((string[])[.. diagnostics.ClientUserIdHistory], Is.EqualTo(s_aliceThenBob));
        }

        private static async Task ActivateAsUserAsync(
            ServerSession session,
            OperationContext context,
            string userName)
        {
            var token = new UserNameIdentityToken
            {
                PolicyId = "user",
                UserName = userName,
                Password = new ByteString(new byte[] { 1, 2, 3 })
            };
            (IUserIdentityTokenHandler handler, UserTokenPolicy? _) =
                await session.ValidateBeforeActivateAsync(
                    context, new SignatureData(), new ExtensionObject(token), new SignatureData(),
                    CancellationToken.None).ConfigureAwait(false);
            var identity = new UserIdentity(handler);
            session.Activate(
                context,
                handler,
                identity,
                identity,
                default,
                Nonce.CreateNonce(SecurityPolicies.None));
        }

        private static SessionSecurityDiagnosticsDataType GetSecurityDiagnostics(ServerSession session)
        {
            return (SessionSecurityDiagnosticsDataType)typeof(ServerSession)
                .GetField("m_securityDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(session)!;
        }

        private static EndpointDescription CreateEndpoint(
            string securityPolicyUri = SecurityPolicies.None,
            MessageSecurityMode securityMode = MessageSecurityMode.None,
            UserTokenPolicy[]? tokens = null)
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840/ActivateSessionValidation",
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                UserIdentityTokens = tokens != null ? new ArrayOf<UserTokenPolicy>(tokens) : default
            };
        }

        private static OperationContext CreateContext(EndpointDescription endpoint)
        {
            var channelContext = new SecureChannelContext("channel-1", endpoint, RequestEncoding.Binary);
            return new OperationContext(
                new RequestHeader(), channelContext, RequestType.ActivateSession, RequestLifetime.None);
        }

        private ServerSession CreateSession(
            EndpointDescription endpoint,
            Certificate? clientCertificate = null)
        {
            using OperationContext context = CreateContext(endpoint);
            return new ServerSession(
                context,
                m_serverMock.Object,
                m_serverCertificate,
                new NodeId(Guid.NewGuid()),
                ByteString.Empty,
                Nonce.CreateNonce(SecurityPolicies.None),
                "ActivateSessionValidation",
                new ApplicationDescription { ApplicationUri = "urn:activate-session-validation:client" },
                endpoint.EndpointUrl!,
                clientCertificate?.AddRef()!,
                [],
                60_000,
                10,
                10);
        }

        private static readonly string[] s_aliceThenBob = ["alice", "bob"];
        private ITelemetryContext m_telemetry = null!;
        private Mock<IServerInternal> m_serverMock = null!;
        private Certificate m_serverCertificate = null!;
    }
}
