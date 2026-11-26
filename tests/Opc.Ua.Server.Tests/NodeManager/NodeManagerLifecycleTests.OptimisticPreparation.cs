// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using static Opc.Ua.Server.Tests.NodeManager.LifecycleInspection;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Independent compatibility additions both complete after their factories capture the same routing view.
        /// </summary>
        [Test]
        public async Task ConcurrentCompatibilityAdditionsCompleteAfterIndependentPublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long originalRevision = lifecycle.Revision;
            var firstCandidates = new List<IAsyncNodeManager>();
            var secondCandidates = new List<IAsyncNodeManager>();
            Task<LifecycleResult> first = CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                CreateFactory(
                kModelNamespaceUri, kFirstRegistrationValue, firstEntered, releaseFirst, firstCandidates)),
                timeout.Token);
            Task<LifecycleResult> second = CommitChangeAsync(lifecycle, LifecycleChange.Add(CreateFactory(
                    kSecondModelNamespaceUri, kSecondRegistrationValue, secondEntered, releaseSecond,
                        secondCandidates)), timeout.Token);
            try
            {
                await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision));
                releaseFirst.TrySetResult(true);
                NodeManagerRegistration firstRegistration = RequireRegistration(
                    (await first.WaitAsync(timeout.Token).ConfigureAwait(false)).Registrations[0]);
                Assert.That(releaseSecond.Task.IsCompleted, Is.False);
                releaseSecond.TrySetResult(true);
                await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(
                    async () => await second.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(lifecycle.Registrations.ToList(), Does.Contain(firstRegistration));
                    Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision + 1));
                    Assert.That(firstCandidates, Has.Count.EqualTo(1));
                    Assert.That(secondCandidates, Has.Count.EqualTo(1));
                    Assert.That(lifecycle.Registrations.Contains(value =>
                        ReferenceEquals(value.NodeManager, secondCandidates[0])), Is.False);
                }
                await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.LessThan(0));
            }
            finally
            {
                releaseFirst.TrySetResult(true);
                releaseSecond.TrySetResult(true);
                await first.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (second.IsCompleted)
                {
                    _ = second.Exception;
                }
                else
                {
                    await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(
                        async () => await second.WaitAsync(timeout.Token)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                }
            }

            IAsyncNodeManagerFactory CreateFactory(
                string uri,
                int value,
                TaskCompletionSource<bool> entered,
                TaskCompletionSource<bool> release,
                List<IAsyncNodeManager> candidates)
            {
                var original = new RuntimeNodeSetNodeManagerFactory(CreateOptions(uri, value));
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(owner => owner.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .Returns(async (
                        IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                        IAsyncNodeManager candidate = await original.CreateAsync(server, configuration, token)
                            .ConfigureAwait(false);
                        candidates.Add(candidate);
                        return candidate;
                    });
                return factory.Object;
            }
        }

        /// <summary>
        /// A compatibility removal preserves progress when an unrelated addition publishes after its preparation.
        /// </summary>
        [Test]
        public async Task CompatibilityRemovalCompletesAfterIndependentPublicationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration original = RequireRegistration((await lifecycle.ApplyAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false)).Registrations[0]);
            await using ILifecycleWriter writer = await Capture(lifecycle, original)
                .BeginAsync(timeout.Token).ConfigureAwait(false);
            await using IPreparedChanges prepared = await writer.PrepareAsync(
                [LifecycleChange.Remove(original.Slot)], timeout.Token).ConfigureAwait(false);
            LifecycleResult independent = await lifecycle.ApplyAsync([LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kSecondModelNamespaceUri,
                    kSecondRegistrationValue)))], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(prepared.IsCommitted, Is.False);
            Assert.That(lifecycle.Registrations.ToList(), Does.Contain(original));
            Assert.That(
                lifecycle.Registrations.ToList(), Does.Contain(RequireRegistration(independent.Registrations[0])));
            Assert.That(lifecycle.Revision, Is.EqualTo(independent.Revision));
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);
        }

        /// <summary>
        /// Compatibility retries rebuild aborted private candidates for repeated independent publications.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionRepreparesAfterRepeatedIndependentPublicationsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var inner = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var factory = new CountingNodeManagerFactory([kModelNamespaceUri], inner.CreateAsync);
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory)], timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration candidate = RequireRegistration(prepared.Registrations[0]);
            var independent = new List<NodeManagerRegistration>();
            for (int index = 0; index < 2; index++)
            {
                LifecycleResult added = await lifecycle.ApplyAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri + index, kSecondRegistrationValue)))], timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(added.Issues, Is.Empty);
                independent.Add(RequireRegistration(added.Registrations[0]));
            }
            long revision = lifecycle.Revision;
            int decisions = 0;
            await Assert.ThrowsAsync<NodeManagerRoutingConflictException>(async () => await prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(decisions, Is.Zero);
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(candidate.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            foreach (NodeManagerRegistration registration in independent)
            {
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(registration));
            }
            LifecycleResult retried = await lifecycle.ApplyAsync([LifecycleChange.Add(factory)], timeout.Token)
                .ConfigureAwait(false);
            Assert.That(retried.Issues, Is.Empty);
            Assert.That(factory.CreateCount, Is.EqualTo(2));
            Assert.That(retried.Revision, Is.EqualTo(revision + 1));
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
        }

        /// <summary>
        /// A serving namespace revision change during candidate creation is retried by the compatibility API.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionRetriesAfterServingNamespaceChangesDuringPreparationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int factoryCalls = 0;
            var innerFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(
                It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        _ = m_server.CurrentInstance.NamespaceUris.GetIndexOrAppend(
                            kSecondModelNamespaceUri + ":CompatibilityRetry");
                    }
                    return innerFactory.CreateAsync(server, configuration, token);
                });

            await Assert.ThrowsAsync<OptimisticPublicationConflictException>(async () => await lifecycle.ApplyAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(factoryCalls, Is.EqualTo(1));
            LifecycleResult publicationResult1 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(factory.Object), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult1.Registrations[0]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(factoryCalls, Is.EqualTo(2));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(registration));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(
                    kSecondModelNamespaceUri + ":CompatibilityRetry"), Is.GreaterThanOrEqualTo(0));
            }
        }

        /// <summary>
        /// A bounded compatibility retry stops replaying a factory that repeatedly publishes nested changes.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionStopsAfterRepeatedNestedPublicationsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int registrationCount = lifecycle.Registrations.Count;
            int factoryCalls = 0;
            int nestedPublications = 0;
            var innerFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(
                It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken token) =>
                {
                    int attempt = Interlocked.Increment(ref factoryCalls);
                    RequireRegistration(
                        (await CommitChangeAsync(
                        lifecycle, LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateOptions(
                            kSecondModelNamespaceUri + ":Nested" + attempt,
                            kSecondRegistrationValue))), token).ConfigureAwait(false)).Registrations[0]);
                    Interlocked.Increment(ref nestedPublications);
                    return await innerFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
                });

            await Assert.ThatAsync(
                () => CommitChangeAsync(lifecycle, LifecycleChange.Add(factory.Object), timeout.Token),
                Throws.TypeOf<NodeManagerRoutingConflictException>()
                    .With.Message.EqualTo("The address space changed while the batch was prepared."))
                .ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(factoryCalls, Is.EqualTo(1));
                Assert.That(nestedPublications, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(registrationCount + 1));
            }
        }

        /// <summary>
        /// A serving type-tree revision change during candidate creation is retried by the compatibility API.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionRetriesAfterServingTypeTreeChangesDuringPreparationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int factoryCalls = 0;
            var innerFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(
                It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        m_server.CurrentInstance.TypeTree.AddSubtype(
                            new NodeId(99001), ObjectTypeIds.BaseObjectType);
                    }
                    return innerFactory.CreateAsync(server, configuration, token);
                });

            await Assert.ThrowsAsync<OptimisticPublicationConflictException>(async () => await lifecycle.ApplyAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(factoryCalls, Is.EqualTo(1));
            LifecycleResult publicationResult2 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(factory.Object), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult2.Registrations[0]);

            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(lifecycle.Registrations.ToList(), Does.Contain(registration));
        }

        /// <summary>
        /// A serving encodeable-factory revision change during candidate creation is retried by the compatibility API.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionRetriesAfterServingFactoryChangesDuringPreparationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            int factoryCalls = 0;
            var innerFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(owner => owner.CreateAsync(
                It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref factoryCalls) == 1)
                    {
                        IEncodeableFactory servingFactory = m_server.CurrentInstance.Factory;
                        if (!servingFactory.TryGetEncodeableType(
                            DataTypeIds.Range, out IEncodeableType? rangeType) ||
                            rangeType is null)
                        {
                            throw new InvalidOperationException("The Range encodeable type is not registered.");
                        }
                        servingFactory.Builder.AddEncodeableType(new ExpandedNodeId(99002), rangeType).Commit();
                    }
                    return innerFactory.CreateAsync(server, configuration, token);
                });

            await Assert.ThrowsAsync<OptimisticPublicationConflictException>(async () => await lifecycle.ApplyAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(factoryCalls, Is.EqualTo(1));
            LifecycleResult publicationResult3 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(factory.Object), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration registration = RequireRegistration(publicationResult3.Registrations[0]);

            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(lifecycle.Registrations.ToList(), Does.Contain(registration));
        }

        /// <summary>
        /// A nested explicit conflict from a reference owner is not a conflict of the current compatibility batch.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityCommitDoesNotRetryUnrelatedNestedReferenceConflictAsync(bool sameView)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(kModelNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner.Object);
            RequireRegistration(
                (await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                ownerFactory.Object), timeout.Token).ConfigureAwait(false)).Registrations[0]);
            await using IPreparedChanges nested = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri + ":Nested", kSecondRegistrationValue)))], timeout.Token)
                .ConfigureAwait(false);
            if (!sameView)
            {
                await PublishIndependentAsync(timeout.Token).ConfigureAwait(false);
            }
            long expectedRevision = lifecycle.Revision + (sameView ? 1 : 0);
            var source = new NodeId(9911, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            int ownerCalls = 0;
            int nestedDecisions = 0;
            InvalidOperationException? nestedFailure = null;
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) => ResolveNestedAsync(token));
            var candidates = new List<IAsyncNodeManager>();
            var candidateGenerations = new List<NodeManagerGeneration>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken _) =>
                {
                    Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
                    candidate.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                        .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                        {
                            candidateGenerations.Add(NodeManagerGenerationRegistry.GetOrCreate(candidate.Object));
                            references.Add(source,
                                [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9912))]);
                        })
                        .Returns(default(ValueTask));
                    candidates.Add(candidate.Object);
                    return new ValueTask<IAsyncNodeManager>(candidate.Object);
                });
            Exception? observed = await Assert.CatchAsync<Exception>(
                () => CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(factory.Object), timeout.Token)).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(nestedFailure, Is.TypeOf<NodeManagerRoutingConflictException>());
                Assert.That(nestedFailure?.InnerException, Is.Null);
                Assert.That(observed, Is.SameAs(nestedFailure));
                Assert.That(ownerCalls, Is.EqualTo(1));
                Assert.That(nestedDecisions, Is.Zero);
                Assert.That(nested.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(expectedRevision));
                Assert.That(candidates, Has.Count.EqualTo(1));
                Assert.That(candidateGenerations, Has.Count.EqualTo(candidates.Count));
                foreach (NodeManagerGeneration generation in candidateGenerations)
                {
                    Assert.That(generation.Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Aborted));
                }
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);

            async ValueTask<object?> ResolveNestedAsync(CancellationToken token)
            {
                if (++ownerCalls > 1)
                {
                    throw new SentinelException();
                }
                if (sameView)
                {
                    await PublishIndependentAsync(token).ConfigureAwait(false);
                }
                try
                {
                    await nested.CommitAsync(_ =>
                    {
                        nestedDecisions++;
                        return default;
                    }, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException failure)
                {
                    nestedFailure = failure;
                    throw;
                }
                return null!;
            }

            async ValueTask PublishIndependentAsync(CancellationToken token)
            {
                RequireRegistration(
                    (await CommitChangeAsync(lifecycle, LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(
                        kSecondModelNamespaceUri + ":Independent", kSecondRegistrationValue))), token)
                        .ConfigureAwait(false)).Registrations[0]);
            }
        }

        /// <summary>
        /// Private preparation owns a refusing generation until its sole publication or abort transition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CandidateGenerationRemainsPreparedUntilPublicationOrAbortAsync(bool publish)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            TrackingLifecycleNodeManager? manager = null;
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges prepared = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(CreateTrackingNodeManagementFactory(
                    kGeneration1Value, created => manager = created))], timeout.Token).ConfigureAwait(false);
            TrackingLifecycleNodeManager candidate = RequireLifecycleValue(manager);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(candidate);
            Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
            bool admitted = generation.Gate.TryEnter(out int stripe);
            if (admitted)
            {
                generation.Gate.Exit(stripe);
            }
            Assert.That(admitted, Is.False);
            Assert.That(generation.Slot.TryGetCurrent(out _), Is.False);
            if (publish)
            {
                LifecycleResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(RequireRegistration(result.Registrations[0]).Identity, Is.SameAs(generation));
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                admitted = generation.Gate.TryEnter(out stripe);
                if (admitted)
                {
                    generation.Gate.Exit(stripe);
                }
                Assert.That(admitted, Is.True);
            }
            else
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
                Assert.That(generation.Gate.State, Is.EqualTo(NodeManagerGenerationState.Aborted));
                Assert.That(generation.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(candidate.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(candidate.DisposeCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Reference discovery permits independent publication and releases its owner on every failure path.
        /// </summary>
        [TestCase("Stale")]
        [TestCase("Retire")]
        [TestCase("Cancel")]
        [TestCase("Failure")]
        public async Task ReferenceDiscoveryDoesNotHoldWriterOrLeakOwnerAdmissionAsync(string outcome)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(kModelNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner.Object);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            await using IPreparedChanges initial = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(ownerFactory.Object)], timeout.Token).ConfigureAwait(false);
            LifecycleResult original = await initial.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            var source = new NodeId(9911, (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri));
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) => ResolveAsync(token));
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                    references.Add(source,
                        [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9912))]))
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(candidate.Object);
            await using IPreparedChanges publication = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            Task<LifecycleResult> pending = publication.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, cancellation.Token).AsTask();
            LifecycleResult? independentResult = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                LifecycleChange change = outcome == "Stale"
                    ? LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri + ":Independent", kSecondRegistrationValue)))
                    : LifecycleChange.Remove(original.Registrations[0].Slot);
                await using IPreparedChanges independent =
                    await lifecycle.PrepareAsync([change], timeout.Token).ConfigureAwait(false);
                independentResult = await independent.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
                if (outcome != "Stale")
                {
                    Assert.That(independentResult.Retired.IsCompleted, Is.False,
                        "Reference discovery must keep its admitted owner alive.");
                    owner.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
                }
            }
            finally
            {
                if (outcome == "Cancel")
                {
                    cancellation.Cancel();
                }
                release.TrySetResult(true);
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    outcome == "Cancel" ? Throws.InstanceOf<OperationCanceledException>() :
                    outcome == "Failure" ? Throws.TypeOf<SentinelException>() :
                    Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
            }
            LifecycleResult committed = RequireLifecycleValue(independentResult);
            await committed.Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(publication.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            owner.Verify(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()), Times.Once);

            async ValueTask<object?> ResolveAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                if (outcome == "Failure")
                {
                    throw new SentinelException();
                }
                return null!;
            }
        }

        /// <summary>
        /// Transition discovery runs without the writer and revalidates its view before a durable decision.
        /// </summary>
        [Test]
        public async Task MonitoredItemPreparationAllowsIndependentPublicationAndRejectsStaleCommitAsync()
        {
            Task exactWriter1Disposal = Task.CompletedTask;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Mock<IAsyncNodeManager> original = CreateLifecycleNodeManager(kModelNamespaceUri);
                original.As<INodeManagerMonitoredItemSource>();
                original.As<INodeManagerReloadParticipant>()
                    .Setup(value => value.PrepareReloadAsync(
                        It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                Mock<IAsyncNodeManager> replacement = CreateLifecycleNodeManager(kModelNamespaceUri);
                Mock<INodeManagerMonitoredItemSource> incoming = replacement.As<INodeManagerMonitoredItemSource>();
                incoming.Setup(value => value.ValidateSourceAsync(
                    It.IsAny<IMonitoredItem>(), It.IsAny<CancellationToken>()))
                    .Returns((IMonitoredItem _, CancellationToken token) => CheckCompatibilityAsync(token));
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(original.Object);
                INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
                await using IPreparedChanges addition = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
                LifecycleResult initial = await addition.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                NodeManagerGeneration generation = RequireRegistration(initial.Registrations[0]).Identity;
                var monitoredItem = new Mock<IMonitoredItem>();
                monitoredItem.SetupGet(value => value.NodeManager).Returns(original.Object);
                monitoredItem.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(replacement.Object);
                await using ILifecycleWriter exactWriter1 = await Capture(
                    m_server.NodeManagerLifecycle, initial.Registrations[0]).BeginAsync(
                    timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges reload = await exactWriter1.PrepareAsync(
                    [LifecycleChange.Replace(initial.Registrations[0].Slot, factory.Object)], timeout.Token)
                    .ConfigureAwait(false);
                generation.MonitoredItems[monitoredItem.Object] = new Mock<ISubscription>().Object;
                int decisions = 0;
                Task<LifecycleResult> pending = reload.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).AsTask();
                exactWriter1Disposal = exactWriter1.DisposeAsync().AsTask();
                LifecycleResult? independentResult = null;
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    await using IPreparedChanges independent = await lifecycle.PrepareAsync(
                        [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                            CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                        .ConfigureAwait(false);
                    independentResult = await independent.CommitAsync(
                        _ => default, timeout.Token).ConfigureAwait(false);
                    Assert.That(release.Task.IsCompleted, Is.False);
                }
                finally
                {
                    generation.MonitoredItems.TryRemove(monitoredItem.Object, out _);
                    release.TrySetResult(true);
                    await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                        Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(decisions, Is.Zero);
                    Assert.That(reload.IsCommitted, Is.False);
                    Assert.That(lifecycle.Revision, Is.EqualTo(RequireLifecycleValue(independentResult).Revision));
                    Assert.That(
                        lifecycle.Registrations.ToList(), Does.Contain(RequireRegistration(initial.Registrations[0])));
                }
                incoming.Verify(value => value.ValidateSourceAsync(
                    monitoredItem.Object, It.IsAny<CancellationToken>()), Times.Once);

                async ValueTask<ServiceResult> CheckCompatibilityAsync(CancellationToken token)
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                    return ServiceResult.Good;
                }
            }
            finally
            {
                await exactWriter1Disposal.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A factory can await a nested publication; the resulting outer conflict never reruns application code.
        /// </summary>
        [Test]
        public async Task CandidateFactoryCanAwaitNestedPublicationBeforeStaleCommitIsRejectedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            LifecycleResult? nestedResult = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateWithNestedPublicationAsync(server, configuration, token));
            await using IPreparedChanges outer = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            LifecycleResult committed = RequireLifecycleValue(nestedResult);
            int decisions = 0;
            await Assert.ThatAsync(() => outer.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
            await outer.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(outer.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(
                    lifecycle.Registrations.ToList(), Does.Contain(RequireRegistration(committed.Registrations[0])));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateWithNestedPublicationAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                await using IPreparedChanges nested = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], token)
                    .ConfigureAwait(false);
                nestedResult = await nested.CommitAsync(_ => default, token).ConfigureAwait(false);
                Assert.That(nestedResult.CleanupFailure, Is.Null);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Failed or cancelled private preparation releases its current-generation admission for physical retirement.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPrivatePreparationReleasesRetiringCurrentGenerationAsync(bool cancel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            LifecycleResult publicationResult4 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                new RuntimeNodeSetNodeManagerFactory(
                CreateGenerationOptions(generation: 1))), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration current = RequireRegistration(publicationResult4.Registrations[0]);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken token) =>
                    FailPreparationAsync(token));
            Task<IPreparedChanges> pending = lifecycle.PrepareAsync(
                [LifecycleChange.Replace(current.Slot, factory.Object)], preparationCancellation.Token).AsTask();
            LifecycleResult? removed = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using ILifecycleWriter exactWriter2 = await Capture(
                    m_server.NodeManagerLifecycle, current).BeginAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedChanges removal = await exactWriter2.PrepareAsync(
                    [LifecycleChange.Remove(current.Slot)], timeout.Token).ConfigureAwait(false);
                removed = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                await exactWriter2.DisposeAsync().ConfigureAwait(false);
                Assert.That(removed.CleanupFailure, Is.Null);
                Assert.That(removed.Retired.IsCompleted, Is.False,
                    "The current participant must remain alive while its admitted preparation callback executes.");
            }
            finally
            {
                if (cancel)
                {
                    preparationCancellation.Cancel();
                }
                else
                {
                    release.TrySetResult(true);
                }
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.TypeOf<SentinelException>())
                    .ConfigureAwait(false);
            }
            await RequireLifecycleValue(removed).Retired.WaitAsync(timeout.Token).ConfigureAwait(false);
            Assert.That(lifecycle.Registrations.ToList(), Does.Not.Contain(current));

            async ValueTask<IAsyncNodeManager> FailPreparationAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                throw new SentinelException();
            }
        }

        /// <summary>
        /// Candidate callbacks do not serialize unrelated publication, and a stale candidate is never rebased.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PrivatePreparationAllowsIndependentPublicationAndRejectsStaleCommitAsync(
            bool blockAddressSpace)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalFactory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(kModelNamespaceUri, kFirstRegistrationValue));
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Returns((IDictionary<NodeId, IList<IReference>> _, CancellationToken token) =>
                    WaitForReleaseAsync(token));
            var delayedFactory = new Mock<IAsyncNodeManagerFactory>();
            int factoryCalls = 0;
            delayedFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateDelayedAsync(server, configuration, token));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            Task<IPreparedChanges> pending = lifecycle.PrepareAsync(
                [LifecycleChange.Add(delayedFactory.Object)], timeout.Token).AsTask();
            IPreparedChanges? first = null;
            LifecycleResult? published = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using var independentTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using IPreparedChanges independent = await lifecycle.PrepareAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(
                            kSecondModelNamespaceUri, kSecondRegistrationValue)))], independentTimeout.Token)
                            .ConfigureAwait(false);
                published = await independent.CommitAsync(_ => default, independentTimeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False,
                    "The second publication must finish while the first factory remains blocked.");
                Assert.That(published.CleanupFailure, Is.Null);
            }
            finally
            {
                release.TrySetResult(true);
                first = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (published is null)
                {
                    await first.DisposeAsync().ConfigureAwait(false);
                }
            }

            IPreparedChanges stale = RequireLifecycleValue(first);
            LifecycleResult committed = RequireLifecycleValue(published);
            await using (stale.ConfigureAwait(false))
            {
                int decisions = 0;
                await Assert.ThatAsync(() => stale.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).AsTask(), Throws.TypeOf<NodeManagerRoutingConflictException>()).ConfigureAwait(false);
                Assert.That(stale.IsCommitted, Is.False);
                Assert.That(decisions, Is.Zero, "A conflicting preparation must fail before any durable decision.");
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factoryCalls, Is.EqualTo(1), "Conflict detection must not silently rerun a factory.");
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(
                    lifecycle.Registrations.ToList(), Does.Contain(RequireRegistration(committed.Registrations[0])));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateDelayedAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                factoryCalls++;
                if (blockAddressSpace)
                {
                    return candidate.Object;
                }
                await WaitForReleaseAsync(token).ConfigureAwait(false);
                return await originalFactory.CreateAsync(server, configuration, token).ConfigureAwait(false);
            }

            async ValueTask WaitForReleaseAsync(CancellationToken token)
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A collected nested owner cannot cause its already-bound routing conflict to be rebound to the outer batch.
        /// </summary>
        /// <exception cref="SentinelException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NodeManagerRoutingConflictException"></exception>
        [Test]
        public async Task CollectedNestedRoutingConflictDoesNotRebindAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(kModelNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner.Object);
            LifecycleResult publicationResult5 = await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(ownerFactory.Object), timeout.Token).ConfigureAwait(false);
            NodeManagerRegistration ownerRegistration = RequireRegistration(publicationResult5.Registrations[0]);

            int ownerNamespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(ownerNamespaceIndex, Is.GreaterThanOrEqualTo(0));
            var source = new NodeId(9911, (ushort)ownerNamespaceIndex);
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                    references.Add(source,
                        [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9912))]))
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(candidate.Object);

            int ownerCalls = 0;
            int outerDecisions = 0;
            int[] nestedDecisions = [0];
            NodeManagerRoutingTable.RoutingSnapshot?[] independentSnapshotCapture = [null];
            NodeManagerRoutingConflictException? nestedMarker = null;
            WeakReference<IPreparedChanges>? nestedObserver = null;
            NodeManagerRoutingTable.RoutingSnapshot? publishedSnapshot = null;
            long publishedRevision = -1;
            int initialRegistrationCount = lifecycle.Registrations.Count;
            NodeManagerRoutingTable.RoutingSnapshot snapshot = host.RoutingRevision;
            long initialRevision = snapshot.Revision;
            Assert.That(lifecycle.Revision, Is.EqualTo(initialRevision));
            Assert.That(host.RoutingRevision, Is.SameAs(snapshot));

            await using IPreparedChanges outer = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
            Assert.That(lifecycle.Revision, Is.EqualTo(initialRevision));
            NodeManagerGeneration candidateGeneration = NodeManagerGenerationRegistry.GetOrCreate(candidate.Object);
            Assert.That(candidateGeneration.Gate.State,
                Is.EqualTo(NodeManagerGenerationState.Prepared));

            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Returns((NodeId _, CancellationToken token) => ResolveNestedAsync(token));
            Exception? observed = await Assert.CatchAsync<Exception>(() => outer.CommitAsync(_ =>
            {
                Interlocked.Increment(ref outerDecisions);
                return default;
            }, timeout.Token).AsTask()).ConfigureAwait(false);
            await outer.DisposeAsync().ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(nestedMarker, Is.Not.Null);
                Assert.That(observed, Is.SameAs(nestedMarker));
                Assert.That(nestedObserver is not null && !IsNestedRoutingConflictOwnerAlive(nestedObserver), Is.True);
                Assert.That(nestedMarker?.RoutingRevision, Is.SameAs(snapshot));
                Assert.That(ownerCalls, Is.EqualTo(1));
                Assert.That(nestedDecisions[0], Is.Zero);
                Assert.That(outerDecisions, Is.Zero);
                Assert.That(outer.IsCommitted, Is.False);
                Assert.That(candidateGeneration.Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Aborted));
                Assert.That(publishedSnapshot, Is.Not.Null);
                Assert.That(publishedSnapshot, Is.Not.SameAs(snapshot));
                Assert.That(publishedRevision, Is.EqualTo(initialRevision + 1));
                Assert.That(publishedSnapshot?.Revision, Is.EqualTo(publishedRevision));
                Assert.That(snapshot.Revision, Is.EqualTo(initialRevision));
                Assert.That(host.RoutingRevision, Is.SameAs(publishedSnapshot));
                Assert.That(lifecycle.Revision, Is.EqualTo(publishedRevision));
                Assert.That(lifecycle.Registrations.Count, Is.EqualTo(initialRegistrationCount + 1));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(ownerRegistration));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri + ":Nested"),
                    Is.EqualTo(-1));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri + ":Independent"),
                    Is.GreaterThanOrEqualTo(0));
            }
            owner.Verify(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()), Times.Once);
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);
            candidate.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Once);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            GC.KeepAlive(snapshot);
            GC.KeepAlive(nestedMarker);
            GC.KeepAlive(outer);

            async ValueTask<object?> ResolveNestedAsync(CancellationToken token)
            {
                if (Interlocked.Increment(ref ownerCalls) != 1)
                {
                    throw new SentinelException();
                }
                Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
                (NodeManagerRoutingConflictException marker, WeakReference<IPreparedChanges> observer) =
                    await CreateCollectibleNestedRoutingConflictAsync(
                        lifecycle, host, snapshot, outer, nestedDecisions, independentSnapshotCapture, token)
                        .ConfigureAwait(false);

                // Give the completed helper's awaiter a chance to leave this callback's frame.
                await Task.Yield();
                nestedMarker = marker;
                nestedObserver = observer;
                NodeManagerRoutingTable.RoutingSnapshot independent = independentSnapshotCapture[0] ??
                    throw new InvalidOperationException("The independent publication was not captured.");
                long independentRevision = independent.Revision;
                publishedSnapshot = independent;
                publishedRevision = independentRevision;
                Assert.That(independent, Is.Not.SameAs(snapshot));
                Assert.That(independentRevision, Is.EqualTo(initialRevision + 1));
                Assert.That(host.RoutingRevision, Is.SameAs(independent));
                Assert.That(lifecycle.Revision, Is.EqualTo(independentRevision));

                CollectNestedRoutingConflictOwner(observer);
                Assert.That(IsNestedRoutingConflictOwnerAlive(observer), Is.False,
                    "The native nested owner must be collected before its routing diagnostic is thrown.");
                Assert.That(marker.InnerException, Is.Null,
                    "Native routing provenance does not wrap a second prepared-owner conflict.");
                Assert.That(marker.RoutingRevision, Is.SameAs(snapshot));
                Assert.That(host.RoutingRevision, Is.SameAs(independent));
                Assert.That(lifecycle.Revision, Is.EqualTo(independentRevision));
                GC.KeepAlive(snapshot);
                GC.KeepAlive(marker);
                throw marker;
            }
        }

        /// <summary>
        /// A foreign routing diagnostic must preserve its original view without rebinding or replay.
        /// </summary>
        [Test]
        public async Task UnboundRoutingConflictForDifferentViewIsNotReboundAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var host = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            NodeManagerRoutingTable.RoutingSnapshot foreignSnapshot = host.RoutingRevision;
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(kModelNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(owner.Object);
            RequireRegistration(
                (await CommitChangeAsync(
                lifecycle, LifecycleChange.Add(
                ownerFactory.Object), timeout.Token).ConfigureAwait(false)).Registrations[0]);

            NodeManagerRoutingTable.RoutingSnapshot snapshot = host.RoutingRevision;
            Assert.That(snapshot, Is.Not.SameAs(foreignSnapshot));
            int ownerNamespaceIndex = m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(ownerNamespaceIndex, Is.GreaterThanOrEqualTo(0));
            var source = new NodeId(9913, (ushort)ownerNamespaceIndex);
            var foreignFailure = new NodeManagerRoutingConflictException("Unrelated routing view.", foreignSnapshot);
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Throws(foreignFailure);

            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                    references.Add(source,
                        [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9914))]))
                .Returns(default(ValueTask));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(candidate.Object);
            await using IPreparedChanges outer = await lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerGeneration candidateGeneration = NodeManagerGenerationRegistry.GetOrCreate(candidate.Object);
            Assert.That(host.RoutingRevision, Is.SameAs(snapshot));

            int decisions = 0;
            Exception? observed = await Assert.CatchAsync<Exception>(() => outer.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask()).ConfigureAwait(false);
            await outer.DisposeAsync().ConfigureAwait(false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(observed, Is.SameAs(foreignFailure));
                Assert.That(foreignFailure.InnerException, Is.Null);
                Assert.That(foreignFailure.RoutingRevision, Is.SameAs(foreignSnapshot));
                Assert.That(decisions, Is.Zero);
                Assert.That(outer.IsCommitted, Is.False);
                Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
                Assert.That(lifecycle.Revision, Is.EqualTo(snapshot.Revision));
                Assert.That(candidateGeneration.Gate.State,
                    Is.EqualTo(NodeManagerGenerationState.Aborted));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            owner.Verify(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()), Times.Once);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            GC.KeepAlive(foreignSnapshot);
            GC.KeepAlive(foreignFailure);
        }

        /// <summary>
        /// Produces a native nested routing conflict without returning its owner or completion tasks.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static async Task<(
            NodeManagerRoutingConflictException Marker,
            WeakReference<IPreparedChanges> Observer)> CreateCollectibleNestedRoutingConflictAsync(
            INodeManagerLifecycle lifecycle,
            IDynamicNodeManagerBatchHost host,
            NodeManagerRoutingTable.RoutingSnapshot snapshot,
            IPreparedChanges outer,
            int[] nestedDecisions,
            NodeManagerRoutingTable.RoutingSnapshot?[] independentSnapshotCapture,
            CancellationToken cancellationToken)
        {
            Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
            ValueTask<IPreparedChanges> preparation = lifecycle.PrepareAsync(
                [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(
                        kSecondModelNamespaceUri + ":Nested", kSecondRegistrationValue)))], cancellationToken);
            IPreparedChanges? nested = await preparation.ConfigureAwait(false);
            preparation = default;
            var observer = new WeakReference<IPreparedChanges>(nested);
            NodeManagerRoutingConflictException? marker = null;
            NodeManagerRoutingTable.RoutingSnapshot? independentSnapshot = null;
            try
            {
                Assert.That(host.RoutingRevision, Is.SameAs(snapshot));
                Assert.That(ReferenceEquals(nested, outer), Is.False);
                LifecycleResult publicationResult6 = await CommitChangeAsync(
                    lifecycle, LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(
                            kSecondModelNamespaceUri + ":Independent", kSecondRegistrationValue))),
                            cancellationToken).ConfigureAwait(false);
                NodeManagerRegistration independent = RequireRegistration(publicationResult6.Registrations[0]);
                independentSnapshot = host.RoutingRevision;
                independentSnapshotCapture[0] = independentSnapshot;
                Assert.That(independentSnapshot, Is.Not.SameAs(snapshot));
                Assert.That(independentSnapshot.Revision, Is.EqualTo(snapshot.Revision + 1));
                Assert.That(lifecycle.Revision, Is.EqualTo(independentSnapshot.Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(independent));

                ValueTask<LifecycleResult> commit = nested.CommitAsync(_ =>
                {
                    Interlocked.Increment(ref nestedDecisions[0]);
                    return default;
                }, cancellationToken);
                try
                {
                    await commit.ConfigureAwait(false);
                    Assert.Fail("The independently superseded nested batch must fail before its decision.");
                }
                catch (InvalidOperationException failure)
                {
                    Assert.That(failure, Is.TypeOf<NodeManagerRoutingConflictException>());
                    Assert.That(failure.InnerException, Is.Null);
                    marker = (NodeManagerRoutingConflictException)failure;
                    AssertNestedRoutingConflictOwnership(marker, nested, snapshot);
                    Assert.That(nested.IsCommitted, Is.False);
                }
                finally
                {
                    commit = default;
                }
                Assert.That(nestedDecisions[0], Is.Zero);
                Assert.That(host.RoutingRevision, Is.SameAs(independentSnapshot));
            }
            finally
            {
                ValueTask disposal = default;
                try
                {
                    disposal = nested.DisposeAsync();
                    nested = null;
                    await disposal.ConfigureAwait(false);
                }
                finally
                {
                    // This local is hoisted by the async state machine; NoInlining alone does not release it.
                    nested = null;
                    disposal = default;
                }
            }

            // Leave no nested owner, await-using resource, or owner-bearing task in the returned result.
            await Task.Yield();
            Assert.That(host.RoutingRevision, Is.SameAs(independentSnapshot));
            Assert.That(lifecycle.Revision, Is.EqualTo(snapshot.Revision + 1));
            GC.KeepAlive(snapshot);
            GC.KeepAlive(outer);
            return (marker!, observer);
        }

        /// <summary>
        /// Checks the live owner in a frame that cannot retain it during later collection.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void AssertNestedRoutingConflictOwnership(
            NodeManagerRoutingConflictException marker,
            IPreparedChanges nested,
            NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            Assert.That(nested.IsCommitted, Is.False);
            Assert.That(marker.RoutingRevision, Is.SameAs(snapshot));
            Assert.That(marker.InnerException, Is.Null);
            GC.KeepAlive(nested);
        }

        /// <summary>
        /// Performs bounded full collections without retaining the observed batch in a polling frame.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void CollectNestedRoutingConflictOwner(WeakReference<IPreparedChanges> observer)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                CollectLifecycleOwnershipTargets();
                if (!IsNestedRoutingConflictOwnerAlive(observer))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Checks whether the nested batch is still alive without retaining it during collection.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool IsNestedRoutingConflictOwnerAlive(WeakReference<IPreparedChanges> observer)
        {
            return observer.TryGetTarget(out _);
        }
    }
}
