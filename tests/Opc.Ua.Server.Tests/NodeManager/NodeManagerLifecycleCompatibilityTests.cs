// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Exercises intentionally retained synchronous compatibility.
// TODO: Remove the legacy cases when the synchronous API is removed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies adapter retries, prepared-owner cleanup and retirement status without starting a server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class NodeManagerLifecycleCompatibilityTests
    {
        /// <summary>
        /// The primary lifecycle contract prepares real candidate ownership without starting a server.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task LifecycleReportsTransactionCapabilitiesOnThePrimaryContract()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:lifecycle-primary-contract");
            await using ILifecycleWriter writer = await harness.Lifecycle
                .CaptureChanges().BeginAsync().ConfigureAwait(false);
            await using IPreparedChanges prepared = await writer.PrepareAsync(
                [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))])
                .ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(writer.IsCurrent, Is.True);
                Assert.That(prepared.IsCommitted, Is.False);
                Assert.That(prepared.Registrations.Count, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Adding a reconciliation stage preserves the numeric identities of existing stages.
        /// </summary>
        [TestCase(NodeManagerReconciliationStage.ApplicationState, 0)]
        [TestCase(NodeManagerReconciliationStage.References, 1)]
        [TestCase(NodeManagerReconciliationStage.Bindings, 2)]
        [TestCase(NodeManagerReconciliationStage.MonitoredItems, 3)]
        [TestCase(NodeManagerReconciliationStage.Readiness, 4)]
        [TestCase(NodeManagerReconciliationStage.Identity, 5)]
        [TestCase(NodeManagerReconciliationStage.Namespaces, 6)]
        [TestCase(NodeManagerReconciliationStage.ModelChange, 7)]
        [TestCase(NodeManagerReconciliationStage.SemanticChange, 8)]
        [TestCase(NodeManagerReconciliationStage.Publication, 9)]
        [TestCase(NodeManagerReconciliationStage.Retirement, 10)]
        [TestCase(NodeManagerReconciliationStage.HistorianCapabilities, 11)]
        public void ReconciliationStagePreservesNumericIdentity(NodeManagerReconciliationStage stage, int value)
        {
            Assert.That((int)stage, Is.EqualTo(value));
        }

        /// <summary>
        /// Empty and null entries are rejected before preparation effects.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetRejectsEmptyAndNullEntriesAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            NodeManagerLifecycle lifecycle = harness.Lifecycle;
            long revision = lifecycle.Revision;
            NodeManagerRoutingTable.RoutingSnapshot routing =
                ((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision;
            await Assert.ThatAsync(async () => await ((INodeManagerLifecycle)null!).ApplyAsync([])
                .ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("lifecycle"))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync([]).ConfigureAwait(false),
                Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync(ArrayOf<LifecycleChange>.Null)
                .ConfigureAwait(false), Throws.ArgumentException).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.ApplyAsync([null!]).ConfigureAwait(false),
                Throws.ArgumentException).ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision, Is.SameAs(routing));
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
        }

        /// <summary>
        /// Revision mismatch and cancellation reject writer admission without changing routing.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetWriterRejectsUnexpectedRevisionAndCancellationAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            NodeManagerLifecycle lifecycle = harness.Lifecycle;
            long revision = lifecycle.Revision;
            NodeManagerRoutingTable.RoutingSnapshot routing =
                ((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision;
            await Assert.ThatAsync(async () => await lifecycle.BeginAsync(revision - 1).ConfigureAwait(false),
                Throws.InvalidOperationException).ConfigureAwait(false);
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
            await using (ILifecycleWriter admitted = await lifecycle.BeginAsync(revision).ConfigureAwait(false))
            {
                Assert.That(admitted.IsCurrent, Is.True);
                Assert.That(harness.Writer.ActiveOperationCount, Is.EqualTo(1));
            }
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await lifecycle.BeginAsync(cancellationToken: cancellation.Token)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(((IDynamicNodeManagerBatchHost)harness.Master).RoutingRevision, Is.SameAs(routing));
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
        }

        /// <summary>
        /// Rejects null lifecycle arguments before consulting server services.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task LifecycleArgumentGuardsRejectNullInputsAsync()
        {
            var host = new Mock<INodeManagerLifecycleHost>(MockBehavior.Strict);
            using var lifecycle = new NodeManagerLifecycle(host.Object, TimeProvider.System, null);
            Assert.That(() => LifecycleChange.Add(null!), Throws.ArgumentNullException
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo("factory"));
            Assert.That(() => LifecycleChange.Replace(null!, Mock.Of<IAsyncNodeManagerFactory>()),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("slot"));
            Assert.That(() => LifecycleChange.Replace(new NodeManagerSlot(), null!),
                Throws.ArgumentNullException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("factory"));
            Assert.That(() => LifecycleChange.Remove(null!), Throws.ArgumentNullException
                .With.Property(nameof(ArgumentException.ParamName)).EqualTo("slot"));
            Assert.That(() => ((INodeManagerFactory)null!).AsAsync(), Throws.ArgumentNullException);
            await Assert.ThrowsAsync<ArgumentException>(async () => await lifecycle.PrepareAsync([])
                .ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await lifecycle.BeginShutdownAsync(null!)
                .ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await lifecycle.CompleteShutdownAsync(null!)
                .ConfigureAwait(false)).ConfigureAwait(false);
            host.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Factory/preparation exceptions are never mistaken for a commit-time routing conflict.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityPreparationFailureIsNotRetriedAsync(bool routingConflict)
        {
            Exception failure = routingConflict
                ? new NodeManagerRoutingConflictException("Preparation callback failed.") : new SentinelException();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>())).Throws(failure);
            await Assert.ThatAsync(() => lifecycle.Object.ApplyAsync([LifecycleChange.Add(CreateFactory())]).AsTask(),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<LifecycleChange>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither an invoked decision, a committed batch nor an unrelated provider error permits retry.
        /// </summary>
        [TestCase("Decision")]
        [TestCase("Committed")]
        [TestCase("Provider")]
        public async Task CompatibilityCommitFailureIsNotReplayedAsync(string stage)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> manager = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:native:irreversible:" + stage);
            int factories = 0;
            int decisions = 0;
            var failure = new SentinelException("The irreversible stage failed.");
            IAsyncNodeManagerFactory factory = NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(manager,
                () =>
                {
                    factories++;
                    if (stage == "Provider")
                    {
                        throw failure;
                    }
                });
            if (stage == "Provider")
            {
                await Assert.ThatAsync(() => harness.Lifecycle.PrepareAsync([LifecycleChange.Add(factory)]).AsTask(),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                Assert.That(factories, Is.EqualTo(1));
                Assert.That(decisions, Is.Zero);
                return;
            }
            await using IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(factory)])
                .ConfigureAwait(false);
            if (stage == "Decision")
            {
                await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
                {
                    decisions++;
                    throw failure;
                }).AsTask(),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }).AsTask(),
                    Throws.Exception.SameAs(failure)).ConfigureAwait(false);
                Assert.That(prepared.IsCommitted, Is.False);
            }
            else
            {
                LifecycleResult result = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                },
                    () => throw failure).ConfigureAwait(false);
                LifecycleResult repeated = await prepared.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                })
                    .ConfigureAwait(false);
                Assert.That(repeated, Is.SameAs(result));
                Assert.That(prepared.IsCommitted, Is.True);
                Assert.That(result.Issues[0].InitialFailure, Is.SameAs(failure));
                Assert.That(result.Issues[0].Stage, Is.EqualTo(NodeManagerReconciliationStage.ApplicationState));
            }
            Assert.That(factories, Is.EqualTo(1));
            Assert.That(decisions, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancellation stops conflict retries after aborting each already-prepared candidate.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictRetriesStopAtCancellationAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            using var cancellation = new CancellationTokenSource();
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:native:cancel-conflict");
            int factories = 0;
            await using IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(
                NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate, () => factories++))])
                .ConfigureAwait(false);
            harness.Data.TypeTree.AddSubtype(new NodeId("cancel-conflict", 0), DataTypeIds.BaseDataType);
            await cancellation.CancelAsync().ConfigureAwait(false);
            int decisions = 0;
            await Assert.ThatAsync(() => prepared.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }, cancellation.Token)
                .AsTask(), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await prepared.DisposeAsync().ConfigureAwait(false);
            Assert.That(factories, Is.EqualTo(1));
            Assert.That(decisions, Is.Zero);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
        }

        /// <summary>
        /// A failed abort is surfaced instead of starting another candidate with unreleased resources.
        /// </summary>
        [Test]
        public async Task CompatibilityConflictDoesNotHideAbortFailureAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:native:conflict-abort");
            var failure = new SentinelException("Candidate deletion failed.");
            candidate.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>())).Throws(failure);
            IPreparedChanges prepared = await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(
                NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))]).ConfigureAwait(false);
            // Another namespace at the candidate's private index is a conflict; an unrelated type would be rebased.
            harness.Data.NamespaceUris.GetIndexOrAppend("urn:test:native:conflict-abort:other");
            AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(
                async () => await prepared.CommitAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(observed!.Flatten().InnerExceptions, Does.Contain(failure));
            Assert.That(
                observed.Flatten().InnerExceptions.Any(value => value is NodeManagerLifecycleConflictException),
                Is.True);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
            Assert.That(harness.Lifecycle.HasPendingShutdownCleanup, Is.True);
        }

        /// <summary>
        /// A malformed provider result is disposed rather than leaked during adapter construction.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetRejectsMalformedProviderAndDisposesPreparedOwnerAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>())).ReturnsAsync((IAsyncNodeManager)null!);
            long revision = harness.Lifecycle.Revision;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Lifecycle.PrepareAsync(
                [LifecycleChange.Add(factory.Object)]).ConfigureAwait(false)).ConfigureAwait(false);
            factory.Verify(value => value.CreateAsync(It.IsAny<IServerInternal>(),
                It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(harness.Lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
        }

        /// <summary>
        /// Malformed provider cleanup preserves the construction failure when disposal also fails.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetMalformedProviderCleanupFailurePreservesBothFailuresAsync()
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            var preparationFailure = new SentinelException("Private preparation failed.");
            var cleanupFailure = new SentinelException("Private cleanup failed.");
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:native:preparation-cleanup");
            candidate.Setup(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                It.IsAny<CancellationToken>())).Throws(preparationFailure);
            candidate.Setup(value => value.DeleteAddressSpaceAsync(
                It.IsAny<CancellationToken>())).Throws(cleanupFailure);
            AggregateException? observed = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Lifecycle.PrepareAsync([LifecycleChange.Add(
                    NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(observed!.Flatten().InnerExceptions, Does.Contain(preparationFailure));
            Assert.That(observed.Flatten().InnerExceptions, Does.Contain(cleanupFailure));
            candidate.Verify(value => value.CreateAddressSpaceAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(harness.Lifecycle.HasPendingShutdownCleanup, Is.True);
        }

        /// <summary>
        /// The read-image adapter preserves validation and cleanup failures with one disposal attempt.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetReadImageAdapterPreservesFailuresAndDisposesOnceAsync(bool cleanupFails)
        {
            await using var harness = new NodeManagerLifecycleChangeSetCleanupTests.ChangeSetCleanupHarness();
            Mock<IAsyncNodeManager> candidate = NodeManagerLifecycleChangeSetCleanupTests.CreateManager(
                "urn:test:native:metadata-cleanup");
            var cleanupFailure = new SentinelException("Metadata candidate disposal failed.");
            if (cleanupFails)
            {
                candidate.As<IDisposable>().Setup(value => value.Dispose()).Throws(cleanupFailure);
            }
            var image = new Mock<INodeManagerReadImage>();
            image.SetupGet(value => value.Owner).Returns(Mock.Of<IAsyncNodeManager>());
            ILifecycleWriter writer = await harness.Lifecycle.CaptureChanges().BeginAsync().ConfigureAwait(false);
            ArgumentException? validationFailure = null;
            Exception? observed;
            try
            {
                observed = await Assert.CatchAsync<Exception>(async () => await writer.ValidateAsync(
                    [LifecycleChange.Add(NodeManagerLifecycleChangeSetCleanupTests.CreateFactory(candidate))],
                    (prepared, _) =>
                    {
                        try
                        {
                            prepared.SetMetadata(image.Object);
                        }
                        catch (ArgumentException failure)
                        {
                            validationFailure = failure;
                            throw;
                        }
                        return default;
                    }).ConfigureAwait(false)).ConfigureAwait(false);
            }
            finally
            {
                if (cleanupFails)
                {
                    AggregateException? writerFailure = await Assert.ThrowsAsync<AggregateException>(
                        async () => await writer.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(writerFailure!.Flatten().InnerExceptions, Does.Contain(cleanupFailure));
                }
                else
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
            }
            Assert.That(validationFailure, Is.Not.Null);
            if (cleanupFails)
            {
                Assert.That(observed, Is.TypeOf<AggregateException>());
                Assert.That(((AggregateException)observed!).Flatten().InnerExceptions, Does.Contain(validationFailure));
                Assert.That(((AggregateException)observed).Flatten().InnerExceptions, Does.Contain(cleanupFailure));
            }
            else
            {
                Assert.That(observed, Is.SameAs(validationFailure));
            }
            candidate.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            candidate.As<IDisposable>().Verify(value => value.Dispose(), Times.Once);
            Assert.That(harness.Writer.ActiveOperationCount, Is.Zero);
        }

        /// <summary>
        /// Physical completion status and legacy result properties delegate to the same task and result owner.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public void SlotChangeSetRetirementStatusUsesAuthoritativeTask()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var failed = Task.FromException(new InvalidOperationException("Earlier cleanup attempt failed."));
            Task pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            (Task Retirement, NodeManagerRetirementStatus Status)[] cases =
            [
                (pending, NodeManagerRetirementStatus.Pending),
                (Task.CompletedTask, NodeManagerRetirementStatus.Completed),
                (failed, NodeManagerRetirementStatus.CompletedWithFailure),
                (Task.FromCanceled(cancellation.Token), NodeManagerRetirementStatus.Canceled)
            ];
            foreach ((Task retirement, NodeManagerRetirementStatus status) in cases)
            {
                var result = new LifecycleResult([], 3, null, 42, [], retirement);

                Assert.That(result.RetirementStatus, Is.EqualTo(status));
            }
            Assert.That(failed.Exception, Is.Not.Null);
        }

        /// <summary>
        /// The explicit factory adapter preserves namespaces and refuses canceled creation before the sync factory.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetAsAsyncPreservesFactoryAndCancellationAsync()
        {
            const string NamespaceUri = "urn:opcfoundation.org:Tests:NodeManagerLifecycle";
            var factory = new Mock<INodeManagerFactory>();
            factory.SetupGet(value => value.NamespacesUris).Returns([NamespaceUri]);
            var manager = new Mock<INodeManager>();
            factory.Setup(value => value.Create(It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()))
                .Returns(manager.Object);
            IAsyncNodeManagerFactory adapted = factory.Object.AsAsync();
            Assert.That(adapted.NamespacesUris[0], Is.EqualTo(NamespaceUri));
            var server = new Mock<IServerInternal>();
            var configuration = new ApplicationConfiguration();
            IAsyncNodeManager created = await adapted.CreateAsync(server.Object, configuration)
                .ConfigureAwait(false);
            Assert.That(created, Is.InstanceOf<AsyncNodeManagerAdapter>());
            Assert.That(((AsyncNodeManagerAdapter)created).SyncNodeManager, Is.SameAs(manager.Object));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await adapted.CreateAsync(
                server.Object, configuration, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            factory.Verify(value => value.Create(It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>()),
                Times.Once);
            factory.Verify(value => value.Create(server.Object, configuration), Times.Once);
            Assert.That(() => ((INodeManagerFactory)null!).AsAsync(), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Creates a factory passed through compatibility calls but never invoked by the mocked lifecycle.
        /// </summary>
        private static RuntimeNodeSetNodeManagerFactory CreateFactory()
        {
            // The mocked lifecycle never invokes this factory; it only observes compatibility-call preparation.
            return new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "Compatibility",
                        _ => new ValueTask<Stream>(Stream.Null),
                        ["urn:opcfoundation:tests:lifecycle-compatibility"])
                ]
            });
        }

        /// <summary>
        /// Identifies the exact preparation or abort failure that must reach the caller.
        /// </summary>
        private sealed class SentinelException : Exception
        {
            /// <summary>
            /// Creates an identifiable failure without a message.
            /// </summary>
            public SentinelException()
            {
            }

            /// <summary>
            /// Creates an identifiable failure with a message.
            /// </summary>
            public SentinelException(string message)
                : base(message)
            {
            }

            /// <summary>
            /// Creates an identifiable failure with its originating exception.
            /// </summary>
            public SentinelException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
    }
}
