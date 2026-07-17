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
    /// Regression tests for the Phase S2a hardening of
    /// <see cref="UadpSecurityWrapper"/>: monotonic replay protection
    /// (SA-MSGSEC-02) and deterministic per-key nonce uniqueness with a
    /// send-side cap (SA-CRYPTO-01 / SA-SKS-04).
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.2", Summary = "PubSub monotonic replay protection")]
    [TestSpec("7.2.4.4.3.2", Summary = "PubSub deterministic nonce uniqueness")]
    public class UadpSecurityWrapperReplayTests
    {
        private const uint TokenId = 1U;

        private static readonly byte[] s_outerPrefix = [0xAA, 0xBB, 0xCC, 0xDD, 0x00, 0x01];

        private static readonly byte[] s_innerPayload =
        [
            0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
        ];

        private static (UadpSecurityWrapper Sender, UadpSecurityWrapper Receiver)
            CreatePair(
                PubSubAes256CtrPolicy policy,
                int receiverHistorySize,
                ulong senderCap = RandomNonceProvider.DefaultMaxMessagesPerKey)
        {
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                TokenId,
                signingKeyLength: policy.SigningKeyLength,
                encryptingKeyLength: policy.EncryptingKeyLength,
                keyNonceLength: policy.NonceLength);

            var senderRing = new PubSubSecurityKeyRing("group");
            senderRing.SetCurrent(key);
            var sender = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group", senderRing),
                new RandomNonceProvider(
                    PublisherId.FromUInt32(0xCAFEBABEU),
                    maxMessagesPerKey: senderCap),
                new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());

            var receiverRing = new PubSubSecurityKeyRing("group");
            receiverRing.SetCurrent(key);
            var receiverWindow = new SecurityTokenWindow(receiverHistorySize);
            receiverWindow.RegisterToken(TokenId);
            var receiver = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group", receiverRing),
                new RandomNonceProvider(PublisherId.FromUInt32(0xCAFEBABEU)),
                receiverWindow,
                NUnitTelemetryContext.Create());

            return (sender, receiver);
        }

        [Test]
        public async Task ReplayedFrameRejectedAfterMoreThanHistorySizeNewerMessagesAsync()
        {
            const int historySize = 8;
            (UadpSecurityWrapper sender, UadpSecurityWrapper receiver) =
                CreatePair(PubSubAes256CtrPolicy.Instance, historySize);

            // Capture the very first secured frame.
            ReadOnlyMemory<byte> captured = await sender
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);

            UadpSecurityWrapper.UnwrapResult firstResult = await receiver
                .TryUnwrapAsync(s_outerPrefix.AsMemory(), captured[s_outerPrefix.Length..])
                .ConfigureAwait(false);
            Assert.That(firstResult.IsSuccess, Is.True, firstResult.Reason);

            // Send far more than HistorySize newer frames, all accepted.
            for (int i = 0; i < historySize * 4; i++)
            {
                ReadOnlyMemory<byte> next = await sender
                    .WrapAsync(s_outerPrefix, s_innerPayload)
                    .ConfigureAwait(false);
                UadpSecurityWrapper.UnwrapResult ok = await receiver
                    .TryUnwrapAsync(s_outerPrefix.AsMemory(), next[s_outerPrefix.Length..])
                    .ConfigureAwait(false);
                Assert.That(ok.IsSuccess, Is.True, ok.Reason);
            }

            // Replaying the captured frame is still rejected even though
            // its nonce was long since evicted from any bounded set.
            UadpSecurityWrapper.UnwrapResult replay = await receiver
                .TryUnwrapAsync(s_outerPrefix.AsMemory(), captured[s_outerPrefix.Length..])
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replay.IsSuccess, Is.False);
                Assert.That(
                    replay.Status,
                    Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            });
        }

        [Test]
        public async Task ConsecutiveSendsProduceDeterministicDistinctNoncesAsync()
        {
            (UadpSecurityWrapper sender, _) =
                CreatePair(PubSubAes256CtrPolicy.Instance, receiverHistorySize: 64);

            ReadOnlyMemory<byte> first = await sender
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> second = await sender
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);

            (ulong seqFirst, byte[] nonceFirst) = ReadNonce(first);
            (ulong seqSecond, byte[] nonceSecond) = ReadNonce(second);

            Assert.Multiple(() =>
            {
                Assert.That(seqFirst, Is.Zero);
                Assert.That(seqSecond, Is.EqualTo(1UL));
                Assert.That(nonceSecond, Is.Not.EqualTo(nonceFirst));
            });
        }

        [Test]
        public async Task SendSideCapForcesRolloverBeforeNonceRepetitionAsync()
        {
            (UadpSecurityWrapper sender, _) =
                CreatePair(PubSubAes256CtrPolicy.Instance, receiverHistorySize: 64, senderCap: 2UL);

            await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);
            await sender.WrapAsync(s_outerPrefix, s_innerPayload).ConfigureAwait(false);

            Assert.That(
                async () => await sender
                    .WrapAsync(s_outerPrefix, s_innerPayload)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task ReplayStateIsScopedByPublisherWriterGroupAndTokenAsync()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                TokenId,
                signingKeyLength: policy.SigningKeyLength,
                encryptingKeyLength: policy.EncryptingKeyLength,
                keyNonceLength: policy.NonceLength);
            PublisherId publisherA = PublisherId.FromUInt32(100U);
            PublisherId publisherB = PublisherId.FromUInt32(200U);
            UadpSecurityWrapper senderAGroup1 = CreateWrapper(policy, key, publisherA);
            UadpSecurityWrapper senderBGroup1 = CreateWrapper(policy, key, publisherB);
            UadpSecurityWrapper senderAGroup2 = CreateWrapper(policy, key, publisherA);
            var receiverWindow = new SecurityTokenWindow();
            receiverWindow.RegisterToken(TokenId);
            UadpSecurityWrapper receiver = CreateWrapper(
                policy,
                key,
                PublisherId.FromUInt32(999U),
                receiverWindow);

            ReadOnlyMemory<byte> publisherAGroup1 = await senderAGroup1
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> publisherBGroup1 = await senderBGroup1
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> publisherAGroup2 = await senderAGroup2
                .WrapAsync(s_outerPrefix, s_innerPayload)
                .ConfigureAwait(false);

            UadpSecurityWrapper.UnwrapResult first = await UnwrapScopedAsync(
                receiver, publisherAGroup1, publisherA, writerGroupId: 1).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult secondPublisher = await UnwrapScopedAsync(
                receiver, publisherBGroup1, publisherB, writerGroupId: 1).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult secondGroup = await UnwrapScopedAsync(
                receiver, publisherAGroup2, publisherA, writerGroupId: 2).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult replay = await UnwrapScopedAsync(
                receiver, publisherAGroup1, publisherA, writerGroupId: 1).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(first.IsSuccess, Is.True, first.Reason);
                Assert.That(secondPublisher.IsSuccess, Is.True, secondPublisher.Reason);
                Assert.That(secondGroup.IsSuccess, Is.True, secondGroup.Reason);
                Assert.That(replay.IsSuccess, Is.False);
            });
        }

        [Test]
        public async Task RequiredModeRejectionDoesNotPoisonReplayStateAsync()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            PubSubSecurityKey key = TestSecurityKeyFactory.Create(
                TokenId,
                signingKeyLength: policy.SigningKeyLength,
                encryptingKeyLength: policy.EncryptingKeyLength,
                keyNonceLength: policy.NonceLength);
            PublisherId publisherId = PublisherId.FromUInt32(300U);
            UadpSecurityWrapper signOnlySender = CreateWrapper(policy, key, publisherId);
            UadpSecurityWrapper securedSender = CreateWrapper(policy, key, publisherId);
            var receiverWindow = new SecurityTokenWindow();
            receiverWindow.RegisterToken(TokenId);
            UadpSecurityWrapper receiver = CreateWrapper(
                policy,
                key,
                PublisherId.FromUInt32(999U),
                receiverWindow);

            ReadOnlyMemory<byte> signOnly = await signOnlySender
                .WrapAsync(s_outerPrefix, s_innerPayload, UadpSecurityWrapOptions.SignOnly)
                .ConfigureAwait(false);
            ReadOnlyMemory<byte> secured = await securedSender
                .WrapAsync(s_outerPrefix, s_innerPayload, UadpSecurityWrapOptions.SignAndEncrypt)
                .ConfigureAwait(false);

            UadpSecurityWrapper.UnwrapResult rejected = await UnwrapScopedAsync(
                receiver,
                signOnly,
                publisherId,
                writerGroupId: 7,
                MessageSecurityMode.SignAndEncrypt).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult accepted = await UnwrapScopedAsync(
                receiver,
                secured,
                publisherId,
                writerGroupId: 7,
                MessageSecurityMode.SignAndEncrypt).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(rejected.IsSuccess, Is.False);
                Assert.That(rejected.Status, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
                Assert.That(accepted.IsSuccess, Is.True, accepted.Reason);
            });
        }

        private static UadpSecurityWrapper CreateWrapper(
            PubSubAes256CtrPolicy policy,
            PubSubSecurityKey key,
            PublisherId publisherId,
            SecurityTokenWindow? window = null)
        {
            var ring = new PubSubSecurityKeyRing("group");
            ring.SetCurrent(key);
            return new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider("group", ring),
                new RandomNonceProvider(publisherId),
                window ?? new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
        }

        private static ValueTask<UadpSecurityWrapper.UnwrapResult> UnwrapScopedAsync(
            UadpSecurityWrapper receiver,
            ReadOnlyMemory<byte> wrapped,
            PublisherId publisherId,
            ushort writerGroupId,
            MessageSecurityMode requiredMode = MessageSecurityMode.SignAndEncrypt)
        {
            return receiver.TryUnwrapAsync(
                s_outerPrefix.AsMemory(),
                wrapped[s_outerPrefix.Length..],
                publisherId,
                writerGroupId,
                requiredMode);
        }

        private static (ulong SequenceNumber, byte[] Nonce) ReadNonce(
            ReadOnlyMemory<byte> wrapped)
        {
            ReadOnlyMemory<byte> securityAndPayload = wrapped[s_outerPrefix.Length..];
            Assert.That(
                UadpSecurityHeader.TryRead(
                    securityAndPayload.Span, out UadpSecurityHeader header, out _),
                Is.True);
            byte[] nonce = header.MessageNonce.ToArray();
            (_, ulong sequenceNumber) = AesCtrNonceLayout.Parse(nonce);
            return (sequenceNumber, nonce);
        }
    }
}
