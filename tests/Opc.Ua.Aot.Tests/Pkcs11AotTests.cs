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
using System.Threading.Tasks;
using Opc.Ua.Security.Pkcs11;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises the PKCS#11 package under Native AOT.
    /// </summary>
    /// <remarks>
    /// <c>Pkcs11Interop</c> carries no trim or AOT annotations, so whether this
    /// package survives trimming is a question that has to be answered by
    /// running it rather than by reading the manifest. Everything short of
    /// touching a device is covered here: store selection, URI parsing and
    /// provider metadata. Loading a real module is left to the PKCS#11 test
    /// suite, which needs hardware or SoftHSM2.
    /// </remarks>
    public class Pkcs11AotTests
    {
        [Test]
        public async Task StoreProviderClaimsPkcs11PathsAsync()
        {
            var provider = new Pkcs11StoreProvider();

            await Assert.That(provider.StoreTypeName)
                .IsEqualTo(Pkcs11CertificateStore.StoreTypeName);
            await Assert.That(provider.SupportsStorePath("pkcs11:token=aot")).IsTrue();
            await Assert.That(provider.SupportsStorePath("/tmp/pki/own")).IsFalse();
        }

        [Test]
        public async Task UriParsingSurvivesTrimmingAsync()
        {
            Pkcs11TokenOptions options = Pkcs11TokenOptions.Parse(
                "pkcs11:token=aot;object=server;id=%01%A2?module-path=/tmp/m.so&pin-value=1234");

            await Assert.That(options.TokenLabel).IsEqualTo("aot");
            await Assert.That(options.ObjectLabel).IsEqualTo("server");
            await Assert.That(options.ModulePath).IsEqualTo("/tmp/m.so");
            await Assert.That(options.GetPin()).IsEqualTo("1234");
            await Assert.That(options.ObjectId.ToArray().Length).IsEqualTo(2);
            await Assert.That(options.ObjectId.ToArray()[0]).IsEqualTo((byte)0x01);
            await Assert.That(options.ObjectId.ToArray()[1]).IsEqualTo((byte)0xA2);
        }

        [Test]
        public async Task CryptoProviderReportsUncertifiedAsync()
        {
            var provider = new Pkcs11CryptoProvider();

            await Assert.That(provider.Name).IsEqualTo("PKCS11");
            await Assert.That(provider.Validation.Level)
                .IsEqualTo(CryptoValidationLevel.Uncertified);
            await Assert.That(provider.Validation.IsAcceptableForFips).IsFalse();
            await Assert.That(provider.Capabilities.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task StoreRejectsANonPkcs11PathAsync()
        {
            using var store = new Pkcs11CertificateStore(DefaultTelemetry.Create(_ => { }));

            await Assert.That(() => store.Open("/tmp/pki/own"))
                .Throws<ArgumentException>();
        }
    }
}
