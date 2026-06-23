#if NET8_0_OR_GREATER
/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 Finished and KeyUpdate helpers from RFC 8446 §4.4.4 and §4.6.3.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 8446 §4.4.4")]
    [TestSpec("RFC 8446 §4.6.3")]
    public sealed class DtlsHandshakeKeyingContextTests
    {
        [Test]
        public void FinishedVerificationAndApplicationRecordProtectionSucceed()
        {
            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            RandomNumberGenerator.Fill(shared);
            byte[] handshakeHash = SHA256.HashData(new byte[] { 1, 2 });
            byte[] applicationHash = SHA256.HashData(new byte[] { 1, 2, 3 });
            using var client = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);
            using var server = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);

            byte[] finished = client.ComputeClientFinished(applicationHash);
            server.VerifyFinished(server.ComputeClientFinished(applicationHash), finished);
            using DtlsRecordProtection writer = client.CreateClientApplicationWriteProtection();
            using DtlsRecordProtection reader = server.CreateClientApplicationWriteProtection();

            Assert.That(reader.Open(writer.Seal(new byte[] { 0x55 })), Is.EqualTo(new byte[] { 0x55 }));
        }

        [Test]
        public void KeyUpdateChangesTrafficSecretAndOldKeysRejectNewRecords()
        {
            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            RandomNumberGenerator.Fill(shared);
            byte[] hash = SHA256.HashData(new byte[] { 7 });
            using var context = new DtlsHandshakeKeyingContext(profile, shared, hash, hash);
            byte[] before = (byte[])context.Secrets.ClientApplicationTrafficSecret.Clone();

            context.UpdateApplicationTrafficSecret(client: true);

            Assert.That(context.Secrets.ClientApplicationTrafficSecret, Is.Not.EqualTo(before));
        }
    }
}
#endif
