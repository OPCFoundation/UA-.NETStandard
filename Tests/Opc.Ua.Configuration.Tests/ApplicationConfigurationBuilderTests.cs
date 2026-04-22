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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Configuration.Tests
{
    /// <summary>
    /// Tests for the ApplicationConfigurationBuilder fluent API.
    /// </summary>
    [TestFixture]
    [Category("ApplicationConfigurationBuilder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationConfigurationBuilderTests
    {
        public const string ApplicationName = "UA Builder Test";
        public const string ApplicationUri = "urn:localhost:opcfoundation.org:BuilderTest";
        public const string ProductUri = "http://opcfoundation.org/UA/BuilderTest";
        public const string SubjectName = "CN=UA Builder Test, O=OPC Foundation, C=US, S=Arizona";
        public const string EndpointUrl = "opc.tcp://localhost:52000";

        [SetUp]
        public void SetUp()
        {
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(m_pkiRoot, true);
            }
            catch
            {
                // best effort cleanup
            }
        }

        [Test]
        public void BuildReturnsBuilderWithCorrectApplicationConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            IApplicationConfigurationBuilderTypes builder = appInstance.Build(ApplicationUri, ProductUri);

            Assert.That(builder, Is.Not.Null);
            Assert.That(appInstance.ApplicationConfiguration, Is.Not.Null);
            Assert.That(appInstance.ApplicationConfiguration.ApplicationName, Is.EqualTo(ApplicationName));
            Assert.That(appInstance.ApplicationConfiguration.ApplicationUri, Is.EqualTo(ApplicationUri));
            Assert.That(appInstance.ApplicationConfiguration.ProductUri, Is.EqualTo(ProductUri));
        }

        [Test]
        public void BuildSetsDefaultTransportQuotas()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri);

            Assert.That(appInstance.ApplicationConfiguration.TransportQuotas, Is.Not.Null);
        }

        [Test]
        public void BuildSetsDefaultTraceConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri);

            Assert.That(appInstance.ApplicationConfiguration.TraceConfiguration, Is.Not.Null);
            Assert.That(
                appInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks,
                Is.EqualTo(Utils.TraceMasks.None));
        }

        [Test]
        public void AsClientSetsClientConfiguration()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient();

            Assert.That(appInstance.ApplicationConfiguration.ClientConfiguration, Is.Not.Null);
            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.Client));
        }

        [Test]
        public void AsClientFromServerTypeSetsClient()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.Client));
        }

        [Test]
        public void AsServerFromClientTypeSetsServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl]);

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        public void AsServerThenClientSetsClientAndServerWithBothConfigs()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(appInstance.ApplicationConfiguration.ClientConfiguration, Is.Not.Null);
            Assert.That(appInstance.ApplicationConfiguration.ServerConfiguration, Is.Not.Null);
        }

        [Test]
        public void AsServerThenClientSetsClientAndServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        public void AsClientFromDiscoveryServerThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.DiscoveryServer
            };

            Assert.Throws<ArgumentException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsClient());
        }

        [Test]
        public void AsServerFromDiscoveryServerThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.DiscoveryServer
            };

            Assert.Throws<ArgumentException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl]));
        }

        [Test]
        public void AsServerSetsBaseAddresses()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string endpoint1 = "opc.tcp://localhost:51000";
            string endpoint2 = "opc.tcp://localhost:51001";

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([endpoint1, endpoint2]);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.BaseAddresses.Count,
                Is.EqualTo(2));
        }

        [Test]
        public void AsServerSetsAlternateBaseAddresses()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string[] alternates = ["opc.tcp://myhost:51000"];

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl], alternates);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.AlternateBaseAddresses.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void AsServerDisablesLdsRegistrationByDefault()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl]);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxRegistrationInterval,
                Is.EqualTo(0));
        }

        [Test]
        public void AsServerInitializesEmptyPolicies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl]);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies.Count,
                Is.EqualTo(0));
            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.UserTokenPolicies.Count,
                Is.EqualTo(0));
        }

        [Test]
        public void AddSecurityConfigurationWithSubjectNameSetsDefaults()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig, Is.Not.Null);
            Assert.That(secConfig.ApplicationCertificate, Is.Not.Null);
            Assert.That(secConfig.ApplicationCertificate.SubjectName, Is.Not.Null.And.Not.Empty);
            Assert.That(secConfig.TrustedPeerCertificates, Is.Not.Null);
            Assert.That(secConfig.TrustedIssuerCertificates, Is.Not.Null);
            Assert.That(secConfig.TrustedHttpsCertificates, Is.Not.Null);
            Assert.That(secConfig.HttpsIssuerCertificates, Is.Not.Null);
            Assert.That(secConfig.TrustedUserCertificates, Is.Not.Null);
            Assert.That(secConfig.UserIssuerCertificates, Is.Not.Null);
            Assert.That(secConfig.RejectedCertificateStore, Is.Not.Null);
        }

        [Test]
        public void AddSecurityConfigurationSetsSecureDefaults()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(secConfig.AddAppCertToTrustedStore, Is.False);
            Assert.That(secConfig.RejectSHA1SignedCertificates, Is.True);
            Assert.That(secConfig.RejectUnknownRevocationStatus, Is.True);
            Assert.That(secConfig.SuppressNonceValidationErrors, Is.False);
            Assert.That(secConfig.SendCertificateChain, Is.True);
            Assert.That(secConfig.MinimumCertificateKeySize, Is.EqualTo(CertificateFactory.DefaultKeySize));
            Assert.That(secConfig.MaxRejectedCertificates, Is.EqualTo(5));
        }

        [Test]
        public void AddSecurityConfigurationWithCertIdListSetsSecureDefaults()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(certs, m_pkiRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.ApplicationCertificates.Count, Is.GreaterThan(0));
            Assert.That(secConfig.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(secConfig.SendCertificateChain, Is.True);
            Assert.That(secConfig.MaxRejectedCertificates, Is.EqualTo(5));
        }

        [Test]
        public void AddSecurityConfigurationStoresSetsAllStores()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string appRoot = Path.Combine(m_pkiRoot, "own");
            string trustedRoot = Path.Combine(m_pkiRoot, "trusted");
            string issuerRoot = Path.Combine(m_pkiRoot, "issuer");
            string rejectedRoot = Path.Combine(m_pkiRoot, "rejected");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfigurationStores(
                    SubjectName,
                    appRoot,
                    trustedRoot,
                    issuerRoot,
                    rejectedRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.ApplicationCertificate, Is.Not.Null);
            Assert.That(secConfig.TrustedPeerCertificates, Is.Not.Null);
            Assert.That(secConfig.TrustedIssuerCertificates, Is.Not.Null);
            Assert.That(secConfig.RejectedCertificateStore, Is.Not.Null);
        }

        [Test]
        public void AddSecurityConfigurationStoresWithoutRejectedRootUsesDefault()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string appRoot = Path.Combine(m_pkiRoot, "own");
            string trustedRoot = Path.Combine(m_pkiRoot, "trusted");
            string issuerRoot = Path.Combine(m_pkiRoot, "issuer");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfigurationStores(
                    SubjectName,
                    appRoot,
                    trustedRoot,
                    issuerRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.RejectedCertificateStore, Is.Not.Null);
            Assert.That(secConfig.RejectedCertificateStore.StorePath, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AddSecurityConfigurationUserStoreConfiguresUserStores()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string appRoot = Path.Combine(m_pkiRoot, "own");
            string trustedRoot = Path.Combine(m_pkiRoot, "trusted");
            string issuerRoot = Path.Combine(m_pkiRoot, "issuer");
            string userTrusted = Path.Combine(m_pkiRoot, "trustedUser");
            string userIssuer = Path.Combine(m_pkiRoot, "issuerUser");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfigurationStores(SubjectName, appRoot, trustedRoot, issuerRoot)
                .AddSecurityConfigurationUserStore(userTrusted, userIssuer);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.TrustedUserCertificates, Is.Not.Null);
            Assert.That(secConfig.UserIssuerCertificates, Is.Not.Null);
        }

        [Test]
        public void AddSecurityConfigurationHttpsStoreConfiguresHttpsStores()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string appRoot = Path.Combine(m_pkiRoot, "own");
            string trustedRoot = Path.Combine(m_pkiRoot, "trusted");
            string issuerRoot = Path.Combine(m_pkiRoot, "issuer");
            string httpsTrusted = Path.Combine(m_pkiRoot, "trustedHttps");
            string httpsIssuer = Path.Combine(m_pkiRoot, "issuerHttps");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfigurationStores(SubjectName, appRoot, trustedRoot, issuerRoot)
                .AddSecurityConfigurationHttpsStore(httpsTrusted, httpsIssuer);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.TrustedHttpsCertificates, Is.Not.Null);
            Assert.That(secConfig.HttpsIssuerCertificates, Is.Not.Null);
        }

        [Test]
        public void SetHiResClockDisabledSetsProperty()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetHiResClockDisabled(true);

            Assert.That(appInstance.ApplicationConfiguration.DisableHiResClock, Is.True);
        }

        [Test]
        public void SetTransportQuotasReplacesQuotas()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var quotas = new TransportQuotas { OperationTimeout = 42000 };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetTransportQuotas(quotas);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.OperationTimeout,
                Is.EqualTo(42000));
        }

        [Test]
        public void SetOperationTimeoutSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetOperationTimeout(15000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.OperationTimeout,
                Is.EqualTo(15000));
        }

        [Test]
        public void SetMaxStringLengthSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxStringLength(1_000_000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxStringLength,
                Is.EqualTo(1_000_000));
        }

        [Test]
        public void SetMaxByteStringLengthSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxByteStringLength(2_000_000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxByteStringLength,
                Is.EqualTo(2_000_000));
        }

        [Test]
        public void SetMaxArrayLengthSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxArrayLength(5000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxArrayLength,
                Is.EqualTo(5000));
        }

        [Test]
        public void SetMaxMessageSizeSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxMessageSize(8_000_000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxMessageSize,
                Is.EqualTo(8_000_000));
        }

        [Test]
        public void SetMaxBufferSizeSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxBufferSize(65536);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxBufferSize,
                Is.EqualTo(65536));
        }

        [Test]
        public void SetChannelLifetimeSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetChannelLifetime(600_000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.ChannelLifetime,
                Is.EqualTo(600_000));
        }

        [Test]
        public void SetSecurityTokenLifetimeSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetSecurityTokenLifetime(3_600_000);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.SecurityTokenLifetime,
                Is.EqualTo(3_600_000));
        }

        [Test]
        public void SetMaxEncodingNestingLevelsSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxEncodingNestingLevels(128);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxEncodingNestingLevels,
                Is.EqualTo(128));
        }

        [Test]
        public void SetMaxDecoderRecoveriesSetsValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .SetMaxDecoderRecoveries(10);

            Assert.That(
                appInstance.ApplicationConfiguration.TransportQuotas.MaxDecoderRecoveries,
                Is.EqualTo(10));
        }

        [Test]
        public void SecurityOptionsSetAutoAcceptUntrustedCertificates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                Is.True);
        }

        [Test]
        public void SecurityOptionsSetAddAppCertToTrustedStore()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetAddAppCertToTrustedStore(true);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.AddAppCertToTrustedStore,
                Is.True);
        }

        [Test]
        public void SecurityOptionsSetRejectSHA1SignedCertificates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetRejectSHA1SignedCertificates(false);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates,
                Is.False);
        }

        [Test]
        public void SecurityOptionsSetRejectUnknownRevocationStatus()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetRejectUnknownRevocationStatus(false);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.RejectUnknownRevocationStatus,
                Is.False);
        }

        [Test]
        public void SecurityOptionsSetUseValidatedCertificates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetUseValidatedCertificates(true);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.UseValidatedCertificates,
                Is.True);
        }

        [Test]
        public void SecurityOptionsSetSuppressNonceValidationErrors()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetSuppressNonceValidationErrors(true);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.SuppressNonceValidationErrors,
                Is.True);
        }

        [Test]
        public void SecurityOptionsSetSendCertificateChain()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetSendCertificateChain(false);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.SendCertificateChain,
                Is.False);
        }

        [Test]
        public void SecurityOptionsSetMinimumCertificateKeySize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetMinimumCertificateKeySize(4096);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize,
                Is.EqualTo(4096));
        }

        [Test]
        public void SecurityOptionsSetMaxRejectedCertificates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetMaxRejectedCertificates(100);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.MaxRejectedCertificates,
                Is.EqualTo(100));
        }

        [Test]
        public void SecurityOptionsSetApplicationCertificates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetApplicationCertificates(certs);

            Assert.That(
                appInstance.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificates.Count,
                Is.GreaterThan(0));
        }

        [Test]
        public void AddUnsecurePolicyNoneAddsPolicyWhenTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddUnsecurePolicyNone();

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(1));
            Assert.That(policies[0].SecurityMode, Is.EqualTo(MessageSecurityMode.None));
        }

        [Test]
        public void AddUnsecurePolicyNoneSkipsWhenFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddUnsecurePolicyNone(false);

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddSignPoliciesAddsPoliciesWhenTrue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSignPolicies();

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.GreaterThan(0));
            Assert.That(
                policies.ToList().All(p => p.SecurityMode >= MessageSecurityMode.Sign),
                Is.True);
        }

        [Test]
        public void AddSignPoliciesSkipsWhenFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSignPolicies(false);

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddSignAndEncryptPoliciesAddsPolicies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSignAndEncryptPolicies();

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.GreaterThan(0));
            Assert.That(
                policies.ToList().All(p => p.SecurityMode == MessageSecurityMode.SignAndEncrypt),
                Is.True);
        }

        [Test]
        public void AddSignAndEncryptPoliciesSkipsWhenFalse()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSignAndEncryptPolicies(false);

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddEccSignPoliciesAddsPolicies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddEccSignPolicies();

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.GreaterThan(0));
            Assert.That(
                policies.ToList().All(p => p.SecurityMode == MessageSecurityMode.Sign),
                Is.True);
        }

        [Test]
        public void AddEccSignAndEncryptPoliciesAddsPolicies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddEccSignAndEncryptPolicies();

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.GreaterThan(0));
            Assert.That(
                policies.ToList().All(p => p.SecurityMode == MessageSecurityMode.SignAndEncrypt),
                Is.True);
        }

        [Test]
        public void AddPolicyWithValidParametersAddsPolicy()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256);

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(1));
            Assert.That(policies[0].SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(policies[0].SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void AddPolicyWithNoneModeThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            Assert.Throws<ArgumentException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.None, SecurityPolicies.None));
        }

        [Test]
        public void AddPolicyWithNoneUriThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            Assert.Throws<ArgumentException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.None));
        }

        [Test]
        public void AddPolicyWithInvalidUriThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            Assert.Throws<ArgumentException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddPolicy(MessageSecurityMode.Sign, "not-a-valid-policy-uri"));
        }

        [Test]
        public void AddDuplicatePolicyDoesNotDuplicate()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256);

            ArrayOf<ServerSecurityPolicy> policies = appInstance.ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
            Assert.That(policies.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddUserTokenPolicyByTypeAddsPolicies()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName);

            ArrayOf<UserTokenPolicy> tokenPolicies = appInstance.ApplicationConfiguration.ServerConfiguration.UserTokenPolicies;
            Assert.That(tokenPolicies.Count, Is.EqualTo(2));
        }

        [Test]
        public void AddUserTokenPolicyWithObjectAddsPolicy()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var policy = new UserTokenPolicy(UserTokenType.Certificate)
            {
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddUserTokenPolicy(policy);

            ArrayOf<UserTokenPolicy> tokenPolicies = appInstance.ApplicationConfiguration.ServerConfiguration.UserTokenPolicies;
            Assert.That(tokenPolicies.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddUserTokenPolicyWithNullThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            Assert.Throws<ArgumentNullException>(() =>
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsServer([EndpointUrl])
                    .AddUserTokenPolicy(null));
        }

        [Test]
        public void ServerOptionsSetMinRequestThreadCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMinRequestThreadCount(5);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MinRequestThreadCount,
                Is.EqualTo(5));
        }

        [Test]
        public void ServerOptionsSetMaxRequestThreadCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxRequestThreadCount(100);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxRequestThreadCount,
                Is.EqualTo(100));
        }

        [Test]
        public void ServerOptionsSetMaxQueuedRequestCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxQueuedRequestCount(200);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxQueuedRequestCount,
                Is.EqualTo(200));
        }

        [Test]
        public void ServerOptionsSetDiagnosticsEnabled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetDiagnosticsEnabled(true);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.DiagnosticsEnabled,
                Is.True);
        }

        [Test]
        public void ServerOptionsSetMaxSessionCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxSessionCount(500);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxSessionCount,
                Is.EqualTo(500));
        }

        [Test]
        public void ServerOptionsSetMaxChannelCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxChannelCount(300);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxChannelCount,
                Is.EqualTo(300));
        }

        [Test]
        public void ServerOptionsSetMinSessionTimeout()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMinSessionTimeout(1000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MinSessionTimeout,
                Is.EqualTo(1000));
        }

        [Test]
        public void ServerOptionsSetMaxSessionTimeout()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxSessionTimeout(60000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxSessionTimeout,
                Is.EqualTo(60000));
        }

        [Test]
        public void ServerOptionsSetContinuationPoints()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxBrowseContinuationPoints(10)
                .SetMaxQueryContinuationPoints(20)
                .SetMaxHistoryContinuationPoints(30);

            ServerConfiguration srv = appInstance.ApplicationConfiguration.ServerConfiguration;
            Assert.That(srv.MaxBrowseContinuationPoints, Is.EqualTo(10));
            Assert.That(srv.MaxQueryContinuationPoints, Is.EqualTo(20));
            Assert.That(srv.MaxHistoryContinuationPoints, Is.EqualTo(30));
        }

        [Test]
        public void ServerOptionsSetMaxRequestAge()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxRequestAge(600_000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxRequestAge,
                Is.EqualTo(600_000));
        }

        [Test]
        public void ServerOptionsSetPublishingIntervals()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMinPublishingInterval(50)
                .SetMaxPublishingInterval(60000)
                .SetPublishingResolution(100);

            ServerConfiguration srv = appInstance.ApplicationConfiguration.ServerConfiguration;
            Assert.That(srv.MinPublishingInterval, Is.EqualTo(50));
            Assert.That(srv.MaxPublishingInterval, Is.EqualTo(60000));
            Assert.That(srv.PublishingResolution, Is.EqualTo(100));
        }

        [Test]
        public void ServerOptionsSetSubscriptionLifetimes()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMinSubscriptionLifetime(1000)
                .SetMaxSubscriptionLifetime(3_600_000);

            ServerConfiguration srv = appInstance.ApplicationConfiguration.ServerConfiguration;
            Assert.That(srv.MinSubscriptionLifetime, Is.EqualTo(1000));
            Assert.That(srv.MaxSubscriptionLifetime, Is.EqualTo(3_600_000));
        }

        [Test]
        public void ServerOptionsSetQueueSizes()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxMessageQueueSize(500)
                .SetMaxNotificationQueueSize(1000)
                .SetMaxNotificationsPerPublish(2000)
                .SetMaxEventQueueSize(3000);

            ServerConfiguration srv = appInstance.ApplicationConfiguration.ServerConfiguration;
            Assert.That(srv.MaxMessageQueueSize, Is.EqualTo(500));
            Assert.That(srv.MaxNotificationQueueSize, Is.EqualTo(1000));
            Assert.That(srv.MaxNotificationsPerPublish, Is.EqualTo(2000));
            Assert.That(srv.MaxEventQueueSize, Is.EqualTo(3000));
        }

        [Test]
        public void ServerOptionsSetMinMetadataSamplingInterval()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMinMetadataSamplingInterval(100);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MinMetadataSamplingInterval,
                Is.EqualTo(100));
        }

        [Test]
        public void ServerOptionsSetAvailableSamplingRates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var rates = new List<SamplingRateGroup>
            {
                new SamplingRateGroup(100, 100, 10)
            }.ToArrayOf();

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetAvailableSamplingRates(rates);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.AvailableSamplingRates.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void ServerOptionsSetRegistrationEndpoint()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var endpoint = new EndpointDescription("opc.tcp://localhost:4840");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetRegistrationEndpoint(endpoint);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.RegistrationEndpoint,
                Is.Not.Null);
        }

        [Test]
        public void ServerOptionsSetMaxRegistrationInterval()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxRegistrationInterval(30000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxRegistrationInterval,
                Is.EqualTo(30000));
        }

        [Test]
        public void ServerOptionsSetNodeManagerSaveFile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetNodeManagerSaveFile("nodemanager.xml");

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.NodeManagerSaveFile,
                Is.EqualTo("nodemanager.xml"));
        }

        [Test]
        public void ServerOptionsSetMaxPublishRequestCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxPublishRequestCount(50);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxPublishRequestCount,
                Is.EqualTo(50));
        }

        [Test]
        public void ServerOptionsSetMaxSubscriptionCount()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxSubscriptionCount(200);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxSubscriptionCount,
                Is.EqualTo(200));
        }

        [Test]
        public void ServerOptionsAddServerProfile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string profile = "http://opcfoundation.org/UA-Profile/Server/StandardUA";

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddServerProfile(profile);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.ServerProfileArray.ToList(),
                Does.Contain(profile));
        }

        [Test]
        public void ServerOptionsSetShutdownDelay()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetShutdownDelay(5);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.ShutdownDelay,
                Is.EqualTo(5));
        }

        [Test]
        public void ServerOptionsAddServerCapabilities()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddServerCapabilities("DA")
                .AddServerCapabilities("HA");

            var capabilities = appInstance.ApplicationConfiguration.ServerConfiguration.ServerCapabilities.ToList();
            Assert.That(capabilities, Does.Contain("DA"));
            Assert.That(capabilities, Does.Contain("HA"));
        }

        [Test]
        public void ServerOptionsSetSupportedPrivateKeyFormats()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var formats = new List<string> { "PEM", "PFX" }.ToArrayOf();

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetSupportedPrivateKeyFormats(formats);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.SupportedPrivateKeyFormats.Count,
                Is.EqualTo(2));
        }

        [Test]
        public void ServerOptionsSetMaxTrustListSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxTrustListSize(65536);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxTrustListSize,
                Is.EqualTo(65536));
        }

        [Test]
        public void ServerOptionsSetMultiCastDnsEnabled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMultiCastDnsEnabled(true);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MultiCastDnsEnabled,
                Is.True);
        }

        [Test]
        public void ServerOptionsSetReverseConnect()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var reverseConnect = new ReverseConnectServerConfiguration();

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetReverseConnect(reverseConnect);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.ReverseConnect,
                Is.Not.Null);
        }

        [Test]
        public void ServerOptionsSetOperationLimits()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var limits = new OperationLimits { MaxNodesPerRead = 100 };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetOperationLimits(limits);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.OperationLimits,
                Is.Not.Null);
            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(100));
        }

        [Test]
        public void ServerOptionsSetAuditingEnabled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetAuditingEnabled(true);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.AuditingEnabled,
                Is.True);
        }

        [Test]
        public void ServerOptionsSetHttpsMutualTls()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetHttpsMutualTls(true);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.HttpsMutualTls,
                Is.True);
        }

        [Test]
        public void ServerOptionsSetDurableSubscriptionsEnabled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetDurableSubscriptionsEnabled(true);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.DurableSubscriptionsEnabled,
                Is.True);
        }

        [Test]
        public void ServerOptionsSetMaxDurableNotificationQueueSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxDurableNotificationQueueSize(5000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxDurableNotificationQueueSize,
                Is.EqualTo(5000));
        }

        [Test]
        public void ServerOptionsSetMaxDurableEventQueueSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxDurableEventQueueSize(3000);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxDurableEventQueueSize,
                Is.EqualTo(3000));
        }

        [Test]
        public void ServerOptionsSetMaxDurableSubscriptionLifetime()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .SetMaxDurableSubscriptionLifetime(720);

            Assert.That(
                appInstance.ApplicationConfiguration.ServerConfiguration.MaxDurableSubscriptionLifetimeInHours,
                Is.EqualTo(720));
        }

        [Test]
        public void ClientOptionsSetDefaultSessionTimeout()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .SetDefaultSessionTimeout(30000);

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout,
                Is.EqualTo(30000));
        }

        [Test]
        public void ClientOptionsAddWellKnownDiscoveryUrls()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddWellKnownDiscoveryUrls("opc.tcp://localhost:4840");

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.WellKnownDiscoveryUrls.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void ClientOptionsAddDiscoveryServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var discovery = new EndpointDescription("opc.tcp://localhost:4840");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddDiscoveryServer(discovery);

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.DiscoveryServers.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void ClientOptionsSetEndpointCacheFilePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .SetEndpointCacheFilePath("endpoints.xml");

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.EndpointCacheFilePath,
                Is.EqualTo("endpoints.xml"));
        }

        [Test]
        public void ClientOptionsSetMinSubscriptionLifetime()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            IApplicationConfigurationBuilderClientOptions clientBuilder =
                appInstance.Build(ApplicationUri, ProductUri)
                    .AsClient();
            clientBuilder.SetMinSubscriptionLifetime(5000);

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.MinSubscriptionLifetime,
                Is.EqualTo(5000));
        }

        [Test]
        public void ClientOptionsSetReverseConnect()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var reverseConnect = new ReverseConnectClientConfiguration();

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .SetReverseConnect(reverseConnect);

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.ReverseConnect,
                Is.Not.Null);
        }

        [Test]
        public void ClientOptionsSetClientOperationLimits()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var limits = new OperationLimits { MaxNodesPerRead = 50 };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .SetClientOperationLimits(limits);

            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.OperationLimits,
                Is.Not.Null);
            Assert.That(
                appInstance.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerRead,
                Is.EqualTo(50));
        }

        [Test]
        public void TraceConfigurationSetOutputFilePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetOutputFilePath("trace.log");

            Assert.That(
                appInstance.ApplicationConfiguration.TraceConfiguration.OutputFilePath,
                Is.EqualTo("trace.log"));
        }

        [Test]
        public void TraceConfigurationSetDeleteOnLoad()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetDeleteOnLoad(true);

            Assert.That(
                appInstance.ApplicationConfiguration.TraceConfiguration.DeleteOnLoad,
                Is.True);
        }

        [Test]
        public void TraceConfigurationSetTraceMasks()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .SetTraceMasks(Utils.TraceMasks.Error | Utils.TraceMasks.Information);

            Assert.That(
                appInstance.ApplicationConfiguration.TraceConfiguration.TraceMasks,
                Is.EqualTo(Utils.TraceMasks.Error | Utils.TraceMasks.Information));
        }

        [Test]
        public void CreateAsyncWithServerTypeAndNoServerConfigThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            appInstance.Build(ApplicationUri, ProductUri);

            // Directly access builder to bypass fluent chain - ServerConfig is null
            var builder = new ApplicationConfigurationBuilder(appInstance);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await builder.CreateAsync().ConfigureAwait(false));
        }

        [Test]
        public void CreateAsyncWithClientTypeAndNoClientConfigThrows()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            appInstance.Build(ApplicationUri, ProductUri);

            // Directly access builder to bypass fluent chain - ClientConfig is null
            var builder = new ApplicationConfigurationBuilder(appInstance);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await builder.CreateAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task CreateAsyncForClientCreatesValidConfigAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await appInstance
                .Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(certs, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.ClientConfiguration, Is.Not.Null);
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
        }

        [Test]
        public async Task CreateAsyncForServerAddsDefaultUserTokenPolicyAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await appInstance
                .Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSecurityConfiguration(certs, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);

            Assert.That(config.ServerConfiguration.UserTokenPolicies.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task CreateAsyncForServerAddsDefaultSecurityPoliciesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await appInstance
                .Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AddSecurityConfiguration(certs, m_pkiRoot)
                .CreateAsync()
                .ConfigureAwait(false);

            Assert.That(config.ServerConfiguration.SecurityPolicies.Count, Is.GreaterThan(0));
        }

        [Test]
        public void CreateDefaultApplicationCertificatesReturnsMultipleCerts()
        {
            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            // RSA + ECC P256 + ECC P384 at minimum, plus Brainpool on non-macOS
            Assert.That(certs.Count, Is.GreaterThanOrEqualTo(3));

            // verify first cert is RSA SHA256
            Assert.That(
                certs[0].CertificateType,
                Is.EqualTo(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public void CreateDefaultApplicationCertificatesIncludesBrainpoolOnNonMacOS()
        {
            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.That(certs.Count, Is.GreaterThanOrEqualTo(5));
            }
            else
            {
                Assert.That(certs.Count, Is.EqualTo(3));
            }
        }

        [Test]
        public void CreateDefaultApplicationCertificatesSetsStoreTypeAndPath()
        {
            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            foreach (CertificateIdentifier cert in certs)
            {
                Assert.That(cert.StoreType, Is.EqualTo(CertificateStoreType.Directory));
                Assert.That(cert.StorePath, Is.EqualTo(m_pkiRoot));
                Assert.That(cert.SubjectName, Is.EqualTo(SubjectName));
            }
        }

        [Test]
        public void CreateDefaultApplicationCertificatesWithNullStoreType()
        {
            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(SubjectName);

            Assert.That(certs.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(certs[0].StoreType, Is.Null);
            Assert.That(certs[0].StorePath, Is.Null);
        }

        [Test]
        public void AddSecurityConfigurationWithDefaultPkiRoot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig, Is.Not.Null);
            Assert.That(secConfig.ApplicationCertificate, Is.Not.Null);
            Assert.That(secConfig.ApplicationCertificate.StorePath, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void AddSecurityConfigurationWithCertIdListAndDefaultPkiRoot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(certs);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig, Is.Not.Null);
            Assert.That(secConfig.TrustedPeerCertificates.StorePath, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public async Task FullClientAndServerBuilderFlowAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            ApplicationConfiguration config = await appInstance
                .Build(ApplicationUri, ProductUri)
                .SetOperationTimeout(10000)
                .SetMaxStringLength(500_000)
                .SetMaxByteStringLength(1_000_000)
                .SetMaxArrayLength(10_000)
                .SetMaxMessageSize(4_000_000)
                .SetMaxBufferSize(65536)
                .SetChannelLifetime(300_000)
                .SetSecurityTokenLifetime(3_600_000)
                .SetMaxEncodingNestingLevels(64)
                .SetMaxDecoderRecoveries(5)
                .AsServer([EndpointUrl], ["opc.tcp://althost:51000"])
                .AddUnsecurePolicyNone()
                .AddSignPolicies()
                .AddSignAndEncryptPolicies()
                .AddEccSignPolicies()
                .AddEccSignAndEncryptPolicies()
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .SetDiagnosticsEnabled(true)
                .SetMaxSessionCount(100)
                .SetMaxChannelCount(50)
                .SetMinSessionTimeout(1000)
                .SetMaxSessionTimeout(60000)
                .SetMaxBrowseContinuationPoints(10)
                .SetMaxQueryContinuationPoints(10)
                .SetMaxHistoryContinuationPoints(10)
                .SetMaxRequestAge(600_000)
                .SetMinPublishingInterval(50)
                .SetMaxPublishingInterval(30000)
                .SetPublishingResolution(50)
                .SetMinSubscriptionLifetime(1000)
                .SetMaxSubscriptionLifetime(3_600_000)
                .SetMaxMessageQueueSize(100)
                .SetMaxNotificationQueueSize(1000)
                .SetMaxNotificationsPerPublish(5000)
                .SetMaxEventQueueSize(10000)
                .SetMinMetadataSamplingInterval(100)
                .SetMaxRegistrationInterval(30000)
                .SetNodeManagerSaveFile("nodes.xml")
                .SetMaxPublishRequestCount(20)
                .SetMaxSubscriptionCount(100)
                .AddServerProfile("http://opcfoundation.org/UA-Profile/Server/StandardUA")
                .SetShutdownDelay(5)
                .AddServerCapabilities("DA")
                .SetMaxTrustListSize(65536)
                .SetMultiCastDnsEnabled(false)
                .SetAuditingEnabled(true)
                .SetHttpsMutualTls(false)
                .SetDurableSubscriptionsEnabled(true)
                .SetMaxDurableNotificationQueueSize(5000)
                .SetMaxDurableEventQueueSize(3000)
                .SetMaxDurableSubscriptionLifetime(720)
                .AsClient()
                .SetDefaultSessionTimeout(30000)
                .AddWellKnownDiscoveryUrls("opc.tcp://localhost:4840")
                .SetEndpointCacheFilePath("endpoints.xml")
                .AddSecurityConfiguration(certs, m_pkiRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetAddAppCertToTrustedStore(true)
                .SetMinimumCertificateKeySize(1024)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(false)
                .SetSendCertificateChain(true)
                .SetSuppressNonceValidationErrors(true)
                .SetMaxRejectedCertificates(10)
                .SetUseValidatedCertificates(true)
                .SetHiResClockDisabled(false)
                .SetOutputFilePath("trace.log")
                .SetDeleteOnLoad(true)
                .SetTraceMasks(Utils.TraceMasks.Error)
                .CreateAsync()
                .ConfigureAwait(false);

            Assert.That(config, Is.Not.Null);
            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            Assert.That(config.ClientConfiguration, Is.Not.Null);
            Assert.That(config.ServerConfiguration, Is.Not.Null);
            Assert.That(config.SecurityConfiguration, Is.Not.Null);
            Assert.That(config.TransportQuotas.OperationTimeout, Is.EqualTo(10000));
            Assert.That(config.ServerConfiguration.DiagnosticsEnabled, Is.True);
            Assert.That(config.ServerConfiguration.DurableSubscriptionsEnabled, Is.True);
        }

        [Test]
        public void AsClientIdempotentWhenAlreadyClient()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.Client));
        }

        [Test]
        public void AsClientIdempotentWhenAlreadyClientAndServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.ClientAndServer
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        public void AsServerIdempotentWhenAlreadyServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl]);

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        public void AsServerIdempotentWhenAlreadyClientAndServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.ClientAndServer
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl]);

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        public void AsServerFromClientTypeAfterClientSelectedSetsClientAndServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Client
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        public void AsClientAfterServerSelectedSetsClientAndServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ApplicationName,
                ApplicationType = ApplicationType.Server
            };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsServer([EndpointUrl])
                .AsClient();

            Assert.That(appInstance.ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
        }

        [Test]
        public void AddSecurityConfigurationWithSeparateRejectedRoot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string rejectedRoot = Path.Combine(m_pkiRoot, "rejected");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot, null, rejectedRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.RejectedCertificateStore, Is.Not.Null);
            Assert.That(secConfig.RejectedCertificateStore.StorePath, Does.Contain("rejected"));
        }

        [Test]
        public void AddSecurityConfigurationWithSeparateAppRoot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string appRoot = Path.Combine(m_pkiRoot, "app");

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot, appRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.ApplicationCertificate, Is.Not.Null);
            Assert.That(secConfig.ApplicationCertificate.StorePath, Does.Contain("app"));
        }

        [Test]
        public void AddSecurityConfigurationWithCertIdListAndSeparateRejectedRoot()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            string rejectedRoot = Path.Combine(m_pkiRoot, "rejected");

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    SubjectName,
                    CertificateStoreType.Directory,
                    m_pkiRoot);

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(certs, m_pkiRoot, rejectedRoot);

            SecurityConfiguration secConfig = appInstance.ApplicationConfiguration.SecurityConfiguration;
            Assert.That(secConfig.RejectedCertificateStore.StorePath, Does.Contain("rejected"));
        }

        [Test]
        public void AddExtensionWithEncodeableAddsExtension()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appInstance = new ApplicationInstance(telemetry) { ApplicationName = ApplicationName };
            var qualifiedName = new System.Xml.XmlQualifiedName("OperationLimits", Namespaces.OpcUa);
            var limits = new OperationLimits { MaxNodesPerRead = 42 };

            appInstance.Build(ApplicationUri, ProductUri)
                .AsClient()
                .AddSecurityConfiguration(SubjectName, m_pkiRoot)
                .AddExtension(qualifiedName, limits);

            OperationLimits extension = appInstance.ApplicationConfiguration.ParseExtension<OperationLimits>(qualifiedName);
            Assert.That(extension, Is.Not.Null);
            Assert.That(extension.MaxNodesPerRead, Is.EqualTo(42));
        }

        private string m_pkiRoot;
    }
}
