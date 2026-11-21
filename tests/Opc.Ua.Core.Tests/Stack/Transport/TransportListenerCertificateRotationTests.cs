// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for the
    /// <see cref="ITransportListenerCertificateRotation"/> capability
    /// added to <see cref="TcpTransportListener"/> and
    /// <see cref="HttpsTransportListener"/> per OPC UA Part 12 §7.10.9
    /// (ApplyChanges → force renegotiate affected SecureChannels).
    /// </summary>
    [TestFixture]
    [Category("TransportListenerCertificateRotation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TransportListenerCertificateRotationTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// The TCP listener implements the optional rotation capability
        /// so <c>ConfigurationNodeManager.ApplyChanges</c> can fan out
        /// post-response channel cuts.
        /// </summary>
        [Test]
        public async Task TcpTransportListenerImplementsCertificateRotationCapabilityAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerCertificateRotation>());
        }

        /// <summary>
        /// The HTTPS listener implements the optional rotation capability
        /// so ApplyChanges can drive a Stop+Start cycle on the Kestrel
        /// host that owns the TLS certificate.
        /// </summary>
        [Test]
        public async Task HttpsTransportListenerImplementsCertificateRotationCapabilityAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerCertificateRotation>());
        }

        /// <summary>
        /// Calling <see cref="ITransportListenerCertificateRotation.CloseChannelsForCertificate"/>
        /// on a fresh, never-opened TCP listener with no channels must
        /// return an empty list rather than throwing.
        /// </summary>
        [Test]
        public async Task TcpListenerCloseChannelsForCertificateOnEmptyListenerReturnsEmptyAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);
            using Certificate oldCertificate = CreateSelfSigned("CN=Old");

            System.Collections.Generic.IReadOnlyList<string> closed =
                await CallCloseChannelsForCertificateAsync(listener, oldCertificate).ConfigureAwait(false);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
        }

        /// <summary>
        /// Passing a <c>null</c> certificate is a contract violation and
        /// must surface an <see cref="ArgumentNullException"/> rather
        /// than corrupting the channel map.
        /// </summary>
        [Test]
        public async Task TcpListenerCloseChannelsForCertificateRejectsNullAsync()
        {
            await using var listener = new TcpTransportListener(m_telemetry);

            Assert.That(
                () => CallCloseChannelsForCertificateAsync(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        /// <summary>
        /// HTTPS rotation is a no-op when the listener is not opened —
        /// <see cref="HttpsTransportListener.StopAsync"/> and
        /// <see cref="HttpsTransportListener.StartAsync"/> must be safe in
        /// that state.
        /// </summary>
        [Test]
        public async Task HttpsListenerCloseChannelsForCertificateOnUnopenedListenerIsSafeAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            using Certificate oldCertificate = CreateSelfSigned("CN=Old");

            System.Collections.Generic.IReadOnlyList<string> closed =
                await CallCloseChannelsForCertificateAsync(listener, oldCertificate).ConfigureAwait(false);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
        }

        /// <summary>
        /// Passing a <c>null</c> certificate to the HTTPS listener is
        /// also a contract violation.
        /// </summary>
        [Test]
        public async Task HttpsListenerCloseChannelsForCertificateRejectsNullAsync()
        {
            await using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);

            Assert.That(
                () => CallCloseChannelsForCertificateAsync(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        private static async Task<System.Collections.Generic.IReadOnlyList<string>> CallCloseChannelsForCertificateAsync(
            ITransportListener listener, Certificate oldCertificate)
        {
            var rotation = (ITransportListenerCertificateRotation)listener;
            return await rotation.CloseChannelsForCertificateAsync(oldCertificate).ConfigureAwait(false);
        }

        private static Certificate CreateSelfSigned(string subjectName)
        {
            return CertificateBuilder.Create(subjectName).CreateForRSA();
        }
    }
}
