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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies certificate validation and transport binding when creating secured and unsecured sessions.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [Category("Integration")]
    public sealed class CreateSessionCertificateRegressionTests
    {
        /// <summary>
        /// Starts an isolated server with trusted, untrusted, expired, and issuer-signed client certificates.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_trusted = CreateCertificate("CN=Trusted Session Client");
            m_otherTrusted = CreateCertificate("CN=Other Trusted Session Client");
            m_untrusted = CreateCertificate("CN=Untrusted Session Client");
            m_expired = CreateCertificate("CN=Expired Session Client", expired: true);
            m_root = CertificateBuilder.Create("CN=Session Client Root")
                .SetNotBefore(s_notBefore).SetNotAfter(s_notAfter).SetCAConstraint(-1)
                .SetRSAKeySize(2048).CreateForRSA();
            m_issued = CreateCertificate("CN=Issued Session Client", issuer: m_root);
            m_pkiRoot = Path.Combine(Path.GetTempPath(), "session-cert-" + Guid.NewGuid().ToString("N"));
            var sessionManagers = new Mock<ISessionManagerFactory>(MockBehavior.Strict);
            sessionManagers.Setup(factory => factory.Create(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<TimeProvider>(), It.IsAny<Func<string, Certificate>>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, TimeProvider timeProvider,
                    Func<string, Certificate> certificateProvider) =>
                {
                    m_restoredSessionCertificateProvider = certificateProvider;
                    return new SessionManager(server, configuration, timeProvider);
                });
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry)
            {
                SessionManagerFactory = sessionManagers.Object
            })
            {
                SecurityNone = true,
                AutoAccept = false
            };
            await m_fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_fixture.Config.SecurityConfiguration.ApplicationCertificates =
                m_fixture.Config.SecurityConfiguration.ApplicationCertificates.ToArray()
                    .OrderBy(identifier => CertificateIdentifier.IsRsaCertificateType(identifier.CertificateType))
                    .ToArrayOf();
            m_fixture.Config.ServerConfiguration.UserTokenPolicies += new UserTokenPolicy(UserTokenType.UserName);
            m_fixture.Config.SecurityConfiguration.RejectUnknownRevocationStatus = false;
            foreach (Certificate certificate in new[] { m_trusted, m_otherTrusted, m_expired, m_root })
            {
                m_fixture.Config.SecurityConfiguration.AddTrustedPeer(certificate.RawData);
            }
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server, releases test certificates, and removes its temporary PKI stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
            m_issued.Dispose();
            m_root.Dispose();
            m_expired.Dispose();
            m_untrusted.Dispose();
            m_otherTrusted.Dispose();
            m_trusted.Dispose();
            if (Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// Verifies that an empty application URI does not bypass trust or validity checks or leave a session behind.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void EmptyApplicationUriCannotBypassCertificateValidation(bool expired)
        {
            Certificate certificate = expired ? m_expired : m_untrusted;
            SecureChannelContext channel = CreateContext(certificate, Profiles.UaTcpTransport);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAndCloseAsync(channel, string.Empty, certificate.RawData.ToByteString())
                    .ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(
                expired ? StatusCodes.BadCertificateTimeInvalid : StatusCodes.BadSecurityChecksFailed));
            Assert.That(m_fixture.Server.CurrentInstance.SessionManager.GetSessions(), Is.Empty);
        }

        /// <summary>
        /// Verifies that even a trusted certificate requires the client description to contain its application URI.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("urn:wrong:application")]
        public void TrustedCertificateStillRequiresMatchingApplicationUri(string applicationUri)
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAndCloseAsync(channel, applicationUri, m_trusted.RawData.ToByteString())
                    .ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
        }

        /// <summary>
        /// Verifies that secure-conversation transports reject a session certificate different from the channel's leaf.
        /// </summary>
        [TestCase(Profiles.UaTcpTransport)]
        [TestCase(Profiles.UaWssTransport)]
        public void SecureConversationRejectsADifferentApplicationCertificate(string profile)
        {
            SecureChannelContext channel = CreateContext(m_trusted, profile);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAndCloseAsync(channel, kApplicationUri, m_otherTrusted.RawData.ToByteString())
                    .ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// Verifies that a secured session cannot omit its application certificate.
        /// </summary>
        [Test]
        public void SecuredSessionRequiresTheApplicationCertificate()
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAndCloseAsync(channel, kApplicationUri, ByteString.Empty).ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// Verifies that appending an issuer chain does not break matching of the channel and session leaf
        /// certificates.
        /// </summary>
        [Test]
        public async Task SameLeafWithAnAppendedIssuerChainIsAcceptedAsync()
        {
            SecureChannelContext channel = CreateContext(m_issued, Profiles.UaTcpTransport);
            ByteString chain = m_issued.RawData.Concat(m_root.RawData).ToArray().ToByteString();
            CreateSessionResponse response = await CreateAndCloseAsync(channel, kApplicationUri, chain)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.SessionId.IsNull, Is.False);
        }

        /// <summary>
        /// Verifies that HTTPS accepts distinct trusted transport and application certificates.
        /// </summary>
        [Test]
        public async Task HttpsMayUseDistinctValidatedTransportAndApplicationCertificatesAsync()
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.HttpsBinaryTransport);
            CreateSessionResponse response = await CreateAndCloseAsync(
                channel, kApplicationUri, m_otherTrusted.RawData.ToByteString()).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that SecurityPolicy None ignores an optional certificate even when expired or malformed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NonePolicyDoesNotValidateAnOptionalApplicationCertificateAsync(bool malformed)
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport, secure: false);
            ByteString certificate = malformed ? ByteString.From([1, 2, 3]) : m_expired.RawData.ToByteString();
            CreateSessionResponse response = await CreateAndCloseAsync(channel, string.Empty, certificate)
                .ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NoneSessionUsesAdvertisedRsaKeyForEncryptedUsernameWithEccFirstAsync(bool longPassword)
        {
            using CertificateEntryCollection entries =
                m_fixture.Server.CertificateManager.SnapshotApplicationCertificates();
            Assert.That(CertificateIdentifier.IsRsaCertificateType(entries[0].CertificateType), Is.False);
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport, secure: false);
            UserTokenPolicy policy = channel.EndpointDescription.UserIdentityTokens.ToArray()
                .Single(value => value.TokenType == UserTokenType.UserName);
            Assert.That(policy.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            byte[] password = Nonce.CreateRandomNonceData(longPassword ? 80 : 24);
            var token = new UserNameIdentityTokenHandler("ephemeral-test-user", password);
            token.UpdatePolicy(policy);

            await CreateAndCloseAsync(channel, string.Empty, ByteString.Empty, async response =>
            {
                using CertificateCollection serverChain = Utils.ParseCertificateChainBlob(
                    response.ServerCertificate.ToArray(), m_fixture.Server.CurrentInstance.Telemetry);
                Assert.That(response.ServerCertificate, Is.EqualTo(channel.EndpointDescription.ServerCertificate));
                await token.EncryptAsync(
                    serverChain[0], response.ServerNonce.ToArray(), policy.SecurityPolicyUri,
                    m_fixture.Server.MessageContext).ConfigureAwait(false);
                ISession session = m_fixture.Server.CurrentInstance.SessionManager.GetSession(
                    response.AuthenticationToken);
                Assert.That(session, Is.Not.Null);
                using var context = new OperationContext(
                    new RequestHeader { AuthenticationToken = response.AuthenticationToken }, channel,
                    RequestType.ActivateSession, RequestLifetime.None);

                (IUserIdentityTokenHandler decoded, UserTokenPolicy decodedPolicy) =
                    await session.ValidateBeforeActivateAsync(
                        context, new SignatureData(), new ExtensionObject(token.Token),
                        new SignatureData(), CancellationToken.None).ConfigureAwait(false);

                Assert.That(decoded, Is.TypeOf<UserNameIdentityTokenHandler>());
                Assert.That(((UserNameIdentityTokenHandler)decoded).DecryptedPassword, Is.EqualTo(password));
                Assert.That(decodedPolicy.PolicyId, Is.EqualTo(policy.PolicyId));
            }).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoredSessionsSelectTheSameCertificateAsLiveEndpoints(bool secure)
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport, secure);
            using Certificate restored = m_restoredSessionCertificateProvider(
                channel.EndpointDescription.SecurityPolicyUri);
            using Certificate advertised = Utils.ParseCertificateBlob(
                channel.EndpointDescription.ServerCertificate, m_fixture.Server.CurrentInstance.Telemetry);

            Assert.That(restored.Thumbprint, Is.EqualTo(advertised.Thumbprint));
        }

        /// <summary>
        /// Creates a channel context using the selected transport profile, security policy, and client certificate.
        /// </summary>
        private SecureChannelContext CreateContext(Certificate channelCertificate, string profile, bool secure = true)
        {
            EndpointDescription template = m_fixture.Server.GetEndpoints().Find(endpoint =>
                endpoint.SecurityPolicyUri == (secure ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None) &&
                endpoint.SecurityMode == (secure ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None))
                ?? throw new InvalidOperationException("The expected test endpoint was not configured.");
            var endpoint = (EndpointDescription)template.Clone();
            endpoint.TransportProfileUri = profile;
            return new SecureChannelContext(
                Guid.NewGuid().ToString("N"), endpoint, RequestEncoding.Binary, channelCertificate.RawData,
                endpoint.ServerCertificate.ToArray());
        }

        /// <summary>
        /// Creates a session with the supplied application identity and closes any successfully created session.
        /// </summary>
        private async Task<CreateSessionResponse> CreateAndCloseAsync(
            SecureChannelContext channel,
            string applicationUri,
            ByteString certificate,
            Func<CreateSessionResponse, Task> verify = null)
        {
            CreateSessionResponse response = await m_fixture.Server.CreateSessionAsync(
                channel,
                new RequestHeader(),
                new ApplicationDescription
                {
                    ApplicationUri = applicationUri,
                    ApplicationName = new LocalizedText("Session certificate regression"),
                    ApplicationType = ApplicationType.Client
                },
                null,
                channel.EndpointDescription.EndpointUrl,
                "certificate-regression",
                Nonce.CreateRandomNonceData(32).ToByteString(),
                certificate,
                60000,
                0,
                RequestLifetime.None).ConfigureAwait(false);
            try
            {
                if (verify != null)
                {
                    await verify(response).ConfigureAwait(false);
                }
                return response;
            }
            finally
            {
                await m_fixture.Server.CurrentInstance.CloseSessionAsync(null, response.SessionId, true)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an RSA client certificate with the test application URI and optional expiry or issuer.
        /// </summary>
        private static Certificate CreateCertificate(string subject, bool expired = false, Certificate issuer = null)
        {
            ICertificateBuilder builder = CertificateBuilder.Create(subject)
                .SetNotBefore(s_notBefore)
                .SetNotAfter(expired ? new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc) : s_notAfter)
                .AddExtension(new X509SubjectAltNameExtension([kApplicationUri], ["localhost"]));
            if (issuer != null)
            {
                builder.SetIssuer(issuer);
            }
            return builder.SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Identifies the application embedded in valid client certificate subject alternative names.
        /// </summary>
        private const string kApplicationUri = "urn:servercore:certificate-regression";

        /// <summary>
        /// Defines the start of validity shared by the generated test certificates.
        /// </summary>
        private static readonly DateTime s_notBefore = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Defines a future expiry for certificates intended to pass time validation.
        /// </summary>
        private static readonly DateTime s_notAfter = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Hosts the server whose CreateSession service performs certificate validation.
        /// </summary>
        private ServerFixture<ReferenceServer> m_fixture;

        private Func<string, Certificate> m_restoredSessionCertificateProvider;

        /// <summary>
        /// Stores the isolated PKI directory removed after server shutdown.
        /// </summary>
        private string m_pkiRoot;

        /// <summary>
        /// Supplies the trusted client certificate normally bound to the secure channel.
        /// </summary>
        private Certificate m_trusted;

        /// <summary>
        /// Supplies a distinct trusted certificate to test transport-to-session binding.
        /// </summary>
        private Certificate m_otherTrusted;

        /// <summary>
        /// Supplies an otherwise valid certificate absent from the server's trust store.
        /// </summary>
        private Certificate m_untrusted;

        /// <summary>
        /// Supplies a trusted certificate whose validity period has ended.
        /// </summary>
        private Certificate m_expired;

        /// <summary>
        /// Supplies the trusted issuer used to test appended certificate chains.
        /// </summary>
        private Certificate m_root;

        /// <summary>
        /// Supplies the client leaf signed by the trusted root certificate.
        /// </summary>
        private Certificate m_issued;
    }
}
