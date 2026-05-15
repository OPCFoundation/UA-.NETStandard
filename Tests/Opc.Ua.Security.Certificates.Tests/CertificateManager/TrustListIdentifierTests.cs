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
