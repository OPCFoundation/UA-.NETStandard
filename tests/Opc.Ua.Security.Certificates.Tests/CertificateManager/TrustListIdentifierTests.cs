/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class TrustListIdentifierTests
    {
        [Test]
        public void WellKnownConstantsHaveCorrectNames()
        {
            Assert.That(TrustListIdentifier.Peers.Name, Is.EqualTo("Peers"));
            Assert.That(TrustListIdentifier.Users.Name, Is.EqualTo("Users"));
            Assert.That(TrustListIdentifier.Https.Name, Is.EqualTo("Https"));
            Assert.That(TrustListIdentifier.Rejected.Name, Is.EqualTo("Rejected"));
        }

        [Test]
        public void EqualityByName()
        {
            var a = new TrustListIdentifier("Custom");
            var b = new TrustListIdentifier("Custom");
            var c = new TrustListIdentifier("Other");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void CustomNameWorks()
        {
            var custom = new TrustListIdentifier("MyTrustList");
            Assert.That(custom.Name, Is.EqualTo("MyTrustList"));
        }

        [Test]
        public void ToStringReturnsName()
        {
            Assert.That(TrustListIdentifier.Peers.ToString(), Is.EqualTo("Peers"));

            var custom = new TrustListIdentifier("Custom");
            Assert.That(custom.ToString(), Is.EqualTo("Custom"));
        }
    }
}
