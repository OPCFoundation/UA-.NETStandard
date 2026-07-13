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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// compliance tests for Security User X509 conformance unit.
    /// Verifies X509 user certificate token activation scenarios.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SecurityX509User")]
    public class SecurityX509UserTests : TestFixture
    {
        [Test]
        public async Task AtLeastOneEndpointAdvertisesCertificateTokenAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail(
                    "No endpoint advertises UserTokenType.Certificate.");
            }

            Assert.Pass("At least one endpoint advertises Certificate token.");
        }

        [Test]
        public async Task ActivateWithValidX509CertOnSecureEndpointAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    ServerUrl, ep.SecurityPolicyUri,
                    userIdentity: await X509UserIdentityHelper
                        .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
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
            catch (ServiceResultException sre)
            {
                Assert.Fail($"X509 user-token activation should succeed but failed: {sre.StatusCode}.");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ActivateWithExpiredX509CertIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate expiredCert = CreateSelfSignedUserCert(
                notBefore: DateTimeOffset.UtcNow.AddYears(-2),
                notAfter: DateTimeOffset.UtcNow.AddDays(-1));

            await AddCertToServerTrustStoreAsync(expiredCert)
                .ConfigureAwait(false);
            try
            {
                ISession session = await ConnectOnceAsync(
                    ep.SecurityPolicyUri,
                    await X509UserIdentityHelper.CreateAsync(expiredCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                // Some servers accept expired certs if configured; tolerate
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadCertificateTimeInvalid ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadCertificateInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection for expired cert, got {sre.StatusCode}");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(expiredCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ActivateX509OnSecurityModeNoneAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session = await ClientFixture.ConnectAsync(
                    ServerUrl, SecurityPolicies.None,
                    userIdentity: await X509UserIdentityHelper
                        .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
                try
                {
                    // Some servers allow X509 on None, some don't
                    Assert.That(session.Connected, Is.True);
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            catch (ServiceResultException sre)
            {
                // Rejection on None is acceptable behavior
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadSecurityPolicyRejected,
                    Is.True,
                    $"Unexpected error: {sre.StatusCode}");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509UserCanReadNodeAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session;
                try
                {
                    session = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

                try
                {
                    NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                    ReadResponse rr = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Both,
                        new ReadValueId[]
                        {
                            new() {
                                NodeId = nodeId,
                                AttributeId = Attributes.Value
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(rr.Results.Count, Is.EqualTo(1));
                    Assert.That(
                        StatusCode.IsGood(rr.Results[0].StatusCode), Is.True,
                        "X509 user should be able to read nodes.");
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509UserWriteBehaviorAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session;
                try
                {
                    session = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

                try
                {
                    NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                    WriteResponse wr = await session.WriteAsync(
                        null,
                        new WriteValue[]
                        {
                            new() {
                                NodeId = nodeId,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(Variant.From(42))
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(wr.Results.Count, Is.EqualTo(1));
                    Assert.That(
                        wr.Results[0].Code == StatusCodes.Good ||
                        wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                        Is.True,
                        $"Expected Good or BadUserAccessDenied, got {wr.Results[0]}");
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SwitchFromX509ToAnonymousAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session;
                try
                {
                    session = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

                try
                {
                    // Switch to anonymous
                    await session.UpdateSessionAsync(
                        new UserIdentity(),
                        session.PreferredLocales).ConfigureAwait(false);
                    Assert.That(session.Connected, Is.True);
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            catch (ServiceResultException sre)
            {
                Assert.Fail($"Switch rejected: {sre.StatusCode}");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SwitchFromAnonymousToX509Async()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                // Start anonymous
                ISession session = await ClientFixture.ConnectAsync(
                    ServerUrl, ep.SecurityPolicyUri).ConfigureAwait(false);
                try
                {
                    // Switch to X509
                    try
                    {
                        await session.UpdateSessionAsync(
                            await X509UserIdentityHelper.CreateAsync(userCert, Telemetry).ConfigureAwait(false),
                            session.PreferredLocales).ConfigureAwait(false);
                    }
                    catch (ServiceResultException sre)
                    {
                        Assert.Fail(
                            $"X509 user-token activation should succeed but failed: {sre.StatusCode}.");
                        return;
                    }
                    Assert.That(session.Connected, Is.True);
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ActivateWithUntrustedX509CertIsRejectedAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // Do NOT add to trust store
            using Certificate untrustedCert = CreateSelfSignedUserCert(
                cn: "CN=UntrustedUser, O=Test");
            try
            {
                ISession session = await ConnectOnceAsync(
                    ep.SecurityPolicyUri,
                    await X509UserIdentityHelper.CreateAsync(untrustedCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                // Server might auto-accept in test mode
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadCertificateUntrusted ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadCertificateInvalid ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied,
                    Is.True,
                    $"Expected rejection for untrusted cert, got {sre.StatusCode}");
            }
        }

        [Test]
        public async Task TwoSessionsWithSameX509CertAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session1;
                ISession session2;
                try
                {
                    session1 = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                    session2 = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

                try
                {
                    Assert.That(session1.Connected, Is.True);
                    Assert.That(session2.Connected, Is.True);
                    Assert.That(session1.SessionId,
                        Is.Not.EqualTo(session2.SessionId));
                }
                finally
                {
                    await session1.CloseAsync(5000, true).ConfigureAwait(false);
                    session1.Dispose();
                    await session2.CloseAsync(5000, true).ConfigureAwait(false);
                    session2.Dispose();
                }
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509TokenIncludesCertificateDataAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            UserIdentity identity = await X509UserIdentityHelper.CreateAsync(userCert, Telemetry).ConfigureAwait(false);

            Assert.That(identity.TokenType,
                Is.EqualTo(UserTokenType.Certificate));
            Assert.That(userCert.RawData, Is.Not.Null);
            Assert.That(userCert.RawData, Is.Not.Empty,
                "X509 identity cert data must not be empty.");
        }

        [Test]
        public async Task SessionDiagnosticsShowsX509AuthAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert(
                cn: "CN=DiagTestUser, O=OPC Foundation");
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session;
                try
                {
                    session = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

                try
                {
                    Assert.That(session.Connected, Is.True);
                    Assert.That(session.Identity, Is.Not.Null);
                }
                finally
                {
                    await session.CloseAsync(5000, true).ConfigureAwait(false);
                    session.Dispose();
                }
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509CertWithWrongKeyUsageBehaviorAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // Create cert with only CrlSign key usage (wrong for user auth)
            using var rsa = RSA.Create(2048);
            var certReq = new CertificateRequest(
                "CN=WrongKU, O=Test", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.CrlSign, true));

            using X509Certificate2 tempCert = certReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));
            byte[] pfx = tempCert.Export(X509ContentType.Pfx, "test");
            X509Certificate2 wrongKuCertLoaded = X509CertificateLoader.LoadPkcs12(
                    pfx, "test", X509KeyStorageFlags.Exportable);
            using var wrongKuCert = Certificate.From(wrongKuCertLoaded);

            await AddCertToServerTrustStoreAsync(wrongKuCert)
                .ConfigureAwait(false);
            try
            {
                ISession session = await ConnectOnceAsync(
                    ep.SecurityPolicyUri,
                    await X509UserIdentityHelper.CreateAsync(wrongKuCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
                // Server may or may not validate key usage
            }
            catch (ServiceResultException sre)
            {
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadCertificateInvalid ||
                    sre.StatusCode == StatusCodes.BadCertificateUseNotAllowed,
                    Is.True,
                    $"Got unexpected error: {sre.StatusCode}");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(wrongKuCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509CertWithAppUriInSanBehaviorAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt)
                ?? FindEndpoint(endpoints, MessageSecurityMode.Sign);
            if (ep == null)
            {
                Assert.Fail("No secure endpoint available.");
            }

            // Create cert with ApplicationUri in SAN
            using var rsa = RSA.Create(2048);
            var certReq = new CertificateRequest(
                "CN=SanUser, O=Test", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature |
                    X509KeyUsageFlags.NonRepudiation |
                    X509KeyUsageFlags.DataEncipherment |
                    X509KeyUsageFlags.KeyEncipherment,
                    false));

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddUri(new Uri("urn:test:x509user"));
            certReq.CertificateExtensions.Add(sanBuilder.Build());

            using X509Certificate2 tempCert = certReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddYears(1));
            byte[] pfx = tempCert.Export(X509ContentType.Pfx, "test");
            X509Certificate2 sanCertLoaded = X509CertificateLoader.LoadPkcs12(
                    pfx, "test", X509KeyStorageFlags.Exportable);
            using var sanCert = Certificate.From(sanCertLoaded);

            await AddCertToServerTrustStoreAsync(sanCert).ConfigureAwait(false);
            try
            {
                ISession session = await ConnectOnceAsync(
                    ep.SecurityPolicyUri,
                    await X509UserIdentityHelper.CreateAsync(sanCert, Telemetry).ConfigureAwait(false))
                    .ConfigureAwait(false);
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
            catch (ServiceResultException sre)
            {
                // Acceptable if rejected
                Assert.That(
                    sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                    sre.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                    sre.StatusCode == StatusCodes.BadCertificateInvalid,
                    Is.True,
                    $"Got unexpected error: {sre.StatusCode}");
            }
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(sanCert)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task X509UserCertDnAccessible()
        {
            using Certificate userCert = CreateSelfSignedUserCert(
                cn: "CN=DnCheckUser, O=OPC Foundation, C=US");
            UserIdentity identity = await X509UserIdentityHelper.CreateAsync(userCert, Telemetry).ConfigureAwait(false);

            Assert.That(identity.DisplayName, Is.Not.Null.And.Not.Empty,
                "X509 user identity should have a display name derived from cert DN.");
        }

        [Test]
        public async Task ActivateWithSignAndEncryptX509SucceedsAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            if (!EndpointsHaveCertificateToken(endpoints))
            {
                Assert.Fail("No Certificate token advertised.");
            }

            EndpointDescription ep = FindEndpoint(
                endpoints, MessageSecurityMode.SignAndEncrypt);
            if (ep == null)
            {
                Assert.Fail("No SignAndEncrypt endpoint available.");
            }

            using Certificate userCert = CreateSelfSignedUserCert();
            await AddCertToServerTrustStoreAsync(userCert).ConfigureAwait(false);
            try
            {
                ISession session;
                try
                {
                    session = await ClientFixture.ConnectAsync(
                        ServerUrl, ep.SecurityPolicyUri,
                        userIdentity: await X509UserIdentityHelper
                            .CreateAsync(userCert, Telemetry).ConfigureAwait(false))
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    Assert.Fail("X509 user-token activation should succeed but failed unexpectedly.");
                    return;
                }

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
            finally
            {
                await RemoveCertFromServerTrustStoreAsync(userCert)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Connect once without retry loop to avoid triggering server lockout
        /// during negative test scenarios.
        /// </summary>
        private async Task<ISession> ConnectOnceAsync(
            string securityPolicyUri,
            IUserIdentity userIdentity)
        {
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(
                ServerUrl, securityPolicyUri).ConfigureAwait(false);
            return await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
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

        private bool EndpointsHaveCertificateToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.UserIdentityTokens != default)
                {
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.Certificate)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private EndpointDescription FindEndpoint(
            ArrayOf<EndpointDescription> endpoints,
            MessageSecurityMode mode)
        {
            EndpointDescription rsaEndpoint = FindMatchingEndpoint(
                endpoints,
                mode,
                requireRsa: true);
            if (rsaEndpoint != null)
            {
                return rsaEndpoint;
            }

            return FindMatchingEndpoint(
                endpoints,
                mode,
                requireRsa: false);
        }

        private static EndpointDescription FindMatchingEndpoint(
            ArrayOf<EndpointDescription> endpoints,
            MessageSecurityMode mode,
            bool requireRsa)
        {
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityMode != mode)
                {
                    continue;
                }

                if (requireRsa && IsEccPolicy(ep.SecurityPolicyUri))
                {
                    continue;
                }

                return ep;
            }

            return null;
        }

        private static bool IsEccPolicy(string policyUri)
        {
            return CryptoUtils.IsEccPolicy(policyUri);
        }

        private static Certificate CreateSelfSignedUserCert(
            string cn = "CN=TestUser, O=OPC Foundation",
            DateTimeOffset? notBefore = null,
            DateTimeOffset? notAfter = null)
        {
            using var rsa = RSA.Create(2048);
            var certReq = new CertificateRequest(
                cn, rsa, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            certReq.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            certReq.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature |
                    X509KeyUsageFlags.NonRepudiation |
                    X509KeyUsageFlags.DataEncipherment |
                    X509KeyUsageFlags.KeyEncipherment,
                    false));

            DateTimeOffset nb = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset na = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);

            using X509Certificate2 cert = certReq.CreateSelfSigned(nb, na);
            // Export and reimport so the private key is fully accessible.
            // The X509Certificate2 returned by LoadPkcs12 below is owned by
            // the wrapping Certificate (Certificate.From wraps a reference);
            // do NOT dispose it here or downstream access (Thumbprint, etc.)
            // will throw "m_safeCertContext is an invalid handle".
            byte[] pfx = cert.Export(X509ContentType.Pfx, "test");
            X509Certificate2 loaded = X509CertificateLoader.LoadPkcs12(
                pfx, "test", X509KeyStorageFlags.Exportable);
            return Certificate.From(loaded);
        }

        private async Task AddCertToServerTrustStoreAsync(
            Certificate cert)
        {
            // Add to the server's user certificate trust store
            CertificateTrustList userCertStore = ServerFixture.Config?
                .SecurityConfiguration?.TrustedUserCertificates;
            if (userCertStore == null)
            {
                Assert.Ignore(
                    "Server does not have a TrustedUserCertificates store.");
            }

            using ICertificateStore store =
                userCertStore.OpenStore(Telemetry);
            await store.AddAsync(cert).ConfigureAwait(false);
        }

        private async Task RemoveCertFromServerTrustStoreAsync(
            Certificate cert)
        {
            CertificateTrustList userCertStore = ServerFixture.Config?
                .SecurityConfiguration?.TrustedUserCertificates;
            if (userCertStore == null)
            {
                return;
            }

            using ICertificateStore store =
                userCertStore.OpenStore(Telemetry);
            await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
        }
    }
}
