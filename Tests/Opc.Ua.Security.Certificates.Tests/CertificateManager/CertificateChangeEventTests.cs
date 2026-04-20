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

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateChangeEventTests
    {
        [Test]
        public void RecordEqualityWorks()
        {
            var trustList = TrustListIdentifier.Peers;
            var certType = new NodeId(1);

            var a = new CertificateChangeEvent(
                CertificateChangeKind.TrustListUpdated,
                trustList,
                certType,
                null,
                null,
                null);

            var b = new CertificateChangeEvent(
                CertificateChangeKind.TrustListUpdated,
                trustList,
                certType,
                null,
                null,
                null);

            var c = new CertificateChangeEvent(
                CertificateChangeKind.CrlUpdated,
                trustList,
                certType,
                null,
                null,
                null);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void KindValuesExist()
        {
            var values = Enum.GetValues(typeof(CertificateChangeKind));
            Assert.That(values, Has.Length.GreaterThanOrEqualTo(5));

            Assert.That(
                Enum.IsDefined(typeof(CertificateChangeKind),
                    CertificateChangeKind.ApplicationCertificateUpdated),
                Is.True);
            Assert.That(
                Enum.IsDefined(typeof(CertificateChangeKind),
                    CertificateChangeKind.TrustListUpdated),
                Is.True);
            Assert.That(
                Enum.IsDefined(typeof(CertificateChangeKind),
                    CertificateChangeKind.CrlUpdated),
                Is.True);
            Assert.That(
                Enum.IsDefined(typeof(CertificateChangeKind),
                    CertificateChangeKind.CertificateRejected),
                Is.True);
            Assert.That(
                Enum.IsDefined(typeof(CertificateChangeKind),
                    CertificateChangeKind.CertificateExpiring),
                Is.True);
        }

        [Test]
        public void PropertiesMatchConstructorArgs()
        {
            var trustList = TrustListIdentifier.Https;
            var certType = new NodeId(42);

            using Certificate oldCert = CertificateBuilder
                .Create("CN=OldCert")
                .CreateForRSA();

            using Certificate newCert = CertificateBuilder
                .Create("CN=NewCert")
                .CreateForRSA();

            using var chain = new CertificateCollection();

            var evt = new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                trustList,
                certType,
                oldCert,
                newCert,
                chain);

            Assert.That(evt.Kind,
                Is.EqualTo(CertificateChangeKind.ApplicationCertificateUpdated));
            Assert.That(evt.TrustList, Is.EqualTo(trustList));
            Assert.That(evt.CertificateType, Is.EqualTo(certType));
            Assert.That(evt.OldCertificate, Is.SameAs(oldCert));
            Assert.That(evt.NewCertificate, Is.SameAs(newCert));
            Assert.That(evt.IssuerChain, Is.SameAs(chain));
        }
    }
}
