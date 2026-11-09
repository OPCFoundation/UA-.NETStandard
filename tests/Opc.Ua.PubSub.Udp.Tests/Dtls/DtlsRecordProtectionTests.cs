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
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 record protection mechanics from RFC 9147 §4 and §4.5.1.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 9147 §4")]
    [TestSpec("RFC 9147 §4.5.1")]
    [TestSpec("RFC 8446 §5.3")]
    public sealed class DtlsRecordProtectionTests
    {
        [TestCase(DtlsCipherSuite.TlsAes128GcmSha256)]
        [TestCase(DtlsCipherSuite.TlsAes256GcmSha384)]
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        public void SealOpenRoundTripSucceeds(DtlsCipherSuite cipherSuite)
        {
            SkipIfAeadUnsupported(cipherSuite);
            byte[] secret = CreateSecret(cipherSuite);
            byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];
            using var writer = new DtlsRecordProtection(CreateProfile(cipherSuite), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(CreateProfile(cipherSuite), secret, epoch: 1);

            byte[] record = writer.Seal(payload);
            byte[] opened = reader.Open(record);

            Assert.That(opened, Is.EqualTo(payload));
        }

        [Test]
        public void SealOpenChaChaRoundTripSucceedsWhenSupported()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsChaCha20Poly1305Sha256);
#if NET8_0_OR_GREATER
            if (!ChaCha20Poly1305.IsSupported)
            {
                Assert.Ignore("ChaCha20-Poly1305 is not supported by this platform.");
            }
