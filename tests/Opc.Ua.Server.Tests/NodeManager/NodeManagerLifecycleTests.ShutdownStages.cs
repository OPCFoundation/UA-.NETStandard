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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// A failed retained event is retried before later events to preserve emission order.
        /// </summary>
        [Test]
        public async Task RetainedEventDeliveryFailureRetriesBeforeLaterEventsAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RetainedEventFailure");
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            var failure = new InvalidOperationException("The retained delivery failed.");
            var delivered = new System.Collections.Generic.List<int>();
            int failedDeliveryAttempts = 0;
            generation.ReadinessPending = true;
            Assert.That(generation.TryDeferEvent(1, () =>
            {
                if (Interlocked.Increment(ref failedDeliveryAttempts) == 1)
                {
                    return Task.FromException(failure);
                }
                delivered.Add(1);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            Assert.That(generation.TryDeferEvent(2, () =>
            {
                delivered.Add(2);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            generation.ReadinessPending = false;

            AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                () => generation.FlushDeferredEventsAsync()).ConfigureAwait(false);

            Assert.That(observed!.InnerExceptions, Has.Exactly(1).SameAs(failure));
            Assert.That(delivered, Is.Empty);
            Assert.That(generation.TryDeferEvent(3, () =>
            {
                delivered.Add(3);
                return Task.CompletedTask;
            }, 1, out _), Is.True);
            await generation.FlushDeferredEventsAsync().ConfigureAwait(false);
            Assert.That(failedDeliveryAttempts, Is.EqualTo(2));
            Assert.That(delivered, Has.Count.EqualTo(3));
            Assert.That(delivered[0], Is.EqualTo(1));
            Assert.That(delivered[1], Is.EqualTo(2));
            Assert.That(delivered[2], Is.EqualTo(3));
        }

        [Test]
        public async Task DeferredEventCapacityCountsEventsAndRetainsAllItemDeliveriesAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DeferredEventCapacity");
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            generation.ReadinessPending = true;

            for (int eventId = 1; eventId <= NodeManagerGeneration.MaxDeferredEvents; eventId++)
            {
                Assert.That(generation.TryDeferEvent(eventId, () => Task.CompletedTask, 2, out _), Is.True);
                Assert.That(generation.TryDeferEvent(eventId, () => Task.CompletedTask, 3, out _), Is.True);
            }

            Assert.That(
                generation.TryDeferEvent(
                    NodeManagerGeneration.MaxDeferredEvents + 1,
                    () => Task.CompletedTask,
                    4,
                    out bool overflowed),
                Is.True);
            Assert.That(overflowed, Is.True);
            Assert.That(generation.DroppedDeferredEvents, Is.EqualTo(1));
            Assert.That(generation.DroppedDeferredDeliveries, Is.EqualTo(5));

            generation.ReadinessPending = false;
            await generation.FlushDeferredEventsAsync().ConfigureAwait(false);
            Assert.That(generation.IsDeferringEvents, Is.False);
        }

        [Test]
        public async Task DeferredEventFlushWaitsForAdmittedReadinessEmissionsAsync()
        {
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DeferredEmissionDrain");
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            generation.ReadinessPending = true;
            Assert.That(generation.CaptureSourceEmission(1, out bool readinessPending), Is.True);
            Assert.That(readinessPending, Is.True);
            Assert.That(generation.TryDeferEvent(1, () => Task.CompletedTask, 1, out _), Is.True);

            InvalidOperationException? notReady = await Assert.ThrowsAsync<InvalidOperationException>(
                () => generation.FlushDeferredEventsAsync()).ConfigureAwait(false);
            Assert.That(notReady!.Message, Does.Contain("readiness succeeds"));
            Assert.That(generation.IsDeferringEvents, Is.True);

            generation.ReadinessPending = false;

            Task flush = generation.FlushDeferredEventsAsync();

            Assert.That(flush.IsCompleted, Is.False);
            generation.ReleaseSourceEmission(1);
            await flush.ConfigureAwait(false);
            Assert.That(generation.IsDeferringEvents, Is.False);
        }

        /// <summary>
        /// Discovery-registration failures do not skip independent application cleanup or replay it on retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrationFailureDoesNotSkipIndependentAddressSpaceShutdownAsync(bool failCancellation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = new CancellationTokenSource();
            var failure = new InvalidOperationException("The discovery registration failed during shutdown.");
            using CancellationTokenRegistration callback = failCancellation
                ? cancellation.Token.Register(() => throw failure)
                : default;
            FieldInfo cancellationField = GetShutdownTestField(typeof(StandardServer), "m_registrationCts");
            FieldInfo registrationField = GetShutdownTestField(typeof(StandardServer), "m_registrationTask");
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RegistrationFailureCleanup");
            Mock<IDisposable> disposable = manager.As<IDisposable>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            await m_server.NodeManagerLifecycle.AddAsync(factory.Object, timeout.Token).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            cancellationField.SetValue(m_server, cancellation);
            registrationField.SetValue(m_server, failCancellation ? Task.CompletedTask : Task.FromException(failure));
            try
            {
                Exception? observed = await Assert.CatchAsync<Exception>(
                    () => m_server.ShutdownInternalsAsync(timeout.Token).AsTask()).ConfigureAwait(false);

                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.ToString(), Does.Contain(failure.Message));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                Assert.That(m_server.CurrentInstance, Is.SameAs(server));
                Assert.That(registrationField.GetValue(m_server), Is.Null,
                    "A completed failed registration task must not be replayed by the next stop attempt.");

                await m_server.ShutdownInternalsAsync(timeout.Token).ConfigureAwait(false);

                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
                AssertServerInternalsDisposed();
            }
            finally
            {
                registrationField.SetValue(m_server, Task.CompletedTask);
                cancellationField.SetValue(m_server, null);
                await FinishShutdownTestAsync().ConfigureAwait(false);
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
            FieldInfo cancellationField = GetShutdownTestField(typeof(SessionManager), "m_workerCts");
            var cancellation = (CancellationTokenSource?)cancellationField.GetValue(sessions)
                ?? throw new AssertionException("The running session-monitor cancellation owner was not found.");
            var failure = new InvalidOperationException("The session-monitor cancellation callback failed.");
            using CancellationTokenRegistration callback = cancellation.Token.Register(() => throw failure);
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
        /// Finds the existing private worker state used to inject a real shutdown-stage failure.
        /// </summary>
        /// <param name="type">The component that owns the worker state.</param>
        /// <param name="name">The worker field name.</param>
        /// <returns>The required field.</returns>
        private static FieldInfo GetShutdownTestField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertionException($"The shutdown worker field {name} was not found.");
        }
    }
}
