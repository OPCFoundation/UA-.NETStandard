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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Exercises hosted canonical forwarding and the one-release compatibility transaction shims.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HostedNodeManagerLifecycleTests
    {
        [Test]
        public void PrimaryContractHasNoConvenienceOrContextMembers()
        {
            Assert.That(typeof(INodeManagerLifecycle).GetMethods()
                .Where(method => !method.IsSpecialName).Select(method => method.Name),
                Is.EquivalentTo(s_primaryMethods));
            Assert.That(typeof(INodeManagerLifecycle).GetMethods()
                .SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IOperationContext)), Is.False);
            Assert.That(typeof(INodeManagerPublicationCapture).GetMethods()
                .SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(IOperationContext)), Is.False);
            Assert.That(typeof(HostedNodeManagerLifecycle).GetMethods()
                .Any(method => method.Name is "AddAsync" or "ReloadAsync" or "RemoveAsync"
                    or "ShadowReloadAsync" or "ImmediateReloadAsync"), Is.False);
            Assert.That(typeof(NodeManagerLifecycle).GetMethods()
                .Any(method => method.Name is "AddAsync" or "ReloadAsync" or "RemoveAsync"
                    or "ShadowReloadAsync" or "ImmediateReloadAsync"), Is.False);
            Assert.That(typeof(NodeManagerLifecycleExtensions).GetMethods()
                .Where(method => method.DeclaringType == typeof(NodeManagerLifecycleExtensions))
                .All(method => Attribute.IsDefined(method, typeof(ObsoleteAttribute))), Is.True);
        }

        [Test]
        public async Task MembersThrowWhenNotAttachedAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Registrations);
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Revision);
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Slots);
            Assert.Throws<InvalidOperationException>(() => hosted.CapturePublication());
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await hosted.PrepareAsync([]).ConfigureAwait(false)).ConfigureAwait(false);
        }

        [Test]
        public void SnapshotMembersDelegateToAttachedLifecycle()
        {
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            NodeManagerRegistration registration = NewRegistration();
            inner.SetupGet(owner => owner.Revision).Returns(17);
            inner.SetupGet(owner => owner.Slots).Returns([registration.Slot]);
            inner.SetupGet(owner => owner.Registrations).Returns([registration]);
            inner.SetupGet(owner => owner.IsShuttingDown).Returns(true);
            inner.SetupGet(owner => owner.SupportsPreparedPublication).Returns(true);
            inner.SetupGet(owner => owner.SupportsPublicationIsolation).Returns(true);
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);

            Assert.That(hosted.Revision, Is.EqualTo(17));
            Assert.That(hosted.Slots[0], Is.SameAs(registration.Slot));
            Assert.That(hosted.Registrations[0], Is.SameAs(registration));
            Assert.That(hosted.IsShuttingDown, Is.True);
            Assert.That(hosted.SupportsPreparedPublication, Is.True);
            Assert.That(hosted.SupportsPublicationIsolation, Is.True);
            inner.VerifyAll();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedPreparationForwardsCanonicalTransactionAsync(bool compatibilityEntryPoint)
        {
            using var cancellation = new CancellationTokenSource();
            ArrayOf<NodeManagerBatchChange> changes =
                [NodeManagerBatchChange.Add(Mock.Of<IAsyncNodeManagerFactory>())];
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.PrepareAsync(changes, cancellation.Token))
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);

            await using (IPreparedNodeManagerBatch actual = compatibilityEntryPoint
                ? await hosted.PrepareAsync(changes, Mock.Of<IOperationContext>(), cancellation.Token)
                    .ConfigureAwait(false)
                : await hosted.PrepareAsync(changes, cancellation.Token).ConfigureAwait(false))
            {
                Assert.That(actual, Is.SameAs(prepared.Object));
            }

            inner.VerifyAll();
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedCapturePreservesOwnerContextFreeTokenAndIdentityAsync(bool legacyContext)
        {
            using var cancellation = new CancellationTokenSource();
            var publication = new Mock<INodeManagerPublication>(MockBehavior.Strict);
            var capture = new Mock<INodeManagerPublicationCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellation.Token))
                .Returns(new ValueTask<INodeManagerPublication>(publication.Object));
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.CapturePublication()).Returns(capture.Object);
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);

            INodeManagerPublicationCapture actualCapture = hosted.CapturePublication();
            INodeManagerPublication actual = legacyContext
                ? await actualCapture.BeginAsync(Mock.Of<IOperationContext>(), cancellation.Token)
                    .ConfigureAwait(false)
                : await actualCapture.BeginAsync(cancellation.Token).ConfigureAwait(false);

            Assert.That(actualCapture, Is.SameAs(capture.Object));
            Assert.That(actual, Is.SameAs(publication.Object));
            inner.VerifyAll();
            capture.VerifyAll();
        }

        [TestCase("Add")]
        [TestCase("Reload")]
        [TestCase("Shadow")]
        [TestCase("Immediate")]
        [TestCase("Remove")]
        public async Task CompatibilityOperationsPrepareCommitDisposeAndPreserveIdentityAsync(string operation)
        {
            using var cancellation = new CancellationTokenSource();
            IAsyncNodeManagerFactory factory = Mock.Of<IAsyncNodeManagerFactory>();
            NodeManagerRegistration current = NewRegistration();
            NodeManagerRegistration expected = NewRegistration();
            ArrayOf<NodeManagerBatchChange> captured = default;
            var retirement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new NodeManagerBatchResult(
                operation == "Remove" ? [] : [expected], 0, null, 17, [expected.Slot], retirement.Task);
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), cancellation.Token))
                .Returns(new ValueTask<NodeManagerBatchResult>(result));
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), cancellation.Token))
                .Callback<ArrayOf<NodeManagerBatchChange>, CancellationToken>((changes, _) => captured = changes)
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            IOperationContext context = Mock.Of<IOperationContext>(MockBehavior.Strict);

            NodeManagerRegistration? actual = null;
            switch (operation)
            {
                case "Add":
                    actual = await hosted.AddAsync(factory, context, cancellation.Token).ConfigureAwait(false);
                    break;
                case "Reload":
                    actual = await hosted.ReloadAsync(current, factory, context, cancellation.Token)
                        .ConfigureAwait(false);
                    break;
                case "Shadow":
                    actual = await hosted.ShadowReloadAsync(current, factory, cancellation.Token)
                        .ConfigureAwait(false);
                    break;
                case "Immediate":
                    actual = await hosted.ImmediateReloadAsync(current, factory, cancellation.Token)
                        .ConfigureAwait(false);
                    break;
                default:
                    await hosted.RemoveAsync(current, context, cancellation.Token).ConfigureAwait(false);
                    break;
            }

            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Current, Is.SameAs(operation == "Add" ? null : current));
            Assert.That(captured[0].Factory, Is.SameAs(operation == "Remove" ? null : factory));
            Assert.That(captured[0].TransitionMode, Is.EqualTo(operation is "Immediate" or "Remove"
                ? NodeManagerMonitoredItemTransitionMode.Invalidate : NodeManagerMonitoredItemTransitionMode.Migrate));
            Assert.That(actual, Is.SameAs(operation == "Remove" ? null : expected));
            Assert.That(retirement.Task.IsCompleted, Is.False);
            retirement.SetResult(true);
            await result.Retirement.ConfigureAwait(false);
            inner.VerifyAll();
            prepared.VerifyAll();
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
        }

        [TestCase("Add")]
        [TestCase("Reload")]
        [TestCase("Remove")]
        public async Task CompatibilityFailuresRetainCommittedResultAndRegistrationAsync(string operation)
        {
            NodeManagerRegistration registration = NewRegistration();
            var failure = new InvalidOperationException("readiness failed");
            var retirement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new NodeManagerBatchResult(
                operation == "Remove" ? [] : [registration],
                0, new AggregateException(failure), 42, [registration.Slot], retirement.Task);
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), CancellationToken.None))
                .Returns(new ValueTask<NodeManagerBatchResult>(result));
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), CancellationToken.None))
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));
            IAsyncNodeManagerFactory factory = Mock.Of<IAsyncNodeManagerFactory>();

            Exception? exception = operation switch
            {
                "Add" => await Assert.ThrowsAsync<NodeManagerAddCommittedException>(
                    async () => await lifecycle.Object.AddAsync(factory).ConfigureAwait(false)).ConfigureAwait(false),
                "Reload" => await Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await lifecycle.Object.ReloadAsync(registration, factory).ConfigureAwait(false))
                    .ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<NodeManagerRemovalCommittedException>(
                    async () => await lifecycle.Object.RemoveAsync(registration).ConfigureAwait(false))
                    .ConfigureAwait(false)
            };
            Assert.That(exception!.InnerException, Is.SameAs(failure));
            Assert.That(exception switch
            {
                NodeManagerAddCommittedException added => added.Registration,
                NodeManagerReloadCommittedException reloaded => reloaded.Registration,
                NodeManagerRemovalCommittedException removed => removed.Registration,
                _ => null
            }, Is.SameAs(registration));
            Assert.That(exception switch
            {
                NodeManagerAddCommittedException added => added.Result,
                NodeManagerReloadCommittedException reloaded => reloaded.Result,
                NodeManagerRemovalCommittedException removed => removed.Result,
                _ => null
            }, Is.SameAs(result));
            Assert.That(result.Retirement.IsCompleted, Is.False);
            retirement.SetResult(true);
            await result.Retirement.ConfigureAwait(false);
            prepared.VerifyAll();
        }

        [TestCase("Add")]
        [TestCase("Reload")]
        [TestCase("Shadow")]
        [TestCase("Immediate")]
        public async Task SyncCompatibilityAdaptersRetainFactoryNamespacesAndCreationAsync(string operation)
        {
            NodeManagerRegistration registration = NewRegistration();
            ArrayOf<NodeManagerBatchChange> captured = default;
            var result = new NodeManagerBatchResult(
                [registration], 0, null, 1, [registration.Slot], Task.CompletedTask);
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), CancellationToken.None))
                .Returns(new ValueTask<NodeManagerBatchResult>(result));
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), CancellationToken.None))
                .Callback<ArrayOf<NodeManagerBatchChange>, CancellationToken>((changes, _) => captured = changes)
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));
            IServerInternal server = Mock.Of<IServerInternal>();
            var configuration = new ApplicationConfiguration();
            INodeManager manager = Mock.Of<INodeManager>();
            var factory = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            factory.SetupGet(owner => owner.NamespacesUris).Returns(["urn:sync:adapter"]);
            factory.Setup(owner => owner.Create(server, configuration)).Returns(manager);
            IOperationContext context = Mock.Of<IOperationContext>(MockBehavior.Strict);

            NodeManagerRegistration actual = operation switch
            {
                "Add" => await lifecycle.Object.AddAsync(factory.Object, context).ConfigureAwait(false),
                "Reload" => await lifecycle.Object.ReloadAsync(registration, factory.Object, context)
                    .ConfigureAwait(false),
                "Shadow" => await lifecycle.Object.ShadowReloadAsync(registration, factory.Object)
                    .ConfigureAwait(false),
                _ => await lifecycle.Object.ImmediateReloadAsync(registration, factory.Object).ConfigureAwait(false)
            };

            Assert.That(actual, Is.SameAs(registration));
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Current, Is.SameAs(operation == "Add" ? null : registration));
            Assert.That(captured[0].TransitionMode, Is.EqualTo(operation == "Immediate"
                ? NodeManagerMonitoredItemTransitionMode.Invalidate : NodeManagerMonitoredItemTransitionMode.Migrate));
            IAsyncNodeManagerFactory adapter = captured[0].Factory!;
            Assert.That(adapter.NamespacesUris, Is.EqualTo(new ArrayOf<string>(["urn:sync:adapter"])));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await adapter.CreateAsync(server, configuration, canceled.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            factory.Verify(owner => owner.Create(server, configuration), Times.Never);
            Assert.That(await adapter.CreateAsync(server, configuration).ConfigureAwait(false), Is.Not.Null);
            factory.VerifyAll();
            prepared.VerifyAll();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityCommitFailureDisposesUncommittedOwnerAsync(bool canceled)
        {
            using var cancellation = new CancellationTokenSource();
            Exception failure = canceled
                ? new OperationCanceledException(cancellation.Token)
                : new InvalidOperationException("decision rejected");
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), cancellation.Token)).Throws(failure);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), cancellation.Token))
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));

            Exception? actual = await Assert.CatchAsync(
                async () => await lifecycle.Object.AddAsync(
                    Mock.Of<IAsyncNodeManagerFactory>(), Mock.Of<IOperationContext>(), cancellation.Token)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(actual, Is.SameAs(failure));
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
            lifecycle.VerifyAll();
        }

        [Test]
        public void AttachDetachRejectNullAndDifferentOwners()
        {
            var hosted = new HostedNodeManagerLifecycle();
            INodeManagerLifecycle first = Mock.Of<INodeManagerLifecycle>();
            INodeManagerLifecycle second = Mock.Of<INodeManagerLifecycle>();
            Assert.Throws<ArgumentNullException>(() => hosted.Attach(null!));
            Assert.Throws<ArgumentNullException>(() => hosted.Detach(null!));
            hosted.Attach(first);
            Assert.DoesNotThrow(() => hosted.Attach(first));
            Assert.Throws<InvalidOperationException>(() => hosted.Attach(second));
            hosted.Detach(second);
            Assert.Throws<InvalidOperationException>(() => hosted.Attach(second));
            hosted.Detach(first);
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Registrations);
            Assert.DoesNotThrow(() => hosted.Attach(second));
        }

        private static NodeManagerRegistration NewRegistration()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(manager => manager.NamespaceUris).Returns(["urn:test:hosted-lifecycle"]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, nodeManager.Object);
        }

        private static readonly string[] s_primaryMethods = ["PrepareAsync", "CapturePublication"];
    }
}
