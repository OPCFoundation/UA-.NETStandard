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
 *
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
using System.Threading;
using System.Threading.Tasks;
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
        /// Rejects null lifecycle arguments before consulting server services.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task LifecycleArgumentGuardsRejectNullInputsAsync()
        {
            var host = new Mock<INodeManagerLifecycleHost>(MockBehavior.Strict);
            using var lifecycle = new NodeManagerLifecycle(host.Object, TimeProvider.System, null);
            IAsyncNodeManagerFactory asyncFactory =
                Mock.Of<IAsyncNodeManagerFactory>();
            INodeManagerFactory syncFactory = Mock.Of<INodeManagerFactory>();

            ArgumentNullException exception =
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await lifecycle
                        .AddAsync((IAsyncNodeManagerFactory)null!, null)
                        .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("factory"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .AddAsync((INodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("factory"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(
                        null!,
                        (IAsyncNodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(
                        null!,
                        (INodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(null!, asyncFactory, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(null!, syncFactory, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(
                        null!,
                        (IAsyncNodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(
                        null!,
                        (INodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(null!, asyncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(null!, syncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ImmediateReloadAsync(null!, (IAsyncNodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ImmediateReloadAsync(null!, (INodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ImmediateReloadAsync(null!, asyncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ImmediateReloadAsync(null!, syncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .RemoveAsync(null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .BeginShutdownAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("server"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .CompleteShutdownAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("server"));
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
                ? new NodeManagerRoutingConflictException("Preparation callback failed.")
                : new SentinelException();
            var lifecycle = new Mock<INodeManagerLifecycle>();
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            await Assert.ThatAsync(() => lifecycle.Object.AddAsync(
                CreateFactory())
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
                CreateFactory())
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
                CreateFactory(),
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
                CreateFactory())
                .AsTask(), Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            lifecycle.Verify(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()), Times.Once);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A malformed provider result is disposed rather than leaked during adapter construction.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetRejectsMalformedProviderAndDisposesPreparedOwnerAsync()
        {
            var provider = new Mock<INodeManagerLifecycle>();
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.SetupGet(value => value.Registrations).Returns([]);
            provider.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);
            await Assert.ThatAsync(async () => await provider.Object.PrepareAsync(
                [LifecycleChange.Add(CreateFactory())])
                .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("candidate registrations"))
                .ConfigureAwait(false);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
            prepared.Verify(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Malformed provider cleanup preserves the construction failure when disposal also fails.
        /// </summary>
        [Test]
        [SetCulture("en-us")]
        [SetUICulture("en-us")]
        public async Task SlotChangeSetMalformedProviderCleanupFailurePreservesBothFailuresAsync()
        {
            var cleanupFailure = new InvalidOperationException("Provider cleanup failed.");
            var provider = new Mock<INodeManagerLifecycle>();
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.SetupGet(value => value.Registrations).Returns([]);
            prepared.Setup(value => value.DisposeAsync())
                .Returns(new ValueTask(Task.FromException(cleanupFailure)));
            provider.Setup(value => value.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);

            Exception? observed = null;
            try
            {
                _ = await provider.Object.PrepareAsync(
                    [LifecycleChange.Add(CreateFactory())])
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                observed = failure;
            }

            AssertAdapterCleanupFailures(
                observed,
                "The provider returned a different number of candidate registrations.",
                cleanupFailure);
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
            prepared.Verify(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()), Times.Never);
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
            const string ExpectedValidationFailure =
                "A prepared read image owner has no registered NodeManager generation.";
            var owner = new Mock<IAsyncNodeManager>().Object;
            var image = new Mock<INodeManagerReadImage>();
            image.SetupGet(value => value.Owner).Returns(owner);
            var prepared = new Mock<IPreparedNodeManagerBatch>();
            prepared.SetupGet(value => value.Registrations).Returns([]);
            InvalidOperationException? cleanupFailure = cleanupFails
                ? new InvalidOperationException("Read-image provider cleanup failed.")
                : null;
            prepared.Setup(value => value.DisposeAsync()).Returns(cleanupFailure is null
                ? default
                : new ValueTask(Task.FromException(cleanupFailure)));
            var publication = new Mock<INodeManagerPublication>();
            publication.Setup(value => value.PrepareReadImagesAsync(
                It.IsAny<ArrayOf<INodeManagerReadImage>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(prepared.Object);

            Exception? observed = null;
            try
            {
                _ = await new LifecyclePublicationWriter(publication.Object)
                    .PrepareReadImagesAsync([image.Object])
                    .ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                observed = failure;
            }

            if (cleanupFailure is null)
            {
                Assert.That(observed, Is.TypeOf<InvalidOperationException>());
                Assert.That(observed!.Message, Is.EqualTo(ExpectedValidationFailure));
            }
            else
            {
                AssertAdapterCleanupFailures(observed, ExpectedValidationFailure, cleanupFailure);
            }
            prepared.Verify(value => value.DisposeAsync(), Times.Once);
            prepared.Verify(value => value.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()), Times.Never);
            publication.Verify(value => value.PrepareReadImagesAsync(
                It.IsAny<ArrayOf<INodeManagerReadImage>>(), It.IsAny<CancellationToken>()), Times.Once);
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
                var compatibility = new NodeManagerBatchResult(result, [], []);
                Assert.That(result.RetirementStatus, Is.EqualTo(status));
                Assert.That(compatibility.RetirementStatus, Is.EqualTo(status));
                Assert.That(compatibility.CommittedResult, Is.SameAs(result));
                Assert.That(compatibility.Retirement, Is.SameAs(result.Retired));
                Assert.That(compatibility.Revision, Is.EqualTo(result.Revision));
                Assert.That(compatibility.Retired, Is.EqualTo(result.RetiredCount));
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
        /// Asserts that adapter construction and its single cleanup attempt both remain observable.
        /// </summary>
        /// <param name="observed">The exception returned by the adapter.</param>
        /// <param name="validationFailureMessage">The original adapter validation failure message.</param>
        /// <param name="cleanupFailure">The cleanup failure that must be preserved.</param>
        private static void AssertAdapterCleanupFailures(
            Exception? observed,
            string validationFailureMessage,
            Exception cleanupFailure)
        {
            Assert.That(observed, Is.TypeOf<AggregateException>());
            AggregateException aggregate = (AggregateException)observed!;
            Assert.That(aggregate.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(aggregate.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
            Assert.That(aggregate.InnerExceptions[0].Message, Is.EqualTo(validationFailureMessage));
            Assert.That(aggregate.InnerExceptions[1], Is.SameAs(cleanupFailure));
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
