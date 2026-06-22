#if NET8_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Security.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Security.Dtls
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
