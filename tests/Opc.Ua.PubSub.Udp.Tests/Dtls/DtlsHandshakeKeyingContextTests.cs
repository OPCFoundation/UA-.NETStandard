// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            if (!DtlsRecordProtection.IsAeadSupported)
            {
                Assert.Ignore(
                    "AEAD DTLS record protection requires .NET 8 or later; the stack assembly " +
                    "under test was compiled without it.");
            }

            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            FillRandom(shared);
            byte[] handshakeHash = DtlsHkdf.HashData(HashAlgorithmName.SHA256, [1, 2]);
            byte[] applicationHash = DtlsHkdf.HashData(HashAlgorithmName.SHA256, [1, 2, 3]);
            using var client = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);
            using var server = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);

            byte[] finished = client.ComputeClientFinished(applicationHash);
            server.VerifyFinished(server.ComputeClientFinished(applicationHash), finished);
            using DtlsRecordProtection writer = client.CreateClientApplicationWriteProtection();
            using DtlsRecordProtection reader = server.CreateClientApplicationWriteProtection();

            Assert.That(reader.Open(writer.Seal([0x55])), Is.EqualTo("U"u8.ToArray()));
        }

        [Test]
        public void KeyUpdateChangesTrafficSecretAndOldKeysRejectNewRecords()
        {
            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            FillRandom(shared);
            byte[] hash = DtlsHkdf.HashData(HashAlgorithmName.SHA256, [7]);
            using var context = new DtlsHandshakeKeyingContext(profile, shared, hash, hash);
            byte[] before = (byte[])context.Secrets.ClientApplicationTrafficSecret.Clone();

            context.UpdateApplicationTrafficSecret(client: true);

            Assert.That(context.Secrets.ClientApplicationTrafficSecret, Is.Not.EqualTo(before));
        }

        private static void FillRandom(byte[] buffer)
        {
            using RandomNumberGenerator random = RandomNumberGenerator.Create();
            random.GetBytes(buffer);
        }
    }
}
