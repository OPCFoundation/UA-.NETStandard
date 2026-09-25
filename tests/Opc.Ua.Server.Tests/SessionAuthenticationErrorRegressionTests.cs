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

using System.Security.Cryptography;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    [Category("Identity")]
    public sealed class SessionAuthenticationErrorRegressionTests
    {
        /// <summary>
        /// Verifies padding, plaintext length and policy errors are indistinguishable at ActivateSession.
        /// </summary>
        [TestCase("padding")]
        [TestCase("length")]
        [TestCase("algorithm")]
        public Task SessionDecryptFailuresReturnIdentityTokenInvalidAsync(string failure)
        {
            return AssertActivationRejectionAsync(
                new ServerIdentityRegistry(),
                StatusCodes.BadIdentityTokenInvalid,
                "Could not decrypt identity token.",
                failure);
        }

        [TestCaseSource(nameof(s_rejectionCodes))]
        public Task AuthenticatorRejectionPreservesStatusAndMessageAsync(StatusCode statusCode)
        {
            var error = new ServiceResult(statusCode, new LocalizedText("Authenticator rejection must survive."));
            var registry = new ServerIdentityRegistry(new UserNamePasswordAuthenticator(
                (_, _) => throw new ServiceResultException(error)));
            return AssertActivationRejectionAsync(registry, statusCode, error.LocalizedText.Text);
        }

        [Test]
        public Task UnhandledNonAnonymousTokenStillFailsClosedAsync()
        {
            return AssertActivationRejectionAsync(
                new ServerIdentityRegistry(), StatusCodes.BadIdentityTokenRejected, null);
        }

        private static async Task AssertActivationRejectionAsync(
            ServerIdentityRegistry registry,
            StatusCode expectedStatus,
            string expectedMessage,
            string decryptionFailure = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var server = new Mock<IServerInternal>();
            server.Setup(value => value.Telemetry).Returns(telemetry);
            server.Setup(value => value.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
            server.Setup(value => value.IdentityRegistry).Returns(registry);
            server.Setup(value => value.DiagnosticsNodeManager).Returns(Mock.Of<IDiagnosticsNodeManager>());
            server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 60000,
                    MaxSessionCount = 10
                }
            };
            using Certificate certificate = CertificateBuilder.Create("CN=Authentication Error Regression")
                .SetRSAKeySize(2048).CreateForRSA();
            using var manager = new SessionManager(server.Object, configuration);
            server.Setup(value => value.SessionManager).Returns(manager);
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost/authentication-error",
                SecurityPolicyUri = SecurityPolicies.None,
                SecurityMode = MessageSecurityMode.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "username",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = decryptionFailure == null
                            ? SecurityPolicies.None
                            : SecurityPolicies.Basic128Rsa15
                    }
                ]
            };
            using var context = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext("authentication-error", endpoint, RequestEncoding.Binary),
                RequestType.ActivateSession,
                RequestLifetime.None);
            CreateSessionResult created = await manager.CreateSessionAsync(
                context, certificate, "authentication-error", default, new ApplicationDescription(),
                endpoint.EndpointUrl, null, [], 60000, 64 * 1024, default).ConfigureAwait(false);
            try
            {
                var token = new UserNameIdentityToken
                {
                    PolicyId = "username",
                    UserName = "test-user",
                    Password = ByteString.From([1])
                };
                if (decryptionFailure != null)
                {
                    using RSA rsa = certificate.GetRSAPublicKey();
                    token.Password = decryptionFailure == "padding"
                        ? ByteString.From(new byte[rsa.KeySize / 8])
                        : ByteString.From(rsa.Encrypt(
                            [0xFF, 0xFF, 0xFF, 0x7F], RSAEncryptionPadding.Pkcs1));
                    token.EncryptionAlgorithm = decryptionFailure == "algorithm"
                        ? SecurityAlgorithms.RsaOaep
                        : SecurityAlgorithms.Rsa15;
                }
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.ActivateSessionAsync(
                        context, created.AuthenticationToken, new SignatureData(), new ExtensionObject(token),
                        new SignatureData(), [], default).ConfigureAwait(false));

                Assert.That(error.StatusCode, Is.EqualTo(expectedStatus));
                if (expectedMessage != null)
                {
                    Assert.That(error.Result.LocalizedText.Text, Is.EqualTo(expectedMessage));
                }
                Assert.That(created.Session.Activated, Is.False);
            }
            finally
            {
                await manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
            }
        }

        private static readonly StatusCode[] s_rejectionCodes =
        [
            StatusCodes.BadIdentityTokenInvalid,
            StatusCodes.BadUserAccessDenied,
            StatusCodes.BadIdentityTokenRejected,
            StatusCodes.BadCertificateUntrusted
        ];
    }
}
