// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="PullSecurityKeyProvider"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class PullSecurityKeyProviderTests
    {
        private const string GroupId = "group-1";

        private static IPubSubSecurityPolicy Policy =>
            PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;

        private static PullSecurityKeyProviderOptions DefaultOptions(int futureKeys = 2)
        {
            return new PullSecurityKeyProviderOptions
            {
                RequestedFutureKeyCount = futureKeys,
                RefreshLeadTime = TimeSpan.FromSeconds(10),
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxConsecutiveFailures = 3
            };
        }

        [Test]
        public async Task StartAsync_PerformsInitialPullAndPopulatesRing()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);

            await provider.StartAsync().ConfigureAwait(false);

            Assert.That(fake.CallCount, Is.EqualTo(1));
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(current.TokenId, Is.EqualTo(1U));
            Assert.That(provider.Ring.Current, Is.SameAs(current));
        }

        [Test]
        public async Task GetCurrentKeyAsync_DoesNotCallSksAfterStart()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            int after = fake.CallCount;
            for (int i = 0; i < 10; i++)
            {
                _ = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            }
            Assert.That(fake.CallCount, Is.EqualTo(after));
        }

        [Test]
        public async Task StartAsync_IsIdempotent()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            await provider.StartAsync().ConfigureAwait(false);
            Assert.That(fake.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task TryGetKeyAsync_TriggersOpportunisticPullForUnknownFutureToken()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            int before = fake.CallCount;
            PubSubSecurityKey? key = await provider.TryGetKeyAsync(99U).ConfigureAwait(false);
            Assert.That(key, Is.Null);
            // The refresh runs in the background, never inline.
            Assert.That(provider.OpportunisticRefreshTask, Is.Not.Null);
            await provider.OpportunisticRefreshTask!.ConfigureAwait(false);
            Assert.That(fake.CallCount, Is.GreaterThan(before));
        }

        [Test]
        public async Task ForgedTokenIdsCallSksAtMostOncePerRefreshInterval()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            PullSecurityKeyProviderOptions options = DefaultOptions();
            options.OpportunisticRefreshInterval = TimeSpan.FromSeconds(30);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                options,
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            int before = fake.CallCount;
            for (uint i = 0; i < 100; i++)
            {
                PubSubSecurityKey? key = await provider.TryGetKeyAsync(uint.MaxValue - i).ConfigureAwait(false);
                Assert.That(key, Is.Null);
                if (provider.OpportunisticRefreshTask is Task pending)
                {
                    await pending.ConfigureAwait(false);
                }
            }
            Assert.That(fake.CallCount, Is.EqualTo(before + 1));

            clock.Advance(TimeSpan.FromSeconds(31));
            _ = await provider.TryGetKeyAsync(uint.MaxValue).ConfigureAwait(false);
            await provider.OpportunisticRefreshTask!.ConfigureAwait(false);
            Assert.That(fake.CallCount, Is.EqualTo(before + 2));
        }

        [Test]
        public async Task TryGetKeyAsyncDoesNotWaitForTheSks()
        {
            var sks = new BlockingSks();
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider(DateTimeOffset.UtcNow));

            ValueTask<PubSubSecurityKey?> lookup = provider.TryGetKeyAsync(7U);

            Assert.That(lookup.IsCompleted, Is.True);
            Assert.That(await lookup.ConfigureAwait(false), Is.Null);
            await sks.Called.Task.ConfigureAwait(false);
            sks.Release.SetResult(true);
            await provider.OpportunisticRefreshTask!.ConfigureAwait(false);
        }

        private sealed class BlockingSks : ISecurityKeyService
        {
            public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged
            {
                add { }
                remove { }
            }

            public TaskCompletionSource<bool> Called { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask<SksKeyResponse> GetSecurityKeysAsync(
                SksKeyRequest request,
                CancellationToken cancellationToken = default)
            {
                Called.TrySetResult(true);
                await Release.Task.ConfigureAwait(false);
                throw new OpcUaSksException(StatusCodes.BadCommunicationError, "unavailable");
            }
        }

        [Test]
        public async Task TryGetKeyAsync_ReturnsKnownKeyWithoutPull()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(futureKeys: 4),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            int before = fake.CallCount;
            PubSubSecurityKey? key = await provider.TryGetKeyAsync(1U).ConfigureAwait(false);
            Assert.That(key, Is.Not.Null);
            Assert.That(key!.TokenId, Is.EqualTo(1U));
            Assert.That(fake.CallCount, Is.EqualTo(before));
        }

        [Test]
        public async Task GetCurrentKeyAsync_KeepsServingLastKeyWhenSksFails()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            PubSubSecurityKey served = await provider.GetCurrentKeyAsync().ConfigureAwait(false);

            fake.FailOnce(new OpcUaSksException(
                StatusCodes.BadCommunicationError,
                "transient"));
            PubSubSecurityKey? lookup = await provider.TryGetKeyAsync(99U).ConfigureAwait(false);
            Assert.That(lookup, Is.Null);
            await provider.OpportunisticRefreshTask!.ConfigureAwait(false);

            PubSubSecurityKey afterFailure = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(afterFailure, Is.SameAs(served));
        }

        [Test]
        public async Task DisposeAsync_StopsBackgroundTaskCleanly()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
            Assert.That(
                async () => await provider.GetCurrentKeyAsync().ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task BackgroundLoop_RefreshesNearLifetimeEnd()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(1));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            int initial = fake.CallCount;

            for (int i = 0; i < 30 && fake.CallCount <= initial; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(20).ConfigureAwait(false);
            }
            Assert.That(fake.CallCount, Is.GreaterThan(initial));
        }

        [Test]
        public void GetCurrentKeyAsync_ThrowsBeforeStart()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            Assert.That(
                async () => await provider.GetCurrentKeyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMinutes(2));
            Assert.That(
                () => new PullSecurityKeyProvider(
                    string.Empty,
                    fake,
                    Policy,
                    DefaultOptions(),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new PullSecurityKeyProvider(
                    GroupId,
                    null!,
                    Policy,
                    DefaultOptions(),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new PullSecurityKeyProvider(
                    GroupId,
                    fake,
                    null!,
                    DefaultOptions(),
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new PullSecurityKeyProvider(
                    GroupId,
                    fake,
                    Policy,
                    null!,
                    NUnitTelemetryContext.Create(),
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new PullSecurityKeyProvider(
                    GroupId,
                    fake,
                    Policy,
                    DefaultOptions(),
                    null!,
                    new FakeTimeProvider()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new PullSecurityKeyProvider(
                    GroupId,
                    fake,
                    Policy,
                    DefaultOptions(),
                    NUnitTelemetryContext.Create(),
                    null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task KeyRotated_FiresWhenCurrentKeyExpires()
        {
            var fake = new FakeSecurityKeyService(Policy, TimeSpan.FromMilliseconds(50));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                fake,
                Policy,
                DefaultOptions(futureKeys: 4),
                NUnitTelemetryContext.Create(),
                clock);
            int rotationCount = 0;
            provider.KeyRotated += (_, _) => Interlocked.Increment(ref rotationCount);
            await provider.StartAsync().ConfigureAwait(false);

            // Advance past the lifetime so the next refresh rotates.
            clock.Advance(TimeSpan.FromMilliseconds(60));
            await provider.TryGetKeyAsync(uint.MaxValue).ConfigureAwait(false);
            await provider.OpportunisticRefreshTask!.ConfigureAwait(false);
            Assert.That(rotationCount, Is.GreaterThan(0));
        }

        [Test]
        public async Task DisposeAsyncDisposesOwnedSecurityKeyServiceAsync()
        {
            var sks = new DisposableTrackingSks();
            var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                ownsSecurityKeyService: true);

            await provider.DisposeAsync().ConfigureAwait(false);

            Assert.That(sks.Disposed, Is.True);
        }

        [Test]
        public async Task DisposeAsyncDoesNotDisposeExternalSecurityKeyServiceAsync()
        {
            var sks = new DisposableTrackingSks();
            var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                DefaultOptions(),
                NUnitTelemetryContext.Create(),
                new FakeTimeProvider(DateTimeOffset.UtcNow));

            await provider.DisposeAsync().ConfigureAwait(false);

            Assert.That(sks.Disposed, Is.False);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task ProviderFollowsTheSksKeyRotationOverManyLifetimesAsync()
        {
            // The provider used to stop refreshing after the SKS answered the
            // lead-time refreshes with BadNotFound ("no newer key yet") and then
            // kept the first key forever.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var sks = new InMemorySks(clock, TimeSpan.FromMinutes(60));
            await sks.AddGroupAsync().ConfigureAwait(false);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                new PullSecurityKeyProviderOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            for (int hour = 1; hour <= 5; hour++)
            {
                // Five minutes into every new key lifetime.
                await AdvanceAsync(clock, TimeSpan.FromMinutes(hour == 1 ? 65 : 60)).ConfigureAwait(false);
                PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
                uint sksCurrent = await sks.CurrentTokenIdAsync().ConfigureAwait(false);
                Assert.That(current.TokenId, Is.EqualTo(sksCurrent), $"token after {hour} lifetime(s)");
                Assert.That(current.IsExpired(clock), Is.False, $"expired after {hour} lifetime(s)");
            }
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task ProviderKeepsRefreshingAfterMaxConsecutiveFailuresAsync()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var sks = new InMemorySks(clock, TimeSpan.FromMinutes(60));
            await sks.AddGroupAsync().ConfigureAwait(false);
            PullSecurityKeyProviderOptions options = new()
            {
                RequestedFutureKeyCount = 1,
                ReconnectDelay = TimeSpan.FromSeconds(30),
                MaxConsecutiveFailures = 3
            };
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                options,
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            // The SKS is unreachable for ten refreshes, well past MaxConsecutiveFailures.
            sks.FailNextCalls = 10;
            await AdvanceAsync(clock, TimeSpan.FromMinutes(70)).ConfigureAwait(false);

            Assert.That(sks.FailNextCalls, Is.Zero, "the provider stopped retrying");
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(current.TokenId, Is.EqualTo(await sks.CurrentTokenIdAsync().ConfigureAwait(false)));
            Assert.That(current.IsExpired(clock), Is.False);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task ProviderAdoptsTheScheduleOfAForcedRotationAsync()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var sks = new InMemorySks(clock, TimeSpan.FromMinutes(60));
            await sks.AddGroupAsync().ConfigureAwait(false);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                new PullSecurityKeyProviderOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            // Token 2 becomes current at minute 10 and now expires at minute 70,
            // not at minute 120 as first scheduled.
            await AdvanceAsync(clock, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            await sks.ForceRotationAsync().ConfigureAwait(false);

            // The lead-time refresh (minute 55) picks up the new schedule.
            await AdvanceAsync(clock, TimeSpan.FromMinutes(46)).ConfigureAwait(false);
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(current.TokenId, Is.EqualTo(2u), "the provider did not follow the forced rotation");
            Assert.That(
                current.IssuedAt + current.Lifetime,
                Is.EqualTo(DateTimeUtc.From(new DateTime(2026, 1, 1, 1, 10, 0, DateTimeKind.Utc))),
                "the provider kept the old expiry of token 2");

            await AdvanceAsync(clock, TimeSpan.FromMinutes(19)).ConfigureAwait(false);
            current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(current.TokenId, Is.EqualTo(await sks.CurrentTokenIdAsync().ConfigureAwait(false)));
            Assert.That(current.IsExpired(clock), Is.False);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task ProviderStopsPublishingWhenEveryCachedKeyExpiredAsync()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var sks = new InMemorySks(clock, TimeSpan.FromMinutes(60));
            await sks.AddGroupAsync().ConfigureAwait(false);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                new PullSecurityKeyProviderOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);

            // Tokens 1-5 cover five hours; the SKS is unreachable for all of them.
            sks.FailNextCalls = int.MaxValue;
            await AdvanceAsync(clock, TimeSpan.FromMinutes(305)).ConfigureAwait(false);

            Assert.That(
                async () => await provider.GetCurrentKeyAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
            Assert.That(
                await provider.TryGetKeyAsync(5).ConfigureAwait(false),
                Is.Not.Null,
                "the expired key must stay available for lookup");

            // Once the SKS answers again, publication resumes with a valid key.
            sks.FailNextCalls = 0;
            await AdvanceAsync(clock, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(current.IsExpired(clock), Is.False);
        }

        [Test]
        [Category("SecurityRegression")]
        public async Task DelayedSksResponseDoesNotExtendTheCurrentKeyAsync()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var sks = new InMemorySks(clock, TimeSpan.FromMinutes(60));
            await sks.AddGroupAsync().ConfigureAwait(false);
            await using var provider = new PullSecurityKeyProvider(
                GroupId,
                sks,
                Policy,
                new PullSecurityKeyProviderOptions(),
                NUnitTelemetryContext.Create(),
                clock);
            await provider.StartAsync().ConfigureAwait(false);
            await AdvanceAsync(clock, TimeSpan.FromMinutes(54)).ConfigureAwait(false);

            // The lead-time refresh at minute 55 reads TimeToNextKey = 5 min,
            // but its response is processed only at minute 66: token 1 must
            // still expire at minute 60, not at minute 71.
            sks.DelayNextResponse = TimeSpan.FromMinutes(11);
            await AdvanceAsync(clock, TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            PubSubSecurityKey current = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            Assert.That(sks.DelayNextResponse, Is.EqualTo(TimeSpan.Zero), "the delayed refresh did not run");
            Assert.That(current.TokenId, Is.EqualTo(await sks.CurrentTokenIdAsync().ConfigureAwait(false)));
            Assert.That(current.IsExpired(clock), Is.False);
        }

        private static async Task AdvanceAsync(FakeTimeProvider clock, TimeSpan duration)
        {
            // Small steps with a real yield let the background loop run its
            // timers as the fake clock moves.
            TimeSpan step = TimeSpan.FromSeconds(15);
            for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += step)
            {
                clock.Advance(step);
                await Task.Delay(2).ConfigureAwait(false);
            }
            await Task.Delay(50).ConfigureAwait(false);
        }

        /// <summary>
        /// The in-box SKS behind an <see cref="ISecurityKeyService"/>, with
        /// injectable failures.
        /// </summary>
        private sealed class InMemorySks : ISecurityKeyService
        {
            private const string Caller = "subscriber";
            private readonly InMemoryPubSubKeyServiceServer m_server;
            private readonly TimeSpan m_keyLifetime;
            private readonly FakeTimeProvider m_clock;
            private int m_failNextCalls;
            private long m_delayNextResponseTicks;

            public InMemorySks(FakeTimeProvider clock, TimeSpan keyLifetime)
            {
                m_server = new InMemoryPubSubKeyServiceServer(clock);
                m_clock = clock;
                m_keyLifetime = keyLifetime;
            }

            /// <summary>
            /// Clock time that passes between the SKS building its next
            /// response and the caller receiving it.
            /// </summary>
            public TimeSpan DelayNextResponse
            {
                get => TimeSpan.FromTicks(Interlocked.Read(ref m_delayNextResponseTicks));
                set => Interlocked.Exchange(ref m_delayNextResponseTicks, value.Ticks);
            }

#pragma warning disable CS0067 // Required by ISecurityKeyService; this test double never raises it.
            public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;
#pragma warning restore CS0067

            public int FailNextCalls
            {
                get => Volatile.Read(ref m_failNextCalls);
                set => Volatile.Write(ref m_failNextCalls, value);
            }

            public ValueTask AddGroupAsync()
            {
                return m_server.AddSecurityGroupAsync(new SksSecurityGroup(
                    GroupId,
                    PubSubSecurityPolicyUri.PubSubAes128Ctr,
                    m_keyLifetime,
                    4,
                    2,
                    Array.Empty<PubSubSecurityKey>(),
                    [Caller]));
            }

            public ValueTask ForceRotationAsync()
            {
                return m_server.ForceKeyRotationAsync(GroupId);
            }

            public async Task<uint> CurrentTokenIdAsync()
            {
                SksKeyResponse response = await m_server.GetSecurityKeysAsync(
                    Caller, new SksKeyRequest(GroupId, 0, 1)).ConfigureAwait(false);
                return response.FirstTokenId;
            }

            public async ValueTask<SksKeyResponse> GetSecurityKeysAsync(
                SksKeyRequest request,
                CancellationToken cancellationToken = default)
            {
                int remaining = Volatile.Read(ref m_failNextCalls);
                if (remaining > 0)
                {
                    Interlocked.Decrement(ref m_failNextCalls);
                    throw new OpcUaSksException(StatusCodes.BadCommunicationError, "SKS unreachable.");
                }
                SksKeyResponse response = await m_server
                    .GetSecurityKeysAsync(Caller, request, default, cancellationToken)
                    .ConfigureAwait(false);
                long delayTicks = Interlocked.Exchange(ref m_delayNextResponseTicks, 0);
                if (delayTicks > 0)
                {
                    m_clock.Advance(TimeSpan.FromTicks(delayTicks));
                }
                return response;
            }
        }

        private sealed class DisposableTrackingSks : ISecurityKeyService, IAsyncDisposable
        {
            public bool Disposed { get; private set; }

#pragma warning disable CS0067 // Required by ISecurityKeyService; this test double never raises it.
            public event EventHandler<SksAvailabilityChangedEventArgs>? AvailabilityChanged;
#pragma warning restore CS0067

            public ValueTask<SksKeyResponse> GetSecurityKeysAsync(
                SksKeyRequest request,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return default;
            }
        }
    }
}
