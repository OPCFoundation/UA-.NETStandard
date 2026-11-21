// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Mqtt.Internal;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TrustedIssuerStoreResolver"/>, which resolves the CA
    /// trust chain referenced by <see cref="MqttTlsOptions.TrustedIssuerCertificateSubjects"/>
    /// (issue #3920) from the application's trusted issuer certificate store.
    /// </summary>
    [TestFixture]
    public sealed class TrustedIssuerStoreResolverTests
    {
        private string m_storePath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            m_storePath = Path.Combine(
                Path.GetTempPath(),
                "mqtt-ca-store-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(m_storePath) && Directory.Exists(m_storePath))
            {
                try
                {
                    Directory.Delete(m_storePath, recursive: true);
                }
                catch (IOException)
                {
                    // best-effort cleanup of the temporary store directory
                }
            }
        }

        [Test]
        public async Task ResolveAsyncWithNoSubjectsReturnsEmptyAsync()
        {
            var resolver = new TrustedIssuerStoreResolver();

            using CertificateCollection resolved = await resolver
                .ResolveAsync([], NUnitTelemetryContext.Create(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task ResolveAsyncWithoutConfigurationReturnsEmptyAsync()
        {
            var resolver = new TrustedIssuerStoreResolver();

            using CertificateCollection resolved = await resolver
                .ResolveAsync(["CN=Root CA"], NUnitTelemetryContext.Create(), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task ResolveAsyncMatchesCaBySubjectAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string subject;
            using (Certificate ca = CreateCaCertificate("CN=MqttTestRootCA"))
            {
                subject = ca.Subject;
                await AddToStoreAsync(ca, telemetry).ConfigureAwait(false);
            }

            var resolver = new TrustedIssuerStoreResolver(CreateConfiguration());
            using CertificateCollection resolved = await resolver
                .ResolveAsync([subject], telemetry, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Has.Count.EqualTo(1));
                Assert.That(resolved[0].Subject, Is.EqualTo(subject));
            });
        }

        [Test]
        public async Task ResolveAsyncMatchesCaByThumbprintAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string thumbprint;
            using (Certificate ca = CreateCaCertificate("CN=MqttTestRootCA"))
            {
                thumbprint = ca.Thumbprint;
                await AddToStoreAsync(ca, telemetry).ConfigureAwait(false);
            }

            var resolver = new TrustedIssuerStoreResolver(CreateConfiguration());
            using CertificateCollection resolved = await resolver
                .ResolveAsync([thumbprint], telemetry, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(resolved, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ResolveAsyncIgnoresUnknownSubjectAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using (Certificate ca = CreateCaCertificate("CN=MqttTestRootCA"))
            {
                await AddToStoreAsync(ca, telemetry).ConfigureAwait(false);
            }

            var resolver = new TrustedIssuerStoreResolver(CreateConfiguration());
            using CertificateCollection resolved = await resolver
                .ResolveAsync(["CN=Does Not Exist"], telemetry, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.Empty);
        }

        [Test]
        public async Task ResolveAsyncReturnsIndependentlyDisposableCollectionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string subject;
            using (Certificate ca = CreateCaCertificate("CN=MqttTestRootCA"))
            {
                subject = ca.Subject;
                await AddToStoreAsync(ca, telemetry).ConfigureAwait(false);
            }

            var resolver = new TrustedIssuerStoreResolver(CreateConfiguration());
            long liveBefore = Certificate.InstancesCreated - Certificate.InstancesDisposed;
            using (CertificateCollection resolved = await resolver
                .ResolveAsync([subject], telemetry, CancellationToken.None)
                .ConfigureAwait(false))
            {
                Assert.That(resolved, Has.Count.EqualTo(1));
            }

            long liveAfter = Certificate.InstancesCreated - Certificate.InstancesDisposed;
            Assert.That(
                liveAfter,
                Is.EqualTo(liveBefore),
                "Disposing the resolved collection must release every resolved handle.");
        }

        private ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                SecurityConfiguration = new SecurityConfiguration
                {
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StorePath = m_storePath,
                        StoreType = "Directory"
                    }
                }
            };
        }

        private async Task AddToStoreAsync(Certificate certificate, ITelemetryContext telemetry)
        {
            var storeIdentifier = new CertificateTrustList
            {
                StorePath = m_storePath,
                StoreType = "Directory"
            };
            using ICertificateStore store = storeIdentifier.OpenStore(telemetry);
            await store.AddAsync(certificate).ConfigureAwait(false);
        }

        private static Certificate CreateCaCertificate(string subjectName)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(subjectName, ecdsa, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(true, false, 0, true));
            return Certificate.From(request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddYears(1)));
        }
    }
}
