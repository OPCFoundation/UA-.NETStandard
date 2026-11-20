/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Unit tests for the <see cref="MqttTlsOptions"/> configuration surface, including the
    /// CA trust-chain reference list added for issue #3920.
    /// </summary>
    [TestFixture]
    public sealed class MqttTlsOptionsTests
    {
        [Test]
        public void TrustedIssuerCertificateSubjectsDefaultsToNull()
        {
            var options = new MqttTlsOptions();

            Assert.That(options.TrustedIssuerCertificateSubjects, Is.Null);
        }

        [Test]
        public void TrustedIssuerCertificateSubjectsRoundTrips()
        {
            string[] subjects = ["CN=Root CA", "1A2B3C"];
            var options = new MqttTlsOptions
            {
                TrustedIssuerCertificateSubjects = subjects
            };

            Assert.That(options.TrustedIssuerCertificateSubjects, Is.EqualTo(subjects));
        }

        [Test]
        public void TrustedIssuerCertificateSubjectsIsIndependentOfClientCertificateSubject()
        {
            var options = new MqttTlsOptions
            {
                ClientCertificateSubject = "CN=Client",
                TrustedIssuerCertificateSubjects = ["CN=Root CA"]
            };

            Assert.Multiple(() =>
            {
                Assert.That(options.ClientCertificateSubject, Is.EqualTo("CN=Client"));
                Assert.That(options.TrustedIssuerCertificateSubjects, Has.Length.EqualTo(1));
                Assert.That(options.ValidateServerCertificate, Is.True);
            });
        }
    }
}
