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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Live lifecycle integration tests for <see cref="INodeManagerLifecycle"/> and its
    /// <see cref="NodeManagerRegistration"/> handles against a real, running
    /// <see cref="ReferenceServer"/>, focused on registration identity/copy semantics and
    /// on the rejection paths for Add/Reload/Remove.
    /// </summary>
    /// <remarks>
    /// Each test starts a fresh <see cref="ServerFixture{ReferenceServer}"/> so that
    /// namespace-table, routing-table, and registration baselines are predictable and
    /// unaffected by other tests' live add/reload/remove mutations.
    /// </remarks>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class NodeManagerLifecycleTests
    {
        private const double kMaxAge = 10000;

        private const string kModelNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeManagerLifecycle";

        private const string kSecondModelNamespaceUri =
            "urn:opcfoundation.org:Tests:NodeManagerLifecycleSecond";

        private const uint kRootNodeId = 8000;
        private const uint kValueNodeId = 8001;
        private const string kRootBrowseName = "LifecycleRoot";
        private const string kValueBrowseName = "LifecycleValue";
        private const int kGeneration1Value = 1;
        private const int kGeneration2Value = 2;
        private const int kFirstRegistrationValue = 101;
        private const int kSecondRegistrationValue = 202;

        private string m_pkiRoot;
        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ILogger m_logger;

        /// <summary>
        /// Starts a fresh <see cref="ReferenceServer"/> and activates a session for the test.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(NodeManagerLifecycleTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create().CreateLogger<NodeManagerLifecycleTests>();

            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
        }

        /// <summary>
        /// Closes the session, stops the server, and cleans up PKI artefacts.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_server?.Dispose();

            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The internal <see cref="NodeManagerRegistration"/> constructor must record the
        /// exact <c>Id</c>, <c>Generation</c>, and <c>NodeManager</c> passed to it, and must
        /// copy the NodeManager's namespace URIs defensively at construction time: mutating
        /// the source list afterwards must not affect the registration.
        /// </summary>
        [Test]
        public void NodeManagerRegistrationCopiesNamespaceUris()
        {
            var sourceNamespaces = new List<string> { "urn:test:one", "urn:test:two" };
            var nodeManagerMock = new Mock<IAsyncNodeManager>();
            nodeManagerMock.Setup(m => m.NamespaceUris).Returns(() => sourceNamespaces);
            IAsyncNodeManager nodeManager = nodeManagerMock.Object;

            var id = Guid.NewGuid();
            const long generation = 7;
            var registration = new NodeManagerRegistration(id, generation, nodeManager);

            // Mutate the source list after construction: the registration must be unaffected.
            sourceNamespaces.Add("urn:test:three");
            sourceNamespaces[0] = "urn:test:mutated";

            Assert.That(registration.Id, Is.EqualTo(id));
            Assert.That(registration.Generation, Is.EqualTo(generation));
            Assert.That(registration.NodeManager, Is.SameAs(nodeManager));
            Assert.That(registration.NamespaceUris.Count, Is.EqualTo(2));
            Assert.That(registration.NamespaceUris[0], Is.EqualTo("urn:test:one"));
            Assert.That(registration.NamespaceUris[1], Is.EqualTo("urn:test:two"));
        }

        /// <summary>
        /// Each read of <see cref="INodeManagerLifecycle.Registrations"/> must return a
        /// fresh, independently backed <see cref="ArrayOf{T}"/> snapshot (earlier snapshots
        /// are frozen and unaffected by later Add calls), while the contained
        /// <see cref="NodeManagerRegistration"/> instances are the very same shared objects
        /// across reads.
        /// </summary>
        [Test]
        public async Task RegistrationsReturnsDefensiveSnapshotsAsync()
        {
            NodeManagerRegistration first = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue))
                .ConfigureAwait(false);

            ArrayOf<NodeManagerRegistration> snapshotAfterFirstAdd =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(snapshotAfterFirstAdd.Count, Is.EqualTo(1));

            NodeManagerRegistration second = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue))
                .ConfigureAwait(false);

            // The earlier snapshot variable must remain frozen at its original contents.
            Assert.That(snapshotAfterFirstAdd.Count, Is.EqualTo(1));
            Assert.That(snapshotAfterFirstAdd[0].Id, Is.EqualTo(first.Id));

            ArrayOf<NodeManagerRegistration> snapshotAfterSecondAdd =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(snapshotAfterSecondAdd.Count, Is.EqualTo(2));

            // Two independent reads of Registrations return distinct ArrayOf snapshots that
            // nonetheless share the same underlying NodeManagerRegistration instances.
            ArrayOf<NodeManagerRegistration> snapshotA = m_server.NodeManagerLifecycle.Registrations;
            ArrayOf<NodeManagerRegistration> snapshotB = m_server.NodeManagerLifecycle.Registrations;
            Assert.That(
                MemoryMarshal.TryGetArray(
                    snapshotA.Memory,
                    out ArraySegment<NodeManagerRegistration> backingA),
                Is.True);
            Assert.That(
                MemoryMarshal.TryGetArray(
                    snapshotB.Memory,
                    out ArraySegment<NodeManagerRegistration> backingB),
                Is.True);
            Assert.That(backingB.Array, Is.Not.SameAs(backingA.Array));

            NodeManagerRegistration firstFromA = snapshotA.Find(r => r.Id == first.Id);
            NodeManagerRegistration firstFromB = snapshotB.Find(r => r.Id == first.Id);
            Assert.That(firstFromA, Is.Not.Null);
            Assert.That(firstFromB, Is.Not.Null);
            Assert.That(ReferenceEquals(firstFromA, firstFromB), Is.True);
            Assert.That(ReferenceEquals(firstFromA, first), Is.True);

            ArrayOf<NodeManagerRegistration> registrationsAfterSnapshotReads =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(registrationsAfterSnapshotReads.Count, Is.EqualTo(2));
            Assert.That(
                registrationsAfterSnapshotReads.Find(r => r.Id == first.Id),
                Is.Not.Null);
            Assert.That(
                registrationsAfterSnapshotReads.Find(r => r.Id == second.Id),
                Is.Not.Null);

            // Routing and reads for both registrations remain available.
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort firstNs = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            ushort secondNs = (ushort)server.NamespaceUris.GetIndex(kSecondModelNamespaceUri);

            Assert.That(master.NamespaceManagers.ContainsKey(firstNs), Is.True);
            Assert.That(
                ReferenceEquals(master.NamespaceManagers[firstNs][0], first.NodeManager),
                Is.True);
            Assert.That(master.NamespaceManagers.ContainsKey(secondNs), Is.True);
            Assert.That(
                ReferenceEquals(master.NamespaceManagers[secondNs][0], second.NodeManager),
                Is.True);

            DataValue firstValue = await ReadValueAsync(new NodeId(kValueNodeId, firstNs))
                .ConfigureAwait(false);
            Assert.That(firstValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstValue.WrappedValue.GetInt32(), Is.EqualTo(kFirstRegistrationValue));

            DataValue secondValue = await ReadValueAsync(new NodeId(kValueNodeId, secondNs))
                .ConfigureAwait(false);
            Assert.That(secondValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondValue.WrappedValue.GetInt32(), Is.EqualTo(kSecondRegistrationValue));
        }

        /// <summary>
        /// A registration handle that is stale (superseded generation), foreign (unknown
        /// <c>Id</c>), or spoofed (wrong <c>NodeManager</c> reference for a known <c>Id</c>)
        /// must be rejected by both Reload and Remove with the provider's ownership-mismatch
        /// message, without invoking a replacement factory and without changing the current
        /// generation's registration, routing, value, or namespace state.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="mismatchKind"/> is not a supported value.
        /// </exception>
        [TestCase(LifecycleOperation.Reload, MismatchKind.StaleGeneration)]
        [TestCase(LifecycleOperation.Reload, MismatchKind.ForeignId)]
        [TestCase(LifecycleOperation.Reload, MismatchKind.ForeignNodeManager)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.StaleGeneration)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.ForeignId)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.ForeignNodeManager)]
        public async Task RegistrationIdentityMismatchIsRejectedWithoutChangingCurrentGenerationAsync(
            LifecycleOperation operation,
            MismatchKind mismatchKind)
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            // Establish a genuinely current generation (2) and a genuinely stale handle
            // (the original, now-superseded generation-1 handle).
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            NodeManagerRegistration mismatched = mismatchKind switch
            {
                MismatchKind.StaleGeneration => original,
                MismatchKind.ForeignId => new NodeManagerRegistration(
                    Guid.NewGuid(),
                    current.Generation,
                    current.NodeManager),
                MismatchKind.ForeignNodeManager => new NodeManagerRegistration(
                    current.Id,
                    current.Generation,
                    new Mock<IAsyncNodeManager>().Object),
                _ => throw new ArgumentOutOfRangeException(nameof(mismatchKind))
            };

            const string expectedMessage =
                "The registration is stale or is not owned by this lifecycle provider.";

            if (operation == LifecycleOperation.Reload)
            {
                var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .ReloadAsync(mismatched, replacementFactory.Object)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(expectedMessage));

                replacementFactory.Verify(
                    f => f.CreateAsync(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            else
            {
                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .RemoveAsync(mismatched)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(expectedMessage));
            }

            // The current registration/generation/routing/value/namespace state must be
            // entirely unchanged.
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration survivor = registrations.Find(r => r.Id == current.Id);
            Assert.That(survivor, Is.Not.Null);
            Assert.That(survivor.Generation, Is.EqualTo(current.Generation));
            Assert.That(ReferenceEquals(survivor.NodeManager, current.NodeManager), Is.True);

            Assert.That(
                master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, current.NodeManager)),
                Is.EqualTo(1));

            DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
        }

        /// <summary>
        /// Adding a NodeManager on a constructed-but-never-started server must be rejected
        /// (the current contract surfaces this as <see cref="ServiceResultException"/> with
        /// <see cref="StatusCodes.BadServerHalted"/>, raised while resolving the running
        /// server instance before the factory is ever consulted) and must leave the
        /// throwaway server's registrations empty. The throwaway server is disposed
        /// deterministically at the end of the test.
        /// </summary>
        [Test]
        public Task AddAsyncBeforeServerStartRejectsWithoutInvokingFactoryAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var freshServer = new ReferenceServer(telemetry);

            var factory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

            Assert.That(
                async () => await freshServer.NodeManagerLifecycle
                    .AddAsync(factory.Object)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadServerHalted));

            factory.Verify(
                f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            Assert.That(freshServer.NodeManagerLifecycle.Registrations.Count, Is.Zero);
            return Task.CompletedTask;
        }

        /// <summary>
        /// When the replacement factory throws during Reload, the sentinel exception must
        /// propagate unchanged, the current generation's registration, routing, value, and
        /// namespace state must be entirely unaffected, and no replacement is published.
        /// </summary>
        [Test]
        public async Task ReloadAsyncWhenReplacementFactoryThrowsKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            replacementFactory
                .Setup(f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new SentinelException());

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .ReloadAsync(original, replacementFactory.Object)
                    .ConfigureAwait(false),
                Throws.TypeOf<SentinelException>());

            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration survivor = registrations.Find(r => r.Id == original.Id);
            Assert.That(survivor, Is.Not.Null);
            Assert.That(survivor.Generation, Is.EqualTo(original.Generation));
            Assert.That(ReferenceEquals(survivor.NodeManager, original.NodeManager), Is.True);

            Assert.That(
                master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, original.NodeManager)),
                Is.EqualTo(1));

            DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
        }

        /// <summary>
        /// When the replacement factory returns <c>null</c> during Reload, the provider
        /// must reject it with its own diagnostic message after invoking the factory exactly
        /// once, and the current generation's registration, routing, value, and namespace
        /// state must be entirely unaffected.
        /// </summary>
        [Test]
        public async Task ReloadAsyncWhenReplacementFactoryReturnsNullKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            replacementFactory
                .Setup(f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IAsyncNodeManager)null!);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .ReloadAsync(original, replacementFactory.Object)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "The replacement NodeManager factory returned null."));

            replacementFactory.Verify(
                f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration survivor = registrations.Find(r => r.Id == original.Id);
            Assert.That(survivor, Is.Not.Null);
            Assert.That(survivor.Generation, Is.EqualTo(original.Generation));
            Assert.That(ReferenceEquals(survivor.NodeManager, original.NodeManager), Is.True);

            Assert.That(
                master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, original.NodeManager)),
                Is.EqualTo(1));

            DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
        }

        /// <summary>
        /// When the replacement NodeManager's <c>CreateAddressSpaceAsync</c> fails during
        /// Reload, the sentinel exception must propagate, the host's own cleanup path must
        /// run exactly once (<c>DeleteAddressSpaceAsync</c>) followed by the lifecycle
        /// provider's own disposal of the failed replacement (<c>Dispose</c>), the current
        /// generation's registration/routing/value must be entirely unaffected, no
        /// replacement is ever published or routed, and the namespace state is stable.
        /// </summary>
        [Test]
        public async Task ReloadAsyncWhenReplacementCreateAddressSpaceFailsKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            var failingManager = new Mock<IAsyncNodeManager>();
            failingManager
                .Setup(m => m.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new SentinelException());
            var cleanupOrder = new List<string>();
            failingManager
                .Setup(m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Callback(() => cleanupOrder.Add("DeleteAddressSpaceAsync"))
                .Returns(new ValueTask());
            Mock<IDisposable> failingManagerAsDisposable = failingManager.As<IDisposable>();
            failingManagerAsDisposable
                .Setup(d => d.Dispose())
                .Callback(() => cleanupOrder.Add("Dispose"));

            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(failingManager.Object);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .ReloadAsync(original, replacementFactory.Object)
                    .ConfigureAwait(false),
                Throws.TypeOf<SentinelException>());

            failingManager.Verify(
                m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            failingManagerAsDisposable.Verify(d => d.Dispose(), Times.Once);
            string[] expectedCleanupOrder = ["DeleteAddressSpaceAsync", "Dispose"];
            Assert.That(cleanupOrder, Is.EqualTo(expectedCleanupOrder));

            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration survivor = registrations.Find(r => r.Id == original.Id);
            Assert.That(survivor, Is.Not.Null);
            Assert.That(survivor.Generation, Is.EqualTo(original.Generation));
            Assert.That(ReferenceEquals(survivor.NodeManager, original.NodeManager), Is.True);

            Assert.That(
                master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, original.NodeManager)),
                Is.EqualTo(1));
            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, failingManager.Object)),
                Is.Zero);

            DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
        }

        /// <summary>
        /// Reload must be rejected while its current NodeManager still owns a reporting
        /// monitored item on a live subscription, without ever invoking the replacement
        /// factory, and must leave the current generation's registration, routing, value,
        /// and namespace state entirely unaffected. Once the owning subscription is deleted,
        /// the guard is lifted and a subsequent reload succeeds.
        /// </summary>
        [Test]
        public async Task ReloadAsyncRejectsOwnedMonitoredItemAndKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                .ConfigureAwait(false);

            try
            {
                var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .ReloadAsync(original, replacementFactory.Object)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(
                        "The NodeManager cannot be reloaded or removed while it owns monitored items."));

                replacementFactory.Verify(
                    f => f.CreateAsync(
                        It.IsAny<IServerInternal>(),
                        It.IsAny<ApplicationConfiguration>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);

                ArrayOf<NodeManagerRegistration> registrations =
                    m_server.NodeManagerLifecycle.Registrations;
                NodeManagerRegistration survivor = registrations.Find(r => r.Id == original.Id);
                Assert.That(survivor, Is.Not.Null);
                Assert.That(survivor.Generation, Is.EqualTo(original.Generation));
                Assert.That(ReferenceEquals(survivor.NodeManager, original.NodeManager), Is.True);

                Assert.That(
                    master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, original.NodeManager)),
                    Is.EqualTo(1));

                DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
                uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            // With the owning subscription gone, the guard must be lifted: the reload now
            // succeeds and advances the generation.
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2))
                .ConfigureAwait(false);
            Assert.That(reloaded.Id, Is.EqualTo(original.Id));
            Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));
        }

        /// <summary>
        /// Remove must be rejected while its NodeManager still owns a reporting monitored
        /// item on a live subscription, and must leave the current registration, routing,
        /// value, and namespace state entirely unaffected. Once the owning subscription is
        /// deleted, the guard is lifted and a subsequent removal succeeds.
        /// </summary>
        [Test]
        public async Task RemoveAsyncRejectsOwnedMonitoredItemAndKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                .ConfigureAwait(false);

            try
            {
                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .RemoveAsync(original)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(
                        "The NodeManager cannot be reloaded or removed while it owns monitored items."));

                ArrayOf<NodeManagerRegistration> registrations =
                    m_server.NodeManagerLifecycle.Registrations;
                NodeManagerRegistration survivor = registrations.Find(r => r.Id == original.Id);
                Assert.That(survivor, Is.Not.Null);
                Assert.That(survivor.Generation, Is.EqualTo(original.Generation));
                Assert.That(ReferenceEquals(survivor.NodeManager, original.NodeManager), Is.True);

                Assert.That(
                    master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, original.NodeManager)),
                    Is.EqualTo(1));

                DataValue value = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
                uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            // With the owning subscription gone, the guard must be lifted: removal succeeds.
            await m_server.NodeManagerLifecycle.RemoveAsync(original).ConfigureAwait(false);

            ArrayOf<NodeManagerRegistration> registrationsAfterRemove =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(
                CountMatches(registrationsAfterRemove, r => r.Id == original.Id),
                Is.Zero);
        }

        /// <summary>
        /// A failure from the first <c>DeleteAddressSpaceAsync</c> call occurs after the
        /// NodeManager has been unpublished. The failure must not republish it, while the
        /// registration remains current so a second Remove can retry deletion and complete
        /// disposal.
        /// </summary>
        /// <exception cref="SentinelException">
        /// Thrown by the injected address-space deletion failure.
        /// </exception>
        [Test]
        public async Task RemoveAsyncWhenDeleteAddressSpaceFailsKeepsRegistrationUnpublishedAndRetryableAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DeleteFailure";
            const string ExpectedMessage = "DeleteAddressSpaceAsync failed.";
            var deleteStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDelete = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task FailFirstDeleteAddressSpaceAsync()
            {
                deleteStarted.TrySetResult(true);
                await releaseDelete.Task.ConfigureAwait(false);
                throw new SentinelException(ExpectedMessage);
            }

            var nodeManager = new Mock<IAsyncNodeManager>();
            Mock<IDisposable> nodeManagerAsDisposable = nodeManager.As<IDisposable>();
            var syncNodeManager = new Mock<INodeManager>();
            nodeManager
                .Setup(m => m.NamespaceUris)
                .Returns([NamespaceUri]);
            nodeManager
                .Setup(m => m.SyncNodeManager)
                .Returns(syncNodeManager.Object);
            nodeManager
                .Setup(m => m.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            nodeManager
                .SetupSequence(m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(FailFirstDeleteAddressSpaceAsync()))
                .Returns(new ValueTask());

            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory.Object)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            int namespaceIndex = server.NamespaceUris.GetIndex(NamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.EqualTo(1));
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.True);
            Assert.That(
                master.NamespaceManagers[namespaceIndex]
                    .Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.EqualTo(1));

            Task firstRemoval = m_server.NodeManagerLifecycle
                .RemoveAsync(registration)
                .AsTask();
            try
            {
                Task deleteStartResult = await Task.WhenAny(
                    deleteStarted.Task,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                Assert.That(deleteStartResult, Is.SameAs(deleteStarted.Task));
                await deleteStarted.Task.ConfigureAwait(false);

                Assert.That(firstRemoval.IsCompleted, Is.False);
                Assert.That(
                    master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                    Is.Zero);
                Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);

                ArrayOf<NodeManagerRegistration> registrations =
                    m_server.NodeManagerLifecycle.Registrations;
                Assert.That(registrations.Count, Is.EqualTo(1));
                Assert.That(registrations[0], Is.SameAs(registration));
                Assert.That(registrations[0].Id, Is.EqualTo(registration.Id));
                Assert.That(registrations[0].Generation, Is.EqualTo(registration.Generation));
                Assert.That(registrations[0].NodeManager, Is.SameAs(nodeManager.Object));
            }
            finally
            {
                releaseDelete.TrySetResult(true);
            }

            Assert.That(
                async () => await firstRemoval.ConfigureAwait(false),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage));

            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.Zero);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);

            ArrayOf<NodeManagerRegistration> registrationsAfterFailure =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(registrationsAfterFailure.Count, Is.EqualTo(1));
            Assert.That(registrationsAfterFailure[0], Is.SameAs(registration));
            Assert.That(registrationsAfterFailure[0].Id, Is.EqualTo(registration.Id));
            Assert.That(
                registrationsAfterFailure[0].Generation,
                Is.EqualTo(registration.Generation));
            Assert.That(
                registrationsAfterFailure[0].NodeManager,
                Is.SameAs(nodeManager.Object));
            nodeManager.Verify(
                m => m.DeleteAddressSpaceAsync(CancellationToken.None),
                Times.Once);
            nodeManagerAsDisposable.Verify(d => d.Dispose(), Times.Never);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration)
                .ConfigureAwait(false);

            nodeManager.Verify(
                m => m.DeleteAddressSpaceAsync(CancellationToken.None),
                Times.Exactly(2));
            factory.Verify(
                f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            nodeManagerAsDisposable.Verify(d => d.Dispose(), Times.Once);

            ArrayOf<NodeManagerRegistration> registrationsAfterRetry =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(
                CountMatches(registrationsAfterRetry, r => r.Id == registration.Id),
                Is.Zero);
            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.Zero);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);
        }

        /// <summary>
        /// An event subscription and monitored item on <see cref="ObjectIds.Server"/>,
        /// created before the live NodeManager is ever added, must receive one
        /// <c>BaseModelChangeEventState</c>-shaped notification per lifecycle operation
        /// (add, same-URI reload, remove), each carrying the provider's exact
        /// <c>SourceNode</c>/<c>SourceName</c>/<c>Message</c> values (no <c>Changes</c>
        /// field is expected, since that only exists on the separate
        /// <c>GeneralModelChangeEventState</c>/<c>SemanticChangeEventState</c> shapes). Add
        /// must also change the server's namespace table
        /// (<c>NamespaceArray</c>/<c>UrisVersion</c>), while a same-URI reload and a
        /// subsequent remove must both leave it unchanged.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycleEvents")]
        public async Task ExistingServerEventSubscriptionReceivesLifecycleModelChangeEventsAsync()
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscriptionResponse = await services
                .CreateSubscriptionAsync(requestHeader, 100, 100, 10, 0, true, 0)
                .ConfigureAwait(false);
            uint subscriptionId = subscriptionResponse.SubscriptionId;

            ArrayOf<MonitoredItemCreateRequest> monitoredItems =
                [CreateModelChangeEventMonitoredItem(clientHandle: 1, queueSize: 10)];

            requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse createItemsResponse = await services
                .CreateMonitoredItemsAsync(requestHeader, subscriptionId, TimestampsToReturn.Both, monitoredItems)
                .ConfigureAwait(false);
            Assert.That(createItemsResponse.Results.Count, Is.EqualTo(1));
            Assert.That(createItemsResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            try
            {
                IServerInternal server = m_server.CurrentInstance;

                // --- Add: registers a brand-new namespace URI. ---
                int namespaceCountBeforeAdd = server.NamespaceUris.Count;
                uint urisVersionBeforeAdd = await ReadUrisVersionAsync().ConfigureAwait(false);

                NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1))
                    .ConfigureAwait(false);

                EventFieldList addEvent;
                (addEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(addEvent, "A live NodeManager was added.");

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBeforeAdd + 1));
                uint urisVersionAfterAdd = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfterAdd, Is.Not.EqualTo(urisVersionBeforeAdd));

                // --- Reload (same URI): must not change the namespace table. ---
                int namespaceCountBeforeReload = server.NamespaceUris.Count;
                uint urisVersionBeforeReload = urisVersionAfterAdd;

                registration = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(registration, CreateGenerationOptions(generation: 2))
                    .ConfigureAwait(false);

                EventFieldList reloadEvent;
                (reloadEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(reloadEvent, "A live NodeManager was reloaded.");

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBeforeReload));
                uint urisVersionAfterReload = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfterReload, Is.EqualTo(urisVersionBeforeReload));

                // --- Remove: must not change the namespace table either. ---
                int namespaceCountBeforeRemove = server.NamespaceUris.Count;
                uint urisVersionBeforeRemove = urisVersionAfterReload;

                await m_server.NodeManagerLifecycle.RemoveAsync(registration).ConfigureAwait(false);

                EventFieldList removeEvent;
                (removeEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(removeEvent, "A live NodeManager was removed.");

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBeforeRemove));
                uint urisVersionAfterRemove = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfterRemove, Is.EqualTo(urisVersionBeforeRemove));
            }
            finally
            {
                requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                ArrayOf<uint> subscriptionIds = [subscriptionId];
                await services
                    .DeleteSubscriptionsAsync(requestHeader, subscriptionIds)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a subscription and a single reporting, data-change monitored item on the
        /// given node's Value attribute.
        /// </summary>
        private async Task<uint> CreateSubscriptionWithMonitoredItemAsync(
            ServerTestServices services,
            NodeId nodeId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscriptionResponse = await services
                .CreateSubscriptionAsync(requestHeader, 100, 100, 10, 0, true, 0)
                .ConfigureAwait(false);
            uint subscriptionId = subscriptionResponse.SubscriptionId;

            ArrayOf<MonitoredItemCreateRequest> monitoredItems =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                }
            ];

            requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse createItemsResponse = await services
                .CreateMonitoredItemsAsync(requestHeader, subscriptionId, TimestampsToReturn.Both, monitoredItems)
                .ConfigureAwait(false);

            Assert.That(createItemsResponse.Results.Count, Is.EqualTo(1));
            Assert.That(createItemsResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            return subscriptionId;
        }

        /// <summary>
        /// Deletes the given subscription so it no longer owns any monitored items.
        /// </summary>
        private async Task DeleteSubscriptionAsync(ServerTestServices services, uint subscriptionId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ArrayOf<uint> subscriptionIds = [subscriptionId];
            DeleteSubscriptionsResponse response = await services
                .DeleteSubscriptionsAsync(requestHeader, subscriptionIds)
                .ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Builds a reporting event monitored item on <see cref="ObjectIds.Server"/> that
        /// selects <c>EventType</c>, <c>SourceNode</c>, <c>SourceName</c>, and
        /// <c>Message</c>, restricted by a <c>WHERE</c> clause to events whose
        /// <c>EventType</c> is exactly <see cref="ObjectTypeIds.BaseModelChangeEventType"/>
        /// (the lifecycle provider's own notification; never a subtype, so unrelated
        /// events such as a <c>SemanticChangeEventType</c> are never delivered).
        /// </summary>
        private static MonitoredItemCreateRequest CreateModelChangeEventMonitoredItem(
            uint clientHandle,
            uint queueSize)
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.SourceNode));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.SourceName));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Message));

            eventFilter.WhereClause.Push(
                FilterOperator.Equals,
                Variant.FromStructure(new SimpleAttributeOperand
                {
                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                    AttributeId = Attributes.Value,
                    BrowsePath = [QualifiedName.From(BrowseNames.EventType)]
                }),
                Variant.FromStructure(new LiteralOperand
                {
                    Value = Variant.From(ObjectTypeIds.BaseModelChangeEventType)
                }));

            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = 0,
                    QueueSize = queueSize,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(eventFilter)
                }
            };
        }

        /// <summary>
        /// Publishes on <paramref name="subscriptionId"/> in a bounded loop, acknowledging
        /// previously delivered sequence numbers on each call, until a notification
        /// carrying at least one event arrives. Live event delivery depends on the
        /// server's own background publish timer (not on any sleep in the test); the
        /// per-call <see cref="CancellationTokenSource"/> only guards against a hang if
        /// delivery never happens, and the bounded attempt count guards against an
        /// unbounded loop - neither is used to assert timing.
        /// </summary>
        private async Task<(EventFieldList EventFields, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishForModelChangeEventAsync(
                ServerTestServices services,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            const int MaxPublishAttempts = 20;
            EventFieldList eventFields = null;

            for (int attempt = 0; attempt < MaxPublishAttempts; attempt++)
            {
                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await services
                    .PublishAsync(requestHeader, acknowledgements, timeoutCts.Token)
                    .ConfigureAwait(false);

                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));

                acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(
                    sequenceNumber => new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = sequenceNumber
                    });

                if (response.NotificationMessage is { } message && message.NotificationData.Count > 0)
                {
                    var deliveredEvents = new List<EventFieldList>();
                    foreach (ExtensionObject notificationData in message.NotificationData)
                    {
                        if (notificationData.TryGetValue(out EventNotificationList eventNotification) &&
                            eventNotification.Events.Count > 0)
                        {
                            eventNotification.Events.ForEach(deliveredEvents.Add);
                        }
                    }

                    if (deliveredEvents.Count > 0)
                    {
                        Assert.That(
                            deliveredEvents,
                            Has.Count.EqualTo(1),
                            "Each lifecycle operation must publish exactly one model-change event.");
                        eventFields = deliveredEvents[0];
                        break;
                    }
                }
            }

            Assert.That(
                eventFields,
                Is.Not.Null,
                $"No model-change event notification for subscription {subscriptionId} " +
                $"arrived within {MaxPublishAttempts} bounded publish attempts.");
            return (eventFields, acknowledgements);
        }

        /// <summary>
        /// Asserts that the selected <c>EventType</c>/<c>SourceNode</c>/<c>SourceName</c>/
        /// <c>Message</c> fields match a live NodeManager lifecycle model-change
        /// notification with the given exact message.
        /// </summary>
        private static void AssertModelChangeEvent(EventFieldList eventFields, string expectedMessage)
        {
            ArrayOf<Variant> fields = eventFields.EventFields;
            Assert.That(fields.Count, Is.EqualTo(4));

            Assert.That(fields[0].TryGetValue(out NodeId eventType), Is.True);
            Assert.That(eventType, Is.EqualTo(ObjectTypeIds.BaseModelChangeEventType));

            Assert.That(fields[1].TryGetValue(out NodeId sourceNode), Is.True);
            Assert.That(sourceNode, Is.EqualTo(ObjectIds.Server));

            Assert.That(fields[2].TryGetValue(out string sourceName), Is.True);
            Assert.That(sourceName, Is.EqualTo("Server"));

            Assert.That(fields[3].TryGetValue(out LocalizedText message), Is.True);
            Assert.That(message.Text, Is.EqualTo(expectedMessage));
        }

        /// <summary>
        /// Builds the <see cref="RuntimeNodeSetOptions"/> for a single in-memory source
        /// owning <paramref name="namespaceUri"/>, with its root/value shape defined by
        /// <see cref="BuildNodeSetXml"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Independently confirmed production defect:</b> a standard single-level
        /// NodeSet2 <c>&lt;Value&gt;&lt;uax:Int32&gt;N&lt;/uax:Int32&gt;&lt;/Value&gt;</c>
        /// payload is imported as the CLR default (<c>0</c>) instead of <c>N</c> by
        /// <c>UANodeSetHelpers.Import</c> / the underlying <c>XmlDecoder</c> path. The XML
        /// below therefore carries no <c>&lt;Value&gt;</c> element for its scalar variable
        /// (leaving one in place would misleadingly imply it is honored), and the
        /// <see cref="RuntimeNodeSetOptions.Configure"/> callback seeds the concrete value
        /// through the supported post-import fluent
        /// <see cref="Server.Fluent.INodeManagerBuilder.Variable{TValue}(string)"/>
        /// hook instead. This keeps the Read-service assertions proving real, concrete
        /// values end-to-end without weakening them, while routing around the defect at the
        /// test-authoring level. The defect itself is a pre-existing production issue and
        /// should be reported/fixed separately.
        /// </para>
        /// </remarks>
        private static RuntimeNodeSetOptions CreateOptions(string namespaceUri, int value)
        {
            string xml = BuildNodeSetXml(namespaceUri);

            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        $"NodeManagerLifecycleTests-{namespaceUri}",
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [namespaceUri])
                ],
                Configure = builder =>
                    // See the defect note above: seed the concrete Int32 value here rather
                    // than relying on the NodeSet2 <Value> import, which is defective for
                    // this scalar shape. Assigning through the resolved node's Value
                    // property also establishes the Good status code a freshly imported,
                    // value-less variable otherwise lacks (BadWaitingForInitialData).
                    builder.Variable<int>($"{kRootBrowseName}/{kValueBrowseName}").Node.Value = value
            };
        }

        /// <summary>
        /// Builds the shared-namespace generation options used by the multi-generation
        /// tests: the same model namespace and node identities across generations, with a
        /// generation-specific concrete Int32 value.
        /// </summary>
        private static RuntimeNodeSetOptions CreateGenerationOptions(int generation)
        {
            return CreateOptions(
                kModelNamespaceUri,
                generation == 1 ? kGeneration1Value : kGeneration2Value);
        }

        /// <summary>
        /// Builds a synthetic NodeSet2 document with a root object organized under Objects
        /// and a readable Int32 value variable. The scalar variable carries no
        /// <c>&lt;Value&gt;</c> element; see the defect note on <see cref="CreateOptions"/>
        /// for why its concrete value is instead wired through the fluent
        /// <c>Configure</c> callback.
        /// </summary>
        private static string BuildNodeSetXml(string namespaceUri)
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris>
                    <Uri>{namespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{namespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={kRootNodeId}" BrowseName="1:{kRootBrowseName}">
                    <DisplayName>{kRootBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35">ns=1;i={kValueNodeId}</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i={kValueNodeId}" BrowseName="1:{kValueBrowseName}"
                              ParentNodeId="ns=1;i={kRootNodeId}" DataType="i=6" ValueRank="-1"
                              AccessLevel="3" UserAccessLevel="3">
                    <DisplayName>{kValueBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=63</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">ns=1;i={kRootNodeId}</Reference>
                    </References>
                  </UAVariable>
                </UANodeSet>
                """;
        }

        /// <summary>
        /// Reads a single node attribute through the Read service and validates the response
        /// and diagnostic shape.
        /// </summary>
        private async Task<DataValue> ReadValueAsync(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            ArrayOf<ReadValueId> readIds =
                [new ReadValueId { NodeId = nodeId, AttributeId = attributeId }];

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse response = await m_server.ReadAsync(
                m_secureChannelContext,
                requestHeader,
                kMaxAge,
                TimestampsToReturn.Neither,
                readIds,
                RequestLifetime.None).ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, response.Results, readIds);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                readIds,
                response.ResponseHeader.StringTable,
                m_logger);

            return response.Results[0];
        }

        /// <summary>
        /// Reads the Server object's <c>UrisVersion</c> variable.
        /// </summary>
        private async Task<uint> ReadUrisVersionAsync()
        {
            DataValue value = await ReadValueAsync(VariableIds.Server_UrisVersion).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            return value.WrappedValue.GetUInt32();
        }

        /// <summary>
        /// Counts the entries in an <see cref="ArrayOf{T}"/> that satisfy the predicate.
        /// </summary>
        /// <typeparam name="T">The type of item in the array.</typeparam>
        private static int CountMatches<T>(ArrayOf<T> array, Predicate<T> predicate)
        {
            int count = 0;
            array.ForEach(item =>
            {
                if (predicate(item))
                {
                    count++;
                }
            });
            return count;
        }

        /// <summary>
        /// Identifies which live lifecycle operation is under test in the parameterized
        /// identity-mismatch test.
        /// </summary>
        public enum LifecycleOperation
        {
            Reload,
            Remove
        }

        /// <summary>
        /// Identifies the kind of registration-identity mismatch under test in the
        /// parameterized identity-mismatch test.
        /// </summary>
        public enum MismatchKind
        {
            StaleGeneration,
            ForeignId,
            ForeignNodeManager
        }

        /// <summary>
        /// A distinctive sentinel exception used to prove that a factory- or
        /// address-space-creation failure propagates unchanged through the lifecycle
        /// provider rather than being swallowed or wrapped.
        /// </summary>
        private sealed class SentinelException : Exception
        {
            public SentinelException()
            {
            }

            public SentinelException(string message)
                : base(message)
            {
            }

            public SentinelException(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }
    }
}
