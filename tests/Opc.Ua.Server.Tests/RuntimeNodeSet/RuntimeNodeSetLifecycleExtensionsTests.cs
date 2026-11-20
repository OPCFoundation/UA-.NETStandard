/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

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
    public class RuntimeNodeSetLifecycleExtensionsTests
    {
        [Test]
        public async Task AddRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .AddRuntimeNodeSetAsync(null!, CreateOptions(), null)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task AddRuntimeNodeSetRejectsNullOptions()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .AddRuntimeNodeSetAsync(null!, null)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("options"));
        }

        /// <summary>
        /// Verifies the compatibility overload preserves the factory and token without forwarding its context.
        /// </summary>
        /// <param name="hasContext">Whether a legacy request context is supplied.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AddRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync(bool hasContext)
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();
            using var cancellation = new CancellationTokenSource();
            IOperationContext? context = hasContext ? Mock.Of<IOperationContext>() : null;

            await lifecycle.Object
                .AddRuntimeNodeSetAsync(CreateOptions(), context, cancellation.Token)
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current == null &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory),
                    cancellation.Token),
                Times.Once);
        }

        [Test]
        public async Task AddRuntimeNodeSetWithoutCallerContextForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .AddRuntimeNodeSetAsync(CreateOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current == null &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions(),
                        null)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ReloadRuntimeNodeSetAsync(null!, CreateOptions(), null)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public async Task ReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ReloadRuntimeNodeSetAsync(CreateRegistration(), null!, null)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        /// <summary>
        /// Verifies the reload compatibility overload preserves the registration, factory, and token.
        /// </summary>
        /// <param name="hasContext">Whether a legacy request context is supplied.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync(bool hasContext)
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();
            NodeManagerRegistration registration = CreateRegistration();
            using var cancellation = new CancellationTokenSource();
            IOperationContext? context = hasContext ? Mock.Of<IOperationContext>() : null;

            await lifecycle.Object
                .ReloadRuntimeNodeSetAsync(registration, CreateOptions(), context, cancellation.Token)
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current == registration &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                        changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    cancellation.Token),
                Times.Once);
        }

        [Test]
        public async Task ReloadRuntimeNodeSetWithoutCallerContextForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ReloadRuntimeNodeSetAsync(
                    CreateRegistration(),
                    CreateOptions(),
                    CancellationToken.None)
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current != null &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                        changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ShadowReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ShadowReloadRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ShadowReloadRuntimeNodeSetAsync(CreateRegistration(), null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task ShadowReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ShadowReloadRuntimeNodeSetAsync(CreateRegistration(), CreateOptions())
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current != null &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                        changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsANullLifecycle()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ImmediateReloadRuntimeNodeSetAsync(
                        null!,
                        CreateRegistration(),
                        CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsANullRegistration()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ImmediateReloadRuntimeNodeSetAsync(null!, CreateOptions())
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("registration"));
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetRejectsNullReplacementOptions()
        {
            ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await CreateLifecycle().Object
                    .ImmediateReloadRuntimeNodeSetAsync(CreateRegistration(), null!)
                    .ConfigureAwait(false));

            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
        }

        [Test]
        public async Task ImmediateReloadRuntimeNodeSetForwardsARuntimeNodeSetFactoryAsync()
        {
            Mock<INodeManagerLifecycle> lifecycle = CreateLifecycle();

            await lifecycle.Object
                .ImmediateReloadRuntimeNodeSetAsync(CreateRegistration(), CreateOptions())
                .ConfigureAwait(false);

            lifecycle.Verify(
                l => l.PrepareAsync(
                    It.Is<ArrayOf<NodeManagerBatchChange>>(changes =>
                        changes.Count == 1 && changes[0].Current != null &&
                        changes[0].Factory is RuntimeNodeSetNodeManagerFactory &&
                        changes[0].TransitionMode == NodeManagerMonitoredItemTransitionMode.Invalidate),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static Mock<INodeManagerLifecycle> CreateLifecycle()
        {
            NodeManagerRegistration registration = CreateRegistration();
            var prepared = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
            prepared.Setup(owner => owner.CommitAsync(
                It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeManagerBatchResult>(new NodeManagerBatchResult(
                    [registration], 0, null, 1, [registration.Slot], Task.CompletedTask)));
            prepared.Setup(owner => owner.DisposeAsync()).Returns(default(ValueTask));
            var lifecycle = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            lifecycle
                .Setup(l => l.PrepareAsync(
                    It.IsAny<ArrayOf<NodeManagerBatchChange>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IPreparedNodeManagerBatch>(prepared.Object));
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
