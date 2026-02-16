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

using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("X509IdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class X509IdentityTokenHandlerTests
    {
        [Test]
        public void CopyPreservesPrivateKeyForSigning()
        {
            using X509Certificate2 cert = CertificateBuilder
                .Create("CN=User Identity Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var tokenHandler = new X509IdentityTokenHandler(cert);
            using X509IdentityTokenHandler copy = tokenHandler.Copy();

            Assert.IsTrue(copy.Certificate.HasPrivateKey);

            SignatureData signature = copy.Sign(
                [0x01, 0x02, 0x03, 0x04],
                SecurityPolicies.Basic256Sha256);

            Assert.NotNull(signature);
            Assert.NotNull(signature.Signature);
            Assert.Greater(signature.Signature.Length, 0);
        }
    }
}
