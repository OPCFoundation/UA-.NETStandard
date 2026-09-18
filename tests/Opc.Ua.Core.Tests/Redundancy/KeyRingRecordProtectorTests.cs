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

            ByteString envelope = ring.Protect(s_plaintext);
            bool ok = ring.TryUnprotect(envelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void ProtectThenUnprotectRoundTripsThroughActiveKey()
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var ring = new KeyRingRecordProtector(active);

            ByteString envelope = ring.Protect(s_plaintext);
            bool ok = ring.TryUnprotect(envelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void UnprotectRecoversRecordWrittenByRetiredKey()
        {
            using var retiredKey = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var activeKey = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);

            // A record produced before rotation, under the now-retired key.
            ByteString legacyEnvelope = retiredKey.Protect(s_plaintext);

            IRecordProtector[] retired = [retiredKey];
            using var ring = new KeyRingRecordProtector(activeKey, retired);

            bool ok = ring.TryUnprotect(legacyEnvelope, out ByteString recovered);

            Assert.That(ok, Is.True);
            Assert.That(recovered.ToArray(), Is.EqualTo(s_plaintext.ToArray()));
        }

        [Test]
        public void UnprotectReturnsFalseForRecordFromUnknownKey()
        {
            using var stranger = new AesCbcHmacRecordProtector(CreateKey(0xCC), keyId: 9);
            ByteString foreignEnvelope = stranger.Protect(s_plaintext);

            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var ring = new KeyRingRecordProtector(active);

            bool ok = ring.TryUnprotect(foreignEnvelope, out ByteString recovered);

            Assert.That(ok, Is.False);
            Assert.That(recovered.IsNull, Is.True);
        }

        [Test]
        public void OwnedContextRejectionDoesNotUseUnboundFallback()
        {
            var member = new Mock<IContextBoundRecordProtector>();
            ByteString rejected = default;
            ByteString unbound = s_plaintext;
            member.Setup(value => value.TryUnprotect(
                    It.IsAny<ByteString>(), It.IsAny<ByteString>(), out rejected))
                .Returns(false);
            member.Setup(value => value.TryUnprotect(It.IsAny<ByteString>(), out unbound))
                .Returns(true);
            using var ring = new KeyRingRecordProtector(member.Object);

            bool accepted = ring.TryUnprotectOwned(
                ByteString.From(new byte[] { 1 }), s_plaintext, out byte[] recovered);

            Assert.That(accepted, Is.False);
            Assert.That(recovered, Is.Empty);
            member.Verify(value => value.TryUnprotect(It.IsAny<ByteString>(), out unbound), Times.Never);
        }

        [Test]
        public void UnboundOwnedReadSupportsContextualMemberWithoutOwnedCapability()
        {
            var member = new Mock<IContextBoundRecordProtector>();
            ByteString plaintext = s_plaintext;
            member.Setup(value => value.TryUnprotect(s_plaintext, out plaintext)).Returns(true);
            using var ring = new KeyRingRecordProtector(member.Object);

            bool accepted = ring.TryUnprotectOwned(s_plaintext, out byte[] recovered);

            Assert.That(accepted, Is.True);
            Assert.That(recovered, Is.EqualTo(s_plaintext.ToArray()));
            member.Verify(value => value.TryUnprotect(s_plaintext, out plaintext), Times.Once);
        }

        [Test]
        public void OwnedContextReadRetainsLegacyMemberCompatibility()
        {
            var member = new Mock<IRecordProtector>();
            ByteString plaintext = s_plaintext;
            member.Setup(value => value.TryUnprotect(s_plaintext, out plaintext)).Returns(true);
            using var ring = new KeyRingRecordProtector(member.Object);

            bool accepted = ring.TryUnprotectOwned(
                ByteString.From(new byte[] { 1 }), s_plaintext, out byte[] recovered);

            Assert.That(accepted, Is.True);
            Assert.That(recovered, Is.EqualTo(s_plaintext.ToArray()));
            member.Verify(value => value.TryUnprotect(s_plaintext, out plaintext), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OwnedContextReadAuthenticatesActiveAndRetiredRecords(bool useRetiredKey)
        {
            using var active = new AesCbcHmacRecordProtector(s_masterKeyA, keyId: 1);
            using var retired = new AesCbcHmacRecordProtector(s_masterKeyB, keyId: 2);
            using var ring = new KeyRingRecordProtector(active, retired);
            ByteString context = ByteString.From(new byte[] { 1, 2, 3 });
            ByteString wrongContext = ByteString.From(new byte[] { 1, 2, 4 });
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
