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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A durable subscription restore that fails must surface instead of
    /// being logged and dropped, unless the manager is told to continue.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class SubscriptionRestoreFailureTests
    {
        [Test]
        public void StoreThatThrowsFailsTheRestore()
        {
            var failure = new InvalidOperationException("store unavailable");
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(exception.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void StoreThatReportsFailureFailsTheRestore()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task StoreWithNothingToRestoreSucceedsAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(true, [])));

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
        }

        [Test]
        public void SubscriptionThatCannotBeRestoredFailsTheRestore()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [CreateStored(41)])),
                failingSubscriptionId: 41);

            AggregateException exception = Assert.ThrowsAsync<AggregateException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
            Assert.That(
                ((ServiceResultException)exception.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ContinueOnRestoreFailureKeepsTheRestoredSubscriptionsAsync()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(
                        true,
                        [CreateStored(41), CreateStored(42)])),
                failingSubscriptionId: 41);
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Has.Count.EqualTo(1));
            Assert.That(fixture.Manager.GetSubscriptions()[0].Id, Is.EqualTo(42u));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.Is<Dictionary<uint, ArrayOf<uint>>>(created =>
                        created.Count == 1 && created.ContainsKey(42u)),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ContinueOnRestoreFailureLogsAStoreThatReportsFailureAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Error &&
                    entry.Message.Contains("could not be restored", StringComparison.Ordinal)));
            fixture.Store.Verify(
                value => value.OnSubscriptionRestoreCompleteAsync(
                    It.IsAny<Dictionary<uint, ArrayOf<uint>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ContinueOnRestoreFailureLogsTheLostSubscriptionsAsync()
        {
            using var fixture = new Fixture(
                store => store
                    .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(
                        true,
                        [CreateStored(41), CreateStored(42)])),
                failingSubscriptionId: 41);
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry =>
                    entry.Level == LogLevel.Warning &&
                    entry.Message.StartsWith("1 of 2 stored durable subscriptions", StringComparison.Ordinal)));
        }

        [Test]
        public void StoreThatReportsFailureIsLoggedBeforeTheRestoreFails()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RestoreSubscriptionResult(false, null)));

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false));

            Assert.That(
                fixture.Logs.Entries,
                Has.Some.Matches<(LogLevel Level, string Message)>(entry => entry.Level == LogLevel.Error));
        }

        [Test]
        public async Task ContinueOnRestoreFailureIgnoresAFailingStoreAsync()
        {
            using var fixture = new Fixture(store => store
                .Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("store unavailable")));
            fixture.Manager.ContinueOnRestoreFailure = true;

            await fixture.Manager.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(fixture.Manager.GetSubscriptions(), Is.Empty);
        }

        private static StoredSubscription CreateStored(uint id)
        {
            return new StoredSubscription
            {
                Id = id,
                IsDurable = true,
                PublishingInterval = 1_000,
                MaxKeepaliveCount = 1,
                MaxLifetimeCount = 3,
                MaxMessageCount = 10,
                SequenceNumber = 1,
                UserIdentityToken = new AnonymousIdentityToken(),
                SentMessages = [],
                MonitoredItems = []
            };
        }

        private sealed class Fixture : IDisposable
        {
            public Fixture(
                Action<Mock<ISubscriptionStore>> setupStore,
                uint failingSubscriptionId = 0)
            {
                var clock = new FakeTimeProvider();
                Mock<IServerInternal> server = DeterministicServerMock.Create(
                    out m_queueFactory, clock);
                server.SetupGet(value => value.IsRunning).Returns(false);
                Logs = new RecordingLogger();
                var loggerFactory = new Mock<ILoggerFactory>();
                loggerFactory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logs);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(loggerFactory.Object);
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                server.SetupGet(value => value.DiagnosticsNodeManager)
                    .Returns(Mock.Of<IDiagnosticsNodeManager>());
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(Mock.Of<ICoreNodeManager>());
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                Store = new Mock<ISubscriptionStore>();
                setupStore(Store);
                server.SetupGet(value => value.SubscriptionStore).Returns(Store.Object);
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
                m_master = new MasterNodeManager(server.Object, configuration, null, [], null);
                server.SetupGet(value => value.NodeManager).Returns(m_master);
                Manager = new FailingSubscriptionManager(
                    server.Object,
                    configuration,
                    clock,
                    failingSubscriptionId);
            }

            public Mock<ISubscriptionStore> Store { get; }

            public FailingSubscriptionManager Manager { get; }

            public RecordingLogger Logs { get; }

            public void Dispose()
            {
                Manager.Dispose();
                m_master.Dispose();
                m_queueFactory.Dispose();
            }

            private readonly MasterNodeManager m_master;
            private readonly MonitoredItemQueueFactory m_queueFactory;
        }

        private sealed class FailingSubscriptionManager : SubscriptionManager
        {
            public FailingSubscriptionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider,
                uint failingSubscriptionId)
                : base(server, configuration, timeProvider)
            {
                m_failingSubscriptionId = failingSubscriptionId;
            }

            protected override ValueTask<ISubscription> RestoreSubscriptionAsync(
                IStoredSubscription storedSubscription,
                CancellationToken cancellationToken = default)
            {
                if (storedSubscription.Id == m_failingSubscriptionId)
                {
                    throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
                }

                return base.RestoreSubscriptionAsync(storedSubscription, cancellationToken);
            }

            private readonly uint m_failingSubscriptionId;
        }

        private sealed class RecordingLogger : ILogger
        {
            public IReadOnlyList<(LogLevel Level, string Message)> Entries
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_entries];
                    }
                }
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (m_lock)
                {
                    m_entries.Add((logLevel, formatter(state, exception)));
                }
            }

            private readonly Lock m_lock = new();
            private readonly List<(LogLevel Level, string Message)> m_entries = [];
        }
    }
}
