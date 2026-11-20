// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Stress.Tests.Channels.Fakes;

namespace Opc.Ua.Stress.Tests.Channels.Contract
{
    /// <summary>
    /// Verifies the fake transport's ownership of successfully opened server certificates.
    /// </summary>
    [TestFixture]
    [Category("Contract")]
    [Category("Certificates")]
    [Parallelizable]
    public sealed class FakeTransportCertificateOwnershipTests : ContractTestBase
    {
        /// <summary>
        /// Verifies a successfully opened transport releases its adopted server certificate on close or disposal.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SuccessfulOpenReleasesServerCertificateOnCloseOrDisposeAsync(bool close)
        {
            using Certificate certificate = CreateCertificate("fake-server-certificate");
            string thumbprint = certificate.Thumbprint;
            using var transport = new FakeTransport();
            var settings = new TransportChannelSettings { ServerCertificate = certificate };

            await transport.OpenAsync(new Uri(DefaultEndpointUrl), settings, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(certificate.Thumbprint, Is.EqualTo(thumbprint));

            if (close)
            {
                await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                transport.Dispose();
            }

            AssertCertificateReleased(certificate);
        }

        /// <summary>
        /// Verifies a failed open leaves the server certificate owned and usable by the caller.
        /// </summary>
        [Test]
        public async Task FailedOpenLeavesServerCertificateWithCaller()
        {
            using Certificate certificate = CreateCertificate("fake-server-open-failed");
            string thumbprint = certificate.Thumbprint;
            using var transport = new FakeTransport();
            transport.ConfigureFault(FaultMode.OpenFails);
            var settings = new TransportChannelSettings { ServerCertificate = certificate };

            await Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.OpenAsync(new Uri(DefaultEndpointUrl), settings, CancellationToken.None)
                    .ConfigureAwait(false));
            transport.Dispose();

            using Certificate retained = certificate.AddRef();
            Assert.That(retained.Thumbprint, Is.EqualTo(thumbprint));
        }

        /// <summary>
        /// Verifies reopening with a different certificate releases the previous certificate and adopts the new one.
        /// </summary>
        [Test]
        public async Task SuccessfulReopenReleasesPreviousServerCertificateAsync()
        {
            using Certificate first = CreateCertificate("fake-server-first");
            using Certificate second = CreateCertificate("fake-server-second");
            string secondThumbprint = second.Thumbprint;
            using var transport = new FakeTransport();

            await transport.OpenAsync(
                new Uri(DefaultEndpointUrl),
                new TransportChannelSettings { ServerCertificate = first },
                CancellationToken.None).ConfigureAwait(false);
            await transport.OpenAsync(
                new Uri(DefaultEndpointUrl),
                new TransportChannelSettings { ServerCertificate = second },
                CancellationToken.None).ConfigureAwait(false);

            AssertCertificateReleased(first);
            Assert.That(second.Thumbprint, Is.EqualTo(secondThumbprint));
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            AssertCertificateReleased(second);
        }

        /// <summary>
        /// Verifies reopening with the same certificate does not prematurely dispose its adopted handle.
        /// </summary>
        [Test]
        public async Task SuccessfulReopenWithSameServerCertificateRetainsOwnershipAsync()
        {
            using Certificate certificate = CreateCertificate("fake-server-same");
            string thumbprint = certificate.Thumbprint;
            using var transport = new FakeTransport();
            var settings = new TransportChannelSettings { ServerCertificate = certificate };

            await transport.OpenAsync(new Uri(DefaultEndpointUrl), settings, CancellationToken.None)
                .ConfigureAwait(false);
            await transport.OpenAsync(new Uri(DefaultEndpointUrl), settings, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(certificate.Thumbprint, Is.EqualTo(thumbprint));
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            AssertCertificateReleased(certificate);
        }

        /// <summary>
        /// Requires the released certificate handle to reject new ownership references.
        /// </summary>
        private static void AssertCertificateReleased(Certificate certificate)
        {
            Assert.Throws<ObjectDisposedException>(() =>
            {
                using Certificate unexpected = certificate.AddRef();
            });
        }
    }
}
