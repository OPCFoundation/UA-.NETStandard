// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Covers the argument contract and factory wiring of the runtime NodeSet
    /// lifecycle extension methods. The extensions are a thin, allocation-only
    /// wrapper over <see cref="INodeManagerLifecycle"/>, so they are verified
    /// against a mocked lifecycle rather than a live server.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Parallelizable]
    public sealed class RuntimeNodeSetLifecycleExtensionsTests
    {
        [Test]
        public async Task AddRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException? exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ((INodeManagerLifecycle)null!).ApplyAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateOptions()))]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task AddRuntimeNodeSetRejectsNullOptions()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RuntimeNodeSetNodeManagerFactory(null!))!;
            Assert.That(exception.ParamName, Is.EqualTo("options"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the compatibility overload preserves the factory and token without forwarding its context.
        /// </summary>
        /// <param name="hasContext">Whether a legacy request context is supplied.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AddRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync(bool hasContext)
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = lifecycle.Object.CaptureChanges();
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Add(factory);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            if (hasContext)
            {
                await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                    .ConfigureAwait(false);
                result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await using IPreparedChanges prepared = await ((ILifecycleWriter)writer)
                    .PrepareAsync([change], cancellation.Token).ConfigureAwait(false);
                result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == null &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token), Times.Once);
        }

        [Test]
        public async Task AddRuntimeNodeSetWithoutCallerContextForwardsARuntimeNodeSetFactoryAsync()
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = lifecycle.Object.CaptureChanges();
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Add(factory);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                .ConfigureAwait(false);
            result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == null &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token), Times.Once);
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException? exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ((INodeManagerLifecycle)null!).ApplyAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateOptions()))]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => LifecycleChange.Replace(
                null!, new RuntimeNodeSetNodeManagerFactory(CreateOptions())))!;
            Assert.That(exception.ParamName, Is.EqualTo("slot"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RuntimeNodeSetNodeManagerFactory(null!))!;
            Assert.That(exception.ParamName, Is.EqualTo("options"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies the reload compatibility overload preserves the registration, factory, and token.
        /// </summary>
        /// <param name="hasContext">Whether a legacy request context is supplied.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync(bool hasContext)
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = LifecycleInspection.Capture(lifecycle.Object, registration);
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Replace(registration.Slot, factory);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            if (hasContext)
            {
                await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                    .ConfigureAwait(false);
                result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await using IPreparedChanges prepared = await ((ILifecycleWriter)writer)
                    .PrepareAsync([change], cancellation.Token).ConfigureAwait(false);
                result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            }
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == registration.Slot &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token), Times.Once);
        }

        [Test]
        public async Task ReloadRuntimeNodeSetWithoutCallerContextForwardsARuntimeNodeSetFactoryAsync()
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = LifecycleInspection.Capture(lifecycle.Object, registration);
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Replace(registration.Slot, factory);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                .ConfigureAwait(false);
            result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == registration.Slot &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token), Times.Once);
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException? exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ((INodeManagerLifecycle)null!).ApplyAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateOptions()))]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => LifecycleChange.Replace(
                null!, new RuntimeNodeSetNodeManagerFactory(CreateOptions())))!;
            Assert.That(exception.ParamName, Is.EqualTo("slot"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RuntimeNodeSetNodeManagerFactory(null!))!;
            Assert.That(exception.ParamName, Is.EqualTo("options"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = LifecycleInspection.Capture(lifecycle.Object, registration);
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Replace(registration.Slot, factory);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                .ConfigureAwait(false);
            result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == registration.Slot &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token), Times.Once);
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException? exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await ((INodeManagerLifecycle)null!).ApplyAsync(
                    [LifecycleChange.Add(new RuntimeNodeSetNodeManagerFactory(CreateOptions()))]).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => LifecycleChange.Replace(
                null!, new RuntimeNodeSetNodeManagerFactory(CreateOptions())))!;
            Assert.That(exception.ParamName, Is.EqualTo("slot"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new RuntimeNodeSetNodeManagerFactory(null!))!;
            Assert.That(exception.ParamName, Is.EqualTo("options"));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            NodeManagerRegistration registration = CreateRegistration();
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle(registration);
            using var cancellation = new CancellationTokenSource();
            ILifecycleCapture capture = LifecycleInspection.Capture(lifecycle.Object, registration);
            var factory = new RuntimeNodeSetNodeManagerFactory(CreateOptions());
            var change = LifecycleChange.Replace(
                registration.Slot, factory, NodeManagerMonitoredItemTransitionMode.Invalidate);
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellation.Token).ConfigureAwait(false);
            LifecycleResult result;
            await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellation.Token)
                .ConfigureAwait(false);
            result = await prepared.CommitAsync(cancellationToken: cancellation.Token).ConfigureAwait(false);
            Assert.That(result.Issues, Is.Empty);
            Mock.Get(writer).Verify(value => value.PrepareAsync(
                It.Is<ArrayOf<LifecycleChange>>(changes => changes.Count == 1 &&
                    changes[0].Slot == registration.Slot &&
                    changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                    changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Invalidate),
                    cancellation.Token), Times.Once);
        }

        private static Mock<INodeManagerLifecycle> CreateLifecycle(NodeManagerRegistration registration)
        {
            var result = new LifecycleResult([new NodeManagerRegistrationSnapshot(registration)],
                0, null, 1, [registration.Slot], Task.CompletedTask);
            var prepared = new Mock<IPreparedChanges>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(result);
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var writer = new Mock<ILifecycleWriter>(MockBehavior.Strict);
            writer.Setup(owner => owner.PrepareAsync(It.IsAny<ArrayOf<LifecycleChange>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(prepared.Object);
            writer.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var capture = new Mock<ILifecycleCapture>(MockBehavior.Strict);
            capture.Setup(owner => owner.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(writer.Object);
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle.SetupGet(owner => owner.Registrations).Returns([registration]);
            lifecycle.Setup(owner => owner.CaptureChanges()).Returns(capture.Object);
            return lifecycle;
        }

        private static RuntimeNodeSetOptions CreateOptions()
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "RuntimeNodeSetLifecycleExtensionsTests",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(kNodeSetXml))),
                        [kModelNamespaceUri])
                ]
            };
        }

        private static NodeManagerRegistration CreateRegistration()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .SetupGet(n => n.NamespaceUris)
                .Returns([kModelNamespaceUri]);
            return new NodeManagerRegistration(Guid.NewGuid(), 1, nodeManager.Object);
        }

        private const string kModelNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions";

        private const string kNodeSetXml =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:opcfoundation.org:Tests:RuntimeNodeSetExtensions"
                       Version="1.0.0" PublicationDate="2026-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;
    }
}
