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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Discovery-registration failures do not skip independent application cleanup or replay it on retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrationFailureDoesNotSkipIndependentAddressSpaceShutdownAsync(bool failCancellation)
        {
            await FinishShutdownTestAsync().ConfigureAwait(false);
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var failure = new InvalidOperationException("The discovery registration failed during shutdown.");
            CancellationToken registrationToken = default;
            int workerCreations = 0;
            var fixture = new ServerFixture<ShutdownStageTestServer>(telemetry =>
                new ShutdownStageTestServer(telemetry, timeProvider, cancellationToken =>
                {
                    registrationToken = cancellationToken;
                    workerCreations++;
                    return failCancellation ? Task.CompletedTask : Task.FromException(failure);
                }))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            CancellationTokenRegistration callback = default;
            try
            {
                await fixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
                fixture.Config.ServerConfiguration!.MaxRegistrationInterval = 60_000;
                ShutdownStageTestServer testServer = await fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
                await testServer.CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    clientApplicationUri: kClientApplicationUri).ConfigureAwait(false);

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RegistrationFailureCleanup");
                Mock<IDisposable> disposable = manager.As<IDisposable>();
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(manager.Object);
                await testServer.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
                IServerInternal server = testServer.CurrentInstance;
                timeProvider.Advance(TimeSpan.FromSeconds(1));
                Assert.That(workerCreations, Is.EqualTo(1));
                Assert.That(registrationToken.CanBeCanceled, Is.True);
                callback = failCancellation
                    ? registrationToken.Register(() => throw failure)
                    : default;

                Exception? observed = await Assert.CatchAsync<Exception>(
                    () => testServer.ShutdownInternalsAsync(timeout.Token).AsTask()).ConfigureAwait(false);

                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.ToString(), Does.Contain(failure.Message));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                Assert.That(testServer.CurrentInstance, Is.SameAs(server));

                await testServer.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(workerCreations, Is.EqualTo(1));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                ServiceResultException? halted = Assert.Throws<ServiceResultException>(
                    () => _ = testServer.CurrentInstance);
                Assert.That(halted!.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            }
            finally
            {
#if NET
                await callback.DisposeAsync().ConfigureAwait(false);
#else
                callback.Dispose();
#endif
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed session-worker drain still releases application managers exactly once,
        /// while system managers stay live until a successful retry.
        /// </summary>
        [Test]
        public async Task SessionShutdownFailureRetainsSystemManagersUntilConsumerDrainSucceedsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            var sessions = (SessionManager)server.SessionManager;
            var failure = new InvalidOperationException("The session-monitor cancellation callback failed.");
            using CancellationTokenRegistration callback =
                sessions.SessionMonitorCancellationToken.Register(() => throw failure);
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:SessionDrainDependencies");
            Mock<IDisposable> disposable = manager.As<IDisposable>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
            NodeManagerGeneration application = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            NodeManagerGeneration core = NodeManagerGenerationRegistry.GetOrCreate(
                server.NodeManager.CoreNodeManager!);
            NodeManagerGeneration configuration = NodeManagerGenerationRegistry.GetOrCreate(
                server.NodeManager.ConfigurationNodeManager!);
            try
            {
                Exception? observed = await Assert.CatchAsync<Exception>(
                    () => m_server.ShutdownInternalsAsync(timeout.Token).AsTask()).ConfigureAwait(false);

                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.ToString(), Does.Contain(failure.Message));
                Assert.That(m_server.CurrentInstance, Is.SameAs(server));
                Assert.That(application.Gate.State, Is.Not.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(core.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configuration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);

                callback.Dispose();
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                AssertServerInternalsDisposed();
            }
            finally
            {
                callback.Dispose();
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Shutdown retries repeat failed producer preparation without replaying successful participants.
        /// </summary>
        [Test]
        public async Task ShutdownRetryOnlyRepeatsFailedManagerPreparationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Mock<IAsyncNodeManager> successful = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:SuccessfulShutdownPreparation");
            Mock<IAsyncNodeManager> retrying = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RetryShutdownPreparation");
            successful.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync())
                .Returns(default(ValueTask));
            int failedAttempts = 0;
            var failure = new InvalidOperationException("The first producer-stop attempt failed.");
            retrying.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
                Interlocked.Increment(ref failedAttempts) == 1
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            foreach (Mock<IAsyncNodeManager> manager in new[] { successful, retrying })
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(manager.Object);
                await m_server.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
            }
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            try
            {
                AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                    () => lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask()).ConfigureAwait(false);
                Assert.That(observed!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(failure));
                await lifecycle.BeginShutdownAsync(server, timeout.Token).ConfigureAwait(false);

                successful.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                retrying.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Exactly(2));

                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                successful.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                retrying.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Exactly(2));
                successful.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                retrying.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                AssertServerInternalsDisposed();
            }
            finally
            {
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A producer preparation failure does not block retirement of independent prepared managers.
        /// </summary>
        [Test]
        public async Task ShutdownRetiresPreparedManagersWhenPeerPreparationFailsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Mock<IAsyncNodeManager> prepared = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:PreparedPeerShutdown");
            Mock<IDisposable> preparedDisposable = prepared.As<IDisposable>();
            prepared.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync())
                .Returns(default(ValueTask));
            Mock<IAsyncNodeManager> retrying = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:FailedPeerShutdown");
            Mock<IDisposable> retryingDisposable = retrying.As<IDisposable>();
            var failure = new InvalidOperationException("The peer producer-stop attempt failed.");
            int failPreparation = 1;
            retrying.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
                Volatile.Read(ref failPreparation) != 0
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            foreach (Mock<IAsyncNodeManager> manager in new[] { prepared, retrying })
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(manager.Object);
                await m_server.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
            }

            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            NodeManagerGeneration preparedGeneration = NodeManagerGenerationRegistry.GetOrCreate(prepared.Object);
            NodeManagerGeneration retryingGeneration = NodeManagerGenerationRegistry.GetOrCreate(retrying.Object);
            NodeManagerGeneration coreGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                server.NodeManager.CoreNodeManager!);
            NodeManagerGeneration configurationGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                server.NodeManager.ConfigurationNodeManager!);
            try
            {
                AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                    () => lifecycle.CompleteShutdownAsync(server, retireSystems: true, timeout.Token).AsTask())
                    .ConfigureAwait(false);

                Assert.That(observed!.Flatten().InnerExceptions, Has.Exactly(1).SameAs(failure));
                Assert.That(preparedGeneration.Gate.State, Is.Not.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(retryingGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(coreGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configurationGeneration.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                prepared.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                preparedDisposable.Verify(value => value.Dispose(), Times.Once);
                retrying.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                retryingDisposable.Verify(value => value.Dispose(), Times.Never);

                Volatile.Write(ref failPreparation, 0);
                await lifecycle.CompleteShutdownAsync(server, retireSystems: true, timeout.Token)
                    .ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(retryingGeneration.Gate.State, Is.Not.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(coreGeneration.Gate.State, Is.Not.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(configurationGeneration.Gate.State, Is.Not.EqualTo(NodeManagerGenerationState.Live));
                retrying.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                retryingDisposable.Verify(value => value.Dispose(), Times.Once);
                AssertServerInternalsDisposed();
            }
            finally
            {
                Volatile.Write(ref failPreparation, 0);
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Concurrent shutdown calls join one producer-stop callback and reuse its success during completion.
        /// </summary>
        [Test]
        public async Task ConcurrentShutdownPreparationSharesEachManagerAttemptAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ConcurrentShutdownPreparation");
            Mock<IDisposable> disposable = manager.As<IDisposable>();
            manager.As<INodeManagerShutdown>().Setup(value => value.PrepareForShutdownAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IServerInternal server = m_server.CurrentInstance;
            Task? first = null;
            Task? second = null;
            try
            {
                first = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                second = lifecycle.BeginShutdownAsync(server, timeout.Token).AsTask();
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                manager.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);

                release.TrySetResult(true);
                await Task.WhenAll(first, second).WaitAsync(timeout.Token).ConfigureAwait(false);
                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                manager.As<INodeManagerShutdown>().Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                AssertServerInternalsDisposed();
            }
            finally
            {
                release.TrySetResult(true);
                if (first is not null)
                {
                    await first.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                if (second is not null)
                {
                    await second.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                await FinishShutdownTestAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Runs shutdown assertions with a constructor-injected discovery-registration worker.
        /// </summary>
        // TODO: Remove when CA1812 recognizes construction through ServerFixture<T>.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "ServerFixture<T> creates this server from the test factory.")]
        private sealed class ShutdownStageTestServer : StandardServer
        {
            /// <summary>
            /// Initializes the server with its clock and registration-worker factory.
            /// </summary>
            public ShutdownStageTestServer(
                ITelemetryContext telemetry,
                TimeProvider timeProvider,
                Func<CancellationToken, Task> registrationWorkerFactory)
                : base(telemetry, timeProvider, registrationWorkerFactory)
            {
            }

            /// <summary>
            /// Runs the server's normal internal shutdown stages.
            /// </summary>
            public ValueTask ShutdownInternalsAsync(CancellationToken cancellationToken)
            {
                return OnServerStoppingAsync(cancellationToken);
            }
        }
    }
}
