/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Covers the awaitable configuration seam on
    /// <see cref="Opc.Ua.Server.Fluent.FluentNodeManagerBase"/> as the
    /// <see cref="DiNodeManager"/> pipeline drives it: the hook runs with an
    /// attached builder, the nodes it stages are registered, its references
    /// to externally owned nodes are published, and the builder is sealed
    /// before <c>CreateAddressSpaceAsync</c> returns.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    public sealed class DiNodeManagerConfigureAsyncTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigureAsyncRunsInsideTheSealedConfigurationPassAsync()
        {
            using var manager = new ProbeNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();

            await manager.CreateAddressSpaceAsync(externalReferences)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    manager.ConfigureAsyncCalls,
                    Is.EqualTo(1),
                    "the hook runs exactly once per activation");
                Assert.That(
                    manager.Builder,
                    Is.Not.Null,
                    "the hook receives the manager's attached builder");
                Assert.That(
                    manager.ContainsPredefined(manager.StagedFolderId),
                    Is.True,
                    "RegisterAuthoredNodes runs after the awaitable hook, so " +
                    "nodes it stages reach the address space");
                Assert.That(
                    externalReferences,
                    Contains.Key(Opc.Ua.ObjectIds.ObjectsFolder),
                    "CompleteConfigure runs after the hook, so the staged " +
                    "subtree publishes its Organizes edge to the Objects folder");
            });

            ServiceResultException sealedException = Assert.Throws<ServiceResultException>(
                () => manager.Builder!.Node("AsyncWired"))!;
            Assert.That(
                sealedException.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadInvalidState),
                "the builder is sealed before CreateAddressSpaceAsync returns");
        }

        /// <summary>
        /// Shared-service configuration waits for readiness and retains the open builder through a failed attempt.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PostSetupWaitsForReadinessAndSealsAfterSuccessfulCallbackAsync(bool failFirst)
        {
            int calls = 0;
            var runner = new Mock<IDiPostSetupRunner>(MockBehavior.Strict);
            runner.Setup(value => value.RunAsync(It.IsAny<DiNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns((DiNodeManager value, CancellationToken _) =>
                {
                    var probe = (ProbeNodeManager)value;
                    Assert.DoesNotThrow(() => probe.Builder!.Node("AsyncWired"));
                    calls++;
                    if (failFirst && calls == 1)
                    {
                        throw new InvalidOperationException("Injected post-setup failure.");
                    }
                    return default;
                });
            using var manager = new ProbeNodeManager(m_server.CurrentInstance, m_fixture.Config, runner.Object);

            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            Assert.That(calls, Is.Zero, "Preparation must not invoke configurators that use shared services.");
            var readiness = (INodeManagerReadinessNotification)(IAsyncNodeManager)manager;
            if (failFirst)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await readiness.OnServerReadyAsync().ConfigureAwait(false));
            }
            await readiness.OnServerReadyAsync().ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(failFirst ? 2 : 1));
            ServiceResultException sealedException = Assert.Throws<ServiceResultException>(
                () => manager.Builder!.Node("AsyncWired"))!;
            Assert.That(sealedException.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));

            await readiness.OnServerReadyAsync().ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(failFirst ? 2 : 1),
                "A completed post-setup must not be repeated by a later readiness notification.");
        }

        /// <summary>
        /// A failed seal retries completion without replaying a successfully completed shared-service callback.
        /// </summary>
        [Test]
        public async Task SealRetryDoesNotRepeatSuccessfulPostSetupAsync()
        {
            int calls = 0;
            int notifications = 0;
            var failure = new InvalidOperationException("Injected node-added failure.");
            var runner = new Mock<IDiPostSetupRunner>(MockBehavior.Strict);
            runner.Setup(value => value.RunAsync(It.IsAny<DiNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns((DiNodeManager value, CancellationToken _) =>
                {
                    calls++;
                    ((ProbeNodeManager)value).Builder!.Node("AsyncWired").OnNodeAdded((_, _) =>
                    {
                        notifications++;
                        if (notifications == 1)
                        {
                            throw failure;
                        }
                    });
                    return default;
                });
            await using var manager = new ProbeNodeManager(m_server.CurrentInstance, m_fixture.Config, runner.Object);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            var readiness = (INodeManagerReadinessNotification)(IAsyncNodeManager)manager;

            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await readiness.OnServerReadyAsync().ConfigureAwait(false));
            Assert.That(observed, Is.SameAs(failure));

            await readiness.OnServerReadyAsync().ConfigureAwait(false);
            await readiness.OnServerReadyAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.EqualTo(1), "The successful shared-service callback is not replayed.");
                Assert.That(notifications, Is.EqualTo(2), "Only the incomplete seal is retried.");
            });
            ServiceResultException sealedException = Assert.Throws<ServiceResultException>(
                () => manager.Builder!.Node("AsyncWired"))!;
            Assert.That(sealedException.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Stopping a prepared manager releases resources without invoking or retrying shared-service configurators.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedManagerCleanupDoesNotInvokeOrRetryPostSetupAsync(bool failReadiness)
        {
            var runner = new Mock<IDiPostSetupRunner>(MockBehavior.Strict);
            if (failReadiness)
            {
                runner.Setup(value => value.RunAsync(It.IsAny<DiNodeManager>(), It.IsAny<CancellationToken>()))
                    .Throws(new InvalidOperationException("Injected post-setup failure before stop."));
            }
            var resource = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            resource.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            await using var manager = new ProbeNodeManager(
                m_server.CurrentInstance, m_fixture.Config, runner.Object);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            manager.RegisterNodeResource(
                manager.FindPredefinedNode<FolderState>(manager.StagedFolderId)!, resource.Object);

            if (failReadiness)
            {
                var readiness = (INodeManagerReadinessNotification)(IAsyncNodeManager)manager;
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await readiness.OnServerReadyAsync().ConfigureAwait(false));
            }
            await manager.DeleteAddressSpaceAsync(CancellationToken.None).ConfigureAwait(false);
            await manager.DisposeAsync().ConfigureAwait(false);

            runner.Verify(value => value.RunAsync(It.IsAny<DiNodeManager>(), It.IsAny<CancellationToken>()),
                Times.Exactly(failReadiness ? 1 : 0));
            resource.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A DI manager that does all its wiring from the awaitable hook —
        /// the shape that used to require the manager to hand-roll
        /// <c>CreateFluentBuilder(...).Configure(...).Seal()</c> from a
        /// separate address-space-ready callback.
        /// </summary>
        private sealed class ProbeNodeManager : DiNodeManager
        {
            public ProbeNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IDiPostSetupRunner? postSetupRunner = null)
                : base(server, configuration, postSetupRunner)
            {
            }

            public int ConfigureAsyncCalls { get; private set; }

            public INodeManagerBuilder? Builder { get; private set; }

            public NodeId StagedFolderId { get; private set; }

            public bool ContainsPredefined(NodeId nodeId)
            {
                return PredefinedNodes.ContainsKey(nodeId);
            }

            protected override async ValueTask ConfigureAsync(
                INodeManagerBuilder builder,
                CancellationToken cancellationToken)
            {
                ConfigureAsyncCalls++;

                // The point of the seam: the hook can suspend. Anything the
                // manager needs to await (a store read, a device factory)
                // happens here rather than being blocked on from Configure.
                await Task.Yield();

                Builder = builder;

                // An explicit NodeId: the DI NodeId factory mints ids from a
                // parent chain, which a free-standing root does not have.
                var folder = new FolderState(null)
                {
                    NodeId = new NodeId("AsyncWired", InstanceNamespaceIndex),
                    BrowseName = new QualifiedName("AsyncWired", InstanceNamespaceIndex),
                    DisplayName = new LocalizedText("AsyncWired")
                };
                StagedFolderId = builder.Add(folder).Node.NodeId;
            }
        }
    }
}
