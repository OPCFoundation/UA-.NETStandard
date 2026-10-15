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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security
{
    /// <summary>
    /// Exercises provider-driven token availability without relying on synchronous priming or rotation events.
    /// </summary>
    [TestFixture]
    public sealed class PubSubResolverTokenInitializationTests
    {
        [Test]
        public async Task ResolutionDoesNotAbandonAnAsynchronousCurrentKeyLookupAsync()
        {
            using PubSubSecurityKey key = CreateKey(1);
            var completion = new TaskCompletionSource<PubSubSecurityKey>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<PubSubSecurityKey>(completion.Task));
            provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(key);
            try
            {
                PubSubSecurityContext context = Resolve(provider.Object);
                provider.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
                Task<ReadOnlyMemory<byte>> send = context.Wrapper.WrapAsync(s_prefix, s_plaintext).AsTask();
                Assert.That(send.IsCompleted, Is.False);
                completion.SetResult(key);
                ReadOnlyMemory<byte> encoded = await send.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                UadpSecurityWrapper.UnwrapResult result = await context.Wrapper.TryUnwrapAsync(
                    s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_plaintext));
                provider.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
                provider.VerifyAdd(value => value.KeyRotated += It.IsAny<EventHandler<PubSubKeyRotatedEventArgs>>(),
                    Times.Never);
            }
            finally
            {
                completion.TrySetResult(key);
            }
        }

        [Test]
        public async Task AProviderRetainedNextTokenAuthenticatesBeforeLocalPromotionWithoutResettingReplayAsync()
        {
            using var receiverRing = new PubSubSecurityKeyRing("group");
            receiverRing.SetCurrent(CreateKey(1));
            receiverRing.AddFuture(CreateKey(2));
            PubSubSecurityKey future = receiverRing.TryGetByTokenId(2) ??
                throw new AssertionException("Missing pre-fetched token.");
            using var senderRing = new PubSubSecurityKeyRing("group");
            senderRing.SetCurrent(new PubSubSecurityKey(
                future.TokenId, ByteString.From(future.SigningKey.Span), ByteString.From(future.EncryptingKey.Span),
                ByteString.From(future.KeyNonce.Span), future.IssuedAt, future.Lifetime));
            PubSubSecurityContext sender = Resolve(new StaticSecurityKeyProvider("group", senderRing));
            PubSubSecurityContext receiver = Resolve(new StaticSecurityKeyProvider("group", receiverRing));
            ReadOnlyMemory<byte> encoded = await sender.Wrapper.WrapAsync(s_prefix, s_plaintext).ConfigureAwait(false);
            byte[] tampered = encoded[s_prefix.Length..].ToArray();
            tampered[^1] ^= 0x01;
            UadpSecurityWrapper.UnwrapResult invalid = await receiver.Wrapper.TryUnwrapAsync(s_prefix, tampered)
                .ConfigureAwait(false);
            Assert.That(invalid.IsSuccess, Is.False);
            UadpSecurityWrapper.UnwrapResult first = await receiver.Wrapper.TryUnwrapAsync(
                s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
            Assert.That(first.IsSuccess, Is.True, first.Reason);
            Assert.That(first.InnerPayload!.Value.ToArray(), Is.EqualTo(s_plaintext));
            Assert.That(receiverRing.Current!.TokenId, Is.EqualTo(1u));
            UadpSecurityWrapper.UnwrapResult replay = await receiver.Wrapper.TryUnwrapAsync(
                s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
            Assert.That(replay.IsSuccess, Is.False);
            Assert.That(replay.Reason, Does.Contain("Replay"));
            Assert.That(receiverRing.RotateToNextFuture(), Is.True);
            UadpSecurityWrapper.UnwrapResult replayAfterRotation = await receiver.Wrapper.TryUnwrapAsync(
                s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
            Assert.That(replayAfterRotation.IsSuccess, Is.False);
        }

        [Test]
        public async Task MatchingKeyBytesCannotAuthorizeADifferentReturnedTokenIdentityAsync()
        {
            using PubSubSecurityKey key = CreateKey(1);
            using var mismatched = new PubSubSecurityKey(9, ByteString.From(key.SigningKey.Span),
                ByteString.From(key.EncryptingKey.Span), ByteString.From(key.KeyNonce.Span),
                key.IssuedAt, key.Lifetime);
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(key);
            provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(mismatched);
            PubSubSecurityContext context = Resolve(provider.Object);
            ReadOnlyMemory<byte> encoded = await context.Wrapper.WrapAsync(s_prefix, s_plaintext).ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult result = await context.Wrapper.TryUnwrapAsync(
                s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(result.InnerPayload, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CanceledLookupsCannotPublishOrConsumeReplayStateWhenAKeyArrivesLateAsync(bool receive)
        {
            using PubSubSecurityKey key = CreateKey(1);
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(key);
            provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(key);
            PubSubSecurityContext context = Resolve(provider.Object);
            ReadOnlyMemory<byte> encoded = await context.Wrapper.WrapAsync(s_prefix, s_plaintext).ConfigureAwait(false);
            var release = new TaskCompletionSource<PubSubSecurityKey>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (receive)
            {
                provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>()))
                    .Returns(async (uint _, CancellationToken _) =>
                    {
                        PubSubSecurityKey result = await release.Task.ConfigureAwait(false);
                        return result;
                    });
            }
            else
            {
                provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<PubSubSecurityKey>(release.Task));
            }
            using var cancellation = new CancellationTokenSource();
            Task pending;
            if (receive)
            {
                pending = context.Wrapper.TryUnwrapAsync(
                    s_prefix, encoded[s_prefix.Length..], cancellation.Token).AsTask();
            }
            else
            {
                pending = context.Wrapper.WrapAsync(
                    s_prefix, s_plaintext, cancellationToken: cancellation.Token).AsTask();
            }
            try
            {
                Assert.That(pending.IsCompleted, Is.False);
                await cancellation.CancelAsync().ConfigureAwait(false);
                release.SetResult(key);
                await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(key);
                UadpSecurityWrapper.UnwrapResult result = await context.Wrapper.TryUnwrapAsync(
                    s_prefix, encoded[s_prefix.Length..]).ConfigureAwait(false);
                Assert.That(result.IsSuccess, Is.True, result.Reason);
                Assert.That(result.InnerPayload!.Value.ToArray(), Is.EqualTo(s_plaintext));
            }
            finally
            {
                release.TrySetResult(key);
            }
        }

        private static PubSubSecurityContext Resolve(IPubSubSecurityKeyProvider provider)
        {
            var resolver = new PubSubSecurityWrapperResolver([provider], NUnitTelemetryContext.Create());
            return resolver.Resolve(new PubSubConnectionDataType
            {
                Name = "Owned token test",
                PublisherId = Variant.From((ushort)2),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                ReaderGroups = [new ReaderGroupDataType
                {
                    SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityGroupId = "group"
                }]
            }) ?? throw new AssertionException("The configured security context is missing.");
        }

        private static PubSubSecurityKey CreateKey(uint token)
        {
            return new PubSubSecurityKey(token, ByteString.From(RandomNumberGenerator.GetBytes(32)),
                ByteString.From(RandomNumberGenerator.GetBytes(32)), ByteString.From(RandomNumberGenerator.GetBytes(4)),
                new DateTimeUtc(2026, 1, 1), TimeSpan.FromMinutes(5));
        }

        private static readonly byte[] s_prefix = [0xb1, 0x10, 0, 1, 0, 0, 0, 0];
        private static readonly byte[] s_plaintext = [10, 20, 30, 40];
    }
}
