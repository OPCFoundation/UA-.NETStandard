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
    /// End-to-end Wrap/Unwrap tests for <see cref="UadpSecurityWrapper"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3", Summary = "UADP NetworkMessage signing/encryption wrapper")]
    public class UadpSecurityWrapperTests
    {
        private static readonly byte[] s_outerPrefix = [0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01];
        private static readonly byte[] s_innerPayload =
        [
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88
        ];

        private static (UadpSecurityWrapper Sender, UadpSecurityWrapper Receiver,
            PubSubSecurityKeyRing SenderRing, PubSubSecurityKeyRing ReceiverRing,
            ISecurityTokenWindow ReceiverWindow)
            CreatePair(IPubSubSecurityPolicy policy, uint tokenId = 1U)
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

            return (sender, receiver, senderRing, receiverRing, receiverWindow);
        }

        [Test]
        public async Task WrapUnwrap_RoundTripsWithAes128Ctr()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver, _, _, _) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped.Slice(s_outerPrefix.Length)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload, Is.Not.Null);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_innerPayload));
            });
        }

        [Test]
        public async Task WrapUnwrap_RoundTripsWithAes256Ctr()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver, _, _, _) =
                CreatePair(PubSubAes256CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped.Slice(s_outerPrefix.Length)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_innerPayload));
            });
        }

        [Test]
        public async Task TryUnwrap_DetectsTamperedCiphertext()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver, _, _, _) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);
            byte[] tampered = wrapped.ToArray();
            // Flip a byte inside the ciphertext (after outerPrefix +
            // SecurityHeader of size 1+4+1+12 = 18 bytes).
            tampered[s_outerPrefix.Length + 18 + 5] ^= 0x01;

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                new ReadOnlyMemory<byte>(tampered, s_outerPrefix.Length, tampered.Length - s_outerPrefix.Length)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            });
        }

        [Test]
        public async Task TryUnwrap_RejectsUnknownToken()
        {
            (UadpSecurityWrapper sender, _, _, _, _) =
                CreatePair(PubSubAes128CtrPolicy.Instance);
            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);

            // Build a receiver with an empty key ring.
            var emptyRing = new PubSubSecurityKeyRing("group");
            var emptyProvider = new StaticSecurityKeyProvider("group", emptyRing);
            var window = new SecurityTokenWindow();
            var receiver = new UadpSecurityWrapper(
                PubSubAes128CtrPolicy.Instance,
                emptyProvider,
                new RandomNonceProvider(PublisherId.FromUInt32(0U)),
                window,
                NUnitTelemetryContext.Create());

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped.Slice(s_outerPrefix.Length)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            });
        }

        [Test]
        public async Task TryUnwrap_RejectsReplayedNonce()
        {
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver, _, _, _) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            ReadOnlyMemory<byte> wrapped = await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult first = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped.Slice(s_outerPrefix.Length)).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult replay = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped.Slice(s_outerPrefix.Length)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsSuccess, Is.True, first.Reason);
                Assert.That(replay.IsSuccess, Is.False);
                Assert.That(replay.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            });
        }

        [Test]
        public async Task TryUnwrap_FailsOnTruncatedSecurityHeader()
        {
            (UadpSecurityWrapper _, UadpSecurityWrapper receiver, _, _, _) =
                CreatePair(PubSubAes128CtrPolicy.Instance);

            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                new ReadOnlyMemory<byte>(new byte[3])).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadDecodingError));
            });
        }

        [Test]
        public void Constructor_RejectsNullArguments()
        {
            var policy = PubSubNonePolicy.Instance;
            var ring = new PubSubSecurityKeyRing("g");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1U));
            var keyProvider = new StaticSecurityKeyProvider("g", ring);
            var nonceProvider = new RandomNonceProvider(PublisherId.FromUInt16(1));
            var window = new SecurityTokenWindow();
            var telemetry = NUnitTelemetryContext.Create();
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new UadpSecurityWrapper(null!, keyProvider, nonceProvider, window, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new UadpSecurityWrapper(policy, null!, nonceProvider, window, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new UadpSecurityWrapper(policy, keyProvider, null!, window, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new UadpSecurityWrapper(policy, keyProvider, nonceProvider, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new UadpSecurityWrapper(policy, keyProvider, nonceProvider, window, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void UnwrapResult_FailureRequiresReason()
        {
            Assert.That(
                () => UadpSecurityWrapper.UnwrapResult.Failure(StatusCodes.BadSecurityChecksFailed, string.Empty),
                Throws.ArgumentException);
        }
    }
}
