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

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

#nullable enable

using System;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="AesCbcHmacRecordProtector"/> and
    /// <see cref="NullRecordProtector"/>: authenticated-encryption round-trip
    /// and fail-closed rejection of tampered, wrong-key, and malformed records.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class RecordProtectorTests
    {
        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(seed + i);
            }
            return key;
        }

        [Test]
        public void ProtectUnprotectRoundTrips()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(1));
            var plaintext = ByteString.From(new byte[] { 10, 20, 30, 40, 50 });

            ByteString sealed1 = protector.Protect(plaintext);
            bool ok = protector.TryUnprotect(sealed1, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(plaintext.ToArray()));
            Assert.That(sealed1.ToArray(), Is.Not.EqualTo(plaintext.ToArray()));
        }

        [Test]
        public void EmptyPayloadRoundTrips()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(2));
            var plaintext = ByteString.From(Array.Empty<byte>());

            ByteString sealed1 = protector.Protect(plaintext);
            bool ok = protector.TryUnprotect(sealed1, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.Empty);
        }

        [Test]
        public void ProtectProducesDistinctCiphertextPerCall()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(3));
            var plaintext = ByteString.From(new byte[] { 1, 2, 3 });

            ByteString a = protector.Protect(plaintext);
            ByteString b = protector.Protect(plaintext);

            // Random IV per call => different envelopes for identical plaintext.
            Assert.That(a.ToArray(), Is.Not.EqualTo(b.ToArray()));
        }

        [Test]
        public void TamperedCiphertextIsRejected()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(4));
            ByteString sealed1 = protector.Protect(ByteString.From(new byte[] { 7, 7, 7, 7 }));

            byte[] tampered = sealed1.ToArray();
            // Flip a byte inside the ciphertext region (after the 21-byte header).
            tampered[25] ^= 0xFF;

            bool ok = protector.TryUnprotect(ByteString.From(tampered), out ByteString recovered);

            Assert.That(ok, Is.False);
            Assert.That(recovered.IsNull, Is.True);
        }

        [Test]
        public void TamperedTagIsRejected()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(5));
            ByteString sealed1 = protector.Protect(ByteString.From(new byte[] { 9, 9 }));

            byte[] tampered = sealed1.ToArray();
            tampered[^1] ^= 0x01;

            bool ok = protector.TryUnprotect(ByteString.From(tampered), out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void TamperedIvIsRejected()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(6));
            ByteString sealed1 = protector.Protect(ByteString.From(new byte[] { 4, 5, 6 }));

            byte[] tampered = sealed1.ToArray();
            // IV occupies bytes [5, 21); the MAC covers it, so a flip is caught.
            tampered[6] ^= 0x80;

            bool ok = protector.TryUnprotect(ByteString.From(tampered), out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void WrongMasterKeyIsRejected()
        {
            using var writer = new AesCbcHmacRecordProtector(MakeKey(7));
            using var reader = new AesCbcHmacRecordProtector(MakeKey(8));
            ByteString sealed1 = writer.Protect(ByteString.From(new byte[] { 1, 1, 1, 1 }));

            bool ok = reader.TryUnprotect(sealed1, out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void DifferentKeyIdIsRejected()
        {
            byte[] key = MakeKey(9);
            using var writer = new AesCbcHmacRecordProtector(key, keyId: 1);
            using var reader = new AesCbcHmacRecordProtector(key, keyId: 2);
            ByteString sealed1 = writer.Protect(ByteString.From(new byte[] { 2, 2 }));

            bool ok = reader.TryUnprotect(sealed1, out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void MalformedOrNullEnvelopeIsRejected()
        {
            using var protector = new AesCbcHmacRecordProtector(MakeKey(10));

            Assert.That(protector.TryUnprotect(default, out _), Is.False);
            Assert.That(protector.TryUnprotect(ByteString.From(new byte[] { 1, 2, 3 }), out _), Is.False);
        }

        [Test]
        public void ConstructorRejectsShortMasterKey()
        {
            Assert.That(
                () => new AesCbcHmacRecordProtector(new byte[16]),
                Throws.ArgumentException);
        }

        [Test]
        public void NullProtectorPassesThrough()
        {
            NullRecordProtector protector = NullRecordProtector.Instance;
            var plaintext = ByteString.From(new byte[] { 3, 1, 4, 1, 5 });

            ByteString sealed1 = protector.Protect(plaintext);
            bool ok = protector.TryUnprotect(sealed1, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(sealed1.ToArray(), Is.EqualTo(plaintext.ToArray()));
            Assert.That(recovered.ToArray(), Is.EqualTo(plaintext.ToArray()));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="KeyRingRecordProtector"/>: staged key rotation
    /// where new writes use the active key while reads still verify against
    /// retired keys.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class KeyRingRecordProtectorTests
    {
        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(seed + i);
            }
            return key;
        }

        [Test]
        public void ActiveKeyRoundTrips()
        {
            using var active = new AesCbcHmacRecordProtector(MakeKey(30), keyId: 2);
            using var ring = new KeyRingRecordProtector(active);
            var plaintext = ByteString.From(new byte[] { 1, 2, 3 });

            ByteString sealed1 = ring.Protect(plaintext);
            bool ok = ring.TryUnprotect(sealed1, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(plaintext.ToArray()));
        }

        [Test]
        public void RetiredKeyStillReadsAfterRotation()
        {
            using var oldKey = new AesCbcHmacRecordProtector(MakeKey(31), keyId: 1);
            using var newKey = new AesCbcHmacRecordProtector(MakeKey(32), keyId: 2);
            var plaintext = ByteString.From(new byte[] { 9, 9, 9 });

            // A record written before rotation, under the old key.
            ByteString legacyRecord = oldKey.Protect(plaintext);

            // After rotation the ring writes under the new key but still reads
            // records produced under the retired key.
            using var ring = new KeyRingRecordProtector(newKey, oldKey);
            bool ok = ring.TryUnprotect(legacyRecord, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(plaintext.ToArray()));
        }

        [Test]
        public void NewWritesUseActiveKeyOnly()
        {
            using var oldKey = new AesCbcHmacRecordProtector(MakeKey(33), keyId: 1);
            using var newKey = new AesCbcHmacRecordProtector(MakeKey(34), keyId: 2);
            using var ring = new KeyRingRecordProtector(newKey, oldKey);

            ByteString sealed1 = ring.Protect(ByteString.From(new byte[] { 5 }));

            // The retired key alone must not be able to read a post-rotation record.
            Assert.That(oldKey.TryUnprotect(sealed1, out _), Is.False);
            Assert.That(newKey.TryUnprotect(sealed1, out _), Is.True);
        }

        [Test]
        public void RecordOutsideRingIsRejected()
        {
            using var member = new AesCbcHmacRecordProtector(MakeKey(35), keyId: 1);
            using var stranger = new AesCbcHmacRecordProtector(MakeKey(36), keyId: 9);
            using var ring = new KeyRingRecordProtector(member);

            ByteString foreignRecord = stranger.Protect(ByteString.From(new byte[] { 7, 7 }));

            Assert.That(ring.TryUnprotect(foreignRecord, out _), Is.False);
        }

        [Test]
        public void ConstructorRejectsNullActive()
        {
            Assert.That(() => new KeyRingRecordProtector(null!), Throws.ArgumentNullException);
        }
    }
}
