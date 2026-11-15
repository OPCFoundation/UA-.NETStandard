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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies independent retirement ownership, exact drains, and staged cleanup retry without a lifecycle writer.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed partial class GenerationReclaimerTests
    {
        /// <summary>
        /// Physical cleanup hands the owner recovery gate to the caller and never restores it during lease disposal.
        /// </summary>
        [Test]
        public async Task PhysicalCleanupLeaseTransfersOwnerRecoveryGateToCallerAsync()
        {
            var context = new ReclamationContext();
            NodeManagerGeneration generation = context.CreateRetirement().Generation;
            generation.Gate.Retire();
            using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease cleanup =
                await generation.ResourceOwnership.EnterCleanupAsync(generation).ConfigureAwait(false);
            Assert.That(generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));

            cleanup.Dispose();

            Assert.That(generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
            Assert.That(generation.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? denied), Is.False);
            Assert.That(denied, Is.Null);
            generation.CleanupRecoveryGate.Restore();
            Assert.That(generation.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? recovery), Is.True);
            recovery!.Dispose();
        }

        /// <summary>
        /// An isolated generation cannot clean a manager owned by a different exact registered generation.
        /// </summary>
        [Test]
        public async Task RetirementRejectsMismatchedAssociationBeforePhysicalCallbacksAsync()
        {
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            NodeManagerGeneration registered = context.CreateRetirement().Generation;
            var isolated = new NodeManagerGeneration(
                registered.Slot, registered.Number, registered.NodeManager, NodeManagerGenerationState.Retiring);
            var rejected = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, isolated, [], needsDetachment: false);
            Assert.That(registered.Gate.TryEnter(out GenerationGate.Lease? admission), Is.True);
            try
            {
                reclaimer.Enqueue([rejected]);
                reclaimer.Activate([rejected]);
                InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(observed!.Message, Is.EqualTo("The cleanup association has changed."));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(registered.NodeManager), Is.SameAs(registered));
                Assert.That(registered.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                Assert.That(registered.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(isolated.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(() => NodeManagerGenerationRegistry.CompleteCleanup(isolated),
                    Throws.InvalidOperationException.With.Message.EqualTo("The cleanup association has changed."));
                Assert.That(isolated.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                Assert.That(rejected.RetryScheduled, Is.False);
                Assert.That(reclaimer.Contains(rejected), Is.True);
                Assert.That(rejected.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    registered.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                Mock.Get(registered.NodeManager).As<IAsyncDisposable>().Verify(
                    value => value.DisposeAsync(), Times.Never);
            }
            finally
            {
                admission?.Dispose();
            }
        }

        /// <summary>
        /// A failed continuation in one session does not prevent independent sessions from being cleaned.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetirementAttemptsContinuationCleanupInEverySessionAsync(bool secondFails)
        {
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            GenerationReclaimer.RetiredGeneration retired = context.CreateRetirement();
            var firstFailure = new InvalidOperationException("First continuation cleanup failed.");
            var secondFailure = new InvalidOperationException("Second continuation cleanup failed.");
            var firstPoint = new Mock<IHistoryContinuationPoint>();
            var secondPoint = new Mock<IHistoryContinuationPoint>();
            firstPoint.SetupGet(value => value.Id).Returns(Guid.NewGuid());
            secondPoint.SetupGet(value => value.Id).Returns(Guid.NewGuid());
            firstPoint.Setup(value => value.Dispose()).Throws(firstFailure);
            if (secondFails)
            {
                secondPoint.Setup(value => value.Dispose()).Throws(secondFailure);
            }
            var firstPoints = new SessionContinuationPoints(() => new NodeId(1, 1), 1, 2, null);
            var secondPoints = new SessionContinuationPoints(() => new NodeId(2, 1), 1, 2, null);
            firstPoints.SaveHistory(firstPoint.Object);
            secondPoints.SaveHistory(secondPoint.Object);
            firstPoints.BindHistoryGeneration(new ByteString(firstPoint.Object.Id.ToByteArray()), retired.Generation);
            secondPoints.BindHistoryGeneration(new ByteString(secondPoint.Object.Id.ToByteArray()), retired.Generation);
            var firstSession = new Mock<ISession>();
            var secondSession = new Mock<ISession>();
            firstSession.SetupGet(value => value.ContinuationPoints).Returns(firstPoints);
            secondSession.SetupGet(value => value.ContinuationPoints).Returns(secondPoints);
            Mock.Get(context.Server.Object.SessionManager)
                .Setup(value => value.GetSessions())
                .Returns([firstSession.Object, secondSession.Object]);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                Exception? failure = await Assert.CatchAsync<Exception>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<AggregateException>());
                IReadOnlyCollection<Exception> failures = ((AggregateException)failure!).Flatten().InnerExceptions;
                Assert.That(failures, Has.Exactly(1).SameAs(firstFailure));
                if (secondFails)
                {
                    Assert.That(failures, Has.Exactly(1).SameAs(secondFailure));
                }
                else
                {
                    Assert.That(failures, Has.None.SameAs(secondFailure));
                }
                Assert.That(reclaimer.Contains(retired), Is.True);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                firstPoint.Verify(value => value.Dispose(), Times.Once);
                secondPoint.Verify(value => value.Dispose(), Times.Once);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);

                Mock.Get(context.Server.Object.SessionManager)
                    .Setup(value => value.GetSessions())
                    .Returns([]);
                ArrayOf<NodeManagerCleanupFailure> retained = NodeManagerCleanupRecovery.CaptureFailures(
                    (INodeManagerCleanupParticipant)retired.Generation);
                Assert.That(retained.Count, Is.EqualTo(secondFails ? 2 : 1));
                Assert.That(retained.ToList().All(item => item.ResourceKind == "HistoryContinuation"), Is.True);
                await Assert.ThatAsync(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                firstPoint.Verify(value => value.Dispose(), Times.Once);
                secondPoint.Verify(value => value.Dispose(), Times.Once);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                firstPoints.Clear();
                secondPoints.Clear();
            }
        }

        /// <summary>
        /// Consumer disposal uses the generation drain and retains a failed attempt without repeating it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredConsumerDisposalUsesGenerationDrainAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var completedResource = new Mock<IDisposable>();
            var resource = new Mock<IDisposable>();
            int attempts = 0;
            var failure = new InvalidOperationException("Consumer disposal failed.");
            resource.Setup(value => value.Dispose()).Callback(() =>
            {
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                if (++attempts == 1 && failFirst)
                {
                    throw failure;
                }
            });

            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False,
                "An ordinary live consumer keeps its existing immediate disposal behavior.");
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            retired.Generation.Gate.Retire();
            try
            {
                Assert.That(retired.Generation.TryDeferDisposal(completedResource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                    "Repeated disposal must transfer ownership only once.");
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.InvalidOperationException);
                completedResource.Verify(value => value.Dispose(), Times.Never);
                resource.Verify(value => value.Dispose(), Times.Never);
            }
            finally
            {
                retired.Generation.Gate.Exit(admission);
            }

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            if (failFirst)
            {
                Exception? cleanupFailure = await Assert.CatchAsync<Exception>(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(cleanupFailure, Is.SameAs(failure));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(retired), Is.True);
                var participant = (INodeManagerCleanupParticipant)retired.Generation;
                ArrayOf<NodeManagerCleanupFailure> firstSnapshot =
                    NodeManagerCleanupRecovery.CaptureFailures(participant);
                ArrayOf<NodeManagerCleanupFailure> secondSnapshot =
                    NodeManagerCleanupRecovery.CaptureFailures(participant);
                Assert.That(firstSnapshot.Count, Is.EqualTo(1));
                Assert.That(firstSnapshot[0].Id, Is.Not.EqualTo(Guid.Empty));
                Assert.That(secondSnapshot.Count, Is.EqualTo(1));
                Assert.That(secondSnapshot[0].Id, Is.EqualTo(firstSnapshot[0].Id));
                Assert.That(firstSnapshot[0].ResourceKind, Is.EqualTo("RetiredConsumer"));
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.Exception.SameAs(failure));
                await Assert.ThatAsync(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("The NodeManager retains unresolved cleanup failures."))
                    .ConfigureAwait(false);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                    "The failed consumer remains owned until its cleanup is verified.");
                completedResource.Verify(value => value.Dispose(), Times.Once);
                resource.Verify(value => value.Dispose(), Times.Once);
                Assert.That(reclaimer.Contains(retired), Is.True);
                return;
            }
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, null, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.False);
            completedResource.Verify(value => value.Dispose(), Times.Once);
            resource.Verify(value => value.Dispose(), Times.Once);
            Assert.That(reclaimer.Contains(retired), Is.False);
        }

        /// <summary>
        /// Retired consumers remain owned while a routed view can still reach them, even after the gate drains.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RetiredConsumerWaitsForRoutedView(bool closeBeforeDeferral, bool holdPin)
        {
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var epoch = new AddressSpaceEpoch();
            var resource = new Mock<IDisposable>();
            int pin = -1;
            if (holdPin)
            {
                Assert.That(epoch.TryPin(out pin), Is.True);
            }
            try
            {
                retired.Generation.RegisterViewEpoch(epoch);
                if (closeBeforeDeferral)
                {
                    epoch.Close();
                }
                retired.Generation.Gate.Retire();

                Assert.That(retired.Generation.Gate.WaitForDrainAsync().IsCompleted, Is.True);
                Assert.That(retired.Generation.IsQuiescent, Is.False);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True);
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.InvalidOperationException);
                resource.Verify(value => value.Dispose(), Times.Never);
            }
            finally
            {
                epoch.Close();
                if (holdPin)
                {
                    epoch.Exit(pin);
                }
            }

            retired.Generation.DisposeRetiredResources();
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A monitored node transfers disposal before entering an override and cannot be rebound afterward.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredMonitoredNodeDefersOverriddenDisposalAsync(bool disposeAsync)
        {
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            var replacement = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            var node = new BaseObjectState(null) { NodeId = new NodeId(42, 1) };
            var monitored = new DisposalProbeMonitoredNode(manager.Object, context.Server.Object, node);
            NodeManagerGenerationReference? reference = monitored.Generation;
            Assert.That(reference, Is.Not.Null);
            Assert.That(reference!.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
            Assert.That(bound, Is.SameAs(generation));
            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            generation.Gate.Retire();
            bool admissionHeld = true;
            try
            {
                if (disposeAsync)
                {
                    await monitored.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    monitored.Dispose();
                }
                Assert.That(monitored.DisposeCount, Is.Zero);
                Assert.That(() => monitored.NodeManager = replacement.Object, Throws.InvalidOperationException);
                monitored.NodeManager = manager.Object;
                monitored.Dispose();
                await monitored.DisposeAsync().ConfigureAwait(false);
                Assert.That(monitored.DisposeCount, Is.Zero);

                generation.Gate.Exit(admission);
                admissionHeld = false;
                monitored.OnDispose = monitored.Dispose;
                generation.DisposeRetiredResources();
                Assert.That(monitored.DisposeCount, Is.EqualTo(1));
                monitored.Dispose();
                await monitored.DisposeAsync().ConfigureAwait(false);
                Assert.That(monitored.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                if (admissionHeld)
                {
                    generation.Gate.Exit(admission);
                }
                generation.DisposeRetiredResources();
                monitored.Dispose();
            }
        }

        /// <summary>
        /// A concurrent caller cannot enter an override while the reclaimer is running that override.
        /// </summary>
        [Test]
        public async Task RetiredMonitoredNodeRejectsConcurrentDisposalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);
            var node = new BaseObjectState(null) { NodeId = new NodeId(43, 1) };
            var monitored = new DisposalProbeMonitoredNode(manager.Object, context.Server.Object, node);
            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            generation.Gate.Retire();
            monitored.Dispose();
            generation.Gate.Exit(admission);
            monitored.OnDispose = () =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromMinutes(1)))
                {
                    throw new TimeoutException("The monitored node disposal callback was not released.");
                }
            };
            Task cleanup = Task.Factory.StartNew(
                generation.DisposeRetiredResources,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                monitored.Dispose();
                await monitored.DisposeAsync().ConfigureAwait(false);
                Assert.That(monitored.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                release.Set();
                await cleanup.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                monitored.Dispose();
            }
            Assert.That(monitored.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Only the retirement owner's synchronous callback may dispose a transferred consumer.
        /// </summary>
        [Test]
        public async Task RetiredConsumerRemainsOwnedThroughDisposalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var resource = new Mock<IDisposable>();
            bool ownerDeferred = true;
            resource.Setup(value => value.Dispose()).Callback(() =>
            {
                ownerDeferred = retired.Generation.TryDeferDisposal(resource.Object);
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The retirement disposal callback was not released.");
                }
            });

            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            try
            {
                retired.Generation.Gate.Retire();
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True);
            }
            finally
            {
                retired.Generation.Gate.Exit(admission);
            }
            Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                "A transferred consumer remains owned after its generation drains.");

            Task disposal = Task.Factory.StartNew(
                retired.Generation.DisposeRetiredResources,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(ownerDeferred, Is.False);
                Assert.That(retired.Generation.TryDeferDisposal(resource.Object), Is.True,
                    "An unrelated caller cannot take over an in-progress disposal.");
            }
            finally
            {
                release.Set();
                await disposal.ConfigureAwait(false);
            }
            resource.Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A staged cleanup retry does not repeat the manager's successful disposal callback.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        public async Task RetirementRetriesStagedResourceAfterManagerDisposalWithoutRepeatingManagerDisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();

            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaceUris);
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            using var servingRegistry = new HistorianProviderRegistry(namespaceUris);
            context.Server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            context.Server.SetupGet(value => value.ServerUris).Returns(serverUris);
            context.Server.SetupGet(value => value.TypeTree).Returns(typeTree);
            context.Server.SetupGet(value => value.Factory).Returns(factory);
            context.Server.SetupGet(value => value.MessageContext).Returns(
                new ServiceMessageContext(context.Server.Object.Telemetry, factory)
                {
                    NamespaceUris = namespaceUris,
                    ServerUris = serverUris
                });
            context.Server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(context.Server.Object));
            context.Server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry).Returns(servingRegistry);

            int stagedCleanupAttempts = 0;
            var failure = new InvalidOperationException("Staged cleanup failed once.");
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.RegisterHistorianBuilder(It.IsAny<HistorianBuilder>()));
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()))
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref stagedCleanupAttempts) == 1)
                    {
                        throw failure;
                    }
                });

            var preparation = new ChangePreparer(context.Server.Object);
            await using ChangePreparer.StagingServer stagingServer = preparation.CreateServer();
            stagingServer.Attach(retired.NodeManager);
            Assert.That(ChangePreparer.GetServer(retired.NodeManager), Is.SameAs(stagingServer));
            _ = new HistorianBuilder(stagingServer)
                .UseProvider(Mock.Of<IHistorianProvider>())
                .RegisterAsDefault();

            Mock<IAsyncDisposable> managerDisposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            int managerDisposeCount = 0;
            managerDisposal.Setup(value => value.DisposeAsync()).Returns(() =>
            {
                managerDisposeCount++;
                return default;
            });
            retired.Generation.Gate.Retire();

            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Exception cleanupFailure = await Assert.CatchAsync<Exception>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("The first retirement cleanup attempt did not fail.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cleanupFailure, Is.TypeOf<AggregateException>());
                Assert.That(
                    ((AggregateException)cleanupFailure).Flatten().InnerExceptions,
                    Has.Member(failure));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(retired), Is.True);
                Assert.That(managerDisposeCount, Is.EqualTo(1));
                Assert.That(stagedCleanupAttempts, Is.EqualTo(1));
            }
            managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);

            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, cleanupFailure, timeout.Token)
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reclaimer.Count, Is.Zero);
                Assert.That(managerDisposeCount, Is.EqualTo(1));
                Assert.That(stagedCleanupAttempts, Is.EqualTo(2));
            }
            managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);
            context.Server.As<IHistorianBuilderRegistry>().Verify(
                value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// A failed manager disposal keeps its staged historian dependencies alive.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [Test]
        public async Task FailedManagerDisposalRetainsStagedHistorianDependenciesAsync()
        {
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var namespaceUris = new NamespaceTable();
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaceUris);
            EncodeableFactory factory = (EncodeableFactory)EncodeableFactory.Create();
            using var servingRegistry = new HistorianProviderRegistry(namespaceUris);
            context.Server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            context.Server.SetupGet(value => value.ServerUris).Returns(serverUris);
            context.Server.SetupGet(value => value.TypeTree).Returns(typeTree);
            context.Server.SetupGet(value => value.Factory).Returns(factory);
            context.Server.SetupGet(value => value.MessageContext).Returns(
                new ServiceMessageContext(context.Server.Object.Telemetry, factory)
                {
                    NamespaceUris = namespaceUris,
                    ServerUris = serverUris
                });
            context.Server.SetupGet(value => value.DefaultSystemContext)
                .Returns(() => new ServerSystemContext(context.Server.Object));
            context.Server.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry).Returns(servingRegistry);
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.RegisterHistorianBuilder(It.IsAny<HistorianBuilder>()));
            context.Server.As<IHistorianBuilderRegistry>()
                .Setup(value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()));

            var preparation = new ChangePreparer(context.Server.Object);
            await using ChangePreparer.StagingServer stagingServer = preparation.CreateServer();
            stagingServer.Attach(retired.NodeManager);
            _ = new HistorianBuilder(stagingServer)
                .UseProvider(Mock.Of<IHistorianProvider>())
                .RegisterAsDefault();
            var failure = new InvalidOperationException("Manager disposal failed.");
            Mock<IAsyncDisposable> managerDisposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            managerDisposal.Setup(value => value.DisposeAsync())
                .Returns(new ValueTask(Task.FromException(failure)));
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            Exception cleanupFailure = await Assert.CatchAsync<Exception>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false) ??
                throw new AssertionException("The retirement cleanup attempt did not fail.");

            Assert.That(cleanupFailure, Is.SameAs(failure));
            Assert.That(reclaimer.Contains(retired), Is.True);
            Assert.That(ChangePreparer.GetServer(retired.NodeManager), Is.SameAs(stagingServer));
            managerDisposal.Verify(value => value.DisposeAsync(), Times.Once);
            context.Server.As<IHistorianBuilderRegistry>().Verify(
                value => value.UnregisterHistorianBuilder(It.IsAny<HistorianBuilder>()), Times.Never);
        }

        /// <summary>
        /// A retired generation cannot be destroyed while confirmation evidence awaits its audit.
        /// </summary>
        [Test]
        public async Task RetirementWaitsForAuditedCleanupConfirmationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var handle = new NodeManagerCleanupFailureHandle(
                retired.Generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Assert.That(retired.Generation.TryBeginCleanupRecovery(), Is.True);
            retired.Generation.RememberCleanupConfirmation(handle);

            reclaimer.Schedule();
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            Assert.That(reclaimer.Contains(retired), Is.True);
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.True);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Verify(value => value.DisposeAsync(), Times.Never);

            Assert.That(retired.Generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            retired.Generation.EndCleanupRecovery();
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.False);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Retained participant failures prevent physical cleanup even after producer shutdown completes.
        /// </summary>
        [Test]
        public async Task RetirementChecksRetainedFailuresBeforePhysicalCleanupAsync()
        {
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            Mock<INodeManagerCleanupParticipant> participant = manager.As<INodeManagerCleanupParticipant>();
            var failure = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "TestResource", null, StatusCodes.BadUnexpectedError);
            var consumer = new Mock<IDisposable>();
            NodeManagerCleanupAccess? discovery = null;
            bool prepared = false;
            shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
            {
                prepared = true;
                return default;
            });
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess access) =>
                {
                    if (!access.IsAuthorizedFor(participant.Object))
                    {
                        throw new InvalidOperationException("Cleanup inspection was not properly admitted.");
                    }
                    discovery = access;
                    return prepared
                        ? new ArrayOf<NodeManagerCleanupFailure>([failure])
                        : ArrayOf<NodeManagerCleanupFailure>.Empty;
                });
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            generation.Gate.Retire();
            try
            {
                Assert.That(generation.TryDeferDisposal(consumer.Object), Is.True);
            }
            finally
            {
                generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? reported = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(reported!.Message, Is.EqualTo("The NodeManager retains unresolved cleanup failures."));
            Assert.That(discovery, Is.Not.Null);
            Assert.That(discovery!.IsAuthorizedFor(participant.Object), Is.False);
            Assert.That(generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Assert.That(reclaimer.Contains(retired), Is.True);
            shutdown.Verify(value => value.PrepareForShutdownAsync(), Times.Once);
            participant.Verify(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()), Times.AtLeastOnce);
            consumer.Verify(value => value.Dispose(), Times.Never);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A failure retained before retirement blocks producer shutdown until it is confirmed.
        /// </summary>
        [Test]
        public async Task PreexistingFailureBlocksProducerShutdownUntilConfirmationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            Mock<INodeManagerCleanupParticipant> participant = manager.As<INodeManagerCleanupParticipant>();
            var descriptor = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "TestResource", null, StatusCodes.BadUnexpectedError);
            int retained = 1;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess access) =>
                {
                    if (!access.IsAuthorizedFor(participant.Object))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    return Volatile.Read(ref retained) == 0
                        ? ArrayOf<NodeManagerCleanupFailure>.Empty
                        : new ArrayOf<NodeManagerCleanupFailure>([descriptor]);
                });
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? reported = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(reported!.Message, Is.EqualTo("The NodeManager retains unresolved cleanup failures."));
            Assert.That(retired.RetryScheduled, Is.False);
            shutdown.Verify(value => value.PrepareForShutdownAsync(), Times.Never);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Never);

            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, generation.GetCleanupParticipantId(participant.Object), descriptor.Id);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            try
            {
                Volatile.Write(ref retained, 0);
                generation.RememberCleanupConfirmation(handle);
                Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            }
            finally
            {
                generation.EndCleanupRecovery();
            }
            reclaimer.RetryConfirmed();
            await AssertRetirementOutcomeAsync(retired.Retirement, reported, timeout.Token).ConfigureAwait(false);
            shutdown.Verify(value => value.PrepareForShutdownAsync(), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
        }

        /// <summary>
        /// A newly retained failure stops the next stage without replaying the completed callback.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        [TestCase("Source")]
        [TestCase("Consumer")]
        [TestCase("Notifications")]
        [TestCase("References")]
        [TestCase("Destruction")]
        [TestCase("DestroyedReferences")]
        [TestCase("Disposal")]
        public async Task NewRetainedFailureStopsFollowingRetirementStagesAsync(string stage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            Mock<INodeManagerCleanupParticipant> participant = manager.As<INodeManagerCleanupParticipant>();
            var retainedFailure = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "TestResource", null, StatusCodes.BadUnexpectedError);
            int retained = 0;
            int stageCalls = 0;
            int laterSourceCalls = 0;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess access) =>
                {
                    if (!access.IsAuthorizedFor(participant.Object))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    return Volatile.Read(ref retained) == 0
                        ? ArrayOf<NodeManagerCleanupFailure>.Empty
                        : new ArrayOf<NodeManagerCleanupFailure>([retainedFailure]);
                });
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            Mock<IMasterNodeManager> master = Mock.Get(context.Server.Object.NodeManager);
            retired.PendingReferences.Add(new LocalReference(
                new NodeId(1), ReferenceTypeIds.HasComponent, false, new NodeId(2)));
            var consumer = new Mock<IDisposable>();
            var laterConsumer = new Mock<IDisposable>();

            void RetainFailure()
            {
                Interlocked.Increment(ref stageCalls);
                Volatile.Write(ref retained, 1);
            }

            ValueTask CompleteStage()
            {
                RetainFailure();
                return default;
            }

            switch (stage)
            {
                case "Source":
                    IMonitoredItem item = Mock.Of<IMonitoredItem>();
                    generation.RetainSourceRelease(item, 1, _ => CompleteStage());
                    generation.RetainSourceRelease(item, 2, _ =>
                    {
                        Interlocked.Increment(ref laterSourceCalls);
                        return default;
                    });
                    break;
                case "Consumer":
                    consumer.Setup(value => value.Dispose()).Callback(RetainFailure);
                    break;
                case "Notifications":
                    context.Host.Setup(value => value.FinalizeRetiredGenerationNotificationsAsync(
                        manager.Object, It.IsAny<CancellationToken>())).Returns(CompleteStage);
                    break;
                case "References":
                    master.Setup(value => value.RemoveReferencesAsync(
                        It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>())).Returns(CompleteStage);
                    break;
                case "Destruction":
                    context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                        manager.Object, It.IsAny<CancellationToken>())).Returns(CompleteStage);
                    break;
                case "DestroyedReferences":
                    context.Host.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                        manager.Object, It.IsAny<CancellationToken>())).Returns(CompleteStage);
                    break;
                case "Disposal":
                    disposal.Setup(value => value.DisposeAsync()).Returns(CompleteStage);
                    break;
                default:
                    throw new AssertionException("Unknown cleanup stage.");
            }

            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            generation.Gate.Retire();
            try
            {
                if (stage == "Consumer")
                {
                    Assert.That(generation.TryDeferDisposal(consumer.Object), Is.True);
                    Assert.That(generation.TryDeferDisposal(laterConsumer.Object), Is.True);
                }
            }
            finally
            {
                generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? reported = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(reported!.Message, Is.EqualTo("The NodeManager retains unresolved cleanup failures."));
            Assert.That(stageCalls, Is.EqualTo(1));
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Assert.That(reclaimer.Contains(retired), Is.True);
            Assert.That(laterSourceCalls, Is.Zero);
            laterConsumer.Verify(value => value.Dispose(), Times.Never);
            if (stage is "Source" or "Consumer" or "Notifications" or "References")
            {
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    manager.Object, It.IsAny<CancellationToken>()), Times.Never);
            }
            if (stage == "Disposal")
            {
                Assert.That(retired.Cleanup.ManagerDisposed, Is.True);
                Assert.That(retired.Cleanup.StagingDisposed, Is.False);
            }
            else
            {
                disposal.Verify(value => value.DisposeAsync(), Times.Never);
            }

            // Host verification is covered separately; this isolates the reclaimer's stage checkpoints.
            Volatile.Write(ref retained, 0);
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, reported, timeout.Token).ConfigureAwait(false);

            Assert.That(reclaimer.Contains(retired), Is.False);
            Assert.That(stageCalls, Is.EqualTo(1));
            Assert.That(laterSourceCalls, Is.EqualTo(stage == "Source" ? 1 : 0));
            if (stage == "Consumer")
            {
                laterConsumer.Verify(value => value.Dispose(), Times.Once);
            }
            context.Host.Verify(value => value.FinalizeRetiredGenerationNotificationsAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Once);
            master.Verify(value => value.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>()), Times.Once);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Once);
            context.Host.Verify(value => value.RemoveDestroyedExternalReferencesAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A producer failure retained by a participant is not retried until the failure has been resolved.
        /// </summary>
        [Test]
        public async Task RetainedProducerFailurePreventsAutomaticShutdownRetryAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            using var reclaimer = new GenerationReclaimer(clock);
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            Mock<INodeManagerCleanupParticipant> participant = manager.As<INodeManagerCleanupParticipant>();
            var descriptor = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "TestResource", null, StatusCodes.BadUnexpectedError);
            var originalFailure = new InvalidOperationException("The producer resource did not close.");
            int retained = 0;
            int attempts = 0;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess access) =>
                {
                    if (!access.IsAuthorizedFor(participant.Object))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    return Volatile.Read(ref retained) == 0
                        ? ArrayOf<NodeManagerCleanupFailure>.Empty
                        : new ArrayOf<NodeManagerCleanupFailure>([descriptor]);
                });
            shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    Volatile.Write(ref retained, 1);
                    return new ValueTask(Task.FromException(originalFailure));
                }
                return default;
            });
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? reported = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(reported, Is.SameAs(originalFailure));
            Assert.That(retired.RetryScheduled, Is.False);
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Assert.ThatAsync(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo("The NodeManager retains unresolved cleanup failures."))
                .ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(retired.RetryScheduled, Is.False);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                manager.Object, It.IsAny<CancellationToken>()), Times.Never);
            disposal.Verify(value => value.DisposeAsync(), Times.Never);

            // Host verification is covered separately; this supplies its audited offer to the reclaimer.
            Volatile.Write(ref retained, 0);
            var handle = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, generation.GetCleanupParticipantId(participant.Object), descriptor.Id);
            Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
            try
            {
                generation.RememberCleanupConfirmation(handle);
                Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            }
            finally
            {
                generation.EndCleanupRecovery();
            }
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, originalFailure, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(2));
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A confirmation offered while the failed worker is finishing starts a new retirement attempt.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task ConfirmationBeforeFailedWorkerReleasesClaimResumesRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var logRelease = new ManualResetEventSlim();
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var logEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            Mock<INodeManagerCleanupParticipant> participant = manager.As<INodeManagerCleanupParticipant>();
            var descriptor = new NodeManagerCleanupFailure(
                Guid.NewGuid(), "TestResource", null, StatusCodes.BadUnexpectedError);
            var failure = new InvalidOperationException("The producer resource did not close.");
            int retained = 0;
            int attempts = 0;
            participant.Setup(value => value.GetCleanupFailures(It.IsAny<NodeManagerCleanupAccess>()))
                .Returns((NodeManagerCleanupAccess access) =>
                {
                    if (!access.IsAuthorizedFor(participant.Object))
                    {
                        throw new UnauthorizedAccessException();
                    }
                    return Volatile.Read(ref retained) == 0
                        ? ArrayOf<NodeManagerCleanupFailure>.Empty
                        : new ArrayOf<NodeManagerCleanupFailure>([descriptor]);
                });
            shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    Volatile.Write(ref retained, 1);
                    return new ValueTask(Task.FromException(failure));
                }
                return default;
            });
            context.Logger.Setup(value => value.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(_ =>
                {
                    logEntered.TrySetResult(true);
                    if (!logRelease.Wait(TimeSpan.FromSeconds(20)))
                    {
                        throw new TimeoutException("The test did not release the retirement logger.");
                    }
                }));
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Task<Exception?>? firstAttempt = null;
            try
            {
                reclaimer.Schedule();
                firstAttempt = retired.Worker ??
                    throw new InvalidOperationException("The retirement worker was not started.");
                await logEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(firstAttempt.IsCompleted, Is.False);
                Assert.That(retired.Worker, Is.SameAs(firstAttempt));

                var handle = new NodeManagerCleanupFailureHandle(
                    generation.CleanupRecoveryId, generation.GetCleanupParticipantId(participant.Object), descriptor.Id);
                Assert.That(generation.CleanupRecoveryGate.TryEnter(out GenerationGate.Lease? lease), Is.True);
                Assert.That(lease, Is.Not.Null);
                using (lease)
                {
                    Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
                    try
                    {
                        Volatile.Write(ref retained, 0);
                        generation.RememberCleanupConfirmation(handle);
                        Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
                    }
                    finally
                    {
                        generation.EndCleanupRecovery();
                    }
                }
                reclaimer.RetryConfirmed();
                Assert.That(retired.Worker, Is.SameAs(firstAttempt));
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(retired.RetryScheduled, Is.False);

                logRelease.Set();
                Assert.That(await firstAttempt.WaitAsync(timeout.Token).ConfigureAwait(false), Is.SameAs(failure));
                await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
                disposal.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                logRelease.Set();
                await JoinRetirementWorkerAsync(firstAttempt).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Confirmed retry can stop a producer whose admitted work prevents quiescence.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Test]
        public async Task ConfirmedRetryStopsProducerBeforeWaitingForGenerationDrainAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("The first producer shutdown failed.");
            int attempts = 0;
            int admissionHeld = 1;
            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(async () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    throw failure;
                }
                if (Interlocked.Exchange(ref admissionHeld, 0) != 0)
                {
                    generation.Gate.Exit(admission);
                }
            });
            generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Task<Exception?>? firstAttempt = null;
            Task<Exception?>? retryAttempt = null;
            try
            {
                reclaimer.Schedule();
                firstAttempt = retired.Worker ??
                    throw new InvalidOperationException("The retirement worker was not started.");
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                release.TrySetResult(true);
                Assert.That(await firstAttempt.WaitAsync(timeout.Token).ConfigureAwait(false), Is.SameAs(failure));
                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
                disposal.Verify(value => value.DisposeAsync(), Times.Never);

                var handle = new NodeManagerCleanupFailureHandle(
                    generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
                Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
                generation.RememberCleanupConfirmation(handle);
                Assert.That(generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
                generation.EndCleanupRecovery();
                reclaimer.RetryConfirmed();
                retryAttempt = retired.Worker ??
                    throw new InvalidOperationException("The confirmed retirement was not restarted.");
                Assert.That(await retryAttempt.WaitAsync(timeout.Token).ConfigureAwait(false), Is.Null);
                await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
                Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
                disposal.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                if (Interlocked.Exchange(ref admissionHeld, 0) != 0)
                {
                    generation.Gate.Exit(admission);
                }
                if (firstAttempt is not null)
                {
                    await firstAttempt.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                if (retryAttempt is not null)
                {
                    await retryAttempt.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A worker drains concurrent recovery before using the completed or uncertain confirmation outcome.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfirmationDuringDrainWaitsForAdmissionAndOutcomeAsync(bool confirmedBeforeRelease)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var manager = new Mock<IAsyncNodeManager>();
            Mock<INodeManagerShutdown> shutdown = manager.As<INodeManagerShutdown>();
            Mock<IAsyncDisposable> disposal = manager.As<IAsyncDisposable>();
            NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
            var retired = new GenerationReclaimer.RetiredGeneration(
                context.Server.Object, context.Host.Object, generation, [], needsDetachment: true);
            var prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown.Setup(value => value.PrepareForShutdownAsync()).Returns(() =>
            {
                prepared.TrySetResult(true);
                return default;
            });
            var first = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            var second = new NodeManagerCleanupFailureHandle(
                generation.CleanupRecoveryId, first.ParticipantId, Guid.NewGuid());
            Assert.That(generation.Gate.TryEnter(out int admission), Is.True);
            int admissionHeld = 1;
            bool recoveryActive = false;
            bool recoveryGateHeld = false;
            int recoveryAdmission = 0;
            Task<Exception?>? worker = null;
            Task<Exception?>? retry = null;
            try
            {
                generation.Gate.Retire();
                reclaimer.Enqueue([retired]);
                reclaimer.Activate([retired]);
                Assert.That(generation.CleanupRecoveryGate.TryEnter(out recoveryAdmission), Is.True);
                recoveryGateHeld = true;
                Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
                recoveryActive = true;
                generation.RememberCleanupConfirmation(first);
                Assert.That(generation.OfferCleanupConfirmation(first, otherFailures: false), Is.True);
                generation.EndCleanupRecovery();
                recoveryActive = false;
                generation.CleanupRecoveryGate.Exit(recoveryAdmission);
                recoveryGateHeld = false;
                reclaimer.Schedule();
                worker = retired.Worker ??
                    throw new InvalidOperationException("The retirement worker was not started.");
                await prepared.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                while (generation.CleanupRecoveryGate.State != NodeManagerGenerationState.Live ||
                    !generation.CanAttemptCleanup)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                Assert.That(generation.CleanupRecoveryGate.TryEnter(out recoveryAdmission), Is.True);
                recoveryGateHeld = true;
                Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
                recoveryActive = true;
                generation.RememberCleanupConfirmation(second);
                Assert.That(generation.CanAttemptCleanup, Is.False);
                if (Interlocked.Exchange(ref admissionHeld, 0) != 0)
                {
                    generation.Gate.Exit(admission);
                }
                while (generation.CleanupRecoveryGate.State != NodeManagerGenerationState.Retiring)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
                Assert.That(generation.CleanupRecoveryGate.ActiveCount, Is.EqualTo(1));
                Assert.That(worker.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    manager.Object, It.IsAny<CancellationToken>()), Times.Never);
                disposal.Verify(value => value.DisposeAsync(), Times.Never);
                if (confirmedBeforeRelease)
                {
                    Assert.That(generation.OfferCleanupConfirmation(second, otherFailures: false), Is.True);
                }
                generation.EndCleanupRecovery();
                recoveryActive = false;
                generation.CleanupRecoveryGate.Exit(recoveryAdmission);
                recoveryGateHeld = false;
                Exception? outcome = await worker.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (confirmedBeforeRelease)
                {
                    Assert.That(outcome, Is.Null);
                }
                else
                {
                    Assert.That(outcome, Is.TypeOf<InvalidOperationException>());
                    Assert.That(outcome!.Message, Is.EqualTo("Cleanup confirmation is not ready for retirement."));
                    Assert.That(generation.HasCleanupConfirmation(first), Is.True);
                    Assert.That(generation.HasCleanupConfirmation(second), Is.True);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                    context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                        manager.Object, It.IsAny<CancellationToken>()), Times.Never);
                    disposal.Verify(value => value.DisposeAsync(), Times.Never);

                    Assert.That(generation.CleanupRecoveryGate.TryEnter(out recoveryAdmission), Is.True);
                    recoveryGateHeld = true;
                    Assert.That(generation.TryBeginCleanupRecovery(), Is.True);
                    recoveryActive = true;
                    Assert.That(generation.OfferCleanupConfirmation(second, otherFailures: false), Is.True);
                    generation.EndCleanupRecovery();
                    recoveryActive = false;
                    generation.CleanupRecoveryGate.Exit(recoveryAdmission);
                    recoveryGateHeld = false;
                    reclaimer.RetryConfirmed();
                    retry = retired.Worker ??
                        throw new InvalidOperationException("The confirmed retirement was not restarted.");
                    Assert.That(await retry.WaitAsync(timeout.Token).ConfigureAwait(false), Is.Null);
                }
                await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(generation.HasPendingCleanupConfirmation, Is.False);
                shutdown.Verify(value => value.PrepareForShutdownAsync(), Times.Once);
                disposal.Verify(value => value.DisposeAsync(), Times.Once);
            }
            finally
            {
                if (recoveryActive)
                {
                    generation.EndCleanupRecovery();
                }
                if (recoveryGateHeld)
                {
                    generation.CleanupRecoveryGate.Exit(recoveryAdmission);
                }
                if (Interlocked.Exchange(ref admissionHeld, 0) != 0)
                {
                    generation.Gate.Exit(admission);
                }
                if (worker is not null)
                {
                    await worker.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                if (retry is not null)
                {
                    await retry.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A fatal retirement callback releases its claim and preserves the original retirement failure.
        /// </summary>
        [Test]
        // This verifies fatal retirement without causing a real allocation failure.
        // TODO: Use a shared fault-injection facility if the test infrastructure adds one.
        [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types",
            Justification = "The original fatal failure must survive retirement claim release.")]
        public async Task FatalRetirementFailureRetainsConfirmationAndOriginalOutcomeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var failure = new OutOfMemoryException("The retirement callback failed.");
            var handle = new NodeManagerCleanupFailureHandle(
                retired.Generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(Task.FromException(failure)));
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Assert.That(retired.Generation.TryBeginCleanupRecovery(), Is.True);
            retired.Generation.RememberCleanupConfirmation(handle);
            Assert.That(retired.Generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            retired.Generation.EndCleanupRecovery();

            OutOfMemoryException? reported = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(reported, Is.SameAs(failure));
            Assert.That(retired.Generation.CleanupRecoveryGate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.True);
            Assert.That(retired.Generation.CanAttemptCleanup, Is.False);
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Verify(value => value.DisposeAsync(), Times.Never);

            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            Assert.That(retired.Generation.TryBeginCleanupRecovery(), Is.True);
            Assert.That(retired.Generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            retired.Generation.EndCleanupRecovery();
            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            OutOfMemoryException? retirementFailure = await Assert.ThrowsAsync<OutOfMemoryException>(
                async () => await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(retirementFailure, Is.SameAs(failure));
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.False);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>()
                .Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Shutdown reports an in-flight confirmation without making it a physical retirement failure.
        /// </summary>
        [Test]
        public async Task ShutdownBlockedByConfirmationDoesNotFaultSuccessfulRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer(new FakeTimeProvider());
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var handle = new NodeManagerCleanupFailureHandle(
                retired.Generation.CleanupRecoveryId, Guid.NewGuid(), Guid.NewGuid());
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            Assert.That(retired.Generation.TryBeginCleanupRecovery(), Is.True);
            retired.Generation.RememberCleanupConfirmation(handle);

            AggregateException? blocked = await Assert.ThrowsAsync<AggregateException>(
                async () => await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Exception blockedFailure = blocked!.InnerExceptions.Single();
            Assert.That(blockedFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(blockedFailure.Message, Is.EqualTo("Cleanup confirmation is not ready for retirement."));
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.True);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);

            Assert.That(retired.Generation.OfferCleanupConfirmation(handle, otherFailures: false), Is.True);
            retired.Generation.EndCleanupRecovery();
            await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(retired.Generation.HasPendingCleanupConfirmation, Is.False);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An unrelated generation finishes while another generation is blocked in its drain or destruction callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndependentRetirementDoesNotWaitForUnrelatedDrainOrDestructionAsync(bool blockDrain)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    if (!blockDrain)
                    {
                        await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            reclaimer.Activate([first, second]);
            try
            {
                reclaimer.Schedule();
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(first), Is.True);
                Assert.That(reclaimer.Contains(second), Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    second.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
                if (blockDrain)
                {
                    context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                        first.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                }
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Both the captured view epoch and the exact generation's admitted callback must drain before destruction.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetirementWaitsForExactEpochAndGenerationGateAsync(bool releaseEpochFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int pin), Is.True);
            retired.Generation.RegisterViewEpoch(epoch);
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            epoch.Close();
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            if (releaseEpochFirst)
            {
                epoch.Exit(pin);
            }
            else
            {
                retired.Generation.Gate.Exit(admission);
            }
            try
            {
                Assert.That(retired.Generation.IsQuiescent, Is.False);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
            }
            finally
            {
                if (releaseEpochFirst)
                {
                    retired.Generation.Gate.Exit(admission);
                }
                else
                {
                    epoch.Exit(pin);
                }
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(retired.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// A delayed reclamation logs its exact blockers once without releasing or destroying the generation.
        /// </summary>
        [Test]
        public async Task QuiescenceWarningReportsExactGenerationAndPinsWithoutForcingCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            var timerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var warning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timeProvider = new Mock<TimeProvider>();
            timeProvider.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            timeProvider.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            timeProvider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ITimer timer = clock.CreateTimer(callback, state, dueTime, period);
                    timerReady.TrySetResult(true);
                    return timer;
                });
            using var reclaimer = new GenerationReclaimer(timeProvider.Object);
            var context = new ReclamationContext();
            GenerationReclaimer.RetiredGeneration retired = context.CreateRetirement();
            context.Logger.Setup(value => value.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                    warning.TrySetResult(invocation.Arguments[2]?.ToString()
                        ?? throw new InvalidOperationException("The warning did not contain log state."))));
            var epoch = new AddressSpaceEpoch();
            Assert.That(epoch.TryPin(out int pin), Is.True);
            retired.Generation.RegisterViewEpoch(epoch);
            Assert.That(retired.Generation.Gate.TryEnter(out int admission), Is.True);
            epoch.Close();
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                reclaimer.Schedule();
                await timerReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                clock.Advance(LifecycleWaitDiagnostics.WarningThreshold);
                string message = await warning.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(message, Does.Contain("retired generation quiescence"));
                Assert.That(message, Does.Contain(LifecycleWaitDiagnostics.WarningThreshold.ToString()));
                Assert.That(message, Does.Contain(retired.Generation.GetQuiescenceWaitContext()));
                Assert.That(message, Does.Contain($"pinnedEpochs=[{epoch.Id}=1]"));
                Assert.That(retired.Generation.Gate.ActiveCount, Is.EqualTo(1));
                Assert.That(epoch.ActiveCount, Is.EqualTo(1));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Never);
                clock.Advance(TimeSpan.FromMinutes(1));
            }
            finally
            {
                epoch.Exit(pin);
                retired.Generation.Gate.Exit(admission);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            context.Logger.Verify(value => value.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(retired.Retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        /// <summary>
        /// A failed post-destruction stage is logged while retry skips every successful stage.
        /// </summary>
        [TestCase("Notifications")]
        [TestCase("References")]
        [TestCase("Destruction")]
        [TestCase("DestroyedReferences")]
        [TestCase("Disposal")]
        public async Task CleanupFailureIsLoggedAndRetriedWithoutReplayingSuccessfulStagesAsync(string stage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            int calls = 0;
            var failure = new InvalidOperationException(stage + " failed.");
            ValueTask FailOnce()
            {
                return ++calls == 1 ? new ValueTask(Task.FromException(failure)) : default;
            }
            Mock<IMasterNodeManager> master = Mock.Get(retired.Server.NodeManager);
            retired.PendingReferences.Add(new LocalReference(
                new NodeId(1), ReferenceTypeIds.HasComponent, false, new NodeId(2)));
            switch (stage)
            {
                case "Notifications":
                    context.Host.Setup(value => value.FinalizeRetiredGenerationNotificationsAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "References":
                    master.Setup(value => value.RemoveReferencesAsync(
                        It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Destruction":
                    context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "DestroyedReferences":
                    context.Host.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                        retired.NodeManager, It.IsAny<CancellationToken>())).Returns(FailOnce);
                    break;
                case "Disposal":
                    disposal.Setup(value => value.DisposeAsync()).Returns(FailOnce);
                    break;
                default:
                    Assert.Fail("Unknown cleanup stage.");
                    break;
            }
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            await Assert.ThatAsync(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(failure.Message)).ConfigureAwait(false);
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            Assert.That(reclaimer.Count, Is.EqualTo(1));
            disposal.Verify(value => value.DisposeAsync(), Times.Exactly(stage == "Disposal" ? 1 : 0));
            context.Logger.Verify(value => value.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

            if (stage == "Disposal")
            {
                var participant = (INodeManagerCleanupParticipant)retired.Generation;
                ArrayOf<NodeManagerCleanupFailure> retained = NodeManagerCleanupRecovery.CaptureFailures(participant);
                Assert.That(retained.Count, Is.EqualTo(1));
                Assert.That(retained[0].ResourceKind, Is.EqualTo("NodeManager"));
                Assert.That(retained[0].Id, Is.Not.EqualTo(Guid.Empty));
                Assert.That(retired.Generation.DisposeRetiredResources, Throws.Exception.SameAs(failure));
                await Assert.ThatAsync(
                    async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.EqualTo("The NodeManager retains unresolved cleanup failures."))
                    .ConfigureAwait(false);
                Assert.That(NodeManagerCleanupRecovery.CaptureFailures(participant)[0].Id, Is.EqualTo(retained[0].Id));
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Count, Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(1));
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
                disposal.Verify(value => value.DisposeAsync(), Times.Once);
                return;
            }

            await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
            await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);
            Assert.That(reclaimer.Count, Is.Zero);
            Assert.That(retired.PendingReferences, Is.Empty);
            Assert.That(calls, Is.EqualTo(2));
            context.Host.Verify(value => value.FinalizeRetiredGenerationNotificationsAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Notifications" ? 2 : 1));
            master.Verify(value => value.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(), It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "References" ? 2 : 1));
            context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "Destruction" ? 2 : 1));
            context.Host.Verify(value => value.RemoveDestroyedExternalReferencesAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()),
                Times.Exactly(stage == "DestroyedReferences" ? 2 : 1));
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A transient cleanup failure is retried after backoff; retirement completes after the retry
        /// and surfaces the first failure.
        /// </summary>
        [Test]
        public async Task CleanupFailureSchedulesDelayedRetryAndCompletesRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var clock = new FakeTimeProvider();
            using var reclaimer = new GenerationReclaimer(clock);
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            int attempts = 0;
            var failure = new InvalidOperationException("Transient destruction failure.");
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>()))
                .Returns(() => ++attempts == 1
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);

            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await reclaimer.ReclaimReadyAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(observed, Is.SameAs(failure));
            Assert.That(retired.Retirement.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            await AssertRetirementOutcomeAsync(retired.Retirement, failure, timeout.Token).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Shutdown independently admits pending generations even if normal post-publication activation never ran.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ShutdownStartsPendingGenerationsIndependentlyAsync(bool blockDrain, bool alreadyAdmitted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var first = context.CreateRetirement();
            var second = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.That(first.Generation.Gate.TryEnter(out int admission), Is.True);
            first.Generation.Gate.Retire();
            second.Generation.Gate.Retire();
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                first.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            if (!blockDrain)
            {
                first.Generation.Gate.Exit(admission);
            }
            reclaimer.Enqueue([first, second]);
            if (alreadyAdmitted)
            {
                reclaimer.Activate([first]);
                reclaimer.Schedule();
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Task shutdown = reclaimer.CompleteShutdownAsync(timeout.Token).AsTask();
            try
            {
                await second.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!blockDrain)
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(first.Retirement.IsCompleted, Is.False);
                Assert.That(reclaimer.Contains(second), Is.False);
            }
            finally
            {
                if (blockDrain)
                {
                    first.Generation.Gate.Exit(admission);
                }
                release.TrySetResult(true);
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
        }

        /// <summary>
        /// Concurrent drain observers cannot replay an already admitted destruction callback.
        /// </summary>
        [Test]
        public async Task ConcurrentSchedulingClaimsGenerationOnceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Host.Setup(value => value.DestroyAddressSpaceAsync(
                retired.NodeManager, It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(reclaimer.Schedule))).WaitAsync(timeout.Token).ConfigureAwait(false);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                context.Host.Verify(value => value.DestroyAddressSpaceAsync(
                    retired.NodeManager, It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Repeated publication activation and a worker share one transition task, including a failed-task retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentActivationSharesTransitionFinalizationAndRetriesFailureAsync(bool failFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var item = new Mock<IMonitoredItem>();
            item.SetupGet(value => value.NodeId).Returns(new NodeId(1));
            Mock<IMonitoredItemGenerationBinding> binding = item.As<IMonitoredItemGenerationBinding>();
            int marks = 0;
            binding.Setup(value => value.TryInvalidateSource(0, false)).Returns(() =>
            {
                if (Interlocked.Increment(ref marks) == 1 && failFirst)
                {
                    throw new InvalidOperationException("Transition finalization failed.");
                }
                return true;
            });
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int drains = 0;
            Func<CancellationToken, ValueTask>? drain = async ct =>
            {
                Interlocked.Increment(ref drains);
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            var current = new Mock<INodeManagerMonitoredItemSource>();
            current.Setup(value => value.GetNotificationDrain(item.Object.NodeId)).Returns(drain);
            retired.Transition = new NodeManagerLifecycle.MonitoredItemTransition(
                current.Object, null, [], [item.Object], _ => true);
            Assert.That(
                await retired.Transition.AdoptAsync(timeout.Token).ConfigureAwait(false),
                Is.Empty);
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            reclaimer.Schedule();
            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(drains, Is.EqualTo(1));
            Assert.That(marks, Is.Zero);
            Task<Exception?> worker = retired.Worker!;
            release.TrySetResult(true);
            if (failFirst)
            {
                Exception? failure = await worker.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<AggregateException>());
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                Assert.That(marks, Is.EqualTo(1));
                await Task.WhenAll(Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => reclaimer.Activate([retired]))))
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                await reclaimer.ReclaimReadyAsync().ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => retired.Retirement.WaitAsync(timeout.Token),
                    Throws.Exception).ConfigureAwait(false);
                Assert.That(retired.Retirement.IsFaulted, Is.True);
            }
            else
            {
                await retired.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(marks, Is.EqualTo(failFirst ? 2 : 1));
            Assert.That(drains, Is.EqualTo(1), "Retry must not repeat completed notification fences.");
            Assert.That(reclaimer.Count, Is.Zero);
            Mock.Get(retired.NodeManager).As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Shutdown joins an admitted asynchronous disposal, and a cancelled join leaves cleanup retryable.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownWaitsForAdmittedCleanupAndCanRetryCancelledWaitAsync(bool cancelWait)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var reclaimer = new GenerationReclaimer();
            var context = new ReclamationContext();
            var retired = context.CreateRetirement();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncDisposable> disposal = Mock.Get(retired.NodeManager).As<IAsyncDisposable>();
            disposal.Setup(value => value.DisposeAsync()).Returns(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            });
            retired.Generation.Gate.Retire();
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            using var cancelled = new CancellationTokenSource();
            Task shutdown = reclaimer.CompleteShutdownAsync(
                cancelWait ? cancelled.Token : timeout.Token).AsTask();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(retired.Retirement.IsCompleted, Is.False);
                if (cancelWait)
                {
                    cancelled.Cancel();
                    await Assert.ThatAsync(async () => await shutdown.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(retired.Retirement.IsCompleted, Is.False);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
            }
            if (!cancelWait)
            {
                await shutdown.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            Assert.That(reclaimer.Count, Is.Zero);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Joins a retirement worker if scheduling completed before the test was interrupted.
        /// </summary>
        private static async Task JoinRetirementWorkerAsync(Task<Exception?>? worker)
        {
            if (worker is not null)
            {
                await worker.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Asserts that retirement completed and surfaced the first cleanup failure, if any, even after a
        /// successful retry.
        /// </summary>
        private static async Task AssertRetirementOutcomeAsync(
            Task retirement,
            Exception? firstFailure,
            CancellationToken cancellationToken)
        {
            if (firstFailure is null)
            {
                await retirement.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(retirement.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                return;
            }
            await Assert.ThatAsync(
                () => retirement.WaitAsync(cancellationToken),
                Throws.Exception.SameAs(firstFailure)).ConfigureAwait(false);
            Assert.That(retirement.IsFaulted, Is.True);
        }

        /// <summary>
        /// Observes calls to the overridable monitored-node disposal method.
        /// </summary>
        private sealed class DisposalProbeMonitoredNode(
            IAsyncNodeManager manager,
            IServerInternal server,
            NodeState node) : MonitoredNode2(manager, server, node)
        {
            /// <summary>
            /// Gets the number of entries into the override.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <summary>
            /// Gets or sets the action run before base disposal.
            /// </summary>
            internal Action? OnDispose { get; set; }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    OnDispose?.Invoke();
                }
                base.Dispose(disposing);
            }

            private int m_disposeCount;
        }

        /// <summary>
        /// Creates the authoritative live generation without publishing a serving slot in server-free fixtures.
        /// </summary>
        private static NodeManagerGeneration CreateLiveGeneration(IAsyncNodeManager manager)
        {
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(manager);
            generation.Gate.Activate();
            Assert.That(NodeManagerGenerationRegistry.TryGet(manager, out NodeManagerGeneration? associated), Is.True);
            Assert.That(associated, Is.SameAs(generation));
            Assert.That(generation.Slot.Current, Is.Null);
            return generation;
        }

        /// <summary>
        /// Supplies deterministic server services and a host without sockets or lifecycle writer state.
        /// </summary>
        private sealed class ReclamationContext
        {
            /// <summary>
            /// Creates an empty server with source-generated logging captured for assertions.
            /// </summary>
            internal ReclamationContext()
            {
                _ = m_server.As<IHistorianRegistryProvider>();
                _ = m_server.As<IHistorianBuilderRegistry>();
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                var subscriptions = new Mock<ISubscriptionManager>();
                subscriptions.Setup(value => value.GetSubscriptions()).Returns([]);
                Logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(Logger.Object);
                var telemetry = new Mock<ITelemetryContext>();
                telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
                m_server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                m_server.SetupGet(value => value.SubscriptionManager).Returns(subscriptions.Object);
                m_server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                Host = m_master.As<IDynamicNodeManagerHost>();
                m_server.SetupGet(value => value.NodeManager).Returns(m_master.Object);
                Host.SetupGet(value => value.GenerationReconciler)
                    .Returns(new GenerationReconciler(m_server.Object));
            }

            /// <summary>
            /// Gets the dynamic host callback mock.
            /// </summary>
            internal Mock<IDynamicNodeManagerHost> Host { get; }

            /// <summary>
            /// Gets the server mock used to construct candidate staging metadata.
            /// </summary>
            internal Mock<IServerInternal> Server => m_server;

            /// <summary>
            /// Gets the cleanup diagnostic logger.
            /// </summary>
            internal Mock<ILogger> Logger { get; } = new();

            /// <summary>
            /// Creates a distinct live generation ready to be retired by the test.
            /// </summary>
            /// <returns>The exact generation's retirement record.</returns>
            internal GenerationReclaimer.RetiredGeneration CreateRetirement()
            {
                var manager = new Mock<IAsyncNodeManager>();
                manager.As<IAsyncDisposable>();
                NodeManagerGeneration generation = CreateLiveGeneration(manager.Object);
                return new GenerationReclaimer.RetiredGeneration(
                    m_server.Object, Host.Object, generation, [], needsDetachment: true);
            }

            /// <summary>The owning server mock.</summary>
            private readonly Mock<IServerInternal> m_server = new();

            /// <summary>The host's master-node-manager interface.</summary>
            private readonly Mock<IMasterNodeManager> m_master = new();
        }
    }
}
