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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using OpcUa = Opc.Ua;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDSPush")]
    [Category("GDS")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class PushTest
    {
        private static readonly ICertificateFactory s_factory = new DefaultCertificateFactory();

        private static readonly HashSet<string> s_supportedPolicyUris =
        [
            .. SecurityPolicies.GetDisplayNames().Select(SecurityPolicies.GetUri)
        ];

        /// <summary>
        /// CertificateTypes to run the Test with.
        /// For ECC types, the additional fourth element is the expected curve friendly name.
        /// </summary>
        public static readonly object[] FixtureArgs =
        [
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                SecurityPolicies.Aes256_Sha256_RsaPss,
                null
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                SecurityPolicies.ECC_nistP256,
                ECCurve.NamedCurves.nistP256
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                SecurityPolicies.ECC_nistP256_AesGcm,
                ECCurve.NamedCurves.nistP256
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                SecurityPolicies.ECC_nistP256_ChaChaPoly,
                ECCurve.NamedCurves.nistP256
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                SecurityPolicies.ECC_nistP384,
                ECCurve.NamedCurves.nistP384
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                SecurityPolicies.ECC_nistP384_AesGcm,
                ECCurve.NamedCurves.nistP384
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                SecurityPolicies.ECC_nistP384_ChaChaPoly,
                ECCurve.NamedCurves.nistP384
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP256r1,
                ECCurve.NamedCurves.brainpoolP256r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
                ECCurve.NamedCurves.brainpoolP256r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly,
                ECCurve.NamedCurves.brainpoolP256r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP384r1,
                ECCurve.NamedCurves.brainpoolP384r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP384r1_AesGcm,
                ECCurve.NamedCurves.brainpoolP384r1
            },
            new object[]
            {
                nameof(OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType),
                OpcUa.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly,
                ECCurve.NamedCurves.brainpoolP384r1
            }
        ];

        public PushTest(string certificateTypeString, NodeId certificateType, string securityPolicyUri, ECCurve? curve)
        {
            if (!s_supportedPolicyUris.Contains(securityPolicyUri))
            {
                NUnit.Framework.Assert.Ignore(
                    $"Security policy {securityPolicyUri} is not supported on this runtime.");
            }

            if (!Utils.IsSupportedCertificateType(certificateType))
            {
                Assert.Ignore(
                    $"Certificate type {certificateTypeString} is not supported on this platform.");
            }

            // Skip brainpool curves on Mac OS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                (securityPolicyUri.Contains("ECC_brainpoolP256r1", StringComparison.Ordinal) ||
                    securityPolicyUri.Contains("ECC_brainpoolP384r1", StringComparison.Ordinal)))
            {
                Assert.Ignore("Brainpool curve is not supported on Mac OS.");
            }

            // If a curve name is provided, perform extra check if ecc is supported
            if (curve != null && !IsCurveSupported(curve.Value))
            {
                Assert.Ignore("ECC curve is not supported on this platform.");
            }

            m_certificateTypeString = certificateTypeString;
            m_certificateType = certificateType;
            m_securityPolicyUri = securityPolicyUri;
        }

        private static bool IsCurveSupported(ECCurve curve)
        {
            ECDsa key = null;
            try
            {
                key = ECDsa.Create(curve);
            }
            catch
            {
                return false;
            }
            finally
            {
                key?.Dispose();
            }
            return true;
        }

        /// <summary>
        /// Overrides ToString to display active certificate type in Test Explorer.
        /// </summary>
        public override string ToString()
        {
            return $"{nameof(PushTest)} [{m_certificateTypeString}]";
        }

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected async Task OneTimeSetUpAsync()
        {
            // start GDS first clean, then restart server
            // to ensure the application cert is not 'fresh'
            m_telemetry = NUnitTelemetryContext.Create();
            m_server = await TestUtils.StartGDSAsync(true, CertificateStoreType.Directory).ConfigureAwait(false);
            await m_server.StopServerAsync().ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
            m_server = await TestUtils.StartGDSAsync(false, CertificateStoreType.Directory).ConfigureAwait(false);

            m_randomSource = new RandomSource(kRandomStart);

            // load clients
            m_gdsClient = new GlobalDiscoveryTestClient(true, m_telemetry);
            await m_gdsClient.LoadClientConfigurationAsync(m_server.BasePort).ConfigureAwait(false);
            m_pushClient = new ServerConfigurationPushTestClient(true, m_telemetry);
            await m_pushClient.LoadClientConfigurationAsync(m_server.BasePort)
                .ConfigureAwait(false);

            // connect once
            await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl)
                .ConfigureAwait(false);

            try
            {
                await m_pushClient.ConnectAsync(m_securityPolicyUri)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException ex) when (
                ex.Message.Contains("No endpoint found for SecurityPolicyUri", StringComparison.Ordinal))
            {
                NUnit.Framework.Assert.Ignore(
                    $"Security policy {m_securityPolicyUri} is not advertised by the GDS test server.");
            }

            await ConnectGDSClientAsync(true).ConfigureAwait(false);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await RegisterPushServerApplicationAsync(m_pushClient.PushClient.EndpointUrl, telemetry).ConfigureAwait(false);

            m_selfSignedServerCert = CertificateFactory.Create(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            m_domainNames = [.. X509Utils.GetDomainsFromCertificate(m_selfSignedServerCert)];

            await CreateCATestCertsAsync(m_pushClient.TempStorePath, telemetry).ConfigureAwait(false);
        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected async Task OneTimeTearDownAsync()
        {
            try
            {
                await ConnectGDSClientAsync(true).ConfigureAwait(false);
                await UnRegisterPushServerApplicationAsync().ConfigureAwait(false);
                await m_gdsClient.DisconnectClientAsync().ConfigureAwait(false);
                await m_pushClient.DisconnectClientAsync().ConfigureAwait(false);
                await m_server.StopServerAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                m_pushClient.Dispose();
                m_gdsClient.Dispose();
                m_selfSignedServerCert.Dispose();
                m_selfSignedServerCert = null;
                m_gdsClient = null;
                m_pushClient = null;
                m_server = null;
            }
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected async Task TearDownAsync()
        {
            await DisconnectGDSClientAsync().ConfigureAwait(false);
            await DisconnectPushClientAsync().ConfigureAwait(false);
        }

        [Test]
        [Order(100)]
        public async Task GetSupportedKeyFormatsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ArrayOf<string> keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            Assert.That(keyFormats.IsNull, Is.False);
        }

        [Test]
        [Order(200)]
        public async Task ReadTrustListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType allTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(allTrustList, Is.Not.Null);
            Assert.That(allTrustList.IssuerCertificates.IsNull, Is.False);
            Assert.That(allTrustList.IssuerCrls.IsNull, Is.False);
            Assert.That(allTrustList.TrustedCertificates.IsNull, Is.False);
            Assert.That(allTrustList.TrustedCrls.IsNull, Is.False);
            TrustListDataType noneTrustList = await m_pushClient.PushClient
                .ReadTrustListAsync(TrustListMasks.None).ConfigureAwait(false);
            Assert.That(noneTrustList, Is.Not.Null);
            Assert.That(noneTrustList.IssuerCertificates.IsNull, Is.False);
            Assert.That(noneTrustList.IssuerCrls.IsNull, Is.False);
            Assert.That(noneTrustList.TrustedCertificates.IsNull, Is.False);
            Assert.That(noneTrustList.TrustedCrls.IsNull, Is.False);
            Assert.That(noneTrustList.IssuerCertificates.Count, Is.Zero);
            Assert.That(noneTrustList.IssuerCrls.Count, Is.Zero);
            Assert.That(noneTrustList.TrustedCertificates.Count, Is.Zero);
            Assert.That(noneTrustList.TrustedCrls.Count, Is.Zero);
            TrustListDataType issuerTrustList = await m_pushClient.PushClient.ReadTrustListAsync(
                (TrustListMasks)((int)TrustListMasks.IssuerCertificates |
                    (int)TrustListMasks.IssuerCrls)).ConfigureAwait(false);
            Assert.That(issuerTrustList, Is.Not.Null);
            Assert.That(issuerTrustList.IssuerCertificates.IsNull, Is.False);
            Assert.That(issuerTrustList.IssuerCrls.IsNull, Is.False);
            Assert.That(issuerTrustList.TrustedCertificates.IsNull, Is.False);
            Assert.That(issuerTrustList.TrustedCrls.IsNull, Is.False);
            Assert.That(issuerTrustList.IssuerCertificates.Count, Is.EqualTo(allTrustList.IssuerCertificates.Count));
            Assert.That(issuerTrustList.IssuerCrls.Count, Is.EqualTo(allTrustList.IssuerCrls.Count));
            Assert.That(issuerTrustList.TrustedCertificates.Count, Is.Zero);
            Assert.That(issuerTrustList.TrustedCrls.Count, Is.Zero);
            TrustListDataType trustedTrustList = await m_pushClient.PushClient.ReadTrustListAsync(
                (TrustListMasks)((int)TrustListMasks.TrustedCertificates |
                    (int)TrustListMasks.TrustedCrls)).ConfigureAwait(false);
            Assert.That(trustedTrustList, Is.Not.Null);
            Assert.That(trustedTrustList.IssuerCertificates.IsNull, Is.False);
            Assert.That(trustedTrustList.IssuerCrls.IsNull, Is.False);
            Assert.That(trustedTrustList.TrustedCertificates.IsNull, Is.False);
            Assert.That(trustedTrustList.TrustedCrls.IsNull, Is.False);
            Assert.That(trustedTrustList.IssuerCertificates.Count, Is.Zero);
            Assert.That(trustedTrustList.IssuerCrls.Count, Is.Zero);
            Assert.That(trustedTrustList.TrustedCertificates.Count, Is.EqualTo(allTrustList.TrustedCertificates.Count));
            Assert.That(trustedTrustList.TrustedCrls.Count, Is.EqualTo(allTrustList.TrustedCrls.Count));
        }

        [Test]
        [Order(300)]
        public async Task UpdateTrustListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType fullTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            TrustListDataType emptyTrustList = await m_pushClient.PushClient
                .ReadTrustListAsync(TrustListMasks.None).ConfigureAwait(false);
            emptyTrustList.SpecifiedLists = (uint)TrustListMasks.All;
            bool requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(emptyTrustList).ConfigureAwait(false);
            Assert.That(requireReboot, Is.False);
            TrustListDataType expectEmptyTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(expectEmptyTrustList, emptyTrustList), Is.True);
            requireReboot = await m_pushClient.PushClient.UpdateTrustListAsync(fullTrustList).ConfigureAwait(false);
            Assert.That(requireReboot, Is.False);
            TrustListDataType expectFullTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(expectFullTrustList, fullTrustList), Is.True);
        }

        [Test]
        [Order(301)]
        public async Task AddRemoveCertAsync()
        {
            using Certificate trustedCert = s_factory
                .CreateApplicationCertificate("uri:x:y:z", "TrustedCert", "CN=Push Server Test")
                .CreateForRSA();
            using Certificate issuerCert = s_factory
                .CreateApplicationCertificate("uri:x:y:z", "IssuerCert", "CN=Push Server Test")
                .CreateForRSA();
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(trustedCert, true).ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(issuerCert, false).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(
                afterAddTrustList.TrustedCertificates.Count,
                Is.GreaterThan(beforeTrustList.TrustedCertificates.Count));
            Assert.That(
                afterAddTrustList.IssuerCertificates.Count,
                Is.GreaterThan(beforeTrustList.IssuerCertificates.Count));
            Assert.That(Utils.IsEqual(beforeTrustList, afterAddTrustList), Is.False);
            await m_pushClient.PushClient.RemoveCertificateAsync(trustedCert.Thumbprint, true).ConfigureAwait(false);
            await m_pushClient.PushClient.RemoveCertificateAsync(issuerCert.Thumbprint, false).ConfigureAwait(false);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(beforeTrustList, afterRemoveTrustList), Is.True);
        }

        [Test]
        [Order(302)]
        public async Task AddRemoveCATrustedCertAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(m_caCert, true).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(
                afterAddTrustList.TrustedCertificates.Count,
                Is.GreaterThan(beforeTrustList.TrustedCertificates.Count));
            Assert.That(beforeTrustList.TrustedCrls.Count, Is.EqualTo(afterAddTrustList.TrustedCrls.Count));
            Assert.That(Utils.IsEqual(beforeTrustList, afterAddTrustList), Is.False);
            ServiceResultException serviceResultException = Assert
                .ThrowsAsync<ServiceResultException>(() =>
                    m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, false).AsTask());
            Assert.That(
                serviceResultException.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument),
                serviceResultException.Message);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(beforeTrustList, afterRemoveTrustList), Is.False);
            await m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, true).ConfigureAwait(false);
            afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(beforeTrustList, afterRemoveTrustList), Is.True);
        }

        [Test]
        [Order(303)]
        public async Task AddRemoveCAIssuerCertAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            TrustListDataType beforeTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            await m_pushClient.PushClient.AddCertificateAsync(m_caCert, false).ConfigureAwait(false);
            TrustListDataType afterAddTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(
                afterAddTrustList.IssuerCertificates.Count,
                Is.GreaterThan(beforeTrustList.IssuerCertificates.Count));
            Assert.That(beforeTrustList.IssuerCrls.Count, Is.EqualTo(afterAddTrustList.IssuerCrls.Count));
            Assert.That(Utils.IsEqual(beforeTrustList, afterAddTrustList), Is.False);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, true).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            TrustListDataType afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(beforeTrustList, afterRemoveTrustList), Is.False);
            await m_pushClient.PushClient.RemoveCertificateAsync(m_caCert.Thumbprint, false).ConfigureAwait(false);
            afterRemoveTrustList = await m_pushClient.PushClient.ReadTrustListAsync().ConfigureAwait(false);
            Assert.That(Utils.IsEqual(beforeTrustList, afterRemoveTrustList), Is.True);
        }

        [Test]
        [Order(400)]
        public async Task CreateSigningRequestBadParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            var invalidCertGroup = new NodeId(333);
            var invalidCertType = new NodeId(Guid.NewGuid());
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(invalidCertGroup, default, null, false, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(default, invalidCertType, default, false, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(default, default, null, false, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .CreateSigningRequestAsync(invalidCertGroup, invalidCertType, null, false, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(401)]
        public async Task CreateSigningRequestNullParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ByteString csr = await m_pushClient.PushClient
                .CreateSigningRequestAsync(default, m_certificateType, null, false, default).ConfigureAwait(false);
            Assert.That(csr.IsEmpty, Is.False);
        }

        [Test]
        [Order(402)]
        public async Task CreateSigningRequestRsaMinNullParmsAsync()
        {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            Assert
                .Ignore("SHA1 not supported on .NET Standard 2.1 and .NET 5.0 or greater");
#endif
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.CreateSigningRequestAsync(
                        default,
                        OpcUa.ObjectTypeIds.RsaMinApplicationCertificateType,
                        null,
                        false,
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(409)]
        public async Task CreateSigningRequestAllParmsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ByteString nonce = ByteString.Empty;
            ByteString csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                string.Empty,
                false,
                nonce).ConfigureAwait(false);
            Assert.That(csr.IsEmpty, Is.False);
        }

        [Test]
        [Order(410)]
        public async Task CreateSigningRequestNullParmsWithNewPrivateKeyAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ByteString csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                default,
                m_certificateType,
                null,
                true,
                ByteString.From(Encoding.ASCII.GetBytes("OPCTest"))).ConfigureAwait(false);
            Assert.That(csr.IsEmpty, Is.False);
        }

        [Test]
        [Order(419)]
        public async Task CreateSigningRequestAllParmsWithNewPrivateKeyAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            byte[] nonce = new byte[32];
            m_randomSource.NextBytes(nonce, 0, nonce.Length);
            ByteString csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                string.Empty,
                true,
                ByteString.From(nonce)).ConfigureAwait(false);
            Assert.That(csr.IsEmpty, Is.False);
        }

        [Test]
        [Order(500)]
        public async Task UpdateCertificateSelfSignedNoPrivateKeyAssertsAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            using Certificate invalidCert = s_factory
                .CreateApplicationCertificate("uri:x:y:z", "TestApp", "CN=Push Server Test")
                .CreateForRSA();
            using Certificate serverCert = CertificateFactory.Create(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
            {
                Assert.Ignore("Server has no self signed cert in use.");
            }
            byte[] invalidRawCert = [0xba, 0xd0, 0xbe, 0xef, 3];
            // negative test all parameter combinations
            var invalidCertGroup = new NodeId(333);
            var invalidCertType = new NodeId(Guid.NewGuid());
            await Assert.ThatAsync(
                () => m_pushClient.PushClient.UpdateCertificateAsync(default, default, default, null, default, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        invalidCertGroup,
                        default,
                        serverCert.RawData.ToByteString(),
                        null,
                        default,
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        invalidCertType,
                        serverCert.RawData.ToByteString(),
                        null,
                        default,
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        invalidCertGroup,
                        invalidCertType,
                        serverCert.RawData.ToByteString(),
                        null,
                        default,
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(default, default, invalidRawCert.ToByteString(), null, default, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(default, default, invalidCert.RawData.ToByteString(), null, default, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(default, default, serverCert.RawData.ToByteString(), "XYZ", default, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        serverCert.RawData.ToByteString(),
                        "XYZ",
                        invalidCert.RawData.ToByteString(),
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        invalidCert.RawData.ToByteString(),
                        null,
                        default,
                        [serverCert.RawData.ToByteString(), invalidCert.RawData.ToByteString()]
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        default,
                        null,
                        default,
                        [serverCert.RawData.ToByteString(), invalidCert.RawData.ToByteString()]
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        invalidRawCert.ToByteString(),
                        null,
                        default,
                        [serverCert.RawData.ToByteString(), invalidCert.RawData.ToByteString()]
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        serverCert.RawData.ToByteString(),
                        null,
                        default,
                        [serverCert.RawData.ToByteString(), invalidRawCert.ToByteString()]
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient
                    .UpdateCertificateAsync(default, default, serverCert.RawData.ToByteString(), null, default, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
        }

        [Test]
        [Order(501)]
        public async Task UpdateCertificateSelfSignedNoPrivateKeyAsync()
        {
            if (m_certificateType != OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                Assert.Ignore("Test only supported for RSA");
            }
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            using Certificate serverCert = CertificateFactory.Create(
                m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate);
            if (!X509Utils.CompareDistinguishedName(serverCert.Subject, serverCert.Issuer))
            {
                Assert.Ignore("Server has no self signed cert in use.");
            }
            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                default,
                m_certificateType,
                serverCert.RawData.ToByteString(),
                null,
                default,
                default).ConfigureAwait(false);
            if (success)
            {
                await ApplyChangesIgnoreChannelTearDownAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(serverCert.RawData.ToByteString()).ConfigureAwait(false);
        }

        [Test]
        [Order(509)]
        public async Task UpdateCertificateCASignedRegeneratePrivateKeyAsync()
        {
            await UpdateCertificateCASignedAsync(true).ConfigureAwait(false);
        }

        [Test]
        [Order(510)]
        public async Task UpdateCertificateCASignedAsync()
        {
            await UpdateCertificateCASignedAsync(false).ConfigureAwait(false);
        }

        public async Task UpdateCertificateCASignedAsync(bool regeneratePrivateKey)
        {
#if NETFRAMEWORK || SKIP_ECC_CERTIFICATE_REQUEST_SIGNING
            if (m_certificateType != OpcUa.ObjectTypeIds.RsaMinApplicationCertificateType &&
                m_certificateType != OpcUa.ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                Assert.Ignore("ECC signing requests not yet supported on .NET Framework");
            }
#endif
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await ConnectGDSClientAsync(true).ConfigureAwait(false);
            TestContext.Out.WriteLine("Create Signing Request");
            ByteString csr = await m_pushClient.PushClient.CreateSigningRequestAsync(
                default,
                m_certificateType,
                m_selfSignedServerCert.Subject + "2",
                regeneratePrivateKey,
                default).ConfigureAwait(false);
            Assert.That(csr.IsEmpty, Is.False);
            TestContext.Out.WriteLine("Start Signing Request");
            NodeId requestId = await m_gdsClient.GDSClient.StartSigningRequestAsync(
                m_applicationRecord.ApplicationId,
                default,
                m_certificateType,
                csr).ConfigureAwait(false);
            Assert.That(requestId.IsNull, Is.False);
            ByteString privateKey = default;
            ByteString certificate = default;
            ArrayOf<ByteString> issuerCertificates = default;

            Thread.Sleep(1000);

            DateTime now = DateTime.UtcNow;
            do
            {
                try
                {
                    TestContext.Out.WriteLine("Finish Signing Request");
                    (certificate, privateKey, issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                        m_applicationRecord.ApplicationId,
                        requestId).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // wait if GDS requires manual approval of cert request
                    if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                        now.AddMinutes(5) > DateTime.UtcNow)
                    {
                        Thread.Sleep(10000);
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (certificate.IsEmpty);
            Assert.That(issuerCertificates.IsNull, Is.False);
            Assert.That(privateKey.IsEmpty, Is.True);
            await DisconnectGDSClientAsync().ConfigureAwait(false);
            TestContext.Out.WriteLine("Update Certificate");
            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                default,
                m_certificateType,
                certificate,
                null,
                default,
                issuerCertificates).ConfigureAwait(false);
            if (success)
            {
                TestContext.Out.WriteLine("Apply Changes");
                await ApplyChangesIgnoreChannelTearDownAsync().ConfigureAwait(false);
            }
            TestContext.Out.WriteLine("Verify Cert Update");
            await VerifyNewPushServerCertAsync(certificate).ConfigureAwait(false);
        }

        [Test]
        [Order(520)]
        public async Task UpdateCertificateSelfSignedPFXAsync()
        {
            await UpdateCertificateSelfSignedAsync("PFX").ConfigureAwait(false);
        }

        [Test]
        [Order(530)]
        public async Task UpdateCertificateSelfSignedPEMAsync()
        {
            await UpdateCertificateSelfSignedAsync("PEM").ConfigureAwait(false);
        }

        public async Task UpdateCertificateSelfSignedAsync(string keyFormat)
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ArrayOf<string> keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            if (!keyFormats.Contains(keyFormat))
            {
                Assert
                    .Ignore($"Push server doesn't support {keyFormat} key update");
            }

            Certificate newCert;

            ECCurve? curve = CryptoUtils.GetCurveFromCertificateTypeId(m_certificateType);

            if (curve != null)
            {
                newCert = s_factory
                    .CreateApplicationCertificate(
                        m_applicationRecord.ApplicationUri,
                        m_applicationRecord.ApplicationNames[0].Text,
                        m_selfSignedServerCert.Subject + "1")
                    .SetECCurve(curve.Value)
                    .CreateForECDsa();
            }
            // RSA Certificate
            else
            {
                newCert = s_factory
                    .CreateApplicationCertificate(
                        m_applicationRecord.ApplicationUri,
                        m_applicationRecord.ApplicationNames[0].Text,
                        m_selfSignedServerCert.Subject + "1")
                    .CreateForRSA();
            }

            byte[] privateKey = null;
            if (keyFormat == "PFX")
            {
                Assert.That(newCert.HasPrivateKey, Is.True);
                privateKey = newCert.Export(X509ContentType.Pfx);
            }
            else if (keyFormat == "PEM")
            {
                Assert.That(newCert.HasPrivateKey, Is.True);
                privateKey = PEMWriter.ExportPrivateKeyAsPEM(newCert);
            }
            else
            {
                Assert.Fail($"Testing unsupported key format {keyFormat}.");
            }

            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                newCert.RawData.ToByteString(),
                keyFormat,
                privateKey.ToByteString(),
                default).ConfigureAwait(false);

            if (success)
            {
                await ApplyChangesIgnoreChannelTearDownAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(newCert.RawData.ToByteString()).ConfigureAwait(false);
        }

        [Test]
        [Order(540)]
        public async Task UpdateCertificateNewKeyPairPFXAsync()
        {
            await UpdateCertificateWithNewKeyPairAsync("PFX").ConfigureAwait(false);
        }

        [Test]
        [Order(550)]
        public async Task UpdateCertificateNewKeyPairPEMAsync()
        {
            await UpdateCertificateWithNewKeyPairAsync("PEM").ConfigureAwait(false);
        }

        public async Task UpdateCertificateWithNewKeyPairAsync(string keyFormat)
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            ArrayOf<string> keyFormats = await m_pushClient.PushClient.GetSupportedKeyFormatsAsync().ConfigureAwait(false);
            if (!keyFormats.Contains(keyFormat))
            {
                Assert
                    .Ignore($"Push server doesn't support {keyFormat} key update");
            }

            NodeId requestId = await m_gdsClient.GDSClient.StartNewKeyPairRequestAsync(
                m_applicationRecord.ApplicationId,
                default,
                m_certificateType,
                m_selfSignedServerCert.Subject + "3",
                m_domainNames,
                keyFormat,
                null).ConfigureAwait(false);

            Assert.That(requestId.IsNull, Is.False);
            ByteString privateKey = default;
            ByteString certificate = default;
            ArrayOf<ByteString> issuerCertificates = default;
            DateTime now = DateTime.UtcNow;
            do
            {
                try
                {
                    Thread.Sleep(500);
                    (certificate, privateKey, issuerCertificates) = await m_gdsClient.GDSClient.FinishRequestAsync(
                        m_applicationRecord.ApplicationId,
                        requestId).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // wait if GDS requires manual approval of cert request
                    if (sre.StatusCode == StatusCodes.BadNothingToDo &&
                        now.AddMinutes(5) > DateTime.UtcNow)
                    {
                        Thread.Sleep(10000);
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (certificate.IsEmpty);
            Assert.That(issuerCertificates.IsNull, Is.False);
            Assert.That(privateKey.IsEmpty, Is.False);
            await DisconnectGDSClientAsync().ConfigureAwait(false);

            bool success = await m_pushClient.PushClient.UpdateCertificateAsync(
                m_pushClient.PushClient.DefaultApplicationGroup,
                m_certificateType,
                certificate,
                keyFormat,
                privateKey,
                issuerCertificates).ConfigureAwait(false);
            if (success)
            {
                await ApplyChangesIgnoreChannelTearDownAsync().ConfigureAwait(false);
            }
            await VerifyNewPushServerCertAsync(certificate).ConfigureAwait(false);
        }

        [Test]
        [Order(600)]
        public async Task GetRejectedListAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            CertificateCollection collection = await m_pushClient.PushClient.GetRejectedListAsync().ConfigureAwait(false);
            Assert.That(collection, Is.Not.Null);
        }

        [Test]
        [Order(610)]
        public async Task GetCertificatesAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => m_pushClient.PushClient.GetCertificatesAsync(default).AsTask(),
                Throws.Exception).ConfigureAwait(false);

            (ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates) = await m_pushClient.PushClient.GetCertificatesAsync(
                m_pushClient.PushClient.DefaultApplicationGroup).ConfigureAwait(false);

            Assert.That(certificateTypeIds.Count, Is.EqualTo(certificates.Count));
            Assert.That(certificates[0].IsEmpty, Is.False);
            using Certificate x509 = CertificateFactory.Create(certificates[0]);
            Assert.That(x509, Is.Not.Null);
        }

        [Test]
        [Order(700)]
        public async Task ApplyChangesAsync()
        {
            await ConnectPushClientAsync(true).ConfigureAwait(false);
            await m_pushClient.PushClient.ApplyChangesAsync().ConfigureAwait(false);
        }

        [Test]
        [Order(800)]
        public async Task VerifyNoUserAccessAsync()
        {
            await ConnectPushClientAsync(false).ConfigureAwait(false);
            await Assert.ThatAsync(() => m_pushClient.PushClient.ApplyChangesAsync().AsTask(), Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(() => m_pushClient.PushClient.GetRejectedListAsync().AsTask(), Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient.GetCertificatesAsync(default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () =>
                    m_pushClient.PushClient.UpdateCertificateAsync(
                        default,
                        default,
                        m_selfSignedServerCert.RawData.ToByteString(),
                        null,
                        default,
                        default
                    ).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => m_pushClient.PushClient.CreateSigningRequestAsync(default, default, null, false, default).AsTask(),
                Throws.Exception).ConfigureAwait(false);
            await Assert
                .ThatAsync(() => m_pushClient.PushClient.ReadTrustListAsync().AsTask(), Throws.Exception).ConfigureAwait(false);
        }

        private async Task ConnectPushClientAsync(
            bool sysAdmin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            int retryCount = 3;
            var retryInterval = TimeSpan.FromSeconds(2);

            while (retryCount > 0)
            {
                try
                {
                    m_pushClient.PushClient.AdminCredentials = sysAdmin
                        ? m_pushClient.SysAdminUser
                        : m_pushClient.AppUser;
                    await m_pushClient.ConnectAsync(m_securityPolicyUri).ConfigureAwait(false);
                    TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connected -- {memberName}");
                    return; // Connection successful, exit the loop
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"Connection attempt failed: {ex.Message}. Retrying in {retryInterval}...");
                    retryCount--;
                    if (retryCount == 0)
                    {
                        TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connection failed after multiple retries -- {memberName}");
                        throw; // Re-throw the exception if all retries failed
                    }
                    await Task.Delay(retryInterval).ConfigureAwait(false);
                }
            }

            TestContext.Progress.WriteLine($"GDS Push({sysAdmin}) Connected -- {memberName}");
        }

        private async Task DisconnectPushClientAsync()
        {
            await m_pushClient.PushClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task ConnectGDSClientAsync(
            bool admin,
            [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            m_gdsClient.GDSClient.AdminCredentials = admin
                ? m_gdsClient.AdminUser
                : m_gdsClient.AppUser;
            await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl)
                .ConfigureAwait(false);
            TestContext.Progress.WriteLine($"GDS Client({admin}) connected -- {memberName}");
        }

        private async Task DisconnectGDSClientAsync()
        {
            await m_gdsClient.GDSClient.DisconnectAsync().ConfigureAwait(false);
        }

        private async Task RegisterPushServerApplicationAsync(
            string discoveryUrl,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (m_applicationRecord == null && discoveryUrl != null)
            {
                EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    m_gdsClient.Configuration,
                    discoveryUrl,
                    true,
                    telemetry,
                    ct).ConfigureAwait(false);
                ApplicationDescription description = endpointDescription.Server;
                m_applicationRecord = new ApplicationRecordDataType
                {
                    ApplicationNames = [description.ApplicationName],
                    ApplicationUri = description.ApplicationUri,
                    ApplicationType = description.ApplicationType,
                    ProductUri = description.ProductUri,
                    DiscoveryUrls = description.DiscoveryUrls,
                    ServerCapabilities = ["NA"]
                };
            }
            Assert.That(m_applicationRecord, Is.Not.Null);
            Assert.That(m_applicationRecord.ApplicationId.IsNull, Is.True);
            NodeId id = await m_gdsClient.GDSClient.RegisterApplicationAsync(m_applicationRecord, ct).ConfigureAwait(false);
            Assert.That(id.IsNull, Is.False);
            m_applicationRecord.ApplicationId = id;

            // add issuer and trusted certs to client stores
            NodeId trustListId = await m_gdsClient.GDSClient.GetTrustListAsync(id, default, ct).ConfigureAwait(false);
            TrustListDataType trustList = await m_gdsClient.GDSClient.ReadTrustListAsync(
                trustListId,
                0,
                ct).ConfigureAwait(false);
            bool result = await AddTrustListToStoreAsync(
                m_gdsClient.Configuration.SecurityConfiguration,
                trustList,
                telemetry).ConfigureAwait(false);
            Assert.That(result, Is.True);
            result = await AddTrustListToStoreAsync(
                m_pushClient.Config.SecurityConfiguration,
                trustList,
                telemetry).ConfigureAwait(false);
            Assert.That(result, Is.True);
        }

        private async Task UnRegisterPushServerApplicationAsync()
        {
            await m_gdsClient.GDSClient.UnregisterApplicationAsync(m_applicationRecord.ApplicationId).ConfigureAwait(false);
            m_applicationRecord.ApplicationId = default;
        }

        /// <summary>
        /// Calls ApplyChanges on the push client and ignores
        /// transport-level errors that happen when the server tears down
        /// the secure channel as part of the certificate update. The
        /// caller is expected to verify the new certificate via
        /// <see cref="VerifyNewPushServerCertAsync"/>, which retries the
        /// connection with the new cert.
        /// </summary>
        private async Task ApplyChangesIgnoreChannelTearDownAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await m_pushClient.PushClient.ApplyChangesAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ApplyChangesAsync races with the server's deferred
                // certificate update task that disposes the active
                // application certificates. Any of the following can be
                // observed: a ServiceResultException with one of several
                // transport status codes (BadRequestTimeout,
                // BadRequestInterrupted, BadSecureChannelClosed, ...),
                // an OperationCanceledException from the bounded CTS, or
                // a wrapping AggregateException. All are expected — the
                // caller's verification step retries the connection with
                // the new server certificate and asserts on identity.
                TestContext.Out.WriteLine(
                    $"ApplyChangesAsync expected channel teardown: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task VerifyNewPushServerCertAsync(ByteString certificateBlob)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<PushTest>();

            await DisconnectPushClientAsync().ConfigureAwait(false);

            const int maxWaitSeconds = 10;
            const int retryIntervalMs = 2000;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < maxWaitSeconds)
            {
                try
                {
                    await m_gdsClient.GDSClient.ConnectAsync(m_gdsClient.GDSClient.EndpointUrl).ConfigureAwait(false);
                    await m_pushClient.ConnectAsync(m_securityPolicyUri).ConfigureAwait(false);

                    Certificate serverCertificate = Utils.ParseCertificateBlob(
                        m_pushClient.PushClient.Session.ConfiguredEndpoint.Description.ServerCertificate,
                        telemetry);

                    if (ByteString.From(serverCertificate.RawData) == certificateBlob)
                    {
                        // Success, exit early
                        return;
                    }

                    await DisconnectPushClientAsync().ConfigureAwait(false);
                    await Task.Delay(retryIntervalMs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failure while verifying new Push Server certificate.");
                }
            }
            Assert.Fail("Server certificate did not match with the Certificate pushed by " +
                "the GDS within the allowed time.");
        }

        private static async Task<bool> AddTrustListToStoreAsync(
            SecurityConfiguration config,
            TrustListDataType trustList,
            ITelemetryContext telemetry)
        {
            int masks = (int)trustList.SpecifiedLists;

            CertificateCollection issuerCertificates = null;
            X509CRLCollection issuerCrls = null;
            CertificateCollection trustedCertificates = null;
            X509CRLCollection trustedCrls = null;

            // test integrity of all CRLs
            if ((masks & (int)TrustListMasks.IssuerCertificates) != 0)
            {
                issuerCertificates = [];
                foreach (ByteString cert in trustList.IssuerCertificates)
                {
                    issuerCertificates.Add(CertificateFactory.Create(cert.ToArray()));
                }
            }
            if ((masks & (int)TrustListMasks.IssuerCrls) != 0)
            {
                issuerCrls = [];
                foreach (ByteString crl in trustList.IssuerCrls)
                {
                    issuerCrls.Add(new X509CRL(crl.ToArray()));
                }
            }
            if ((masks & (int)TrustListMasks.TrustedCertificates) != 0)
            {
                trustedCertificates = [];
                foreach (ByteString cert in trustList.TrustedCertificates)
                {
                    trustedCertificates.Add(CertificateFactory.Create(cert.ToArray()));
                }
            }
            if ((masks & (int)TrustListMasks.TrustedCrls) != 0)
            {
                trustedCrls = [];
                foreach (ByteString crl in trustList.TrustedCrls)
                {
                    trustedCrls.Add(new X509CRL(crl.ToArray()));
                }
            }

            // update store
            // test integrity of all CRLs
            int updateMasks = (int)TrustListMasks.None;
            if ((masks & (int)TrustListMasks.IssuerCertificates) != 0 &&
                await UpdateStoreCertificatesAsync(
                    config.TrustedIssuerCertificates,
                    issuerCertificates,
                    telemetry)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.IssuerCertificates;
            }
            if ((masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                await UpdateStoreCrlsAsync(
                    config.TrustedIssuerCertificates,
                    issuerCrls,
                    telemetry)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.IssuerCrls;
            }
            if ((masks & (int)TrustListMasks.TrustedCertificates) != 0 &&
                await UpdateStoreCertificatesAsync(
                    config.TrustedPeerCertificates,
                    trustedCertificates,
                    telemetry)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.TrustedCertificates;
            }
            if ((masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                await UpdateStoreCrlsAsync(
                    config.TrustedPeerCertificates,
                    trustedCrls,
                    telemetry)
                    .ConfigureAwait(false))
            {
                updateMasks |= (int)TrustListMasks.TrustedCrls;
            }

            return masks == updateMasks;
        }

        private static async Task<bool> UpdateStoreCrlsAsync(
            CertificateTrustList trustList,
            X509CRLCollection updatedCrls,
            ITelemetryContext telemetry)
        {
            bool result = true;
            try
            {
                using ICertificateStore store = trustList.OpenStore(telemetry);
                X509CRLCollection storeCrls = await store.EnumerateCRLsAsync()
                    .ConfigureAwait(false);
                foreach (X509CRL crl in storeCrls)
                {
                    if (!updatedCrls.Remove(crl) &&
                        !await store.DeleteCRLAsync(crl).ConfigureAwait(false))
                    {
                        result = false;
                    }
                }
                foreach (X509CRL crl in updatedCrls)
                {
                    await store.AddCRLAsync(crl).ConfigureAwait(false);
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private static async Task<bool> UpdateStoreCertificatesAsync(
            CertificateTrustList trustList,
            CertificateCollection updatedCerts,
            ITelemetryContext telemetry)
        {
            bool result = true;
            try
            {
                using ICertificateStore store = trustList.OpenStore(telemetry);
                CertificateCollection storeCerts = await store.EnumerateAsync()
                    .ConfigureAwait(false);
                foreach (Certificate cert in storeCerts)
                {
                    if (!updatedCerts.Contains(cert))
                    {
                        if (!store.DeleteAsync(cert.Thumbprint).Result)
                        {
                            result = false;
                        }
                    }
                    else
                    {
                        updatedCerts.Remove(cert);
                    }
                }
                foreach (Certificate cert in updatedCerts)
                {
                    await store.AddAsync(cert).ConfigureAwait(false);
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        /// <summary>
        /// Create CA test certificates.
        /// </summary>
        private async Task CreateCATestCertsAsync(string tempStorePath, ITelemetryContext telemetry)
        {
            var certificateStoreIdentifier = new CertificateStoreIdentifier(tempStorePath, false);
            Assert.That(EraseStore(certificateStoreIdentifier, telemetry), Is.True);
            const string subjectName = "CN=CA Test Cert, O=OPC Foundation";
            ECCurve? curve = CryptoUtils.GetCurveFromCertificateTypeId(m_certificateType);

            if (curve != null)
            {
                m_caCert = await s_factory
                    .CreateCertificate(subjectName)
                    .SetCAConstraint()
                    .SetECCurve(curve.Value)
                    .CreateForECDsa()
                    .AddToStoreAsync(certificateStoreIdentifier, telemetry: telemetry)
                    .ConfigureAwait(false);
            }
            // RSA Certificate
            else
            {
                m_caCert = await s_factory
                    .CreateCertificate(subjectName)
                    .SetCAConstraint()
                    .CreateForRSA()
                    .AddToStoreAsync(certificateStoreIdentifier, telemetry: telemetry)
                    .ConfigureAwait(false);
            }

            // initialize cert revocation list (CRL)
            X509CRL caCrl = await CertificateGroup
                .LoadCrlCreateEmptyIfNonExistantAsync(m_caCert, certificateStoreIdentifier, telemetry)
                .ConfigureAwait(false);
            Assert.That(caCrl, Is.Not.Null);
        }

        private static bool EraseStore(CertificateStoreIdentifier storeIdentifier, ITelemetryContext telemetry)
        {
            bool result = true;
            try
            {
                using ICertificateStore store = storeIdentifier.OpenStore(telemetry);
                foreach (Certificate cert in store.EnumerateAsync().Result)
                {
                    if (!store.DeleteAsync(cert.Thumbprint).Result)
                    {
                        result = false;
                    }
                }
                foreach (X509CRL crl in store.EnumerateCRLsAsync().Result)
                {
                    if (!store.DeleteCRLAsync(crl).Result)
                    {
                        result = false;
                    }
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private const int kRandomStart = 1;
        private RandomSource m_randomSource;
        private ITelemetryContext m_telemetry;
        private GlobalDiscoveryTestServer m_server;
        private GlobalDiscoveryTestClient m_gdsClient;
        private ServerConfigurationPushTestClient m_pushClient;
        private ApplicationRecordDataType m_applicationRecord;
        private Certificate m_selfSignedServerCert;
        private string[] m_domainNames;
        private Certificate m_caCert;
        private readonly string m_certificateTypeString;
        private readonly NodeId m_certificateType;
        private readonly string m_securityPolicyUri;
    }
}
