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
