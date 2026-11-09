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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transports;
using UaLens.Plugins.PubSub;

namespace UaLens.Tests.PubSub
{
    [TestFixture]
    public sealed class PubSubKeyTransitionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingKeysCancelsAndDrainsLateLookupsWithoutDisposingTheirProviderAsync(bool lookup)
        {
            var clock = new PubSubTestClock();
            using PubSubSecurityKey key = CreateKey(1, clock.GetUtcNow());
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task WaitAsync(CancellationToken token)
            {
                using CancellationTokenRegistration registration = token.Register(() => canceled.TrySetResult());
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .Returns(async (CancellationToken token) =>
                {
                    await WaitAsync(token).ConfigureAwait(false);
                    return key;
                });
            provider.Setup(value => value.TryGetKeyAsync(1, It.IsAny<CancellationToken>()))
                .Returns(async (uint _, CancellationToken token) =>
                {
                    await WaitAsync(token).ConfigureAwait(false);
                    return key;
                });
            var guarded = new PubSubValidatedKeyProvider(
                provider.Object, "group", clock, new PubSubObservationStore(2));
            Task pending;
            if (lookup)
            {
                pending = guarded.TryGetKeyAsync(1).AsTask();
            }
            else
            {
                pending = guarded.GetCurrentKeyAsync().AsTask();
            }
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task disposal = guarded.DisposeAsync().AsTask();
                await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(disposal.IsCompleted, Is.False, "The provider is still using its owned resources.");
                await Assert.ThatAsync(() => guarded.GetCurrentKeyAsync().AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                release.TrySetResult();
                await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                    .ConfigureAwait(false);
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await guarded.DisposeAsync().ConfigureAwait(false);
                provider.VerifyRemove(value => value.KeyRotated -= It.IsAny<EventHandler<PubSubKeyRotatedEventArgs>>(),
                    Times.Once);
                Assert.That(key.SigningKey.Span.ToArray(), Is.Not.All.Zero,
                    "Only the configured owner may destroy borrowed key material.");
            }
            finally
            {
                release.TrySetResult();
                await guarded.DisposeAsync().ConfigureAwait(false);
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task RetainedReceiveKeysPreserveTheProvidersPreviousAndNextTokenOverlapAsync(int offset)
        {
            var clock = new PubSubTestClock();
            using PubSubSecurityKey key = CreateKey(7, clock.GetUtcNow().AddSeconds(offset));
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.TryGetKeyAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(key);
            provider.Setup(value => value.TryGetKeyAsync(99, It.IsAny<CancellationToken>()))
                .ReturnsAsync((PubSubSecurityKey?)null);
            var guarded = new PubSubValidatedKeyProvider(
                provider.Object, "group", clock, new PubSubObservationStore(2));
            await using (guarded.ConfigureAwait(false))
            {
                Assert.That(await guarded.TryGetKeyAsync(7).ConfigureAwait(false), Is.SameAs(key));
                Assert.That(await guarded.TryGetKeyAsync(99).ConfigureAwait(false), Is.Null);
                provider.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
        }

        [TestCase("expired")]
        [TestCase("future")]
        [TestCase("group")]
        [TestCase("canceled")]
        public async Task LateCurrentKeyRepliesAreRevalidatedBeforeTheyCanBeUsedAsync(string change)
        {
            var clock = new PubSubTestClock();
            using PubSubSecurityKey key = CreateKey(1,
                clock.GetUtcNow().AddSeconds(change == "future" ? 1 : 0));
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            string group = "group";
            provider.SetupGet(value => value.SecurityGroupId).Returns(() => group);
            var release = new TaskCompletionSource<PubSubSecurityKey>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<PubSubSecurityKey>(release.Task));
            var observations = new PubSubObservationStore(2);
            var guarded = new PubSubValidatedKeyProvider(provider.Object, "group", clock, observations);
            using var cancellation = new CancellationTokenSource();
            Task<PubSubSecurityKey> pending = guarded.GetCurrentKeyAsync(cancellation.Token).AsTask();
            try
            {
                if (change == "expired")
                {
                    clock.Advance(TimeSpan.FromSeconds(1));
                }
                else if (change == "group")
                {
                    group = "changed";
                }
                else if (change == "canceled")
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
                release.SetResult(key);
                if (change == "canceled")
                {
                    await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => pending, Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadSecurityChecksFailed))
                        .ConfigureAwait(false);
                    Assert.That(observations.Snapshot().Evidence.Contains(row =>
                        row.Area == "Security key" && row.Status == StatusCodes.BadSecurityChecksFailed), Is.True);
                }
                provider.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(key);
                await guarded.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AProviderCannotSubstituteAnotherTokenForTheReceivedTokenAsync()
        {
            var clock = new PubSubTestClock();
            using PubSubSecurityKey key = CreateKey(9, clock.GetUtcNow());
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.TryGetKeyAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(key);
            var guarded = new PubSubValidatedKeyProvider(
                provider.Object, "group", clock, new PubSubObservationStore(2));
            await using (guarded.ConfigureAwait(false))
            {
                await Assert.ThatAsync(() => guarded.TryGetKeyAsync(7).AsTask(), Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task StoppingDuringLateKeyAcquisitionReleasesLeasesWithoutOpeningTheTransportAsync()
        {
            var released = new List<string>();
            var transport = new Mock<IPubSubTransportFactory>(MockBehavior.Strict);
            transport.SetupGet(value => value.TransportProfileUri).Returns(Profiles.PubSubUdpUadpTransport);
            var binding = new ConfiguredPubSubTransportProvider("owned-loopback", [PubSubProfile.UdpUadp],
                static _ => new PubSubPrerequisite("Transport", PubSubReadiness.Ready, "Owned binding."),
                (_, _, _) => ValueTask.FromResult(new PubSubTransportLease(
                    transport.Object, new ReleaseOwner(() => released.Add("transport")))));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var keys = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            var provider = new ConfiguredPubSubKeyProvider("keys", async (_, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() => canceled.TrySetResult());
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return new PubSubKeyProviderLease(keys.Object, new ReleaseOwner(() => released.Add("keys")));
            });
            PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
            {
                TransportProviderId = binding.Id,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityGroupId = "group",
                SecurityProviderId = provider.Id,
                KeySource = PubSubKeySource.ConfiguredProvider
            };
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var factory = new PubSubRuntimeFactory(
                telemetry, transportProviders: [binding], keyProviders: [provider]);
            var workspace = new PubSubWorkspace(factory, telemetry);
            await using (workspace.ConfigureAwait(false))
            {
                await workspace.ConfigureAsync(configuration).ConfigureAwait(false);
                Task start = workspace.StartAsync(new());
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Task stop = workspace.StopAsync();
                    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(stop.IsCompleted, Is.False);
                    Assert.That(released, Is.Empty);
                    release.TrySetResult();
                    await stop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await Assert.ThatAsync(() => start, Throws.InstanceOf<OperationCanceledException>())
                        .ConfigureAwait(false);
                    Assert.That(released, Is.EqualTo(new List<string> { "keys", "transport" }));
                    transport.Verify(value => value.Create(
                        It.IsAny<PubSubConnectionDataType>(), It.IsAny<ITelemetryContext>(), It.IsAny<TimeProvider>()),
                        Times.Never);
                    keys.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
                    Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Offline));
                }
                finally
                {
                    release.TrySetResult();
                    await workspace.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [TestCase(MessageSecurityMode.Sign, false)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, false)]
        [TestCase(MessageSecurityMode.Sign, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, true)]
        public async Task ExpiredCurrentKeysNeverReachTheTransportAndRotationUsesTheNextKeyAsync(
            MessageSecurityMode securityMode, bool asynchronousKeys)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
            var clock = new PubSubTestClock();
            using var ring = new PubSubSecurityKeyRing("group", clock);
            ring.SetCurrent(CreateKey(1, clock.GetUtcNow()));
            ring.AddFuture(CreateKey(2, clock.GetUtcNow().AddSeconds(1)));
            IPubSubSecurityKeyProvider keys = new StaticSecurityKeyProvider("group", ring);
            if (asynchronousKeys)
            {
                keys = new AsynchronousKeyProvider(keys);
            }
            var transport = new PubSubControlledRuntimeTests.EncodedLoopbackTransport();
            await using (transport.ConfigureAwait(false))
            {
                var binding = new ConfiguredPubSubTransportProvider("owned-loopback", [PubSubProfile.UdpUadp],
                    static _ => new PubSubPrerequisite("Transport", PubSubReadiness.Ready, "Owned encoded loopback."),
                    (_, _, _) => ValueTask.FromResult(new PubSubTransportLease(transport)));
                var provider = new ConfiguredPubSubKeyProvider("keys",
                    (_, _) => ValueTask.FromResult(new PubSubKeyProviderLease(keys)));
                PubSubConfiguration configuration = PubSubTestRuntime.Configuration with
                {
                    TransportProviderId = binding.Id,
                    SecurityMode = securityMode,
                    SecurityGroupId = "group",
                    SecurityProviderId = provider.Id,
                    KeySource = PubSubKeySource.ConfiguredProvider,
                    Publication = PubSubPublication.Synthetic,
                    PublishingIntervalMs = 60000,
                    MaxPublishedMessages = 10,
                    PublisherFilter = 2,
                    Fields = [new() { Name = "Value", Type = BuiltInType.Int32 }]
                };
                var observations = new PubSubObservationStore(8);
                var factory = new PubSubRuntimeFactory(telemetry, clock, [binding], [provider]);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                PubSubRuntimeHandle runtime = await factory.CreateAsync(
                    configuration, new PubSubStartAuthorization(AllowPublication: true),
                    null, observations, deadline.Token).ConfigureAwait(false);
                await using (runtime.ConfigureAwait(false))
                {
                    await runtime.Application.StartAsync(deadline.Token).ConfigureAwait(false);
                    var writer = (WriterGroup)runtime.Application.Connections[0].WriterGroups[0];
                    await writer.DisableAsync(deadline.Token).ConfigureAwait(false);
                    Func<PubSubNetworkMessage, CancellationToken, ValueTask> send = writer.PublishSink ??
                        throw new AssertionException("The real runtime has no publication sink.");
                    await send(Message(1), deadline.Token).ConfigureAwait(false);
                    await observations.WaitForDataSetsAsync(1, deadline.Token).ConfigureAwait(false);
                    clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1));
                    await send(Message(2), deadline.Token).ConfigureAwait(false);
                    await observations.WaitForDataSetsAsync(2, deadline.Token).ConfigureAwait(false);
                    long before = transport.SentFrames;
                    clock.Advance(TimeSpan.FromTicks(1));
                    await Assert.ThatAsync(() => send(Message(3), deadline.Token).AsTask(),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property("StatusCode").EqualTo(StatusCodes.BadSecurityChecksFailed))
                        .ConfigureAwait(false);
                    Assert.That(transport.SentFrames, Is.EqualTo(before));
                    Assert.That(ring.RotateToNextFuture(), Is.True);
                    await send(Message(4), deadline.Token).ConfigureAwait(false);
                    await observations.WaitForDataSetsAsync(3, deadline.Token).ConfigureAwait(false);
                    Assert.That(observations.Snapshot().Values[0].Value, Is.EqualTo(Variant.From(4)));
                    Assert.That(transport.SentFrames, Is.EqualTo(before + 1));
                    Assert.That(ring.Current!.TokenId, Is.EqualTo(2u));
                }
                Assert.That(transport.IsConnected, Is.False);
                Assert.That(ring.Current!.TokenId, Is.EqualTo(2u),
                    "The document borrows the registered provider; only its owner may clear the ring.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ProviderFailuresRemainFailuresAndNeverUseACachedKeyAsync(bool denied)
        {
            var clock = new PubSubTestClock();
            using PubSubSecurityKey key = CreateKey(1, clock.GetUtcNow());
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.SetupSequence(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(key)
                .ThrowsAsync(new ServiceResultException(denied
                    ? StatusCodes.BadUserAccessDenied : StatusCodes.BadCommunicationError));
            var guarded = new PubSubValidatedKeyProvider(
                provider.Object, "group", clock, new PubSubObservationStore(2));
            await using (guarded.ConfigureAwait(false))
            {
                Assert.That(await guarded.GetCurrentKeyAsync().ConfigureAwait(false), Is.SameAs(key));
                await Assert.ThatAsync(() => guarded.GetCurrentKeyAsync().AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(denied
                        ? StatusCodes.BadUserAccessDenied : StatusCodes.BadCommunicationError)).ConfigureAwait(false);
                provider.Verify(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
        }

        [Test]
        public async Task AMissingCurrentKeyIsAnExplicitSecurityFailure()
        {
            var provider = new Mock<IPubSubSecurityKeyProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SecurityGroupId).Returns("group");
            provider.Setup(value => value.GetCurrentKeyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PubSubSecurityKey)null!);
            var guarded = new PubSubValidatedKeyProvider(
                provider.Object, "group", new PubSubTestClock(), new PubSubObservationStore(2));
            await using (guarded.ConfigureAwait(false))
            {
                await Assert.ThatAsync(() => guarded.GetCurrentKeyAsync().AsTask(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadSecurityChecksFailed))
                    .ConfigureAwait(false);
            }
        }

        private static UadpNetworkMessage Message(ushort sequence)
        {
            return new UadpNetworkMessage
            {
                PublisherId = PublisherId.FromUInt16(2),
                WriterGroupId = 100,
                SequenceNumber = sequence,
                ContentMask = PubSubContentMasks.DefaultUadpNetwork,
                DataSetMessages = [new UadpDataSetMessage
                {
                    DataSetWriterId = 1,
                    SequenceNumber = sequence,
                    MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1 },
                    ContentMask = PubSubContentMasks.DefaultUadpDataSet,
                    Fields = [PubSubTestRuntime.Field(sequence)]
                }]
            };
        }

        private static PubSubSecurityKey CreateKey(uint token, DateTimeOffset issuedAt)
        {
            return new PubSubSecurityKey(token, ByteString.From(RandomNumberGenerator.GetBytes(32)),
                ByteString.From(RandomNumberGenerator.GetBytes(32)), ByteString.From(RandomNumberGenerator.GetBytes(4)),
                DateTimeUtc.From(issuedAt), TimeSpan.FromSeconds(1));
        }

        private sealed class ReleaseOwner(Action released) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                released();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class AsynchronousKeyProvider(IPubSubSecurityKeyProvider provider) : IPubSubSecurityKeyProvider
        {
            public string SecurityGroupId => provider.SecurityGroupId;

            public event EventHandler<PubSubKeyRotatedEventArgs>? KeyRotated
            {
                add => provider.KeyRotated += value;
                remove => provider.KeyRotated -= value;
            }

            public async ValueTask<PubSubSecurityKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                return await provider.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
            }

            public async ValueTask<PubSubSecurityKey?> TryGetKeyAsync(
                uint tokenId, CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                return await provider.TryGetKeyAsync(tokenId, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
