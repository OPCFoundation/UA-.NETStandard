/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies expiration scheduling for durable subscriptions restored without an attached client session.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class RestoredSubscriptionExpiryTests
    {
        /// <summary>
        /// Verifies that an abandoned restored subscription consumes its remaining lifetime and is deleted without
        /// reconnect.
        /// </summary>
        /// <remarks>
        /// A subscription persisted from an anonymous session may carry no UserIdentityToken; its owner
        /// must still be an anonymous identity so TransferSubscriptions can check the token type.
        /// </remarks>
        [TestCase(true)]
        [TestCase(false)]
        public async Task RestoredDurableSubscriptionExpiresWithoutAReconnectingClientAsync(bool storeAnonymousToken)
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory, clock);
            using (queueFactory)
            {
                server.SetupGet(value => value.IsRunning).Returns(false);
                server.SetupGet(value => value.DiagnosticsNodeManager)
                    .Returns(Mock.Of<IDiagnosticsNodeManager>());
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(Mock.Of<ICoreNodeManager>());
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var stored = new StoredSubscription
                {
                    Id = 41,
                    IsDurable = true,
                    PublishingInterval = 1_000,
                    MaxKeepaliveCount = 1,
                    MaxLifetimeCount = 3,
                    // every publishing cycle advances the lifetime (OPC 10000-4 §5.14.1.4), so
                    // one cycle consumes the next unit and the following one expires it.
                    LifetimeCounter = 1,
                    MaxMessageCount = 10,
                    SequenceNumber = 1,
                    UserIdentityToken = storeAnonymousToken ? new AnonymousIdentityToken() : null,
                    SentMessages = [],
                    MonitoredItems = []
                };
                var store = new Mock<ISubscriptionStore>();
                store.Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [stored]));
                server.SetupGet(value => value.SubscriptionStore).Returns(store.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        DurableSubscriptionsEnabled = true,
                        MaxSubscriptionCount = 10,
                        MinSubscriptionLifetime = 0,
                        MaxDurableSubscriptionLifetimeInHours = 24
                    }
                };
                using var master = new MasterNodeManager(server.Object, configuration, null, [], null);
                server.SetupGet(value => value.NodeManager).Returns(master);
                using var manager = new SubscriptionManager(server.Object, configuration, clock);
                var deleted = new TaskCompletionSource<StatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
                server.Setup(value => value.DeleteSubscriptionAsync(
                        It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                    .Returns<uint, CancellationToken>(async (id, ct) =>
                    {
                        try
                        {
                            StatusCode status = await manager.DeleteSubscriptionAsync(null!, id, ct)
                                .ConfigureAwait(false);
                            deleted.TrySetResult(status);
                        }
                        catch (Exception exception)
                        {
                            deleted.TrySetException(exception);
                            throw;
                        }
                    });

                await manager.RestoreSubscriptionsAsync().ConfigureAwait(false);
                ISubscription restored = manager.GetSubscriptions()[0];
                Assert.That(restored.EffectiveIdentity, Is.Not.Null);
                Assert.That(restored.EffectiveIdentity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
                Assert.That(manager.CaptureAbandonedPublishTimerSnapshot(), Has.Count.EqualTo(1));
                clock.Advance(TimeSpan.FromMilliseconds(1_001));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                Assert.That(deleted.Task.IsCompleted, Is.False);
                Assert.That(restored.ToStorableSubscription().LifetimeCounter, Is.EqualTo(2));

                clock.Advance(TimeSpan.FromMilliseconds(1_000));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                StatusCode result = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(result, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.GetSubscriptions(), Is.Empty);
                Assert.That(manager.CaptureAbandonedPublishTimerSnapshot(), Is.Empty);
                Assert.That(restored.IsDeleted, Is.True);
            }
        }
    }
}
