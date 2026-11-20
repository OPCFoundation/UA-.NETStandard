// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests for the UANodeSet helper.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ClientTests
    {
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
        }

        [TearDown]
        protected void TearDown()
        {
        }

        /// <summary>
        /// Ensure that use of OriginalString preserves a scope id of a IPv6 address.
        /// </summary>
        [Test]
        [TestCase("opc.tcp://another.server.com:4840/CustomEndpoint")]
        [TestCase("opc.tcp://10.11.222.12:62541/ReferenceServer")]
        [TestCase("opc.tcp://[2003:d9:1f40:bc00:a115:d9c7:6134:f347]:4840/AnEndpoint")]
        [TestCase("opc.tcp://[fe80::280:deff:fa02:c63e%eth0]:4840/")]
        [TestCase("opc.tcp://[fe80::de39:6fff:feae:c78%12]:4840/Endpoint1")]
        public void DiscoveryEndPointUrls(string urlString)
        {
            var uri = new Uri(urlString);
            Assert.That(uri.IsWellFormedOriginalString(), Is.True);

            var uriBuilder = new UriBuilder
            {
                Scheme = uri.Scheme,
                Host = uri.IdnHost,
                Port = uri.Port,
                Path = uri.AbsolutePath
            };

            Assert.That(uriBuilder.Uri.OriginalString, Is.EqualTo(uri.OriginalString));
        }

        [Test]
        public async Task ValidateAppConfigWithoutAppCert()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appCertId = new CertificateIdentifier();

            var appConfig = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Test",
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = appCertId,
                    TrustedPeerCertificates = new CertificateTrustList { StorePath = "Test" },
                    TrustedIssuerCertificates = new CertificateTrustList { StorePath = "Test" }
                }
            };
            await Assert.DoesNotThrowAsync(() =>
                appConfig.ValidateAsync(ApplicationType.Client));
        }
    }
}
