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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

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
            Task<NodeManagerRegistration> first = lifecycle.AddAsync(
                CreateFactory(kModelNamespaceUri, kFirstRegistrationValue, firstEntered, releaseFirst, firstCandidates),
                timeout.Token)
                .AsTask();
            Task<NodeManagerRegistration> second = lifecycle.AddAsync(
                CreateFactory(
                    kSecondModelNamespaceUri, kSecondRegistrationValue, secondEntered, releaseSecond, secondCandidates),
                timeout.Token).AsTask();
            try
            {
                await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision));
                releaseFirst.TrySetResult(true);
                NodeManagerRegistration firstRegistration = await first.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(releaseSecond.Task.IsCompleted, Is.False);
                releaseSecond.TrySetResult(true);
                NodeManagerRegistration secondRegistration = await second.WaitAsync(timeout.Token)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(lifecycle.Registrations.ToList(), Does.Contain(firstRegistration));
                    Assert.That(lifecycle.Registrations.ToList(), Does.Contain(secondRegistration));
                    Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision + 2));
                    Assert.That(firstCandidates, Has.Count.EqualTo(1));
                    Assert.That(secondCandidates, Has.Count.EqualTo(2));
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(secondCandidates[0]).Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Aborted));
                    Assert.That(secondRegistration.Identity,
                        Is.SameAs(NodeManagerGenerationRegistry.GetOrCreate(secondCandidates[1])));
                }
                await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);
            }
            finally
            {
                releaseFirst.TrySetResult(true);
                releaseSecond.TrySetResult(true);
                await Task.WhenAll(first, second).WaitAsync(timeout.Token).ConfigureAwait(false);
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
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            NodeManagerRegistration original = await lifecycle.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)),
                timeout.Token).ConfigureAwait(false);
            int preparations = 0;
            var delayed = new Mock<INodeManagerLifecycle>();
            delayed.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<NodeManagerBatchChange> changes, CancellationToken token) =>
                {
                    IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(changes, token)
                        .ConfigureAwait(false);
                    if (Interlocked.Increment(ref preparations) == 1)
                    {
                        entered.TrySetResult(true);
                        await release.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    return prepared;
                });
            Task removal = delayed.Object.RemoveAsync(original, timeout.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration independent = await lifecycle.AddAsync(
                    new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)), timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(removal.IsCompleted, Is.False);
                release.TrySetResult(true);
                await removal.WaitAsync(timeout.Token).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(lifecycle.Registrations.ToList(), Does.Not.Contain(original));
                    Assert.That(lifecycle.Registrations.ToList(), Does.Contain(independent));
                    Assert.That(preparations, Is.EqualTo(2));
                }
                await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue)
                    .ConfigureAwait(false);
            }
            finally
            {
                release.TrySetResult(true);
                await removal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Compatibility retries rebuild every aborted private candidate rather than imposing a conflict limit.
        /// </summary>
        [Test]
        public async Task CompatibilityAdditionRepreparesAfterRepeatedIndependentPublicationsAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            long originalRevision = lifecycle.Revision;
            var candidates = new List<IPreparedNodeManagerBatch>();
            var independent = new List<NodeManagerRegistration>();
            var proxy = new Mock<INodeManagerLifecycle>();
            proxy.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<NodeManagerBatchChange> changes, CancellationToken token) =>
                {
                    IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(changes, token)
                        .ConfigureAwait(false);
                    candidates.Add(prepared);
                    if (candidates.Count <= 3)
                    {
                        independent.Add(await lifecycle.AddAsync(
                            new RuntimeNodeSetNodeManagerFactory(CreateOptions(
                                kSecondModelNamespaceUri + candidates.Count, kSecondRegistrationValue)), token)
                            .ConfigureAwait(false));
                    }
                    return prepared;
                });
            NodeManagerRegistration registration = await proxy.Object.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)),
                timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(candidates, Has.Count.EqualTo(4));
                Assert.That(lifecycle.Revision, Is.EqualTo(originalRevision + 4));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(registration));
                foreach (NodeManagerRegistration addition in independent)
                {
                    Assert.That(lifecycle.Registrations.ToList(), Does.Contain(addition));
                }
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(candidates[i].IsCommitted, Is.False);
                    Assert.That(candidates[i].Registrations[0].Identity.Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Aborted));
                }
                Assert.That(candidates[3].IsCommitted, Is.True);
                Assert.That(registration.Identity, Is.SameAs(candidates[3].Registrations[0].Identity));
            }
            await AssertReadinessValueAsync(kModelNamespaceUri, kFirstRegistrationValue).ConfigureAwait(false);
        }

        /// <summary>
        /// Factory/preparation exceptions are never mistaken for a commit-time routing conflict.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityPreparationFailureIsNotRetriedAsync(bool routingConflict)
        {
            Exception failure = routingConflict
                ? new NodeManagerRoutingConflictException("Preparation callback failed.")
                : new SentinelException();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither an invoked decision, a committed batch nor an unrelated provider error permits retry.
        /// </summary>
        [TestCase("Decision")]
        [TestCase("Committed")]
        [TestCase("Provider")]
        public async Task CompatibilityCommitFailureIsNotReplayedAsync(string stage)
        {
            int decisions = 0;
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            Exception failure = stage == "Provider"
                ? new InvalidOperationException("The address space changed while the batch was prepared.")
                : new InvalidOperationException(
                    "Routing conflict after an irreversible stage.",
                    new NodeManagerRoutingConflictException(
                        new NodeManagerRoutingConflictException("Routing conflict after an irreversible stage."),
                        prepared.Object));
            prepared.SetupGet(value => value.IsCommitted).Returns(stage == "Committed");
            prepared.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Func<CancellationToken, ValueTask> decideAsync, CancellationToken token) =>
                {
                    if (stage == "Decision")
                    {
                        decisions++;
                        await decideAsync(token).ConfigureAwait(false);
                    }
                    throw failure;
                });
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(stage == "Decision" ? 1 : 0));
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Cancellation stops conflict retries after aborting each already-prepared candidate.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictRetriesStopAtCancellationAsync()
        {
            using var cancellation = new CancellationTokenSource();
            int attempts = 0;
            var candidates = new List<Mock<IPreparedNodeManagerBatch>>();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeManagerBatchChange> _, CancellationToken _) =>
                {
                    var prepared = new Mock<IPreparedNodeManagerBatch>();
                    prepared.Setup(value => value.CommitAsync(
                        It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                        .Returns((Func<CancellationToken, ValueTask> _, CancellationToken _) =>
                        {
                            if (++attempts == 2)
                            {
                                cancellation.Cancel();
                            }
                            throw new InvalidOperationException(
                                "Independent routing publication.",
                                new NodeManagerRoutingConflictException(
                                    new NodeManagerRoutingConflictException("Independent routing publication."),
                                    prepared.Object));
                        });
                    candidates.Add(prepared);
                    return new ValueTask<IPreparedNodeManagerBatch>(prepared.Object);
                });
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)),
                cancellation.Token).AsTask(), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(candidates, Has.Count.EqualTo(2));
            foreach (Mock<IPreparedNodeManagerBatch> candidate in candidates)
            {
                candidate.Verify(value => value.DisposeAsync(), Times.Once);
            }
        }

        /// <summary>
        /// A failed abort is surfaced instead of starting another candidate with unreleased resources.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictDoesNotHideAbortFailureAsync()
        {
            var failure = new SentinelException();
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.Setup(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException(
                    "Independent routing publication.",
                    new NodeManagerRoutingConflictException(
                        new NodeManagerRoutingConflictException("Independent routing publication."), prepared.Object)));
            prepared.Setup(value => value.DisposeAsync()).Returns(new ValueTask(Task.FromException(failure)));
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue)))
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
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
            await lifecycle.AddAsync(ownerFactory.Object, timeout.Token).ConfigureAwait(false);
            await using IPreparedNodeManagerBatch nested = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
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
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken _) =>
                {
                    Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kSecondModelNamespaceUri);
                    candidate.Setup(value => value.CreateAddressSpaceAsync(
                        It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()))
                        .Callback((IDictionary<NodeId, IList<IReference>> references, CancellationToken _) =>
                            references.Add(source,
                                [new NodeStateReference(ReferenceTypeIds.HasComponent, false, new NodeId(9912))]))
                        .Returns(default(ValueTask));
                    candidates.Add(candidate.Object);
                    return new ValueTask<IAsyncNodeManager>(candidate.Object);
                });
            Exception? observed = await Assert.CatchAsync<Exception>(
                () => lifecycle.AddAsync(factory.Object, timeout.Token).AsTask()).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(nestedFailure, Is.TypeOf<InvalidOperationException>());
                Assert.That(nestedFailure?.InnerException, Is.TypeOf<NodeManagerRoutingConflictException>());
                Assert.That(observed, Is.SameAs(nestedFailure));
                Assert.That(ownerCalls, Is.EqualTo(1));
                Assert.That(nestedDecisions, Is.Zero);
                Assert.That(nested.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(expectedRevision));
                Assert.That(candidates, Has.Count.EqualTo(1));
                foreach (IAsyncNodeManager candidate in candidates)
                {
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(candidate).Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Aborted));
                }
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);

            async ValueTask<object> ResolveNestedAsync(CancellationToken token)
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
                await lifecycle.AddAsync(new RuntimeNodeSetNodeManagerFactory(
                    CreateOptions(kSecondModelNamespaceUri + ":Independent", kSecondRegistrationValue)), token)
                    .ConfigureAwait(false);
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
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
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
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                Assert.That(result.Registrations[0].Identity, Is.SameAs(generation));
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
            await using IPreparedNodeManagerBatch initial = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(ownerFactory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult original = await initial.CommitAsync(_ => default, timeout.Token)
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
            await using IPreparedNodeManagerBatch publication = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            int decisions = 0;
            Task<NodeManagerBatchResult> pending = publication.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, cancellation.Token).AsTask();
            NodeManagerBatchResult? independentResult = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                NodeManagerBatchChange change = outcome == "Stale"
                    ? NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kModelNamespaceUri + ":Independent", kSecondRegistrationValue)))
                    : NodeManagerBatchChange.Remove(original.Registrations[0]);
                await using IPreparedNodeManagerBatch independent =
                    await lifecycle.PrepareAsync([change], timeout.Token).ConfigureAwait(false);
                independentResult = await independent.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
                if (outcome != "Stale")
                {
                    Assert.That(independentResult.Retirement.IsCompleted, Is.False,
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
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            NodeManagerBatchResult committed = RequireLifecycleValue(independentResult);
            await committed.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(publication.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kSecondModelNamespaceUri), Is.EqualTo(-1));
            }
            owner.Verify(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()), Times.Once);

            async ValueTask<object> ResolveAsync(CancellationToken token)
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
            await using IPreparedNodeManagerBatch addition = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult initial = await addition.CommitAsync(_ => default, timeout.Token)
                .ConfigureAwait(false);
            NodeManagerGeneration generation = initial.Registrations[0].Identity;
            var monitoredItem = new Mock<IMonitoredItem>();
            monitoredItem.SetupGet(value => value.NodeManager).Returns(original.Object);
            monitoredItem.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacement.Object);
            await using IPreparedNodeManagerBatch reload = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(initial.Registrations[0], factory.Object)], timeout.Token)
                .ConfigureAwait(false);
            generation.MonitoredItems[monitoredItem.Object] = new Mock<ISubscription>().Object;
            int decisions = 0;
            Task<NodeManagerBatchResult> pending = reload.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask();
            NodeManagerBatchResult? independentResult = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch independent = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))], timeout.Token)
                    .ConfigureAwait(false);
                independentResult = await independent.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(release.Task.IsCompleted, Is.False);
            }
            finally
            {
                generation.MonitoredItems.TryRemove(monitoredItem.Object, out _);
                release.TrySetResult(true);
                await Assert.ThatAsync(() => pending.WaitAsync(timeout.Token),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(reload.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(RequireLifecycleValue(independentResult).Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(initial.Registrations[0]));
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
            NodeManagerBatchResult? nestedResult = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal server, ApplicationConfiguration configuration, CancellationToken token) =>
                    CreateWithNestedPublicationAsync(server, configuration, token));
            await using IPreparedNodeManagerBatch outer = await lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult committed = RequireLifecycleValue(nestedResult);
            int decisions = 0;
            await Assert.ThatAsync(() => outer.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, timeout.Token).AsTask(), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await outer.DisposeAsync().ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(outer.IsCommitted, Is.False);
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(committed.Registrations[0]));
                Assert.That(m_server.CurrentInstance.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(-1));
            }
            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Once);
            await AssertReadinessValueAsync(kSecondModelNamespaceUri, kSecondRegistrationValue).ConfigureAwait(false);

            async ValueTask<IAsyncNodeManager> CreateWithNestedPublicationAsync(
                IServerInternal server, ApplicationConfiguration configuration, CancellationToken token)
            {
                await using IPreparedNodeManagerBatch nested = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
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
            NodeManagerRegistration current = await lifecycle.AddRuntimeNodeSetAsync(
                CreateGenerationOptions(generation: 1), null, timeout.Token).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IServerInternal _, ApplicationConfiguration _, CancellationToken token) =>
                    FailPreparationAsync(token));
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Replace(current, factory.Object)], preparationCancellation.Token).AsTask();
            NodeManagerBatchResult? removed = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch removal = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(current)], timeout.Token).ConfigureAwait(false);
                removed = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(removed.CleanupFailure, Is.Null);
                Assert.That(removed.Retirement.IsCompleted, Is.False,
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
            await RequireLifecycleValue(removed).Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
            Task<IPreparedNodeManagerBatch> pending = lifecycle.PrepareAsync(
                [NodeManagerBatchChange.Add(delayedFactory.Object)], timeout.Token).AsTask();
            IPreparedNodeManagerBatch? first = null;
            NodeManagerBatchResult? published = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                using var independentTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await using IPreparedNodeManagerBatch independent = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(new RuntimeNodeSetNodeManagerFactory(
                        CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue)))],
                    independentTimeout.Token).ConfigureAwait(false);
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

            IPreparedNodeManagerBatch stale = RequireLifecycleValue(first);
            NodeManagerBatchResult committed = RequireLifecycleValue(published);
            await using (stale.ConfigureAwait(false))
            {
                int decisions = 0;
                await Assert.ThatAsync(() => stale.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, timeout.Token).AsTask(), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                Assert.That(stale.IsCommitted, Is.False);
                Assert.That(decisions, Is.Zero, "A conflicting preparation must fail before any durable decision.");
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(factoryCalls, Is.EqualTo(1), "Conflict detection must not silently rerun a factory.");
                Assert.That(lifecycle.Revision, Is.EqualTo(committed.Revision));
                Assert.That(lifecycle.Registrations.ToList(), Does.Contain(committed.Registrations[0]));
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
    }
}
