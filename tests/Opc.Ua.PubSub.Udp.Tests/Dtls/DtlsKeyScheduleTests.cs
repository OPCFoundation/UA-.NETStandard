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
    /// Tests TLS 1.3 key schedule behavior from RFC 8446 §7.1.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 8446 §4.4.1")]
    [TestSpec("RFC 8446 §7.1")]
    public sealed class DtlsKeyScheduleTests
    {
        [TestCase(DtlsCipherSuite.TlsAes128GcmSha256)]
        [TestCase(DtlsCipherSuite.TlsAes256GcmSha384)]
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        public void BothPeersDeriveIdenticalTrafficSecrets(DtlsCipherSuite cipherSuite)
        {
            byte[] sharedSecret = new byte[32];
            FillRandom(sharedSecret);
            DtlsKeySchedule clientSchedule = new(cipherSuite);
            DtlsKeySchedule serverSchedule = new(cipherSuite);
            byte[] handshakeHash = BuildTranscriptHash(clientSchedule.HashAlgorithmName, 0x01, 0x02);
            byte[] applicationHash = BuildTranscriptHash(clientSchedule.HashAlgorithmName, 0x01, 0x02, 0x14);

            DtlsTrafficSecrets clientSecrets = clientSchedule.DeriveTrafficSecrets(
                sharedSecret,
                handshakeHash,
                applicationHash);
            DtlsTrafficSecrets serverSecrets = serverSchedule.DeriveTrafficSecrets(
                sharedSecret,
                handshakeHash,
                applicationHash);

            Assert.Multiple(() =>
            {
                Assert.That(clientSecrets.ClientHandshakeTrafficSecret, Is.EqualTo(serverSecrets.ClientHandshakeTrafficSecret));
                Assert.That(clientSecrets.ServerHandshakeTrafficSecret, Is.EqualTo(serverSecrets.ServerHandshakeTrafficSecret));
                Assert.That(clientSecrets.ClientApplicationTrafficSecret, Is.EqualTo(serverSecrets.ClientApplicationTrafficSecret));
                Assert.That(clientSecrets.ServerApplicationTrafficSecret, Is.EqualTo(serverSecrets.ServerApplicationTrafficSecret));
                Assert.That(clientSecrets.ClientFinishedKey, Is.EqualTo(serverSecrets.ClientFinishedKey));
                Assert.That(clientSecrets.ServerFinishedKey, Is.EqualTo(serverSecrets.ServerFinishedKey));
            });
        }

        [Test]
        public void TranscriptHashChangesWhenHandshakeMessagesChange()
        {
            var transcriptA = new DtlsTranscriptHash(HashAlgorithmName.SHA256);
            var transcriptB = new DtlsTranscriptHash(HashAlgorithmName.SHA256);

            transcriptA.Append([0x01, 0x00, 0x00, 0x00]);
            transcriptB.Append([0x02, 0x00, 0x00, 0x00]);

            Assert.That(transcriptA.GetHash(), Is.Not.EqualTo(transcriptB.GetHash()));
        }

        [Test]
        public void FinishedMacVerifiesWithConstantTimeComparison()
        {
            DtlsKeySchedule schedule = new(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] secret = new byte[32];
            FillRandom(secret);
            byte[] transcriptHash = BuildTranscriptHash(HashAlgorithmName.SHA256, 0x01, 0x02, 0x08);
            byte[] finishedKey = schedule.FinishedKey(secret);
            byte[] verifyData = schedule.ComputeFinished(finishedKey, transcriptHash);
            byte[] verifyDataAgain = schedule.ComputeFinished(finishedKey, transcriptHash);

            Assert.That(CryptoUtils.FixedTimeEquals(verifyData, verifyDataAgain), Is.True);
        }

        private static byte[] BuildTranscriptHash(HashAlgorithmName hashAlgorithmName, params byte[] bytes)
        {
            var transcript = new DtlsTranscriptHash(hashAlgorithmName);
            transcript.Append(bytes);
            return transcript.GetHash();
        }

        private static void FillRandom(byte[] buffer)
        {
#if NET8_0_OR_GREATER
            RandomNumberGenerator.Fill(buffer);
#else
            using RandomNumberGenerator random = RandomNumberGenerator.Create();
            random.GetBytes(buffer);
#endif
        }
    }
}
