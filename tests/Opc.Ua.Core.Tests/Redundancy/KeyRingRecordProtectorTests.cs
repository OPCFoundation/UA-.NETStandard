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

#nullable enable

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Core.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="KeyRingRecordProtector"/> staged key rotation.
    /// </summary>
    [TestFixture]
    [Category("Redundancy")]
    [Parallelizable(ParallelScope.All)]
    public sealed class KeyRingRecordProtectorTests
    {
        private static readonly byte[] s_masterKeyA = CreateKey(0xA1);
        private static readonly byte[] s_masterKeyB = CreateKey(0xB2);
        private static readonly ByteString s_plaintext = new(new byte[] { 10, 20, 30, 40, 50 });

        [Test]
        public void ConstructorWithNullActiveThrows()
        {
            Assert.That(() => new KeyRingRecordProtector(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ConstructorWithNullRetiredEntryThrows()
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            IRecordProtector[] retired = [null!];

            Assert.That(
                () => new KeyRingRecordProtector(active, retired),
                Throws.ArgumentException);
        }

        [Test]
        public void ConstructorWithNullRetiredArrayUsesActiveOnly()
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var ring = new KeyRingRecordProtector(active, null!);

            ByteString envelope = ring.Protect(default, s_plaintext);
            bool ok = ring.TryUnprotect(default, envelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void ProtectThenUnprotectRoundTripsThroughActiveKey()
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var ring = new KeyRingRecordProtector(active);

            ByteString envelope = ring.Protect(default, s_plaintext);
            bool ok = ring.TryUnprotect(default, envelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void UnprotectRecoversRecordWrittenByRetiredKey()
        {
            using var retiredKey = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var activeKey = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);

            // A record produced before rotation, under the now-retired key.
            ByteString legacyEnvelope = retiredKey.Protect(default, s_plaintext);

            IRecordProtector[] retired = [retiredKey];
            using var ring = new KeyRingRecordProtector(activeKey, retired);

            bool ok = ring.TryUnprotect(default, legacyEnvelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void UnprotectReturnsFalseForRecordFromUnknownKey()
        {
            using var stranger = new AesCbcHmacRecordProtector(CreateKey(0xCC), keyId: 9);
            ByteString foreignEnvelope = stranger.Protect(default, s_plaintext);

            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var ring = new KeyRingRecordProtector(active);

            bool ok = ring.TryUnprotect(default, foreignEnvelope, out ByteString recovered);

            Assert.That(ok, Is.False);
            Assert.That(recovered.IsNull, Is.True);
        }

        [Test]
        public void OwnedContextRejectionDoesNotUseImmutableFallback()
        {
            var member = new Mock<IOwnedRecordProtector>();
            byte[] rejected = [];
            ByteString unbound = s_plaintext;
            member.Setup(value => value.TryUnprotectOwned(
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), out rejected))
                .Returns(false);
            member.Setup(value => value.TryUnprotect(It.IsAny<ByteString>(), It.IsAny<ByteString>(), out unbound))
                .Returns(true);
            using var ring = new KeyRingRecordProtector(member.Object);

            bool accepted = ring.TryUnprotectOwned(
                ByteString.From(new byte[] { 1 }), s_plaintext, out byte[] recovered);

            Assert.That(accepted, Is.False);
            Assert.That(recovered, Is.Empty);
            member.Verify(value => value.TryUnprotect(
                It.IsAny<ByteString>(), It.IsAny<ByteString>(), out unbound), Times.Never);
        }

        [Test]
        public void StringContextExtensionsUseCanonicalUtf8Bytes()
        {
            using var protector = new AesCbcHmacRecordProtector(s_masterKeyA);
            const string context = "record|store/\u00E9";
            var contextBytes = ByteString.From(Encoding.UTF8.GetBytes(context));
            ByteString record = protector.Protect(context, s_plaintext);
            Assert.That(protector.TryUnprotect(contextBytes, record, out ByteString plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(s_plaintext));
            Assert.That(protector.TryUnprotect(context, record, out plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(s_plaintext));
            Assert.That(protector.TryUnprotectOwned(context, record, out byte[] owned), Is.True);
            try
            {
                Assert.That(owned, Is.EqualTo(s_plaintext.ToArray()));
            }
            finally
            {
                CryptoUtils.ZeroMemory(owned);
            }

            ByteString emptyContextRecord = protector.Protect(null, s_plaintext);
            Assert.That(protector.TryUnprotect(ByteString.Empty, emptyContextRecord, out plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(s_plaintext));
            Assert.That(protector.TryUnprotect(string.Empty, emptyContextRecord, out plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(s_plaintext));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OwnedContextReadAuthenticatesActiveAndRetiredRecords(bool useRetiredKey)
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var retired = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);
            using var ring = new KeyRingRecordProtector(active, retired);
            var context = ByteString.From(new byte[] { 1, 2, 3 });
            var wrongContext = ByteString.From(new byte[] { 1, 2, 4 });
            ByteString record = (useRetiredKey ? retired : active).Protect(context, s_plaintext);

            Assert.That(ring.TryUnprotectOwned(context, record, out byte[] recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(s_plaintext.ToArray()));
            recovered[0] = 0;
            Assert.That(ring.TryUnprotect(context, record, out ByteString secondRead), Is.True);
            Assert.That(secondRead.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
            Assert.That(ring.TryUnprotectOwned(wrongContext, record, out byte[] rejected), Is.False);
            Assert.That(rejected, Is.Empty);
        }

        [Test]
        public void OwnedContextReadTransfersOriginalMemberBuffer(
            [Values] bool useRetiredMember,
            [Values] bool textContext)
        {
            var context = ByteString.From(new byte[] { 1, 2, 3 });
            byte[] owned = s_plaintext.ToArray();
            var unowned = ByteString.From(owned);
            var member = new Mock<IOwnedRecordProtector>(MockBehavior.Strict);
            member.Setup(value => value.TryUnprotect(context, s_plaintext, out unowned)).Returns(true);
            member.Setup(value => value.TryUnprotectOwned(context, s_plaintext, out owned)).Returns(true);
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA);
            using KeyRingRecordProtector ring = useRetiredMember
                ? new KeyRingRecordProtector(active, member.Object)
                : new KeyRingRecordProtector(member.Object);

            try
            {
                bool accepted = textContext
                    ? ring.TryUnprotectOwned("\u0001\u0002\u0003", s_plaintext, out byte[] actual)
                    : ring.TryUnprotectOwned(context, s_plaintext, out actual);
                Assert.That(accepted, Is.True);
                Assert.That(actual, Is.SameAs(owned), "The decrypted buffer must be transferred, never copied.");
                member.Verify(value => value.TryUnprotect(context, s_plaintext, out unowned), Times.Never);
                CryptoUtils.ZeroMemory(actual);
                Assert.That(owned, Is.All.Zero);
            }
            finally
            {
                CryptoUtils.ZeroMemory(owned);
            }
        }

        [Test]
        public void OwnedReadFailsClosedForMemberWithoutOwnedCapability()
        {
            var member = new Mock<IRecordProtector>();
            using var ring = new KeyRingRecordProtector(member.Object);

            Assert.That(ring.TryUnprotectOwned(default, s_plaintext, out byte[] plaintext), Is.False);
            Assert.That(plaintext, Is.Empty);
            member.Verify(value => value.TryUnprotect(
                It.IsAny<ByteString>(), It.IsAny<ByteString>(), out It.Ref<ByteString>.IsAny), Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OwnedReadSkipsUnsupportedMembersWithoutImmutableFallback(
            bool unsupportedFirst,
            bool authenticatedRecord)
        {
            using var owned = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var other = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);
            var unsupported = new Mock<IRecordProtector>(MockBehavior.Strict);
            ByteString immutable = s_plaintext;
            unsupported.Setup(value => value.TryUnprotect(
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), out immutable))
                .Returns(true);
            using KeyRingRecordProtector ring = unsupportedFirst
                ? new KeyRingRecordProtector(unsupported.Object, owned)
                : new KeyRingRecordProtector(owned, unsupported.Object);
            var context = ByteString.From("pending-key"u8);
            ByteString record = (authenticatedRecord ? owned : other).Protect(context, s_plaintext);
            byte[] plaintext = [];
            try
            {
                bool accepted = ring.TryUnprotectOwned(context, record, out plaintext);

                Assert.That(accepted, Is.EqualTo(authenticatedRecord));
                Assert.That(plaintext, Is.EqualTo(authenticatedRecord ? s_plaintext.ToArray() : []));
                unsupported.Verify(value => value.TryUnprotect(
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), out immutable), Times.Never);
            }
            finally
            {
                CryptoUtils.ZeroMemory(plaintext);
            }
        }

        [Test]
        public void NullAndEmptyContextsAreCanonical(
            [Values] bool writeNull,
            [Values] bool readNull,
            [Values] bool useRetiredKey)
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var retired = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);
            using var ring = new KeyRingRecordProtector(active, retired);
            ByteString writeContext = writeNull ? default : ByteString.Empty;
            ByteString readContext = readNull ? default : ByteString.Empty;
            ByteString record = (useRetiredKey ? retired : active).Protect(writeContext, s_plaintext);

            Assert.That(ring.TryUnprotect(readContext, record, out ByteString recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(s_plaintext));
            Assert.That(ring.TryUnprotectOwned(readContext, record, out byte[] owned), Is.True);
            try
            {
                Assert.That(owned, Is.EqualTo(s_plaintext.ToArray()));
                Assert.That(ring.TryUnprotect("nonempty", record, out ByteString rejected), Is.False);
                Assert.That(rejected.IsNull, Is.True);
                Assert.That(ring.TryUnprotectOwned(
                    ByteString.From(new byte[] { 1 }), record, out byte[] rejectedOwned), Is.False);
                Assert.That(rejectedOwned, Is.Empty);
            }
            finally
            {
                CryptoUtils.ZeroMemory(owned);
            }
        }

        [Test]
        public void NullAndEmptyPlaintextProduceAuthenticatedEmptyRecords([Values] bool nullPlaintext)
        {
            using var protector = new AesCbcHmacRecordProtector(s_masterKeyA);
            ByteString plaintext = nullPlaintext ? default : ByteString.Empty;
            ByteString record = protector.Protect(s_plaintext, plaintext);

            Assert.That(record.Length, Is.EqualTo(69), "Empty plaintext still has one padded AES block.");
            Assert.That(protector.TryUnprotect(s_plaintext, record, out ByteString recovered), Is.True);
            Assert.That(recovered.IsNull, Is.False);
            Assert.That(recovered.IsEmpty, Is.True);
            Assert.That(protector.TryUnprotectOwned(s_plaintext, record, out byte[] owned), Is.True);
            Assert.That(owned, Is.Empty);
            Assert.That(protector.TryUnprotect(s_plaintext, default, out _), Is.False);
            Assert.That(protector.TryUnprotect(s_plaintext, ByteString.Empty, out _), Is.False);
        }

        [Test]
        public void ActiveAndRetiredRecordsRejectTampering(
            [Values] bool useRetiredKey,
            [Values(0, 1, 5, 21, 68)] int changedByte)
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var retired = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);
            using var ring = new KeyRingRecordProtector(active, retired);
            byte[] record = (useRetiredKey ? retired : active).Protect(s_plaintext, s_plaintext).ToArray();
            record[changedByte] ^= 0x80;

            Assert.That(ring.TryUnprotect(s_plaintext, ByteString.From(record), out ByteString plaintext), Is.False);
            Assert.That(plaintext.IsNull, Is.True);
            Assert.That(ring.TryUnprotectOwned(s_plaintext, ByteString.From(record), out byte[] owned), Is.False);
            Assert.That(owned, Is.Empty);
        }

        [Test]
        public void EnvelopeMacAuthenticatesCanonicalLengthPrefixedContext([Values] bool emptyContext)
        {
            using var protector = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 0x01020304);
            ByteString context = emptyContext ? default : s_plaintext;
            ByteString record = protector.Protect(context, s_plaintext);
            Assert.That(record.Length, Is.EqualTo(69));
            Assert.That(record[0], Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(record.Span[1..]), Is.EqualTo(0x01020304));

            using var derivation = new HMACSHA256(s_masterKeyA);
            byte[] macKey = derivation.ComputeHash(Encoding.ASCII.GetBytes("OpcUaDistributed-HMAC-SHA256"));
            try
            {
                const int tagLength = 32;
                int authenticatedLength = record.Length - tagLength;
                byte[] authenticated = new byte[sizeof(int) + context.Length + authenticatedLength];
                BinaryPrimitives.WriteInt32LittleEndian(authenticated, context.Length);
                context.Span.CopyTo(authenticated.AsSpan(sizeof(int)));
                record.Span[..authenticatedLength].CopyTo(authenticated.AsSpan(sizeof(int) + context.Length));
                using var hmac = new HMACSHA256(macKey);
                Assert.That(hmac.ComputeHash(authenticated), Is.EqualTo(record.Span[authenticatedLength..].ToArray()));
            }
            finally
            {
                CryptoUtils.ZeroMemory(macKey);
            }
        }

        [Test]
        public void StoreContextsBindFullKeyAndRecordType()
        {
            using var protector = new AesCbcHmacRecordProtector(s_masterKeyA);
            ByteString context = RecordProtectionContext.Create("value", "site/a|b");
            Assert.That(context.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes("value|site/a|b")));
            ByteString record = protector.Protect(context, s_plaintext);

            Assert.That(protector.TryUnprotect(
                RecordProtectionContext.Create("value", "site/a|b"), record, out ByteString plaintext), Is.True);
            Assert.That(plaintext, Is.EqualTo(s_plaintext));
            Assert.That(protector.TryUnprotect(
                RecordProtectionContext.Create("node", "site/a|b"), record, out plaintext), Is.False);
            Assert.That(plaintext.IsNull, Is.True);
            Assert.That(protector.TryUnprotect(
                RecordProtectionContext.Create("value", "other/a|b"), record, out plaintext), Is.False);
            Assert.That(plaintext.IsNull, Is.True);
        }

        [TestCase(null, "key")]
        [TestCase("", "key")]
        [TestCase("a|b", "key")]
        [TestCase("type", null)]
        [TestCase("type", "")]
        public void StoreContextsRejectAmbiguousOrMissingIdentity(string? recordType, string? key)
        {
            Assert.That(() => RecordProtectionContext.Create(recordType!, key!), Throws.ArgumentException);
        }

        [Test]
        public void StoreContextsPreserveUnicodeIdentityAndRejectInvalidText()
        {
            Assert.That(
                RecordProtectionContext.Create("type", "e\u0301"),
                Is.Not.EqualTo(RecordProtectionContext.Create("type", "\u00E9")));
            Assert.That(
                () => RecordProtectionContext.Create("type", "\uD800"),
                Throws.TypeOf<EncoderFallbackException>());
            Assert.That(
                () => RecordProtectionContext.Create("\uD800", "key"),
                Throws.TypeOf<EncoderFallbackException>());
        }

        [Test]
        public void NullOwnedReadDoesNotAliasStoreInput()
        {
            ByteString stored = NullRecordProtector.Instance.Protect(default, s_plaintext);
            Assert.That(
                NullRecordProtector.Instance.TryUnprotectOwned(ByteString.Empty, stored, out byte[] owned), Is.True);
            Assert.That(owned, Is.EqualTo(s_plaintext.ToArray()));

            CryptoUtils.ZeroMemory(owned);

            Assert.That(stored, Is.EqualTo(s_plaintext));
            Assert.That(NullRecordProtector.Instance.TryUnprotect(
                default, stored, out ByteString recovered), Is.True);
            Assert.That(recovered, Is.EqualTo(s_plaintext));
        }

        [Test]
        public void DisposeDisposesDisposableMembersAndIgnoresOthers()
        {
            var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            IRecordProtector[] retired = [NullRecordProtector.Instance];
            var ring = new KeyRingRecordProtector(active, retired);

            // The active member owns key material (IDisposable); the retired
            // Null protector does not, exercising both dispose branches. A
            // second dispose must remain safe because member dispose is
            // idempotent.
            Assert.That(ring.Dispose, Throws.Nothing);
            Assert.That(ring.Dispose, Throws.Nothing);
        }

        private static byte[] CreateKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(seed + ii);
            }
            return key;
        }
    }
}
