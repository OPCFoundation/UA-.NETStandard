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
        public void TcpTransportListenerImplementsCertificateRotationCapability()
        {
            using var listener = new TcpTransportListener(m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerCertificateRotation>());
        }

        /// <summary>
        /// The HTTPS listener implements the optional rotation capability
        /// so ApplyChanges can drive a Stop+Start cycle on the Kestrel
        /// host that owns the TLS certificate.
        /// </summary>
        [Test]
        public void HttpsTransportListenerImplementsCertificateRotationCapability()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            Assert.That(listener, Is.InstanceOf<ITransportListenerCertificateRotation>());
        }

        /// <summary>
        /// Calling <see cref="ITransportListenerCertificateRotation.CloseChannelsForCertificate"/>
        /// on a fresh, never-opened TCP listener with no channels must
        /// return an empty list rather than throwing.
        /// </summary>
        [Test]
        public void TcpListenerCloseChannelsForCertificateOnEmptyListenerReturnsEmpty()
        {
            using var listener = new TcpTransportListener(m_telemetry);
            using Certificate oldCertificate = CreateSelfSigned("CN=Old");

            System.Collections.Generic.IReadOnlyList<string> closed =
                CallCloseChannelsForCertificate(listener, oldCertificate);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
        }

        /// <summary>
        /// Passing a <c>null</c> certificate is a contract violation and
        /// must surface an <see cref="ArgumentNullException"/> rather
        /// than corrupting the channel map.
        /// </summary>
        [Test]
        public void TcpListenerCloseChannelsForCertificateRejectsNull()
        {
            using var listener = new TcpTransportListener(m_telemetry);

            Assert.That(
                () => CallCloseChannelsForCertificate(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        /// <summary>
        /// HTTPS rotation is a no-op when the listener is not opened —
        /// <see cref="HttpsTransportListener.Stop"/> and
        /// <see cref="HttpsTransportListener.Start"/> must be safe in
        /// that state.
        /// </summary>
        [Test]
        public void HttpsListenerCloseChannelsForCertificateOnUnopenedListenerIsSafe()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
            using Certificate oldCertificate = CreateSelfSigned("CN=Old");

            System.Collections.Generic.IReadOnlyList<string> closed =
                CallCloseChannelsForCertificate(listener, oldCertificate);

            Assert.That(closed, Is.Not.Null);
            Assert.That(closed, Is.Empty);
        }

        /// <summary>
        /// Passing a <c>null</c> certificate to the HTTPS listener is
        /// also a contract violation.
        /// </summary>
        [Test]
        public void HttpsListenerCloseChannelsForCertificateRejectsNull()
        {
            using var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);

            Assert.That(
                () => CallCloseChannelsForCertificate(listener, null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        private static System.Collections.Generic.IReadOnlyList<string> CallCloseChannelsForCertificate(
            ITransportListener listener, Certificate oldCertificate)
        {
            var rotation = (ITransportListenerCertificateRotation)listener;
            return rotation.CloseChannelsForCertificate(oldCertificate);
        }

        private static Certificate CreateSelfSigned(string subjectName)
        {
            return CertificateBuilder.Create(subjectName).CreateForRSA();
        }
    }
}
