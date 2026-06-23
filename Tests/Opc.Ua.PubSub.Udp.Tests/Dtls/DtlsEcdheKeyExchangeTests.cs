#if NET8_0_OR_GREATER
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
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 ECDHE key_share handling from RFC 8446 §4.2.8.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 8446 §4.2.8")]
    [TestSpec("RFC 9147 §5")]
    public sealed class DtlsEcdheKeyExchangeTests
    {
        [TestCase(DtlsNamedCurve.NistP256)]
        [TestCase(DtlsNamedCurve.NistP384)]
        public void SupportedNistGroupsDeriveSameSecret(DtlsNamedCurve curve)
        {
            using var client = new DtlsEcdheKeyExchange(curve);
            using var server = new DtlsEcdheKeyExchange(curve);

            byte[] clientSecret = client.DeriveSharedSecret(server.PublicKey);
            byte[] serverSecret = server.DeriveSharedSecret(client.PublicKey);

            Assert.That(clientSecret, Is.EqualTo(serverSecret));
        }

        [TestCase(DtlsNamedCurve.BrainpoolP256r1)]
        [TestCase(DtlsNamedCurve.BrainpoolP384r1)]
        public void SupportedBrainpoolGroupsDeriveSameSecretWhenPlatformSupportsThem(DtlsNamedCurve curve)
        {
            try
            {
                using var client = new DtlsEcdheKeyExchange(curve);
                using var server = new DtlsEcdheKeyExchange(curve);
                Assert.That(client.DeriveSharedSecret(server.PublicKey), Is.EqualTo(server.DeriveSharedSecret(client.PublicKey)));
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or CryptographicException)
            {
                Assert.Ignore($"Brainpool group {curve} is not supported by this platform: {ex.Message}");
            }
        }

        [Test]
        public void Curve25519AndCurve448FailClosed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => DtlsEcdheKeyExchange.ToEccCurve(DtlsNamedCurve.Curve25519),
                    Throws.TypeOf<DtlsHandshakeException>());
                Assert.That(() => DtlsEcdheKeyExchange.ToEccCurve(DtlsNamedCurve.Curve448),
                    Throws.TypeOf<DtlsHandshakeException>());
            });
        }
    }
}
#endif
