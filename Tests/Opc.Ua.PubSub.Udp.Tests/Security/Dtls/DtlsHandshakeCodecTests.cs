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
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Security.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Security.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 handshake message encoding from RFC 9147 §5 and RFC 8446 §4.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 9147 §5")]
    [TestSpec("RFC 8446 §4")]
    public sealed class DtlsHandshakeCodecTests
    {
        [Test]
        public void ClientHelloRoundTripsWithDtls13Extensions()
        {
            DtlsClientHello hello = CreateClientHello();

            byte[] encoded = DtlsHandshakeCodec.EncodeClientHello(hello);
            DtlsClientHello decoded = DtlsHandshakeCodec.DecodeClientHello(encoded);

            Assert.Multiple(() =>
            {
                Assert.That(decoded.Random, Is.EqualTo(hello.Random));
                Assert.That(decoded.CipherSuites, Is.EqualTo(hello.CipherSuites));
                Assert.That(decoded.Extensions.SupportedVersions, Does.Contain(DtlsHandshakeCodec.Dtls13Version));
                Assert.That(decoded.Extensions.SupportedGroups, Does.Contain(DtlsNamedCurve.NistP256));
                Assert.That(decoded.Extensions.KeyShares[0].Group, Is.EqualTo(DtlsNamedCurve.NistP256));
                Assert.That(decoded.Extensions.Cookie, Is.EqualTo(new byte[] { 0x10, 0x11 }));
            });
        }

        [Test]
        public void ServerHelloRoundTripsWithSelectedCipherAndKeyShare()
        {
            byte[] random = CreateRandom(0x22);
            var hello = new DtlsServerHello(
                random,
                [0x01],
                DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsHelloExtensions.CreateDefault(
                    [DtlsNamedCurve.NistP256],
                    [new DtlsKeyShareEntry(DtlsNamedCurve.NistP256, [0x04, 0x05])],
                    cookie: null));

            DtlsServerHello decoded = DtlsHandshakeCodec.DecodeServerHello(DtlsHandshakeCodec.EncodeServerHello(hello));

            Assert.Multiple(() =>
            {
                Assert.That(decoded.Random, Is.EqualTo(random));
                Assert.That(decoded.CipherSuite, Is.EqualTo(DtlsCipherSuite.TlsAes128GcmSha256));
                Assert.That(decoded.Extensions.KeyShares[0].KeyExchange, Is.EqualTo(new byte[] { 0x04, 0x05 }));
            });
        }

        [Test]
        public void HandshakeFrameRoundTripsMessageSequenceAndFragment()
        {
            byte[] body = [0x01, 0x02, 0x03];

            byte[] encoded = DtlsHandshakeCodec.EncodeFrame(DtlsHandshakeType.ClientHello, 7, body);
            DtlsHandshakeFrame frame = DtlsHandshakeCodec.DecodeFrame(encoded);

            Assert.Multiple(() =>
            {
                Assert.That(frame.MessageType, Is.EqualTo(DtlsHandshakeType.ClientHello));
                Assert.That(frame.MessageSequence, Is.EqualTo(7));
                Assert.That(frame.FragmentOffset, Is.Zero);
                Assert.That(frame.Fragment, Is.EqualTo(body));
            });
        }

        [Test]
        public void UnsupportedVersionAndCurve25519FailClosed()
        {
            DtlsClientHello hello = CreateClientHello([0xfefc]);
            byte[] encoded = DtlsHandshakeCodec.EncodeClientHello(hello);

            Assert.Multiple(() =>
            {
                Assert.That(() => DtlsHandshakeCodec.DecodeClientHello(encoded), Throws.TypeOf<DtlsHandshakeException>());
                Assert.That(() => DtlsHandshakeCodec.ToWireNamedGroup(DtlsNamedCurve.Curve25519),
                    Throws.TypeOf<DtlsHandshakeException>());
                Assert.That(() => DtlsHandshakeCodec.FromWireNamedGroup(0x001d),
                    Throws.TypeOf<DtlsHandshakeException>());
            });
        }

        private static DtlsClientHello CreateClientHello(ushort[]? versions = null)
        {
            return new DtlsClientHello(
                CreateRandom(0x11),
                [0x01, 0x02],
                [DtlsCipherSuite.TlsAes128GcmSha256, DtlsCipherSuite.TlsSha256Sha256],
                new DtlsHelloExtensions(
                    versions ?? [DtlsHandshakeCodec.Dtls13Version],
                    [DtlsNamedCurve.NistP256, DtlsNamedCurve.BrainpoolP256r1],
                    [new DtlsKeyShareEntry(DtlsNamedCurve.NistP256, [0x04, 0x01, 0x02])],
                    [DtlsSignatureScheme.EcdsaSecp256r1Sha256],
                    [0x10, 0x11]));
        }

        private static byte[] CreateRandom(byte seed)
        {
            byte[] random = new byte[32];
            for (int ii = 0; ii < random.Length; ii++)
            {
                random[ii] = (byte)(seed + ii);
            }

            return random;
        }
    }
}