#endif

            byte[] secret = CreateSecret(DtlsCipherSuite.TlsChaCha20Poly1305Sha256);
            byte[] payload = [0x10, 0x20, 0x30];
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsChaCha20Poly1305Sha256), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsChaCha20Poly1305Sha256), secret, epoch: 1);

            Assert.That(reader.Open(writer.Seal(payload)), Is.EqualTo(payload));
        }

        [Test]
        public void OpenReplayThrowsCryptographicException()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] payload = [0xaa, 0xbb, 0xcc];
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);

            byte[] record = writer.Seal(payload);
            Assert.That(reader.Open(record), Is.EqualTo(payload));
            Assert.That(() => reader.Open(record), Throws.TypeOf<CryptographicException>());
        }

        [Test]
        public void AntiReplayWindowRejectsDuplicateAndTooOldRecords()
        {
            var window = new DtlsAntiReplayWindow(windowSize: 4);

            Assert.Multiple(() =>
            {
                Assert.That(window.TryAccept(10), Is.True);
                Assert.That(window.TryAccept(10), Is.False);
                Assert.That(window.TryAccept(13), Is.True);
                Assert.That(window.TryAccept(9), Is.False);
                Assert.That(window.TryAccept(12), Is.True);
            });
        }

        [Test]
        public void ForgedRecordDoesNotPoisonReplayWindow()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] payload = [0x01, 0x02, 0x03, 0x04];
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);

            byte[] genuine = writer.Seal(payload);
            byte[] forged = (byte[])genuine.Clone();
            forged[^1] ^= 0xff;

            Assert.Multiple(() =>
            {
                Assert.That(() => reader.Open(forged), Throws.TypeOf<CryptographicException>(),
                    "SA-DTLS-CRYPTO-04: a forged record must fail authentication.");
                Assert.That(reader.Open(genuine), Is.EqualTo(payload),
                    "SA-DTLS-CRYPTO-04: the anti-replay window must not be advanced by the forged record, " +
                    "so the genuine record at the same sequence number is still accepted.");
            });
        }

        [TestCase(DtlsCipherSuite.TlsAes128GcmSha256)]
        [TestCase(DtlsCipherSuite.TlsAes256GcmSha384)]
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        public void SequenceNumberMaskRoundTripsAcrossRecords(DtlsCipherSuite cipherSuite)
        {
            SkipIfAeadUnsupported(cipherSuite);
            byte[] secret = CreateSecret(cipherSuite);
            using var writer = new DtlsRecordProtection(CreateProfile(cipherSuite), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(CreateProfile(cipherSuite), secret, epoch: 1);

            for (int i = 0; i < 6; i++)
            {
                byte[] payload = [(byte)i, (byte)(i + 1), (byte)(i + 2)];
                byte[] record = writer.Seal(payload);
                Assert.That(reader.Open(record), Is.EqualTo(payload),
                    "SA-DTLS-CRYPTO-01: the ciphertext-derived sequence-number mask must round-trip per record.");
            }
        }

        [Test]
        public void SequenceNumberReconstructionSurvivesSixteenBitWraparound()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsAes128GcmSha256);
            // SA-DTLS-CRYPTO-03: the 16-bit on-wire sequence number must be
            // reconstructed to the sender's full 64-bit counter, so records keep
            // decrypting past 2^16 in an epoch (the AEAD nonce stays aligned).
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);

            byte[] payload = [0xAA, 0xBB, 0xCC];
            for (int i = 0; i <= 0x10003; i++)
            {
                byte[] record = writer.Seal(payload);
                Assert.That(reader.Open(record), Is.EqualTo(payload),
                    "Record at sequence " + i + " must decrypt after 16-bit wraparound.");
            }
        }

        /// <summary>
        /// Captured records cannot authenticate in the next truncated sequence cycle or consume its genuine record.
        /// </summary>
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        [TestCase(DtlsCipherSuite.TlsAes128GcmSha256)]
        [Category("SecurityRegression")]
        public void SecurityF20CapturedRecordCannotCrossSequenceCycle(DtlsCipherSuite cipherSuite)
        {
            SkipIfAeadUnsupported(cipherSuite);
            byte[] secret = CreateSecret(cipherSuite);
            DtlsProfile profile = CreateProfile(cipherSuite);
            using var originalWriter = new DtlsRecordProtection(profile, secret, epoch: 1);
            using var rolloverWriter = new DtlsRecordProtection(
                profile, secret, epoch: 1, initialWriteSequenceNumber: ushort.MaxValue);
            using var reader = new DtlsRecordProtection(profile, secret, epoch: 1);
            byte[] originalPayload = [0x00, 0x11];
            byte[] lastPayload = [0xff, 0xff];
            byte[] rolloverPayload = [0x01, 0x00, 0x00];
            byte[] nextPayload = [0x01, 0x00, 0x01];
            byte[] captured = originalWriter.Seal(originalPayload);

            Assert.That(reader.Open(captured), Is.EqualTo(originalPayload));
            Assert.That(reader.Open(rolloverWriter.Seal(lastPayload)), Is.EqualTo(lastPayload));
            byte[] rolloverRecord = rolloverWriter.Seal(rolloverPayload);

            Assert.Multiple(() =>
            {
                Assert.That(reader.TryOpen(captured, out byte[]? replayed), Is.False,
                    "Sequence zero must not authenticate as sequence 65536 after the truncated counter wraps.");
                Assert.That(replayed, Is.Null);
            });
            Assert.That(reader.Open(rolloverRecord), Is.EqualTo(rolloverPayload),
                "Rejecting the replay must not consume the genuine sequence 65536.");
            Assert.That(reader.Open(rolloverWriter.Seal(nextPayload)), Is.EqualTo(nextPayload));
            Assert.That(reader.TryOpen(rolloverRecord, out _), Is.False,
                "A genuine record is still accepted only once.");
        }

        /// <summary>
        /// Integrity authentication distinguishes full epochs with the same two on-wire epoch bits.
        /// </summary>
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        [Category("SecurityRegression")]
        public void SecurityF20IntegrityMacBindsFullEpoch(DtlsCipherSuite cipherSuite)
        {
            byte[] secret = CreateSecret(cipherSuite);
            DtlsProfile profile = CreateProfile(cipherSuite);
            using var previousWriter = new DtlsRecordProtection(profile, secret, epoch: 1);
            using var currentWriter = new DtlsRecordProtection(profile, secret, epoch: 5);
            using var reader = new DtlsRecordProtection(profile, secret, epoch: 5);
            byte[] payload = [0x45, 0x50, 0x4f, 0x43, 0x48];
            byte[] previousRecord = previousWriter.Seal(payload);
            byte[] currentRecord = currentWriter.Seal(payload);

            Assert.Multiple(() =>
            {
                Assert.That(currentRecord, Is.Not.EqualTo(previousRecord),
                    "Epochs with identical truncated wire bits must produce different integrity tags.");
                Assert.That(reader.TryOpen(previousRecord, out byte[]? replayed), Is.False);
                Assert.That(replayed, Is.Null);
            });
            Assert.That(reader.Open(currentRecord), Is.EqualTo(payload),
                "An earlier epoch must not poison the current epoch's replay window.");
        }

        /// <summary>
        /// A record authenticated with previous traffic keys cannot block the current key's genuine record.
        /// </summary>
        [TestCase(DtlsCipherSuite.TlsSha256Sha256)]
        [TestCase(DtlsCipherSuite.TlsSha384Sha384)]
        [Category("SecurityRegression")]
        public void SecurityF20PreviousTrafficKeyCannotPoisonReplayWindow(DtlsCipherSuite cipherSuite)
        {
            byte[] previousSecret = CreateSecret(cipherSuite);
            byte[] currentSecret = [.. previousSecret];
            currentSecret[0] ^= 0xff;
            DtlsProfile profile = CreateProfile(cipherSuite);
            using var previousWriter = new DtlsRecordProtection(profile, previousSecret, epoch: 1);
            using var currentWriter = new DtlsRecordProtection(profile, currentSecret, epoch: 1);
            using var reader = new DtlsRecordProtection(profile, currentSecret, epoch: 1);
            byte[] payload = [0x4b, 0x45, 0x59];

            Assert.Multiple(() =>
            {
                Assert.That(reader.TryOpen(previousWriter.Seal(payload), out byte[]? rejected), Is.False);
                Assert.That(rejected, Is.Null);
            });
            Assert.That(reader.Open(currentWriter.Seal(payload)), Is.EqualTo(payload));
        }

        [Test]
        public async Task ParallelAeadProtectionSerializesSequenceAndCryptoUseAsync()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsAes128GcmSha256);
            const int recordCount = 64;
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);
            using var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256), secret, epoch: 1);
            var startSealing = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<byte[]>[] sealTasks = [.. Enumerable.Range(0, recordCount)
                .Select(index => Task.Run(async () =>
                {
                    await startSealing.Task.ConfigureAwait(false);
                    return writer.Seal(BitConverter.GetBytes(index));
                }))];

            startSealing.SetResult(true);
            byte[][] records = await Task.WhenAll(sealTasks).ConfigureAwait(false);

            var startOpening = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task<byte[]>[] openTasks = [.. records
                .Select(record => Task.Run(async () =>
                {
                    await startOpening.Task.ConfigureAwait(false);
                    return reader.Open(record);
                }))];

            startOpening.SetResult(true);
            byte[][] plaintexts = await Task.WhenAll(openTasks).ConfigureAwait(false);

            for (int index = 0; index < plaintexts.Length; index++)
            {
                Assert.That(plaintexts[index], Is.EqualTo(BitConverter.GetBytes(index)));
            }
        }

        [Test]
        public void SealRejectsSequenceNumberExhaustion()
        {
            SkipIfAeadUnsupported(DtlsCipherSuite.TlsAes128GcmSha256);
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            using var writer = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256),
                secret,
                epoch: 1,
                initialWriteSequenceNumber: DtlsRecordProtection.MaximumRecordSequenceNumber);

            Assert.That(writer.Seal([0x01]), Is.Not.Empty);
            Assert.That(
                () => writer.Seal([0x02]),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("sequence number exhausted"));
        }

        [Test]
        public void ConstructorRejectsInitialSequenceNumberAboveEpochLimit()
        {
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            Assert.That(
                () => new DtlsRecordProtection(
                    CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256),
                    secret,
                    epoch: 1,
                    initialWriteSequenceNumber: DtlsRecordProtection.MaximumRecordSequenceNumber + 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            byte[] secret = CreateSecret(DtlsCipherSuite.TlsAes128GcmSha256);
            var reader = new DtlsRecordProtection(
                CreateProfile(DtlsCipherSuite.TlsAes128GcmSha256),
                secret,
                epoch: 1);
            reader.Dispose();
            Assert.That(reader.Dispose, Throws.Nothing);
        }

        private static void SkipIfAeadUnsupported(DtlsCipherSuite cipherSuite)
        {
            if (DtlsRecordProtection.IsAeadCipherSuite(cipherSuite) &&
                !DtlsRecordProtection.IsAeadSupported)
            {
                Assert.Ignore(
                    "AEAD DTLS record protection requires .NET 8 or later; the stack assembly " +
                    "under test was compiled without it.");
            }
        }

        private static byte[] CreateSecret(DtlsCipherSuite cipherSuite)
        {
            int length = cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384 ? 48 : 32;
            byte[] secret = new byte[length];
            FillRandom(secret);
            return secret;
        }

        private static DtlsProfile CreateProfile(DtlsCipherSuite cipherSuite)
        {
            return new DtlsProfile(
                cipherSuite.ToString(),
                cipherSuite,
                DtlsNamedCurve.NistP256,
                DtlsNamedCurve.NistP256,
                isMandatory: false);
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
