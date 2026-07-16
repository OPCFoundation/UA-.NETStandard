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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Verifies the <see cref="UadpSecurityWrapOptions"/> branches of
    /// <see cref="UadpSecurityWrapper.WrapAsync"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Part 14 §A.2.2.5</see>. Per the Annex the sign-only path leaves
    /// the security footer empty and the encrypt-only path skips the
    /// signature.
    /// </remarks>
    [TestFixture]
    [TestSpec("A.2.2.5", Summary = "UADP security mode selector (SignOnly / EncryptOnly / SignAndEncrypt)")]
    public class UadpSecurityWrapperSignOnlyTests
    {
        private static readonly byte[] s_outerPrefix =
        [
            0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01
        ];

        private static readonly byte[] s_innerPayload =
        [
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88
        ];

        private static (UadpSecurityWrapper Sender, UadpSecurityWrapper Receiver)
            CreatePair(PubSubAes128CtrPolicy policy, uint tokenId = 1U)
        {
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                tokenId,
                signingKeyLength: policy.SigningKeyLength == 0 ? 1 : policy.SigningKeyLength,
                encryptingKeyLength: policy.EncryptingKeyLength == 0 ? 1 : policy.EncryptingKeyLength,
                keyNonceLength: policy.NonceLength == 0 ? 1 : policy.NonceLength);

            var senderRing = new PubSubSecurityKeyRing("group");
            senderRing.SetCurrent(key);
            var senderProvider = new StaticSecurityKeyProvider("group", senderRing);
            var nonceProvider = new RandomNonceProvider(PublisherId.FromUInt32(0xDEADBEEFU));
            var senderWindow = new SecurityTokenWindow();
            var sender = new UadpSecurityWrapper(
                policy,
                senderProvider,
                nonceProvider,
                senderWindow,
                NUnitTelemetryContext.Create());

            var receiverRing = new PubSubSecurityKeyRing("group");
            receiverRing.SetCurrent(key);
            var receiverProvider = new StaticSecurityKeyProvider("group", receiverRing);
            var receiverWindow = new SecurityTokenWindow();
            receiverWindow.RegisterToken(tokenId);
            var receiver = new UadpSecurityWrapper(
                policy,
                receiverProvider,
                new RandomNonceProvider(PublisherId.FromUInt32(0xDEADBEEFU)),
                receiverWindow,
                NUnitTelemetryContext.Create());

            return (sender, receiver);
        }

        [Test]
        [TestSpec("A.2.2.5", Summary = "SignOnly: payload is in cleartext but signed")]
        public async Task WrapAsync_SignOnly_LeavesPayloadCleartextAndAuthenticatesAsync()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(
                s_outerPrefix, s_innerPayload,
                UadpSecurityWrapOptions.SignOnly).ConfigureAwait(false);

            // Cleartext payload must appear verbatim somewhere in the wrapped
            // frame after the SecurityHeader and before the trailing signature.
            byte[] wrappedBytes = wrapped.ToArray();
            int marker = IndexOf(wrappedBytes, s_innerPayload);
            Assert.That(marker, Is.GreaterThanOrEqualTo(0),
                "SignOnly must keep the inner payload in cleartext.");

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped[s_outerPrefix.Length..]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload, Is.Not.Null);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_innerPayload));
            });
        }

        [Test]
        [TestSpec("A.2.2.5", Summary = "EncryptOnly: payload is ciphertext, no signature")]
        public async Task WrapAsync_EncryptOnly_EncryptsPayloadWithoutSignatureAsync()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(
                s_outerPrefix, s_innerPayload,
                UadpSecurityWrapOptions.EncryptOnly).ConfigureAwait(false);

            byte[] wrappedBytes = wrapped.ToArray();
            int marker = IndexOf(wrappedBytes, s_innerPayload);
            Assert.That(marker, Is.LessThan(0),
                "EncryptOnly must not leave the plaintext payload in the frame.");

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped[s_outerPrefix.Length..]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_innerPayload));
            });
        }

        [Test]
        [TestSpec("A.2.2.5", Summary = "SignAndEncrypt remains the default")]
        public async Task WrapAsync_SignAndEncrypt_DefaultBehaviourMatchesExplicitFlagAsync()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreatePair(PubSubAes128CtrPolicy.Instance);
            (UadpSecurityWrapper sender2, UadpSecurityWrapper receiver2) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> implicitWrap = await sender.WrapAsync(
                s_outerPrefix, s_innerPayload).ConfigureAwait(false);
            ReadOnlyMemory<byte> explicitWrap = await sender2.WrapAsync(
                s_outerPrefix, s_innerPayload,
                UadpSecurityWrapOptions.SignAndEncrypt).ConfigureAwait(false);

            // Both wraps verify successfully (nonces differ → bytes differ
            // but length and structure match).
            UadpSecurityWrapper.UnwrapResult implicitResult = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                implicitWrap[s_outerPrefix.Length..]).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult explicitResult = await receiver2.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                explicitWrap[s_outerPrefix.Length..]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(implicitResult.IsSuccess, Is.True, implicitResult.Reason);
                Assert.That(explicitResult.IsSuccess, Is.True, explicitResult.Reason);
                Assert.That(implicitResult.InnerPayload!.Value.ToArray(),
                    Is.EqualTo(s_innerPayload));
                Assert.That(explicitResult.InnerPayload!.Value.ToArray(),
                    Is.EqualTo(s_innerPayload));
                Assert.That(implicitWrap.Length, Is.EqualTo(explicitWrap.Length));
            });
        }

        private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        {
            if (needle.IsEmpty || haystack.Length < needle.Length)
            {
                return -1;
            }
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
