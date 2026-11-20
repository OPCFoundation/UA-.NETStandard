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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.Hosting
{
    /// <summary>
    /// Tests native hosted forwarding. Former compatibility case names retain their original case identities.
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
            foreach (Type type in new[] { typeof(INodeManagerLifecycle), typeof(ILifecycleCapture) })
            {
                Assert.That(type.GetMethods().SelectMany(method => method.GetParameters())
                    .Any(parameter => parameter.ParameterType == typeof(IOperationContext)), Is.False);
            }
            foreach (Type type in new[] { typeof(NodeManagerLifecycle), typeof(HostedNodeManagerLifecycle) })
            {
                Assert.That(type.GetMethods().Any(method => method.Name is
                    "AddAsync" or "ReloadAsync" or "RemoveAsync" or "ShadowReloadAsync" or "ImmediateReloadAsync"),
                    Is.False);
            }
            Assert.That(typeof(NodeManagerLifecycle).Assembly.GetType("Opc.Ua.Server.NodeManagerLifecycleExtensions"),
                Is.Null);
        }

        [Test]
        public async Task MembersThrowWhenNotAttachedAsync()
        {
            var hosted = new HostedNodeManagerLifecycle();
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Registrations);
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Revision);
            Assert.Throws<InvalidOperationException>(() => _ = hosted.Slots);
            Assert.Throws<InvalidOperationException>(() => hosted.CaptureChanges());
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
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            Assert.That(hosted.Revision, Is.EqualTo(17));
            Assert.That(hosted.Slots[0], Is.SameAs(registration.Slot));
            Assert.That(hosted.Registrations[0], Is.SameAs(registration));
            Assert.That(hosted.IsShuttingDown, Is.True);
            Assert.That(typeof(NodeManagerLifecycle).Assembly.GetType("Opc.Ua.Server.INodeManagerPublicationLifecycle"),
                Is.Null);
            inner.VerifyAll();
        }

        /// <summary>
        /// Concrete and interface entry points forward the same prepared transaction and cancellation token.
        /// </summary>
        // TODO: Remove this suppression when RCS1151 recognizes intentional interface-dispatch controls.
        [SuppressMessage(
            "Style",
            "RCS1151:Remove redundant cast",
            Justification = "The false case must call through the interface, not repeat the concrete entry point.")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedPreparationForwardsCanonicalTransactionAsync(bool compatibilityEntryPoint)
        {
            using var cancellation = new CancellationTokenSource();
            ArrayOf<LifecycleChange> changes = [LifecycleChange.Add(Mock.Of<IAsyncNodeManagerFactory>())];
            var prepared = new Mock<IPreparedChanges>(MockBehavior.Strict);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.PrepareAsync(changes, cancellation.Token)).ReturnsAsync(prepared.Object);
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            await using (IPreparedChanges actual = compatibilityEntryPoint
                ? await hosted.PrepareAsync(changes, cancellation.Token).ConfigureAwait(false)
                : await ((INodeManagerLifecycle)hosted).PrepareAsync(changes, cancellation.Token).ConfigureAwait(false))
            {
                Assert.That(actual, Is.SameAs(prepared.Object));
            }
            inner.VerifyAll();
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Both entry points preserve the exact capture and the writer's cancellation token.
        /// </summary>
        // TODO: Remove this suppression when RCS1151 recognizes intentional interface-dispatch controls.
        [SuppressMessage(
            "Style",
            "RCS1151:Remove redundant cast",
            Justification = "The false case must call through the interface, not repeat the concrete entry point.")]
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedCapturePreservesOwnerContextFreeTokenAndIdentityAsync(bool legacyContext)
        {
            using var cancellation = new CancellationTokenSource();
            var writer = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            writer.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellation.Token)).ReturnsAsync(writer.Object);
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            ILifecycleCapture actualCapture = legacyContext
                ? hosted.CaptureChanges()
                : ((INodeManagerLifecycle)hosted).CaptureChanges();
            await using (ILifecycleWriter actual = await actualCapture.BeginAsync(cancellation.Token)
                .ConfigureAwait(false))
            {
                Assert.That(actualCapture, Is.SameAs(capture.Object));
                Assert.That(actual, Is.SameAs(writer.Object));
            }
            inner.VerifyAll();
            capture.VerifyAll();
            writer.Verify(owner => owner.DisposeAsync(), Times.Once);
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
            ArrayOf<LifecycleChange> captured = default;
            var retirement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new LifecycleResult(
                operation == "Remove" ? [] : [new NodeManagerRegistrationSnapshot(expected)],
                0, null, 17, [expected.Slot], retirement.Task);
            var prepared = new Mock<IPreparedChanges>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(null, cancellation.Token)).ReturnsAsync(result);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var writer = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            writer.Setup(owner => owner.PrepareAsync(It.IsAny<ArrayOf<LifecycleChange>>(), cancellation.Token))
                .Callback<ArrayOf<LifecycleChange>, CancellationToken>((changes, _) => captured = changes)
                .ReturnsAsync(prepared.Object);
            writer.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(cancellation.Token)).ReturnsAsync(writer.Object);
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            if (operation != "Add")
            {
                inner.SetupGet(owner => owner.Registrations).Returns([current]);
            }
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            ILifecycleCapture evidence = operation == "Add" ? hosted.CaptureChanges() :
                LifecycleInspection.Capture(hosted, current);
            LifecycleChange change = operation switch
            {
                "Add" => LifecycleChange.Add(factory),
                "Remove" => LifecycleChange.Remove(current.Slot),
                _ => LifecycleChange.Replace(current.Slot, factory, operation == "Immediate"
                    ? NodeManagerMonitoredItemTransitionMode.Invalidate
                    : NodeManagerMonitoredItemTransitionMode.Migrate)
            };
            LifecycleResult actual;
            await using (ILifecycleWriter invocation = await evidence.BeginAsync(cancellation.Token)
                .ConfigureAwait(false))
            {
                await using IPreparedChanges unit = await invocation.PrepareAsync([change], cancellation.Token)
                    .ConfigureAwait(false);
                actual = await unit.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            Assert.That(actual, Is.SameAs(result));
            Assert.That(actual.Issues, Is.Empty);
            Assert.That(captured.Count, Is.EqualTo(1));
            Assert.That(captured[0].Slot, Is.SameAs(operation == "Add" ? null : current.Slot));
            Assert.That(captured[0].Factory, Is.SameAs(operation == "Remove" ? null : factory));
            Assert.That(captured[0].TransitionMode, Is.EqualTo(operation is "Immediate" or "Remove"
                ? NodeManagerMonitoredItemTransitionMode.Invalidate : NodeManagerMonitoredItemTransitionMode.Migrate));
            if (operation != "Remove")
            {
                Assert.That(LifecycleInspection.Registration(actual.Registrations[0]), Is.SameAs(expected));
            }
            else
            {
                Assert.That(actual.Registrations, Is.Empty);
            }
            Assert.That(retirement.Task.IsCompleted, Is.False);
            retirement.SetResult(true);
            await actual.Retired.ConfigureAwait(false);
            inner.VerifyAll();
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
            writer.Verify(owner => owner.DisposeAsync(), Times.Once);
        }

        [TestCase("Add")]
        [TestCase("Reload")]
        [TestCase("Remove")]
        public async Task CompatibilityFailuresRetainCommittedResultAndRegistrationAsync(string operation)
        {
            NodeManagerRegistration registration = NewRegistration();
            var failure = new InvalidOperationException("readiness failed");
            var retirement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new LifecycleResult(
                operation == "Remove" ? [] : [new NodeManagerRegistrationSnapshot(registration)],
                0, new AggregateException(failure), 42, [registration.Slot], retirement.Task);
            var prepared = new Mock<IPreparedChanges>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(null, CancellationToken.None)).ReturnsAsync(result);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var writer = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            writer.Setup(owner => owner.PrepareAsync(It.IsAny<ArrayOf<LifecycleChange>>(), CancellationToken.None))
                .ReturnsAsync(prepared.Object);
            writer.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(CancellationToken.None)).ReturnsAsync(writer.Object);
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            if (operation != "Add")
            {
                inner.SetupGet(owner => owner.Registrations).Returns([registration]);
            }
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            IAsyncNodeManagerFactory factory = Mock.Of<IAsyncNodeManagerFactory>();
            ILifecycleCapture evidence = operation == "Add" ? hosted.CaptureChanges() :
                LifecycleInspection.Capture(hosted, registration);
            LifecycleChange change = operation switch
            {
                "Add" => LifecycleChange.Add(factory),
                "Remove" => LifecycleChange.Remove(registration.Slot),
                _ => LifecycleChange.Replace(registration.Slot, factory)
            };
            LifecycleResult actual;
            await using (ILifecycleWriter invocation = await evidence.BeginAsync().ConfigureAwait(false))
            {
                await using IPreparedChanges unit = await invocation.PrepareAsync([change]).ConfigureAwait(false);
                actual = await unit.CommitAsync().ConfigureAwait(false);
            }
            Assert.That(actual, Is.SameAs(result));
            Assert.That(actual.CleanupFailure!.InnerException, Is.SameAs(failure));
            Assert.That(actual.Revision, Is.EqualTo(42));
            if (operation != "Remove")
            {
                Assert.That(LifecycleInspection.Registration(actual.Registrations[0]), Is.SameAs(registration));
            }
            Assert.That(actual.Retired, Is.SameAs(retirement.Task));
            Assert.That(actual.Retired.IsCompleted, Is.False);
            retirement.SetResult(true);
            await actual.Retired.ConfigureAwait(false);
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
            writer.Verify(owner => owner.DisposeAsync(), Times.Once);
            inner.VerifyAll();
        }

        [TestCase("Add")]
        [TestCase("Reload")]
        [TestCase("Shadow")]
        [TestCase("Immediate")]
        public async Task SyncCompatibilityAdaptersRetainFactoryNamespacesAndCreationAsync(string operation)
        {
            IServerInternal server = Mock.Of<IServerInternal>();
            var configuration = new ApplicationConfiguration();
            INodeManager manager = Mock.Of<INodeManager>();
            var factory = new Mock<INodeManagerFactory>(MockBehavior.Strict);
            factory.SetupGet(owner => owner.NamespacesUris).Returns(["urn:sync:adapter"]);
            factory.Setup(owner => owner.Create(server, configuration)).Returns(manager);
            IAsyncNodeManagerFactory adapter = factory.Object.AsAsync();
            NodeManagerRegistration registration = NewRegistration();
            LifecycleChange change = operation == "Add" ? LifecycleChange.Add(adapter) :
                LifecycleChange.Replace(registration.Slot, adapter, operation == "Immediate"
                    ? NodeManagerMonitoredItemTransitionMode.Invalidate
                    : NodeManagerMonitoredItemTransitionMode.Migrate);
            Assert.That(change.Factory, Is.SameAs(adapter));
            Assert.That(change.Slot, Is.SameAs(operation == "Add" ? null : registration.Slot));
            Assert.That(change.TransitionMode, Is.EqualTo(operation == "Immediate"
                ? NodeManagerMonitoredItemTransitionMode.Invalidate : NodeManagerMonitoredItemTransitionMode.Migrate));
            Assert.That(adapter.NamespacesUris, Is.EqualTo(new ArrayOf<string>(["urn:sync:adapter"])));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await adapter.CreateAsync(server, configuration, canceled.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            factory.Verify(owner => owner.Create(server, configuration), Times.Never);
            IAsyncNodeManager actual = await adapter.CreateAsync(server, configuration).ConfigureAwait(false);
            Assert.That(actual.SyncNodeManager, Is.SameAs(manager));
            factory.Verify(owner => owner.Create(server, configuration), Times.Once);
            factory.VerifyAll();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CompatibilityCommitFailureDisposesUncommittedOwnerAsync(bool canceled)
        {
            using var cancellation = new CancellationTokenSource();
            Exception failure = canceled ? new OperationCanceledException(cancellation.Token) :
                new InvalidOperationException("decision rejected");
            var prepared = new Mock<IPreparedChanges>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(null, cancellation.Token)).Throws(failure);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var inner = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            inner.Setup(owner => owner.PrepareAsync(It.IsAny<ArrayOf<LifecycleChange>>(), cancellation.Token))
                .ReturnsAsync(prepared.Object);
            var hosted = new HostedNodeManagerLifecycle();
            hosted.Attach(inner.Object);
            Exception? actual = await Assert.CatchAsync(async () => await hosted.ApplyAsync(
                [LifecycleChange.Add(Mock.Of<IAsyncNodeManagerFactory>())], cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(actual, Is.SameAs(failure));
            prepared.Verify(owner => owner.DisposeAsync(), Times.Once);
            inner.VerifyAll();
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
            Assert.DoesNotThrow(() => hosted.Attach(second));
        }

        /// <summary>
        /// Creates a strong inspection handle for forwarding identity controls.
        /// </summary>
        private static NodeManagerRegistration NewRegistration()
        {
            var manager = new Mock<IAsyncNodeManager>();
            manager.SetupGet(owner => owner.NamespaceUris).Returns(["urn:hosted:identity"]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, manager.Object);
        }

        /// <summary>The native primary transaction member names.</summary>
        private static readonly string[] s_primaryMethods = ["PrepareAsync", "CaptureChanges"];
    }
}
