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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [Category("Integration")]
    public sealed class CreateSessionCertificateRegressionTests
    {
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
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                SecurityNone = true,
                AutoAccept = false
            };
            await m_fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_fixture.Config.SecurityConfiguration.RejectUnknownRevocationStatus = false;
            foreach (Certificate certificate in new[] { m_trusted, m_otherTrusted, m_expired, m_root })
            {
                m_fixture.Config.SecurityConfiguration.AddTrustedPeer(certificate.RawData);
            }
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

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

        [Test]
        public void SecuredSessionRequiresTheApplicationCertificate()
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.UaTcpTransport);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAndCloseAsync(channel, kApplicationUri, ByteString.Empty).ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

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

        [Test]
        public async Task HttpsMayUseDistinctValidatedTransportAndApplicationCertificatesAsync()
        {
            SecureChannelContext channel = CreateContext(m_trusted, Profiles.HttpsBinaryTransport);
            CreateSessionResponse response = await CreateAndCloseAsync(
                channel, kApplicationUri, m_otherTrusted.RawData.ToByteString()).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
        }

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

        private async Task<CreateSessionResponse> CreateAndCloseAsync(
            SecureChannelContext channel,
            string applicationUri,
            ByteString certificate)
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
            await m_fixture.Server.CurrentInstance.CloseSessionAsync(null, response.SessionId, true)
                .ConfigureAwait(false);
            return response;
        }

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

        private const string kApplicationUri = "urn:servercore:certificate-regression";
        private static readonly DateTime s_notBefore = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_notAfter = new(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private ServerFixture<ReferenceServer> m_fixture;
        private string m_pkiRoot;
        private Certificate m_trusted;
        private Certificate m_otherTrusted;
        private Certificate m_untrusted;
        private Certificate m_expired;
        private Certificate m_root;
        private Certificate m_issued;
    }
}
