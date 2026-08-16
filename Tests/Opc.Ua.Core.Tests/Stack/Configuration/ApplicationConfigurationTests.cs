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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    /// <summary>
    /// Tests for the <see cref="ApplicationConfiguration"/> validation behavior.
    /// </summary>
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ApplicationConfigurationTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// ValidateAsync defaults TransportQuotas when it is null so the transport
        /// layer does not fail with a NullReferenceException on server start.
        /// </summary>
        [Test]
        public async Task ValidateAsyncDefaultsTransportQuotasWhenNull()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotas_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableServerConfig(pkiPath);
                config.TransportQuotas = null;

                await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.Not.Null);
                Assert.That(
                    config.TransportQuotas!.MaxMessageSize,
                    Is.EqualTo(DefaultEncodingLimits.MaxMessageSize));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        /// <summary>
        /// ValidateAsync preserves an explicitly configured TransportQuotas instance.
        /// </summary>
        [Test]
        public async Task ValidateAsyncKeepsExplicitTransportQuotas()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotas_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableServerConfig(pkiPath);
                var quotas = new TransportQuotas { MaxMessageSize = 1234567 };
                config.TransportQuotas = quotas;

                await config.ValidateAsync(ApplicationType.Server).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.SameAs(quotas));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        /// <summary>
        /// ValidateAsync defaults TransportQuotas when it is null on the client path,
        /// ensuring the behavior is consistent regardless of ApplicationType.
        /// </summary>
        [Test]
        public async Task ValidateAsyncDefaultsTransportQuotasWhenNullForClient()
        {
            string pkiPath = Path.Combine(
                Path.GetTempPath(),
                "OpcUaTestTransportQuotasClient_" + Guid.NewGuid().ToString("N"));
            try
            {
                ApplicationConfiguration config = CreateValidatableClientConfig(pkiPath);
                config.TransportQuotas = null;

                await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

                Assert.That(config.TransportQuotas, Is.Not.Null);
                Assert.That(
                    config.TransportQuotas!.MaxMessageSize,
                    Is.EqualTo(DefaultEncodingLimits.MaxMessageSize));
            }
            finally
            {
                TryDeleteDirectory(pkiPath);
            }
        }

        private ApplicationConfiguration CreateValidatableClientConfig(string pkiPath)
        {
            const string applicationName = "TransportQuotasTestClient";
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = applicationName,
                ApplicationUri = "urn:test:" + applicationName,
                ApplicationType = ApplicationType.Client,
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "own"),
                        SubjectName = "CN=" + applicationName
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "trusted")
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "issuers")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                }
            };
        }

        private ApplicationConfiguration CreateValidatableServerConfig(string pkiPath)
        {
            const string applicationName = "TransportQuotasTestServer";
            return new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = applicationName,
                ApplicationUri = "urn:test:" + applicationName,
                ApplicationType = ApplicationType.Server,
                ServerConfiguration = new ServerConfiguration(),
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "own"),
                        SubjectName = "CN=" + applicationName
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "trusted")
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "issuers")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiPath, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                }
            };
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
