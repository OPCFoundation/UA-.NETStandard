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
            PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr)!;

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

            await provider.StartAsync();

            Assert.That(fake.CallCount, Is.EqualTo(1));
            PubSubSecurityKey current = await provider.GetCurrentKeyAsync();
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
            await provider.StartAsync();

            int after = fake.CallCount;
            for (int i = 0; i < 10; i++)
            {
                _ = await provider.GetCurrentKeyAsync();
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
            await provider.StartAsync();
            await provider.StartAsync();
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
            await provider.StartAsync();

            int before = fake.CallCount;
            PubSubSecurityKey? key = await provider.TryGetKeyAsync(99U);
            Assert.That(fake.CallCount, Is.GreaterThan(before));
            Assert.That(key, Is.Null);
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
            await provider.StartAsync();

            int before = fake.CallCount;
            PubSubSecurityKey? key = await provider.TryGetKeyAsync(1U);
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
            await provider.StartAsync();
            PubSubSecurityKey served = await provider.GetCurrentKeyAsync();

            fake.FailOnce(new OpcUaSksException(
                StatusCodes.BadCommunicationError,
                "transient"));
            PubSubSecurityKey? lookup = await provider.TryGetKeyAsync(99U);
            Assert.That(lookup, Is.Null);

            PubSubSecurityKey afterFailure = await provider.GetCurrentKeyAsync();
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
            await provider.StartAsync();
            await provider.DisposeAsync();
            Assert.That(
                async () => await provider.GetCurrentKeyAsync(),
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
            await provider.StartAsync();
            await provider.DisposeAsync();
            await provider.DisposeAsync();
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
            await provider.StartAsync();
            int initial = fake.CallCount;

            for (int i = 0; i < 30 && fake.CallCount <= initial; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(20);
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
                async () => await provider.GetCurrentKeyAsync(),
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
            await provider.StartAsync();

            // Advance past the lifetime so the next refresh rotates.
            clock.Advance(TimeSpan.FromMilliseconds(60));
            await provider.TryGetKeyAsync(uint.MaxValue);
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

            await provider.DisposeAsync();

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

            await provider.DisposeAsync();

            Assert.That(sks.Disposed, Is.False);
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
