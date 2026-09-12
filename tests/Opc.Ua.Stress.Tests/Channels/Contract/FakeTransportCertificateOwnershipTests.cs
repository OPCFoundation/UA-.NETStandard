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

        [Test]
        public void FailedOpenLeavesServerCertificateWithCaller()
        {
            using Certificate certificate = CreateCertificate("fake-server-open-failed");
            string thumbprint = certificate.Thumbprint;
            using var transport = new FakeTransport();
            transport.ConfigureFault(FaultMode.OpenFails);
            var settings = new TransportChannelSettings { ServerCertificate = certificate };

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.OpenAsync(new Uri(DefaultEndpointUrl), settings, CancellationToken.None)
                    .ConfigureAwait(false));
            transport.Dispose();

            using Certificate retained = certificate.AddRef();
            Assert.That(retained.Thumbprint, Is.EqualTo(thumbprint));
        }

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

        private static void AssertCertificateReleased(Certificate certificate)
        {
            Assert.Throws<ObjectDisposedException>(() =>
            {
                using Certificate unexpected = certificate.AddRef();
            });
        }
    }
}
