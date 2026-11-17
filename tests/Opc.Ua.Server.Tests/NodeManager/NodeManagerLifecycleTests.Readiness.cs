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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.TestFramework;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        private static readonly TimeSpan s_readinessTimeout = TimeSpan.FromSeconds(5);

        private const string kReadinessProbeNamespaceUri = kModelNamespaceUri + ":ReadinessProbe";

        /// <summary>
        /// A delayed readiness participant logs its published identity once without completing or destroying it.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessWarningReportsPublishedGenerationWithoutForcingCompletionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var warning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timeProvider = new Mock<TimeProvider>();
            timeProvider.Setup(value => value.GetUtcNow()).Returns(clock.GetUtcNow);
            timeProvider.Setup(value => value.GetTimestamp()).Returns(clock.GetTimestamp);
            timeProvider.SetupGet(value => value.TimestampFrequency).Returns(clock.TimestampFrequency);
            timeProvider.Setup(value => value.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
                {
                    ITimer timer = clock.CreateTimer(callback, state, dueTime, period);
                    if (entered.Task.IsCompleted && dueTime == LifecycleWaitDiagnostics.WarningThreshold)
                    {
                        timerReady.TrySetResult(true);
                    }
                    return timer;
                });
            var logger = new Mock<ILogger>();
            logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            logger.Setup(value => value.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(invocation =>
                    warning.TrySetResult(invocation.Arguments[2]?.ToString()
                        ?? throw new InvalidOperationException("The warning did not contain log state."))));
            var loggerProvider = new Mock<ILoggerProvider>();
            loggerProvider.Setup(value => value.CreateLogger(It.IsAny<string>()))
                .Returns(NullLogger.Instance);
            loggerProvider.Setup(value => value.CreateLogger(typeof(NodeManagerLifecycle).FullName!))
                .Returns(logger.Object);
            var fixture = new ServerFixture<StandardServer>(
                telemetry =>
                {
                    telemetry.LoggerFactory.AddProvider(loggerProvider.Object);
                    return new StandardServer(telemetry, timeProvider.Object);
                })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };
            StandardServer? server = null;
            Task<LifecycleResult>? add = null;
            try
            {
                server = await fixture.StartAsync(Path.Combine(m_pkiRoot, "readiness-warning"))
                    .ConfigureAwait(false);
                INodeManagerLifecycle lifecycle = server.NodeManagerLifecycle;
                ReadinessLifecycleNodeManager? participant = null;
                var factory = new CountingNodeManagerFactory(
                    [kModelNamespaceUri],
                    (internalServer, configuration, _) =>
                    {
                        participant = new ReadinessLifecycleNodeManager(
                            internalServer, configuration, m_logger, kGeneration1Value)
                        {
                            ReadinessCallback = async ct =>
                            {
                                entered.TrySetResult(true);
                                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                            }
                        };
                        return new ValueTask<IAsyncNodeManager>(participant);
                    });
                add = CommitChangeAsync(lifecycle, LifecycleChange.Add(factory), timeout.Token);
                await timerReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                NodeManagerRegistration registration = RequireLifecycleValue(lifecycle.Registrations.Find(
                    candidate => candidate.NamespaceUris.Contains(kModelNamespaceUri)));
                long publishedRevision = lifecycle.Revision;
                clock.Advance(LifecycleWaitDiagnostics.WarningThreshold);
                string message = await warning.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(message, Does.Contain("NodeManager readiness callback"));
                    Assert.That(message, Does.Contain(LifecycleWaitDiagnostics.WarningThreshold.ToString()));
                    Assert.That(message, Does.Contain(registration.Identity.GetQuiescenceWaitContext()));
                    Assert.That(message, Does.Contain("pendingReadinessCount=1"));
                    Assert.That(registration.NodeManager, Is.SameAs(participant));
                    Assert.That(add.IsCompleted, Is.False);
                    Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                    Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                    Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(participant.DisposeCount, Is.Zero);
                }
                clock.Advance(TimeSpan.FromMinutes(1));
                release.TrySetResult(true);
                Assert.That(
                    RequireRegistration((await add.WaitAsync(timeout.Token).ConfigureAwait(false)).Registrations[0]),
                    Is.SameAs(registration));
                Assert.That(lifecycle.Revision, Is.EqualTo(publishedRevision));
                Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                logger.Verify(value => value.Log(
                    LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    if (add is not null)
                    {
                        await add.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    server?.Dispose();
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Reconciliation permits a nested writer while its result retains the revision and slots it published.
        /// </summary>
        [Test]
        public async Task BindingReconciliationAwaitsNestedAddWithoutChangingPublishedResultAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            RequireRegistration(
                (await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateTrackingNodeManagementFactory(
                kGeneration1Value, _ => { })), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint itemId) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services, ObjectIds.Server).ConfigureAwait(false);
            TrackingLifecycleNodeManager? candidate = null;
            TrackingLifecycleNodeManager? child = null;
            NodeManagerRegistration? childRegistration = null;
            long revisionBeforeChild = 0;
            var childFactory = new CountingNodeManagerFactory(
                [kReadinessProbeNamespaceUri],
                CreateTrackingNodeManagementFactory(
                    304, value => child = value, kReadinessProbeNamespaceUri).CreateAsync);
            try
            {
                await using IPreparedChanges prepared =
                    await ((INodeManagerLifecycle)lifecycle).PrepareAsync(
                        [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                            kSecondRegistrationValue,
                            value =>
                            {
                                candidate = value;
                                value.AllEventsCallback = async (item, unsubscribe, token) =>
                                {
                                    if (!unsubscribe && item.Id == itemId)
                                    {
                                        revisionBeforeChild = lifecycle.Revision;
                                        LifecycleResult publicationResult1 = await CommitChangeAsync(
                                            lifecycle, LifecycleChange.Add(childFactory), token).ConfigureAwait(false);
                                        childRegistration = RequireRegistration(publicationResult1.Registrations[0]);
                                    }
                                };
                            },
                            kSecondModelNamespaceUri))], timeout.Token).ConfigureAwait(false);

                LifecycleResult result = await prepared.CommitAsync(static _ => default, timeout.Token)
                    .AsTask().WaitAsync(timeout.Token).ConfigureAwait(false);

                AssertLifecycleValue(candidate);
                AssertLifecycleValue(child);
                AssertLifecycleValue(childRegistration);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Revision, Is.EqualTo(revisionBeforeChild));
                    Assert.That(lifecycle.Revision, Is.GreaterThan(result.Revision));
                    Assert.That(result.Slots.ToList(), Does.Contain(result.Registrations[0].Slot));
                    Assert.That(result.Slots.ToList(), Does.Not.Contain(childRegistration.Slot));
                    Assert.That(candidate.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(child.SubscribedAllEventIds.ToList(), Is.EqualTo(new[] { itemId }));
                    Assert.That(childFactory.CreateCount, Is.EqualTo(1));
                }
            }
            finally
            {
                if (candidate is not null)
                {
                    candidate.AllEventsCallback = (_, _, _) => default;
                }
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Readiness runs on the published manager, can register a real child, and remains
        /// part of the Add operation even when a synchronous factory exposes both adapters.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessPublishesAndAwaitsNestedRegistrationAsync(bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            TrackingLifecycleNodeManager? childManager = null;
            NodeManagerRegistration? observed = null;
            NodeManagerRegistration? child = null;

            Task<LifecycleResult> addTask = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Generation, Is.EqualTo(1));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);

                        LifecycleResult publicationResult2 = await CommitChangeAsync(
                            lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(
                                    kModelNamespaceUri, kRootNodeId))), ct).WaitAsync(
                                    s_readinessTimeout, ct).ConfigureAwait(false);
                        child = RequireRegistration(publicationResult2.Registrations[0]);
                        Assert.That(child.Id, Is.Not.EqualTo(observed.Id));
                        Assert.That(child.Generation, Is.EqualTo(1));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration? added = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(addTask.IsCompleted, Is.False, "Add must await the participant's unfinished work.");
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                added = RequireRegistration(
                    (await addTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false)).Registrations[0]);
            }

            AssertLifecycleValue(added);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(child);
            AssertLifecycleValue(childManager);
            Assert.That(added, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(added));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(added, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// All replacement-factory overloads run readiness on committed gen2, not on a
        /// prepared candidate. The old manager is deliberately an async reload participant.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessObservesCommittedGenerationAndAwaitsNestedRegistrationAsync(
            ReadinessChange change,
            bool synchronousFactory)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? originalManager = null;
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager)), timeout.Token).WaitAsync(
                        s_readinessTimeout).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult3.Registrations[0]);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            TrackingLifecycleNodeManager? childManager = null;
            NodeManagerRegistration? observed = null;
            NodeManagerRegistration? child = null;

            Task<LifecycleResult> reloadTask = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        Assert.That(observed, Is.Not.SameAs(original));
                        AssertReadinessManager(observed, manager, synchronousFactory);
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        LifecycleResult publicationResult4 = await CommitChangeAsync(
                            lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(
                                    kModelNamespaceUri, kRootNodeId))), ct).WaitAsync(
                                    s_readinessTimeout, ct).ConfigureAwait(false);
                        child = RequireRegistration(publicationResult4.Registrations[0]);
                        Assert.That(child.Id, Is.Not.EqualTo(original.Id));
                        await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);

            NodeManagerRegistration? next = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(participant);
                Assert.That(reloadTask.IsCompleted, Is.False, "Reload must await committed-generation readiness.");
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(2));
                Assert.That(participant.ReadinessCount, Is.EqualTo(1));
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                next = RequireRegistration(
                    (await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false)).Registrations[0]);
            }

            AssertLifecycleValue(next);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(originalManager);
            AssertLifecycleValue(child);
            AssertLifecycleValue(childManager);
            Assert.That(next, Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(next));
            Assert.That(next.Id, Is.EqualTo(original.Id));
            Assert.That(next.Generation, Is.EqualTo(2));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await originalManager.DisposalCompleted.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(child).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(next, participant, synchronousFactory).ConfigureAwait(false);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the optional-participant invariant to the existing preparation rollback
        /// tests: a candidate with real, partly created nodes is never made ready.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task PreparationFailureNeverInvokesReadinessAsync(bool reload)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration? original = null;
            TrackingLifecycleNodeManager? originalManager = null;
            if (reload)
            {
                LifecycleResult publicationResult5 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => originalManager = manager)), timeout.Token).WaitAsync(
                            s_readinessTimeout).ConfigureAwait(false);
                original = RequireRegistration(publicationResult5.Registrations[0]);
            }

            var expected = new SentinelException("Readiness candidate preparation failed.");
            ReadinessLifecycleNodeManager? participant = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                reload ? ReadinessChange.Reload : ReadinessChange.Add,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.PreparationFailure = expected;
                },
                timeout.Token);
            SentinelException failure = await ExpectReadinessExceptionAsync<SentinelException>(operation)
                .ConfigureAwait(false);
            AssertLifecycleValue(participant);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(participant.ReadinessCount, Is.Zero);
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            NodeId candidateValueId = new(
                kValueNodeId, (ushort)participant.Server.NamespaceUris.GetIndex(kModelNamespaceUri));
            Assert.That(participant.Find(candidateValueId), Is.Null);
            if (reload)
            {
                AssertLifecycleValue(original);
                AssertLifecycleValue(originalManager);
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
                Assert.That(FindReadinessRegistration(), Is.SameAs(original));
                Assert.That(originalManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(originalManager.DisposeCount, Is.Zero);
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(original).ConfigureAwait(false);
            }
            else
            {
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
                DataValue missing = await ReadValueAsync(candidateValueId).ConfigureAwait(false);
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
        }

        /// <summary>
        /// An Add callback exception is postcommit and preserves the exact live handle
        /// for explicit cleanup and a later, independent registration.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessFailureRetainsRegistrationForCleanupAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Published Add readiness failed.");
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        throw expected;
                    };
                },
                timeout.Token);
            LifecycleResult failure =
                await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(observed);

            Assert.That(
                failure.Issues.ToList().Any(issue => issue.Stage == NodeManagerReconciliationStage.Readiness), Is.True);
            Assert.That(
                RequireLifecycleValue(
                failure).Issues.ToList().Single(
                issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure, Is.SameAs(expected));
            Assert.That(RequireRegistration(failure.Registrations[0]), Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Candidate historian bindings stay private through a failed readiness attempt,
        /// then publish once after the exact registration succeeds on retry.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task StagedHistorianBindingsPublishOnlyAfterSuccessfulReadinessRetryAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Historian readiness failed once.");
            int readinessAttempts = 0;
            ReadinessLifecycleNodeManager? participant = null;
            StagedHistorianState? staged = null;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;

            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    staged = CreateStagedHistorianState(manager, serving);
                    manager.ReadinessCallback = _ =>
                    {
                        AssertCandidateHistorianRegistrationsArePrivate(staged!, serving);
                        if (Interlocked.Increment(ref readinessAttempts) == 1)
                        {
                            throw expected;
                        }
                        return default;
                    };
                },
                timeout.Token);

            LifecycleResult failure =
                await operation.WaitAsync(s_readinessTimeout)
                    .ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(staged);
            StagedHistorianState candidate = staged!;

            Assert.That(
                RequireLifecycleValue(
                failure).Issues.ToList().Single(
                issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure, Is.SameAs(expected));
            Assert.That(RequireRegistration(failure.Registrations[0]), Is.SameAs(FindReadinessRegistration()));
            Assert.That(RequireRegistration(failure.Registrations[0]).NodeManager, Is.SameAs(participant));
            Assert.That(readinessAttempts, Is.EqualTo(1));
            AssertCandidateHistorianRegistrationsArePrivate(candidate, serving);
            Assert.That(
                candidate.CaptureHandlerTarget.TryGetTarget(out _),
                Is.True,
                "The candidate builder must retain its capture handler while the committed readiness result " +
                "is retryable.");

            LifecycleResult readinessResult = failure ??
                throw new AssertionException("The failed readiness operation did not retain its retry result.");
            await readinessResult.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(readinessAttempts, Is.EqualTo(2));
            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
            Assert.That(
                serving.Resolve(candidate.NamespaceNode),
                Is.SameAs(candidate.NamespaceProvider));
            Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(candidate.DefaultProvider));
            Assert.That(candidate.LocalRegistry.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
            Assert.That(
                candidate.LocalRegistry.Resolve(candidate.NamespaceNode), Is.SameAs(candidate.NamespaceProvider));
            Assert.That(candidate.LocalRegistry.Resolve(candidate.DefaultNode), Is.SameAs(candidate.DefaultProvider));
            Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.True);
            Assert.That(serving.Providers.Contains(candidate.NamespaceProvider), Is.True);
            Assert.That(serving.Providers.Contains(candidate.DefaultProvider), Is.True);

            await readinessResult.RetryReconciliationAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(readinessAttempts, Is.EqualTo(2), "A completed retry must not republish or rerun readiness.");
            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));

            ReadinessLifecycleNodeManager? replacementManager = null;
            StagedHistorianState? replacementState = null;
            LifecycleResult publicationResult6 = await StartReadinessChangeAsync(
                ReadinessChange.Reload,
                RequireRegistration(failure.Registrations[0]),
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    replacementManager = manager;
                    replacementState = CreateStagedHistorianState(manager, serving);
                    manager.ReadinessCallback = _ =>
                    {
                        Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.BaselineExactProvider));
                        Assert.That(
                            serving.Resolve(candidate.NamespaceNode),
                            Is.SameAs(candidate.BaselineNamespaceProvider));
                        Assert.That(
                            serving.Resolve(candidate.DefaultNode),
                            Is.SameAs(candidate.BaselineDefaultProvider));
                        Assert.That(
                            candidate.LocalRegistry.Resolve(candidate.ExactNode), Is.SameAs(candidate.NodeProvider));
                        Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.False);
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration replacement = RequireRegistration(publicationResult6.Registrations[0]);
            AssertLifecycleValue(replacementManager);
            AssertLifecycleValue(replacementState);
            StagedHistorianState replacementCandidate = replacementState!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    serving.Resolve(replacementCandidate.ExactNode), Is.SameAs(replacementCandidate.NodeProvider));
                Assert.That(
                    serving.Resolve(replacementCandidate.NamespaceNode),
                    Is.SameAs(replacementCandidate.NamespaceProvider));
                Assert.That(
                    serving.Resolve(replacementCandidate.DefaultNode),
                    Is.SameAs(replacementCandidate.DefaultProvider));
                Assert.That(serving.Providers.Contains(candidate.NodeProvider), Is.False);
                Assert.That(serving.Providers.Contains(replacementCandidate.NodeProvider), Is.True);
            }

            await RemoveReadinessRegistrationAsync(replacement, replacementManager).ConfigureAwait(false);

            Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(candidate.BaselineExactProvider));
            Assert.That(serving.Resolve(candidate.NamespaceNode), Is.SameAs(candidate.BaselineNamespaceProvider));
            Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(candidate.BaselineDefaultProvider));
            AssertHistorianCaptureReleased(candidate);
            AssertHistorianCaptureReleased(replacementCandidate);
            Assert.That(candidate.NodeProvider.DisposeCount, Is.Zero,
                "The serving registry retains disposal responsibility for a published provider.");
            Assert.That(candidate.NamespaceProvider.DisposeCount, Is.Zero);
            Assert.That(candidate.DefaultProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.NodeProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.NamespaceProvider.DisposeCount, Is.Zero);
            Assert.That(replacementCandidate.DefaultProvider.DisposeCount, Is.Zero);
        }

        /// <summary>
        /// Aborting preparation withdraws only candidate state and disposes its staged
        /// providers and capture resources without publishing any bindings.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task AbortedHistorianCandidateDoesNotPublishAndCleansBuilderResourcesAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Historian candidate preparation failed.");
            ReadinessLifecycleNodeManager? participant = null;
            StagedHistorianState? staged = null;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            var legacyProvider = new LifecycleHistorianProvider();
            var otherOwnerProvider = new LifecycleHistorianProvider();
            serving.RegisterDefault(legacyProvider);
            using var otherOwner = new StagedHistorianProviderRegistry(
                m_server.CurrentInstance.NamespaceUris,
                serving,
                new object());
            var otherOwnerNode = new NodeId("other-owner", 0);
            otherOwner.RegisterForNode(otherOwnerNode, otherOwnerProvider);
            otherOwner.Publish();

            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    staged = CreateStagedHistorianState(manager, serving);
                    manager.PreparationFailure = expected;
                },
                timeout.Token);

            SentinelException failure =
                await ExpectReadinessExceptionAsync<SentinelException>(operation).ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(staged);
            StagedHistorianState candidate = staged!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.SameAs(expected));
                Assert.That(participant.ReadinessCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(participant.DisposeCount, Is.EqualTo(1));
                Assert.That(serving.Resolve(candidate.ExactNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(candidate.NamespaceNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(candidate.DefaultNode), Is.SameAs(legacyProvider));
                Assert.That(serving.Resolve(otherOwnerNode), Is.SameAs(otherOwnerProvider));
                Assert.That(serving.Providers.Contains(otherOwnerProvider), Is.True);
                Assert.That(legacyProvider.DisposeCount, Is.Zero);
                Assert.That(otherOwnerProvider.DisposeCount, Is.Zero);
                Assert.That(candidate.NodeProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(candidate.NamespaceProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(candidate.DefaultProvider.DisposeCount, Is.EqualTo(1));
            }
            AssertHistorianCaptureReleased(candidate);
        }

        /// <summary>
        /// Rejects candidate historian registrations when the manager cannot complete readiness.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task HistorianRegistrationRequiresReadinessParticipantAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var provider = new LifecycleHistorianProvider();
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            NodeId defaultNode = new("no-readiness", 0);
            IHistorianProvider? baselineProvider = serving.Resolve(defaultNode);
            TrackingLifecycleNodeManager? manager = null;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                RequireRegistration(
                    (await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        created =>
                        {
                            manager = created;
                            new HistorianBuilder(created.SystemContext.Server)
                                .UseProvider(provider)
                                .RegisterAsDefault();
                        })), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            }).ConfigureAwait(false);

            AssertLifecycleValue(manager);
            TrackingLifecycleNodeManager actualManager = manager ??
                throw new AssertionException("The historian candidate factory did not create its manager.");
            ServiceResultException actualFailure = failure ??
                throw new AssertionException("Historian registration did not require a readiness participant.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actualFailure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(actualFailure.Message, Does.Contain("requires").And.Contain("OnServerReadyAsync"));
                Assert.That(actualManager.DisposeCount, Is.EqualTo(1));
                Assert.That(actualManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(serving.Resolve(defaultNode), Is.SameAs(baselineProvider));
                Assert.That(serving.Providers.Contains(provider), Is.False);
                Assert.That(provider.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Historian bindings created with the address space require readiness before publication.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task HistorianRegistrationDuringAddressSpaceCreationRequiresReadinessBeforePublicationAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var baselineProvider = new LifecycleHistorianProvider();
            var candidateProvider = new LifecycleHistorianProvider();
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            serving.RegisterDefault(baselineProvider);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long revisionBeforePreparation = lifecycle.Revision;
            var probeNode = new NodeId("address-space-historian", 0);
            AddressSpaceHistorianNodeManager? manager = null;
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri],
                (server, configuration, _) =>
                {
                    var created = new AddressSpaceHistorianNodeManager(
                        server,
                        configuration,
                        m_logger,
                        candidateProvider);
                    manager = created;
                    return new ValueTask<IAsyncNodeManager>(created);
                });

            ServiceResultException? caughtFailure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                RequireRegistration(
                    (await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            }).ConfigureAwait(false);
            ServiceResultException failure = caughtFailure ??
                throw new AssertionException("Historian registration did not require a readiness participant.");

            AddressSpaceHistorianNodeManager actualManager = manager ??
                throw new AssertionException("The historian candidate factory did not create its manager.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actualManager.AddressSpaceRegistrationCount, Is.EqualTo(1));
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(failure.Message, Does.Contain("requires").And.Contain("OnServerReadyAsync"));
                Assert.That(lifecycle.Revision, Is.EqualTo(revisionBeforePreparation));
                Assert.That(serving.Resolve(probeNode), Is.SameAs(baselineProvider));
                Assert.That(serving.Providers.Contains(candidateProvider), Is.False);
                Assert.That(actualManager.DisposeCount, Is.EqualTo(1));
                Assert.That(candidateProvider.DisposeCount, Is.EqualTo(1));
                Assert.That(baselineProvider.DisposeCount, Is.Zero);
            }
        }

        /// <summary>
        /// Bare Historize borrows another manager's default without requiring readiness participation.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task BareHistorizeBorrowsServingDefaultWithoutReadinessAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            var provider = new LifecycleHistorianProvider();
            var probeNode = new NodeId("borrowed-historian-default", 0);
            NodeManagerRegistration? owner = null;
            NodeManagerRegistration? borrower = null;
            BorrowingDefaultHistorianNodeManager? manager = null;
            try
            {
                LifecycleResult publicationResult7 = await StartReadinessChangeAsync(
                    ReadinessChange.Add,
                    null,
                    synchronousFactory: false,
                    kGeneration1Value,
                    ownerManager =>
                    {
                        ((IHistorianRegistryProvider)ownerManager.SystemContext.Server)
                            .HistorianRegistry.RegisterDefault(provider);
                    },
                    timeout.Token).ConfigureAwait(false);
                owner = RequireRegistration(publicationResult7.Registrations[0]);

                Assert.That(serving.Resolve(probeNode), Is.SameAs(provider));
                Assert.That(
                    ((IHistorianOwnedProviderRegistry)serving).IsOwnedBinding(
                        HistorianProviderRegistrationKind.Default,
                        NodeId.Null,
                        null,
                        provider),
                    Is.True,
                    "The default must be owned by the first manager, not registered directly on the server.");

                var factory = new CountingNodeManagerFactory(
                    [kSecondModelNamespaceUri],
                    (server, _, _) =>
                    {
                        manager = new BorrowingDefaultHistorianNodeManager(server, kSecondModelNamespaceUri);
                        return new ValueTask<IAsyncNodeManager>(manager);
                    });
                LifecycleResult publicationResult8 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false);
                borrower = RequireRegistration(publicationResult8.Registrations[0]);

                AssertLifecycleValue(manager);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(borrower.NodeManager, Is.SameAs(manager));
                    Assert.That(serving.Resolve(probeNode), Is.SameAs(provider));
                    Assert.That(serving.Providers.Contains(provider), Is.True);
                    Assert.That(provider.DisposeCount, Is.Zero);
                }

                // A timed-out retirement is still lifecycle-owned and must not be started again.
                NodeManagerRegistration retiringOwner = owner;
                owner = null;
                await RemoveAndRetireAsync(lifecycle, retiringOwner, timeout.Token)
                    .WaitAsync(s_readinessTimeout).ConfigureAwait(false);

                Assert.That(serving.Resolve(probeNode), Is.SameAs(provider));
                Assert.That(provider.DisposeCount, Is.Zero,
                    "The borrowing manager must retain the default after its original owner retires.");
            }
            finally
            {
                try
                {
                    if (borrower is not null)
                    {
                        using var cleanupTimeout = new CancellationTokenSource(s_readinessTimeout);
                        await RemoveAndRetireAsync(lifecycle, borrower, cleanupTimeout.Token)
                            .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await RemovePendingReadinessOwnerAsync(lifecycle, owner).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Runtime historian registrations from a non-readiness manager reach the serving registry.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task RuntimeHistorianRegistrationPublishesWithoutReadinessAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            var borrowedProvider = new LifecycleHistorianProvider();
            var runtimeProvider = new LifecycleHistorianProvider();
            serving.RegisterDefault(borrowedProvider);
            BorrowingDefaultHistorianNodeManager? manager = null;
            IServerInternal? managerServer = null;
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri],
                (server, _, _) =>
                {
                    managerServer = server;
                    manager = new BorrowingDefaultHistorianNodeManager(server);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            LifecycleResult publicationResult9 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult9.Registrations[0]);
            AssertLifecycleValue(manager);
            IServerInternal actualManagerServer = managerServer ??
                throw new AssertionException("The historian manager factory did not capture its staging server.");
            ((IHistorianRegistryProvider)actualManagerServer).HistorianRegistry.RegisterDefault(runtimeProvider);

            try
            {
                Assert.That(
                    serving.Resolve(new NodeId("runtime-historian-registration", 0)),
                    Is.SameAs(runtimeProvider));
            }
            finally
            {
                await RemoveReadinessRegistrationAsync(registration).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A replacement retains its predecessor's historian binding while its readiness callback is pending.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task RetainedHistorianBindingRemainsAvailableWhileReplacementReadinessIsPendingAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            IHistorianProviderRegistry serving =
                ((IHistorianRegistryProvider)m_server.CurrentInstance).HistorianRegistry;
            var provider = new LifecycleHistorianProvider();
            var probeNode = new NodeId("retained-historian-binding", 0);
            LifecycleResult publicationResult10 = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    ((IHistorianRegistryProvider)manager.SystemContext.Server)
                        .HistorianRegistry.RegisterDefault(provider);
                },
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult10.Registrations[0]);

            var readinessEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReadiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LifecycleResult> replacementTask = StartReadinessChangeAsync(
                ReadinessChange.ImmediateReload,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    manager.ReadinessCallback = async ct =>
                    {
                        readinessEntered.TrySetResult(true);
                        await releaseReadiness.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                    var stagedRegistry = (StagedHistorianProviderRegistry)
                        ((IHistorianRegistryProvider)manager.SystemContext.Server).HistorianRegistry;
                    Assert.That(stagedRegistry.ResolveForHistorization(probeNode), Is.SameAs(provider));
                },
                timeout.Token);

            try
            {
                await readinessEntered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(
                    serving.Resolve(probeNode),
                    Is.SameAs(provider),
                    "The predecessor's historian binding must remain available until replacement readiness succeeds.");
            }
            finally
            {
                releaseReadiness.TrySetResult(true);
                NodeManagerRegistration replacement = RequireRegistration(
                    (await replacementTask.WaitAsync(timeout.Token)
                    .ConfigureAwait(false)).Registrations[0]);
                await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Lifecycle readiness completes deferred fluent configuration even when the callback does not forward it.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task LifecycleCompletesDeferredConfigurationAfterReadinessCallbackAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            DeferredFluentReadinessNodeManager? manager = null;
            var factory = new CountingNodeManagerFactory(
                [kModelNamespaceUri],
                (server, _, _) =>
                {
                    manager = new DeferredFluentReadinessNodeManager(server);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });

            LifecycleResult publicationResult11 = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, LifecycleChange.Add(factory), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult11.Registrations[0]);

            try
            {
                AssertLifecycleValue(manager);
                Assert.That(manager!.DeferredConfigurationCount, Is.EqualTo(1));
            }
            finally
            {
                await RemoveReadinessRegistrationAsync(registration).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A readiness retry owns its pending registration until the callback attempt completes.
        /// </summary>
        [Test]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessRetryRejectsRemovalWhileCallbackIsActiveAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var expected = new SentinelException("Published Add readiness failed before retry.");
            var retryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            ReadinessLifecycleNodeManager? participant = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        if (Interlocked.Increment(ref attempts) == 1)
                        {
                            throw expected;
                        }
                        retryEntered.TrySetResult(true);
                        await releaseRetry.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);
            LifecycleResult failure =
                await operation.WaitAsync(s_readinessTimeout)
                    .ConfigureAwait(false);
            LifecycleResult result = failure ??
                throw new AssertionException("The committed Add result was not retained.");

            Task retry = result.RetryReconciliationAsync(timeout.Token).AsTask();
            try
            {
                await retryEntered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                InvalidOperationException removeFailure =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        CommitChangeAsync(
                            Capture(
                            m_server.NodeManagerLifecycle, RequireRegistration(
                            failure.Registrations[0])), LifecycleChange.Remove(
                            RequireRegistration(failure.Registrations[0]).Slot), timeout.Token)).ConfigureAwait(false);
                Assert.That(removeFailure.Message, Does.Contain("completing readiness"));
            }
            finally
            {
                releaseRetry.TrySetResult(true);
            }

            await retry.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(result.Issues.ToArray(), Is.Empty);
            await RemoveReadinessRegistrationAsync(
                RequireRegistration(failure.Registrations[0]), participant).ConfigureAwait(false);
            Assert.That(participant!.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Replacing a service-level owner revokes its writer before the replacement readiness callback claims it.
        /// </summary>
        [Test]
        public async Task ReplacingServiceLevelOwnerRevokesWriterForNextGenerationAsync()
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            Action<byte>? retiredWriter = null;
            Action<byte>? replacementWriter = null;
            ReadinessLifecycleNodeManager? retiredManager = null;
            ReadinessLifecycleNodeManager? replacementManager = null;
            LifecycleResult publicationResult12 = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    retiredManager = manager;
                    manager.ReadinessCallback = _ =>
                    {
                        retiredWriter = ((IServerServiceLevelControl)manager.SystemContext.Server)
                            .ClaimServiceLevelControl();
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult12.Registrations[0]);

            LifecycleResult publicationResult13 = await StartReadinessChangeAsync(
                ReadinessChange.Reload,
                registration,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    replacementManager = manager;
                    manager.ReadinessCallback = _ =>
                    {
                        replacementWriter = ((IServerServiceLevelControl)manager.SystemContext.Server)
                            .ClaimServiceLevelControl();
                        return default;
                    };
                },
                timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration replacement = RequireRegistration(publicationResult13.Registrations[0]);

            AssertLifecycleValue(retiredManager);
            AssertLifecycleValue(replacementManager);
            Assert.That(replacement.Generation, Is.EqualTo(registration.Generation + 1));
            Assert.That(retiredWriter, Is.Not.Null);
            Assert.That(replacementWriter, Is.Not.Null);
            Assert.That(() => retiredWriter!(10), Throws.TypeOf<InvalidOperationException>());
            replacementWriter!(20);
            await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
        }

        /// <summary>
        /// Caller cancellation cannot abort committed readiness. Independent participant cancellation
        /// is reported with the authoritative committed identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("NodeManagerReadiness")]
        public async Task AddAsyncReadinessCancellationPreservesCommittedOutcomeAsync(bool observeCancellation)
        {
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            CancellationToken receivedToken = default;
            OperationCanceledException? callbackCancellation = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        receivedToken = ct;
                        observed = FindReadinessRegistration();
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value)
                            .ConfigureAwait(false);
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(s_readinessTimeout, CancellationToken.None).ConfigureAwait(false);
                        if (observeCancellation)
                        {
                            try
                            {
                                caller.Token.ThrowIfCancellationRequested();
                            }
                            catch (OperationCanceledException exception)
                            {
                                callbackCancellation = exception;
                                throw;
                            }
                        }
                    };
                },
                caller.Token);

            LifecycleResult? failure = null;
            NodeManagerRegistration? added = null;
            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(receivedToken.CanBeCanceled, Is.False);
                Assert.That(operation.IsCompleted, Is.False);
                caller.Cancel();
            }
            finally
            {
                release.TrySetResult(true);
                if (observeCancellation)
                {
                    failure = await operation.WaitAsync(s_readinessTimeout)
                        .ConfigureAwait(false);
                }
                else
                {
                    added = RequireRegistration(
                        (await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false)).Registrations[0]);
                }
            }

            AssertLifecycleValue(participant);
            AssertLifecycleValue(observed);
            if (observeCancellation)
            {
                AssertLifecycleValue(failure);
                Assert.That(
                    failure.Issues.ToList().Any(
                    issue => issue.Stage == NodeManagerReconciliationStage.Readiness), Is.True);
                Assert.That(
                    RequireLifecycleValue(
                    failure).Issues.ToList().Single(
                    issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure,
                    Is.InstanceOf<OperationCanceledException>());
                Assert.That(
                    RequireLifecycleValue(
                        RequireLifecycleValue(
                        failure).Issues.ToList().Single(
                        issue => issue.Stage == NodeManagerReconciliationStage.Readiness)
                        .InitialFailure as OperationCanceledException).CancellationToken,
                    Is.EqualTo(caller.Token));
                Assert.That(
                    RequireLifecycleValue(
                    failure).Issues.ToList().Single(
                    issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure, Is.SameAs(
                    callbackCancellation));
                Assert.That(RequireRegistration(failure.Registrations[0]), Is.SameAs(observed));
                Assert.That(RequireRegistration(failure!.Registrations[0]), Is.SameAs(observed));
            }
            else
            {
                Assert.That(added, Is.SameAs(observed));
            }
            Assert.That(FindReadinessRegistration(), Is.SameAs(observed));
            Assert.That(observed.Generation, Is.EqualTo(1));
            Assert.That(observed.NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(observeCancellation ? 0 : 1));
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            if (observeCancellation)
            {
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            }
            else
            {
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration1Value).ConfigureAwait(false);
            }
            await AssertReadinessCleanupAndRecoveryAsync(observed, participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Callback failures in every reload mode report the authoritative next handle,
        /// including cancellation, and release the readiness claim for explicit cleanup.
        /// </summary>
        [TestCase(ReadinessChange.Reload, false)]
        [TestCase(ReadinessChange.Reload, true)]
        [TestCase(ReadinessChange.ShadowReload, false)]
        [TestCase(ReadinessChange.ShadowReload, true)]
        [TestCase(ReadinessChange.ImmediateReload, false)]
        [TestCase(ReadinessChange.ImmediateReload, true)]
        [Category("NodeManagerReadiness")]
        public async Task ReloadReadinessFailureReportsExactCommittedRegistrationAsync(
            ReadinessChange change,
            bool cancel)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var caller = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? originalManager = null;
            LifecycleResult publicationResult14 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager)), caller.Token).WaitAsync(
                        s_readinessTimeout).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult14.Registrations[0]);
            Exception expected = cancel
                ? new OperationCanceledException("Committed reload readiness cancelled.", caller.Token)
                : new SentinelException("Committed reload readiness failed.");
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        Assert.That(ct.CanBeCanceled, Is.False);
                        observed = FindReadinessRegistration();
                        Assert.That(observed.Id, Is.EqualTo(original.Id));
                        Assert.That(observed.Generation, Is.EqualTo(2));
                        await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value)
                            .ConfigureAwait(false);
                        if (cancel)
                        {
                            caller.Cancel();
                            Assert.That(ct.IsCancellationRequested, Is.False);
                        }
                        throw expected;
                    };
                },
                caller.Token);
            LifecycleResult failure =
                await operation.WaitAsync(s_readinessTimeout)
                    .ConfigureAwait(false);
            AssertLifecycleValue(participant);
            AssertLifecycleValue(originalManager);

            Assert.That(
                failure.Issues.ToList().Any(issue => issue.Stage == NodeManagerReconciliationStage.Readiness), Is.True);
            Assert.That(
                RequireLifecycleValue(
                failure).Issues.ToList().Single(
                issue => issue.Stage == NodeManagerReconciliationStage.Readiness).InitialFailure, Is.SameAs(expected));
            Assert.That(RequireRegistration(failure.Registrations[0]), Is.SameAs(observed));
            Assert.That(FindReadinessRegistration(), Is.SameAs(RequireRegistration(failure.Registrations[0])));
            Assert.That(RequireRegistration(failure.Registrations[0]).Id, Is.EqualTo(original.Id));
            Assert.That(RequireRegistration(failure.Registrations[0]).Generation, Is.EqualTo(2));
            Assert.That(RequireRegistration(failure.Registrations[0]).NodeManager, Is.SameAs(participant));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.Zero);
            Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
            Assert.That(participant.DisposeCount, Is.Zero);
            await originalManager.DisposalCompleted.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
            // The replacement is live but never completed readiness, so external reads stay hidden.
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
            TrackingLifecycleNodeManager? rejectedManager = null;
            using var rejectTimeout = new CancellationTokenSource(s_readinessTimeout);
            Assert.That(
                () => CommitChangeAsync(
                    LifecycleInspection.Capture(
                    lifecycle, RequireRegistration(
                    failure.Registrations[0])), LifecycleChange.Replace(
                    RequireRegistration(failure.Registrations[0]).Slot, CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        manager => rejectedManager = manager)), rejectTimeout.Token).WaitAsync(s_readinessTimeout),
                Throws.InvalidOperationException);
            Assert.That(rejectedManager, Is.Null);
            await AssertReadinessCleanupAndRecoveryAsync(
                RequireRegistration(failure.Registrations[0]), participant).ConfigureAwait(false);
        }

        /// <summary>
        /// Readiness reserves only its generation. Conflicts must reject before invoking
        /// factories or deletion, not wait for readiness while retaining the global gate.
        /// </summary>
        [TestCase(ReadinessChange.Add)]
        [TestCase(ReadinessChange.Reload)]
        [TestCase(ReadinessChange.ShadowReload)]
        [TestCase(ReadinessChange.ImmediateReload)]
        [Category("NodeManagerReadiness")]
        public async Task ReadinessRejectsSameHandleChangesWithoutBlockingIndependentRegistrationAsync(
            ReadinessChange change)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            NodeManagerRegistration? original = null;
            if (change != ReadinessChange.Add)
            {
                LifecycleResult publicationResult15 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(
                    CreateNodeManagementFactory(
                    kGeneration1Value, includeEuRange: false)), timeout.Token).WaitAsync(
                    s_readinessTimeout).ConfigureAwait(false);
                original = RequireRegistration(publicationResult15.Registrations[0]);
            }

            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? participant = null;
            NodeManagerRegistration? observed = null;
            Task<LifecycleResult> operation = StartReadinessChangeAsync(
                change,
                original,
                synchronousFactory: false,
                kGeneration2Value,
                manager =>
                {
                    participant = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        observed = FindReadinessRegistration();
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                },
                timeout.Token);
            var asyncReplacement = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            var syncReplacement = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            NodeManagerRegistration? completed = null;

            try
            {
                await entered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                AssertLifecycleValue(observed);
                AssertLifecycleValue(participant);
                Assert.That(operation.IsCompleted, Is.False);
                InvalidOperationException removeFailure =
                    await ExpectReadinessExceptionAsync<InvalidOperationException>(
                        CommitChangeAsync(
                            Capture(
                            lifecycle, observed), LifecycleChange.Remove(
                            observed.Slot), timeout.Token)).ConfigureAwait(false);
                Assert.That(removeFailure.Message, Does.Contain("completing readiness"));

                asyncReplacement.SetupGet(factory => factory.NamespacesUris).Returns([]);
                syncReplacement.SetupGet(factory => factory.NamespacesUris).Returns([]);
                ReadinessChange[] reloads =
                [
                    ReadinessChange.Reload,
                    ReadinessChange.ShadowReload,
                    ReadinessChange.ImmediateReload
                ];
                foreach (ReadinessChange reload in reloads)
                {
                    InvalidOperationException reloadFailure =
                        await ExpectReadinessExceptionAsync<InvalidOperationException>(
                            InvokeReadinessChangeAsync(
                                lifecycle,
                                reload,
                                observed,
                                asyncReplacement.Object,
                                timeout.Token).AsTask()).ConfigureAwait(false);
                    Assert.That(reloadFailure.Message, Does.Contain("completing readiness"));

                    // One held Add covers the sync competitor overloads too; the other
                    // cases focus on ownership of each distinct reload operation.
                    if (change == ReadinessChange.Add)
                    {
                        InvalidOperationException syncFailure =
                            await ExpectReadinessExceptionAsync<InvalidOperationException>(
                                InvokeReadinessChangeAsync(
                                    lifecycle,
                                    reload,
                                    observed,
                                    syncReplacement.Object,
                                    timeout.Token).AsTask()).ConfigureAwait(false);
                        Assert.That(syncFailure.Message, Does.Contain("completing readiness"));
                    }
                }

                asyncReplacement.Verify(
                    factory => factory.CreateAsync(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
                syncReplacement.Verify(
                    factory => factory.Create(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>()),
                    Times.Never);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(FindReadinessRegistration(), Is.SameAs(observed));

                TrackingLifecycleNodeManager? independentManager = null;
                LifecycleResult publicationResult16 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kSecondRegistrationValue,
                        manager => independentManager = manager,
                        kSecondModelNamespaceUri)), timeout.Token).WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                NodeManagerRegistration independent = RequireRegistration(publicationResult16.Registrations[0]);
                Assert.That(independent.Id, Is.Not.EqualTo(observed.Id));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(independent).ConfigureAwait(false);
                AssertLifecycleValue(independentManager);
                Assert.That(independentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(independentManager.DisposeCount, Is.EqualTo(1));
                await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
                await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(participant.ReadinessCompletedCount, Is.Zero);
                Assert.That(participant.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(participant.DisposeCount, Is.Zero);
                Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                completed = RequireRegistration(
                    (await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false)).Registrations[0]);
            }

            AssertLifecycleValue(completed);
            AssertLifecycleValue(participant);
            Assert.That(completed, Is.SameAs(observed));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCompletedCount, Is.EqualTo(1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(completed).ConfigureAwait(false);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Detached manager destruction may remove its real child through the lifecycle.
        /// Removal and retired-drain claims still prevent duplicate destruction while that
        /// callback is suspended, including a shadow generation with a real monitored item.
        /// </summary>
        /// <exception cref="AssertionException">The stale owner was not rejected.</exception>
        [TestCase(ReadinessCleanup.Remove)]
        [TestCase(ReadinessCleanup.Reload)]
        [TestCase(ReadinessCleanup.ShadowDrain)]
        [Category("NodeManagerReadiness")]
        public async Task DetachedDeletionRemovesRealDependentRegistrationWithSingleCleanupAsync(
            ReadinessCleanup cleanup)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            var deleteEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDelete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ReadinessLifecycleNodeManager? parentManager = null;
            TrackingLifecycleNodeManager? childManager = null;
            TrackingLifecycleNodeManager? replacementManager = null;
            NodeManagerRegistration? child = null;
            LifecycleResult publicationResult17 = await StartReadinessChangeAsync(
                ReadinessChange.Add,
                null,
                synchronousFactory: false,
                kGeneration1Value,
                manager =>
                {
                    parentManager = manager;
                    manager.ReadinessCallback = async ct =>
                    {
                        child = RequireRegistration(
                            (await CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                                kSecondRegistrationValue,
                                created => childManager = created,
                                kSecondModelNamespaceUri,
                                ReadinessNodeId(
                                    kModelNamespaceUri, kRootNodeId))), ct).WaitAsync(
                                    s_readinessTimeout, ct).ConfigureAwait(false)).Registrations[0]);
                    };
                    manager.DeleteCallback = async ct =>
                    {
                        // If an earlier assertion fails, the existing ordered fixture
                        // shutdown owns all remaining registrations, including the child.
                        if (lifecycle.IsShuttingDown)
                        {
                            return;
                        }
                        deleteEntered.TrySetResult(true);
                        await releaseDelete.Task.WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                        using var nestedTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        nestedTimeout.CancelAfter(s_readinessTimeout);
                        await RemoveAndRetireAsync(lifecycle, RequireLifecycleValue(child), nestedTimeout.Token)
                            .WaitAsync(s_readinessTimeout, ct).ConfigureAwait(false);
                    };
                },
                timeout.Token).WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            NodeManagerRegistration original = RequireRegistration(publicationResult17.Registrations[0]);
            AssertLifecycleValue(parentManager);
            AssertLifecycleValue(childManager);
            AssertLifecycleValue(child);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = 0;
            Task? cleanupTask = null;
            Task<NodeManagerRegistration>? reloadTask = null;
            NodeManagerRegistration? replacement = null;
            try
            {
                if (cleanup == ReadinessCleanup.ShadowDrain)
                {
                    // The monitored item migrates at commit; the test separately awaits physical retirement.
                    (subscriptionId, _) = await CreateSubscriptionWithMonitoredItemAsync(
                        services,
                        ReadinessNodeId(kModelNamespaceUri, kValueNodeId))
                        .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                }
                reloadTask = RunWithoutExecutionContext(async () =>
                {
                    await using ILifecycleWriter writer = await Capture(lifecycle, original)
                        .BeginAsync(timeout.Token).ConfigureAwait(false);
                    LifecycleChange change = cleanup == ReadinessCleanup.Remove
                        ? LifecycleChange.Remove(original.Slot)
                        : LifecycleChange.Replace(original.Slot, CreateTrackingNodeManagementFactory(
                            kGeneration2Value, manager => replacementManager = manager));
                    await using IPreparedChanges prepared = await writer.PrepareAsync([change], timeout.Token)
                        .ConfigureAwait(false);
                    LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(result.CleanupFailure, Is.Null);
                    await result.Retired.WaitAsync(s_readinessTimeout, timeout.Token).ConfigureAwait(false);
                    return cleanup == ReadinessCleanup.Remove ? original : RequireRegistration(result.Registrations[0]);
                });
                cleanupTask = reloadTask;

                await deleteEntered.Task.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(childManager.DisposeCount, Is.Zero);
                Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.SameAs(child));
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);

                InvalidOperationException competingRemove = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await CommitChangeAsync(
                        LifecycleInspection.Capture(lifecycle, original),
                        LifecycleChange.Remove(original.Slot), timeout.Token).ConfigureAwait(false))
                    .ConfigureAwait(false) ??
                    throw new AssertionException("The stale owner was not rejected.");
                Assert.That(competingRemove.Message, Does.Contain("stale").Or.Contain("not owned"));

                // These real operations also give retired cleanup another opportunity
                // to run while the first destruction callback owns its drain claim.
                TrackingLifecycleNodeManager? probeManager = null;
                LifecycleResult publicationResult19 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                        kFirstRegistrationValue,
                        manager => probeManager = manager,
                        kReadinessProbeNamespaceUri)), timeout.Token).WaitAsync(
                            s_readinessTimeout).ConfigureAwait(false);
                NodeManagerRegistration probe = RequireRegistration(publicationResult19.Registrations[0]);
                await AssertReadinessValueAsync(kReadinessProbeNamespaceUri, kFirstRegistrationValue)
                    .ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(probe).ConfigureAwait(false);
                AssertLifecycleValue(probeManager);
                Assert.That(probeManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(probeManager.DisposeCount, Is.EqualTo(1));
                Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(parentManager.DisposeCount, Is.Zero);
                Assert.That(childManager.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(cleanupTask.IsCompleted, Is.False);
                if (cleanup == ReadinessCleanup.Remove)
                {
                    await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
                }
                else
                {
                    AssertLifecycleValue(replacementManager);
                    Assert.That(FindReadinessRegistration().Generation, Is.EqualTo(2));
                    await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                    Assert.That(replacementManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(replacementManager.DisposeCount, Is.Zero);
                }
            }
            finally
            {
                releaseDelete.TrySetResult(true);
                try
                {
                    if (cleanupTask is not null)
                    {
                        await cleanupTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (subscriptionId != 0)
                    {
                        await DeleteSubscriptionAsync(services, subscriptionId)
                            .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
                    }
                }
            }

            if (reloadTask is not null && cleanup != ReadinessCleanup.Remove)
            {
                replacement = await reloadTask.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            Assert.That(parentManager.ReadinessCount, Is.EqualTo(1));
            Assert.That(parentManager.ReadinessCompletedCount, Is.EqualTo(1));
            Assert.That(parentManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(parentManager.DisposeCount, Is.EqualTo(1));
            Assert.That(parentManager.Find(ReadinessNodeId(kModelNamespaceUri, kValueNodeId)), Is.Null);
            Assert.That(childManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(childManager.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.Find(candidate => candidate.Id == child.Id), Is.Null);
            await AssertReadinessNodeUnknownAsync(kSecondModelNamespaceUri).ConfigureAwait(false);
            await AssertReadinessNodeUnknownAsync(kReadinessProbeNamespaceUri).ConfigureAwait(false);
            if (replacement is not null)
            {
                AssertLifecycleValue(replacementManager);
                Assert.That(FindReadinessRegistration(), Is.SameAs(replacement));
                Assert.That(replacement.Id, Is.EqualTo(original.Id));
                Assert.That(replacement.Generation, Is.EqualTo(2));
                await AssertReadinessValueAsync(kModelNamespaceUri, kGeneration2Value).ConfigureAwait(false);
                await RemoveReadinessRegistrationAsync(replacement).ConfigureAwait(false);
                Assert.That(replacementManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(replacementManager.DisposeCount, Is.EqualTo(1));
            }
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private Task<LifecycleResult> StartReadinessChangeAsync(
            ReadinessChange change,
            NodeManagerRegistration? original,
            bool synchronousFactory,
            int value,
            Action<ReadinessLifecycleNodeManager> configure,
            CancellationToken cancellationToken)
        {
            ReadinessLifecycleNodeManager CreateManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                var manager = new ReadinessLifecycleNodeManager(
                    server,
                    configuration,
                    m_logger,
                    value);
                configure(manager);
                return manager;
            }

            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            if (synchronousFactory)
            {
                var factory = new Mock<INodeManagerFactory>();
                factory.Setup(candidate => candidate.Create(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>()))
                    .Returns((IServerInternal server, ApplicationConfiguration configuration) =>
                        new SyncNodeManagerAdapter(CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
            else
            {
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((
                        IServerInternal server,
                        ApplicationConfiguration configuration,
                        CancellationToken _) => new ValueTask<IAsyncNodeManager>(
                            CreateManager(server, configuration)));
                return RunWithoutExecutionContext(() => InvokeReadinessChangeAsync(
                    lifecycle, change, original, factory.Object, cancellationToken).AsTask());
            }
        }

        private static async ValueTask<LifecycleResult> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration? original,
            IAsyncNodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            ILifecycleCapture capture = change == ReadinessChange.Add
                ? lifecycle.CaptureChanges()
                : Capture(lifecycle, RequireLifecycleValue(original));
            LifecycleChange unit = change switch
            {
                ReadinessChange.Add => LifecycleChange.Add(factory),
                ReadinessChange.Reload or ReadinessChange.ShadowReload =>
                    LifecycleChange.Replace(RequireLifecycleValue(original).Slot, factory),
                ReadinessChange.ImmediateReload => LifecycleChange.Replace(
                    RequireLifecycleValue(original).Slot, factory, NodeManagerMonitoredItemTransitionMode.Invalidate),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellationToken).ConfigureAwait(false);
            await using IPreparedChanges prepared = await writer.PrepareAsync([unit], cancellationToken)
                .ConfigureAwait(false);
            return await prepared.CommitAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private static ValueTask<LifecycleResult> InvokeReadinessChangeAsync(
            INodeManagerLifecycle lifecycle,
            ReadinessChange change,
            NodeManagerRegistration? original,
            INodeManagerFactory factory,
            CancellationToken cancellationToken)
        {
            return InvokeReadinessChangeAsync(lifecycle, change, original, factory.AsAsync(), cancellationToken);
        }

        private NodeManagerRegistration FindReadinessRegistration()
        {
            NodeManagerRegistration? registration = m_server.NodeManagerLifecycle.Registrations.Find(
                candidate => candidate.NamespaceUris.Contains(kModelNamespaceUri));
            Assert.That(registration, Is.Not.Null, "Readiness requires an already committed registration.");
            return RequireLifecycleValue(registration);
        }

        private NodeId ReadinessNodeId(string namespaceUri, uint identifier)
        {
            int namespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return new NodeId(identifier, (ushort)namespaceIndex);
        }

        private async Task AssertReadinessValueAsync(string namespaceUri, int expected)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(expected));
        }

        private async Task AssertReadinessNodeUnknownAsync(string namespaceUri)
        {
            DataValue value = await ReadValueAsync(ReadinessNodeId(namespaceUri, kValueNodeId))
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        private static StagedHistorianState CreateStagedHistorianState(
            ReadinessLifecycleNodeManager manager,
            IHistorianProviderRegistry serving)
        {
            IServerInternal stagingServer = manager.SystemContext.Server;
            var localRegistry =
                ((IHistorianRegistryProvider)stagingServer).HistorianRegistry;
            ushort namespaceIndex = manager.NamespaceIndexes[0];
            var exactNode = new NodeId("historian-exact", namespaceIndex);
            var namespaceNode = new NodeId("historian-namespace", namespaceIndex);
            var defaultNode = new NodeId("historian-default", 0);
            var nodeProvider = new LifecycleHistorianProvider();
            var namespaceProvider = new LifecycleHistorianProvider();
            var defaultProvider = new LifecycleHistorianProvider();
            var variable = new BaseDataVariableState(parent: null)
            {
                NodeId = exactNode,
                BrowseName = new QualifiedName("S9HistorianCapture", namespaceIndex),
                DisplayName = new LocalizedText("S9HistorianCapture"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            var nodeBuilder = new HistorianBuilder(stagingServer).UseProvider(nodeProvider);
            nodeBuilder.RegisterForNode(exactNode);
            nodeBuilder.Historize(
                variable,
                systemContext: manager.SystemContext,
                captureOptions: new HistorianCaptureOptions
                {
                    BatchTarget = 1,
                    BatchWindow = TimeSpan.FromSeconds(1)
                });
            new HistorianBuilder(stagingServer)
                .UseProvider(namespaceProvider)
                .RegisterForNamespace(kModelNamespaceUri);
            new HistorianBuilder(stagingServer)
                .UseProvider(defaultProvider)
                .RegisterAsDefault();

            WeakReference<object> captureHandlerTarget = nodeBuilder.CreateCaptureHandlerTargetReference(variable);

            return new StagedHistorianState(
                localRegistry,
                exactNode,
                namespaceNode,
                defaultNode,
                serving.Resolve(exactNode),
                serving.Resolve(namespaceNode),
                serving.Resolve(defaultNode),
                nodeProvider,
                namespaceProvider,
                defaultProvider,
                variable,
                captureHandlerTarget);
        }

        private static void AssertCandidateHistorianRegistrationsArePrivate(
            StagedHistorianState staged,
            IHistorianProviderRegistry serving)
        {
            Assert.That(staged.LocalRegistry.Resolve(staged.ExactNode), Is.SameAs(staged.NodeProvider));
            Assert.That(staged.LocalRegistry.Resolve(staged.NamespaceNode), Is.SameAs(staged.NamespaceProvider));
            Assert.That(staged.LocalRegistry.Resolve(staged.DefaultNode), Is.SameAs(staged.DefaultProvider));
            Assert.That(serving.Resolve(staged.ExactNode), Is.SameAs(staged.BaselineExactProvider));
            Assert.That(serving.Resolve(staged.NamespaceNode), Is.SameAs(staged.BaselineNamespaceProvider));
            Assert.That(serving.Resolve(staged.DefaultNode), Is.SameAs(staged.BaselineDefaultProvider));
            Assert.That(serving.Providers.Contains(staged.NodeProvider), Is.False);
            Assert.That(serving.Providers.Contains(staged.NamespaceProvider), Is.False);
            Assert.That(serving.Providers.Contains(staged.DefaultProvider), Is.False);
        }

        /// <summary>
        /// Verifies callback release while its original event publisher remains retained.
        /// </summary>
        /// <param name="candidate">The retired or aborted historian candidate.</param>
        private static void AssertHistorianCaptureReleased(StagedHistorianState candidate)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(candidate.CaptureHandlerTarget.TryGetTarget(out _), Is.False,
                "Cleanup must release the builder-owned capture target while its event publisher remains alive.");
            GC.KeepAlive(candidate.Variable);
        }

        private static void AssertReadinessManager(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant,
            bool synchronousFactory)
        {
            if (synchronousFactory)
            {
                Assert.That(registration.NodeManager, Is.TypeOf<AsyncNodeManagerAdapter>());
                Assert.That(registration.NodeManager.SyncNodeManager, Is.TypeOf<SyncNodeManagerAdapter>());
            }
            else
            {
                Assert.That(registration.NodeManager, Is.SameAs(participant));
            }
        }

        private static async Task<TException> ExpectReadinessExceptionAsync<TException>(Task operation)
            where TException : Exception
        {
            TException? failure = null;
            try
            {
                await operation.WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
            catch (TException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.TypeOf<TException>());
            return RequireLifecycleValue(failure);
        }

        /// <summary>
        /// Removes an owner whose retirement was not transferred before an interrupted readiness assertion.
        /// </summary>
        private static async Task RemovePendingReadinessOwnerAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerRegistration? owner)
        {
            if (owner is not null)
            {
                using var cleanupTimeout = new CancellationTokenSource(s_readinessTimeout);
                await RemoveAndRetireAsync(lifecycle, owner, cleanupTimeout.Token)
                    .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            }
        }

        private async Task RemoveReadinessRegistrationAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager? participant = null,
            bool synchronousFactory = false)
        {
            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, registration, timeout.Token)
                .WaitAsync(s_readinessTimeout).ConfigureAwait(false);
            if (synchronousFactory)
            {
                // The legacy sync facade does not own disposal of its underlying async
                // manager. Release that test-owned resource only after real removal.
                RequireLifecycleValue(participant).Dispose();
            }
        }

        private async Task AssertReadinessCleanupAndRecoveryAsync(
            NodeManagerRegistration registration,
            ReadinessLifecycleNodeManager participant)
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
            await RemoveReadinessRegistrationAsync(registration).ConfigureAwait(false);
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            Assert.That(participant.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(participant.DisposeCount, Is.EqualTo(1));
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(s_readinessTimeout);
            TrackingLifecycleNodeManager? laterManager = null;
            LifecycleResult publicationResult20 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kFirstRegistrationValue,
                    manager => laterManager = manager)), timeout.Token).WaitAsync(
                        s_readinessTimeout).ConfigureAwait(false);
            NodeManagerRegistration later = RequireRegistration(publicationResult20.Registrations[0]);
            Assert.That(later.Id, Is.Not.EqualTo(registration.Id));
            Assert.That(later.Generation, Is.EqualTo(1));
            Assert.That(later.NodeManager, Is.SameAs(laterManager));
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
            await RemoveReadinessRegistrationAsync(later).ConfigureAwait(false);
            AssertLifecycleValue(laterManager);
            Assert.That(laterManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(laterManager.DisposeCount, Is.EqualTo(1));
            Assert.That(participant.ReadinessCount, Is.EqualTo(1));
            Assert.That(GetBranchRegistrations(lifecycle), Is.Empty);
            await AssertReadinessNodeUnknownAsync(kModelNamespaceUri).ConfigureAwait(false);
        }

        private sealed class ReadinessLifecycleNodeManager :
            NodeManagementLifecycleNodeManager,
            INodeManagerReadinessNotification
        {
            public ReadinessLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                int value)
                : base(server, configuration, logger, kModelNamespaceUri, value)
            {
            }

            public Func<CancellationToken, ValueTask>? ReadinessCallback { get; set; }

            public Func<CancellationToken, ValueTask>? DeleteCallback { get; set; }

            public Exception? PreparationFailure { get; set; }

            public int ReadinessCount => Volatile.Read(ref m_readinessCount);

            public int ReadinessCompletedCount => Volatile.Read(ref m_readinessCompletedCount);

            public int DeleteAddressSpaceCount => Volatile.Read(ref m_deleteAddressSpaceCount);

            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public int HistoryUpdateCount => Volatile.Read(ref m_historyUpdateCount);

            public Task DisposalCompleted => m_disposed.Task;

            public ValueTask ReportRootEventAsync(string message, CancellationToken cancellationToken)
            {
                NodeState root = RequireLifecycleValue(Find(new NodeId(kRootNodeId, NamespaceIndexes[0])));
                var occurrence = new BaseEventState(null);
                occurrence.Initialize(SystemContext, root, EventSeverity.Medium, new LocalizedText(message));
                return OnReportEventAsync(SystemContext, root, occurrence, cancellationToken);
            }

            public override ValueTask HistoryUpdateAsync(
                OperationContext context,
                Type detailsType,
                ArrayOf<HistoryUpdateDetails> nodesToUpdate,
                IList<HistoryUpdateResult> results,
                IList<ServiceResult> errors,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_historyUpdateCount);
                return base.HistoryUpdateAsync(
                    context, detailsType, nodesToUpdate, results, errors, cancellationToken);
            }

            public async ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_readinessCount);
                if (ReadinessCallback is not null)
                {
                    await ReadinessCallback(cancellationToken).ConfigureAwait(false);
                }
                Interlocked.Increment(ref m_readinessCompletedCount);
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                if (PreparationFailure is not null)
                {
                    throw PreparationFailure;
                }
            }

            public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteAddressSpaceCount);
                if (DeleteCallback is not null)
                {
                    await DeleteCallback(cancellationToken).ConfigureAwait(false);
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    m_disposed.TrySetResult(true);
                }
            }

            private readonly TaskCompletionSource<bool> m_disposed =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_readinessCount;
            private int m_readinessCompletedCount;
            private int m_deleteAddressSpaceCount;
            private int m_disposeCount;
            private int m_historyUpdateCount;
        }

        /// <summary>
        /// Registers historian state during address-space creation without participating in readiness.
        /// </summary>
        private sealed class AddressSpaceHistorianNodeManager : NodeManagementLifecycleNodeManager
        {
            /// <summary>
            /// Creates a manager that stages the supplied provider during address-space creation.
            /// </summary>
            internal AddressSpaceHistorianNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                IHistorianProvider provider)
                : base(server, configuration, logger, kModelNamespaceUri, kGeneration1Value)
            {
                m_provider = provider;
            }

            /// <summary>
            /// Gets the number of historian registrations made during address-space creation.
            /// </summary>
            internal int AddressSpaceRegistrationCount => Volatile.Read(ref m_addressSpaceRegistrationCount);

            /// <summary>
            /// Gets the number of completed manager disposal callbacks.
            /// </summary>
            internal int DisposeCount => Volatile.Read(ref m_disposeCount);

            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                new HistorianBuilder(SystemContext.Server)
                    .UseProvider(m_provider)
                    .RegisterAsDefault();
                Interlocked.Increment(ref m_addressSpaceRegistrationCount);
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                }
            }

            /// <summary>The provider staged during address-space creation.</summary>
            private readonly IHistorianProvider m_provider;

            /// <summary>The number of historian registrations made during creation.</summary>
            private int m_addressSpaceRegistrationCount;

            /// <summary>The number of completed disposal callbacks.</summary>
            private int m_disposeCount;
        }

        /// <summary>
        /// Historizes a variable using the serving registry default without joining lifecycle readiness.
        /// </summary>
        private sealed class BorrowingDefaultHistorianNodeManager : FluentNodeManagerBase
        {
            /// <summary>
            /// Creates a manager that borrows the serving historian default.
            /// </summary>
            /// <param name="server">The server hosting the manager.</param>
            /// <param name="namespaceUri">The namespace managed by the borrower.</param>
            internal BorrowingDefaultHistorianNodeManager(
                IServerInternal server,
                string namespaceUri = kModelNamespaceUri)
                : base(server, namespaceUri)
            {
            }

            /// <inheritdoc/>
            protected override ValueTask ConfigureAsync(
                INodeManagerBuilder builder,
                CancellationToken cancellationToken)
            {
                builder.AddVariable<int>("Historized").Historize();
                return default;
            }
        }

        private sealed class DeferredFluentReadinessNodeManager :
            FluentNodeManagerBase,
            INodeManagerReadinessNotification
        {
            /// <summary>
            /// Creates a fluent manager with deferred configuration and a readiness callback that does not forward it.
            /// </summary>
            /// <param name="server">The server hosting the manager.</param>
            internal DeferredFluentReadinessNodeManager(IServerInternal server)
                : base(server, kModelNamespaceUri)
            {
            }

            /// <summary>
            /// Gets how many times the deferred configuration callback ran.
            /// </summary>
            internal int DeferredConfigurationCount => Volatile.Read(ref m_deferredConfigurationCount);

            /// <inheritdoc/>
            public ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            /// <inheritdoc/>
            protected override ValueTask ConfigureAsync(
                INodeManagerBuilder builder,
                CancellationToken cancellationToken)
            {
                DeferConfigurationUntilReady(
                    builder,
                    _ =>
                    {
                        Interlocked.Increment(ref m_deferredConfigurationCount);
                        return default;
                    });
                return default;
            }

            private int m_deferredConfigurationCount;
        }

        private sealed record StagedHistorianState(
            IHistorianProviderRegistry LocalRegistry,
            NodeId ExactNode,
            NodeId NamespaceNode,
            NodeId DefaultNode,
            IHistorianProvider? BaselineExactProvider,
            IHistorianProvider? BaselineNamespaceProvider,
            IHistorianProvider? BaselineDefaultProvider,
            LifecycleHistorianProvider NodeProvider,
            LifecycleHistorianProvider NamespaceProvider,
            LifecycleHistorianProvider DefaultProvider,
            BaseDataVariableState Variable,
            WeakReference<object> CaptureHandlerTarget);

        private sealed class LifecycleHistorianProvider :
            HistorianProviderBase,
            IHistorianBulkInsertProvider,
            IDisposable
        {
            public int DisposeCount { get; private set; }

            public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
                HistorianOperationContext context,
                ArrayOf<HistorianDataBatch> batch,
                CancellationToken cancellationToken)
            {
                return new ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>>(
                    Array.Empty<HistorianUpdateOutcome<DataValue>>());
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        /// <summary>
        /// Public lifecycle entry points that own a newly ready generation.
        /// </summary>
        public enum ReadinessChange
        {
            Add,
            Reload,
            ShadowReload,
            ImmediateReload
        }

        /// <summary>
        /// Distinct removal and retired-generation destruction paths.
        /// </summary>
        public enum ReadinessCleanup
        {
            Remove,
            Reload,
            ShadowDrain
        }
    }
}
