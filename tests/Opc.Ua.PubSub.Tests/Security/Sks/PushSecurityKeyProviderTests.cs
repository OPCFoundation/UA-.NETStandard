/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Tests;
using UadpEncoder = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using static Opc.Ua.PubSub.Tests.Transcoding.TranscodingTestUtilities;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Verifies pushed schedules at cryptographic and clock boundaries without timers or sockets.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class PushSecurityKeyProviderTests
    {
        /// <summary>
        /// Overlapping pushes retire old tokens before signature verification or replay promotion.
        /// </summary>
        [Test]
        public async Task SecurityR05ExpiredPushedTokenCannotAuthenticateDatasetAsync()
        {
            var clock = new FakeTimeProvider();
            await using var provider = new PushSecurityKeyProvider("group", timeProvider: clock);
            await PushAsync(provider, 1).ConfigureAwait(false);
            using var ring = new PubSubSecurityKeyRing("group");
            ring.SetCurrent(TestSecurityKeyFactory.Create(1));
            using var nonces = new RandomNonceProvider(PublisherId.FromUInt32(7));
            var attacker = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("group", ring), nonces, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            ReadOnlyMemory<byte> forged = await attacker.WrapAsync(
                ReadOnlyMemory<byte>.Empty, new byte[] { 42 }).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(10));
            await PushAsync(provider, 2).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(10));
            await PushAsync(provider, 3).ConfigureAwait(false);
            var receiver = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance, provider,
                nonces, new SecurityTokenWindow(), NUnitTelemetryContext.Create());
            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                ReadOnlyMemory<byte>.Empty, forged).ConfigureAwait(false);
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(await provider.TryGetKeyAsync(1).ConfigureAwait(false), Is.Null);
            Assert.That(await provider.TryGetKeyAsync(2).ConfigureAwait(false), Is.Null);
            Assert.That((await provider.GetCurrentKeyAsync().ConfigureAwait(false)).TokenId, Is.EqualTo(3));
        }

        /// <summary>
        /// Current and future keys follow half-open validity intervals with no implicit grace.
        /// </summary>
        [Test]
        public async Task SecurityR05CurrentFutureAndGraceKeysFollowScheduledValidityAsync()
        {
            var clock = new FakeTimeProvider();
            await using var provider = new PushSecurityKeyProvider("group", timeProvider: clock);
            await PushAsync(provider, 1).ConfigureAwait(false);
            Assert.That(await provider.TryGetKeyAsync(1).ConfigureAwait(false), Is.Not.Null);
            Assert.That(await provider.TryGetKeyAsync(2).ConfigureAwait(false), Is.Null);
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            Assert.That((await provider.GetCurrentKeyAsync().ConfigureAwait(false)).TokenId, Is.EqualTo(1));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(await provider.TryGetKeyAsync(1).ConfigureAwait(false), Is.Null);
            Assert.That((await provider.GetCurrentKeyAsync().ConfigureAwait(false)).TokenId, Is.EqualTo(2));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.That(await provider.TryGetKeyAsync(2).ConfigureAwait(false), Is.Not.Null);
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            Assert.That(await provider.TryGetKeyAsync(2).ConfigureAwait(false), Is.Null);
            await Assert.ThatAsync(async () => await provider.GetCurrentKeyAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        /// <summary>
        /// Actual signed data messages yield field 42 only inside the selected token's scheduled interval.
        /// </summary>
        [TestCase(99999999L, 1U, true)]
        [TestCase(100000000L, 1U, false)]
        [TestCase(100000001L, 1U, false)]
        [TestCase(99999999L, 2U, false)]
        [TestCase(100000000L, 2U, true)]
        [TestCase(100000001L, 2U, true)]
        [TestCase(199999999L, 2U, true)]
        [TestCase(200000000L, 2U, false)]
        [TestCase(200000001L, 2U, false)]
        public async Task SecurityR05ScheduledKeysAuthenticateOnlyWithinTheirIntervalsAsync(
            long elapsedTicks, uint tokenId, bool expectedSuccess)
        {
            var clock = new FakeTimeProvider();
            await using var provider = new PushSecurityKeyProvider("group", timeProvider: clock);
            await PushAsync(provider, 1).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromTicks(elapsedTicks));
            using var ring = new PubSubSecurityKeyRing("group");
            ring.SetCurrent(TestSecurityKeyFactory.Create(tokenId));
            using var nonces = new RandomNonceProvider(PublisherId.FromByte(4));
            var sender = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                new StaticSecurityKeyProvider("group", ring), nonces, new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            var window = new SecurityTokenWindow();
            window.RegisterToken(tokenId);
            var receiver = new UadpSecurityWrapper(PubSubAes128CtrPolicy.Instance,
                provider, nonces, window, NUnitTelemetryContext.Create());
            var message = NewUadpMessage(PublisherId.FromByte(4), 8, 12, Field("value", new Variant(42)));
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                message, NewContext().EncodingContext, out int boundary);
            ReadOnlyMemory<byte> frame = await sender.WrapAsync(encoded[..boundary], encoded[boundary..])
                .ConfigureAwait(false);
            UadpSecurityWrapper.UnwrapResult result = await receiver.TryUnwrapAsync(
                frame[..boundary], frame[boundary..]).ConfigureAwait(false);
            Assert.That(result.IsSuccess, Is.EqualTo(expectedSuccess), result.Reason);
            if (expectedSuccess)
            {
                byte[] clear = new byte[boundary + result.InnerPayload!.Value.Length];
                frame.Span[..boundary].CopyTo(clear);
                result.InnerPayload.Value.Span.CopyTo(clear.AsSpan(boundary));
                var decoded = await DecodeUadpAsync(clear, NewContext()).ConfigureAwait(false);
                Assert.That(decoded.DataSetMessages[0].Fields[0].Value, Is.EqualTo(new Variant(42)));
                Assert.That(decoded.PublisherId, Is.EqualTo(PublisherId.FromByte(4)));
            }
            else
            {
                Assert.That(result.InnerPayload, Is.Null);
            }
        }

        /// <summary>
        /// Replacing a schedule bounds retention and zeroizes retired material, without harming an owned snapshot.
        /// </summary>
        [Test]
        public async Task SecurityR05OverlappingRotationBoundsAndZeroizesRetiredKeysAsync()
        {
            var clock = new FakeTimeProvider();
            await using var provider = new PushSecurityKeyProvider("group", timeProvider: clock);
            await PushAsync(provider, 1).ConfigureAwait(false);
            PubSubSecurityKey retired = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            using PubSubSecurityKey snapshot = provider.GetSnapshot(1, CancellationToken.None)!;
            byte[] original = snapshot.SigningKey.Span.ToArray();
            clock.Advance(TimeSpan.FromSeconds(10));
            await PushAsync(provider, 2).ConfigureAwait(false);
            Assert.That(retired.SigningKey.Span.ToArray(), Is.All.Zero);
            Assert.That(snapshot.SigningKey.Span.ToArray(), Is.EqualTo(original));
            Assert.That(await provider.TryGetKeyAsync(1).ConfigureAwait(false), Is.Null);
            await PushAsync(provider, uint.MaxValue).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.That((await provider.GetCurrentKeyAsync().ConfigureAwait(false)).TokenId, Is.EqualTo(1));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThatAsync(async () => await provider.TryGetKeyAsync(1, cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// Invalid, cancelled, and disposed pushes cannot silently replace the live schedule.
        /// </summary>
        [Test]
        public async Task SecurityR05InvalidPushLeavesLiveScheduleUnchangedAsync()
        {
            await using var provider = new PushSecurityKeyProvider("group", timeProvider: new FakeTimeProvider());
            await PushAsync(provider, 1).ConfigureAwait(false);
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            byte[] original = current.SigningKey.Span.ToArray();
            await Assert.ThatAsync(async () => await provider.SetSecurityKeysAsync(
                PubSubSecurityPolicyUri.PubSubAes128Ctr, 2, Pack(2), [ByteString.Create([1])],
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)).ConfigureAwait(false),
                Throws.TypeOf<OpcUaSksException>());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(async () => await provider.SetSecurityKeysAsync(
                PubSubSecurityPolicyUri.PubSubAes128Ctr, 2, Pack(2), [],
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await provider.GetCurrentKeyAsync().ConfigureAwait(false), Is.SameAs(current));
            Assert.That(current.SigningKey.Span.ToArray(), Is.EqualTo(original));
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(current.SigningKey.Span.ToArray(), Is.All.Zero);
            await Assert.ThatAsync(async () => await PushAsync(provider, 2).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Pushes the deterministic current and next policy-sized key.
        /// </summary>
        internal static ValueTask PushAsync(PushSecurityKeyProvider provider, uint tokenId)
        {
            uint next = tokenId == uint.MaxValue ? 1 : tokenId + 1;
            return provider.SetSecurityKeysAsync(
                PubSubSecurityPolicyUri.PubSubAes128Ctr, tokenId, Pack(tokenId), [Pack(next)],
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private static ByteString Pack(uint tokenId)
        {
            using PubSubSecurityKey key = TestSecurityKeyFactory.Create(tokenId);
            byte[] packed = new byte[key.SigningKey.Length + key.EncryptingKey.Length + key.KeyNonce.Length];
            key.SigningKey.Span.CopyTo(packed);
            key.EncryptingKey.Span.CopyTo(packed.AsSpan(key.SigningKey.Length));
            key.KeyNonce.Span.CopyTo(packed.AsSpan(key.SigningKey.Length + key.EncryptingKey.Length));
            return ByteString.Create(packed);
        }
    }
}
