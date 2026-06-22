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

using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Security.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Security.Dtls
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
            RandomNumberGenerator.Fill(sharedSecret);
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

            transcriptA.Append(new byte[] { 0x01, 0x00, 0x00, 0x00 });
            transcriptB.Append(new byte[] { 0x02, 0x00, 0x00, 0x00 });

            Assert.That(transcriptA.GetHash(), Is.Not.EqualTo(transcriptB.GetHash()));
        }

        [Test]
        public void FinishedMacVerifiesWithConstantTimeComparison()
        {
            DtlsKeySchedule schedule = new(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] secret = new byte[32];
            RandomNumberGenerator.Fill(secret);
            byte[] transcriptHash = BuildTranscriptHash(HashAlgorithmName.SHA256, 0x01, 0x02, 0x08);
            byte[] finishedKey = schedule.FinishedKey(secret);
            byte[] verifyData = schedule.ComputeFinished(finishedKey, transcriptHash);
            byte[] verifyDataAgain = schedule.ComputeFinished(finishedKey, transcriptHash);

            Assert.That(Opc.Ua.PubSub.Udp.Security.Dtls.CryptographicOperations.FixedTimeEquals(verifyData, verifyDataAgain), Is.True);
        }

        private static byte[] BuildTranscriptHash(HashAlgorithmName hashAlgorithmName, params byte[] bytes)
        {
            var transcript = new DtlsTranscriptHash(hashAlgorithmName);
            transcript.Append(bytes);
            return transcript.GetHash();
        }
    }
}

