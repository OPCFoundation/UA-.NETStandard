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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

// Test code exercises RequestManager.RequestCompleted, which is obsolete for callers
// because requests are completed by disposing the OperationContext.
#pragma warning disable CS0618

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Live lifecycle integration tests for <see cref="INodeManagerLifecycle"/> and its
    /// <see cref="NodeManagerRegistration"/> handles against a real, running
    /// <see cref="ReferenceServer"/>, focused on registration identity/copy semantics,
    /// monitored-item transitions, and rollback paths for Add/Reload/Remove.
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
        private const uint kEuRangeNodeId = 8002;
        private const string kRootBrowseName = "LifecycleRoot";
        private const string kValueBrowseName = "LifecycleValue";
        private const int kGeneration1Value = 1;
        private const int kGeneration2Value = 2;
        private const int kFirstRegistrationValue = 101;
        private const int kSecondRegistrationValue = 202;

        private string m_pkiRoot;
        private ServerFixture<LifecycleTestServer> m_fixture;
        private LifecycleTestServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ILogger m_logger;
        private HashSet<Guid> m_startupRegistrationIds;

        /// <summary>
        /// Shared by every Session the fixture activates. A subscription transfer between
        /// two anonymous Sessions is only permitted when both report the same client
        /// ApplicationUri (OPC 10000-4 §5.7.3.1).
        /// </summary>
        private const string kClientApplicationUri =
            "urn:localhost:opcfoundation.org:NodeManagerLifecycleTests";

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

            m_fixture = new ServerFixture<LifecycleTestServer>(t => new LifecycleTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create().CreateLogger<NodeManagerLifecycleTests>();
            m_startupRegistrationIds = [];
            m_server.NodeManagerLifecycle.Registrations.ForEach(
                registration => m_startupRegistrationIds.Add(registration.Id));

            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    clientApplicationUri: kClientApplicationUri)
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
        /// A custom-routed NodeManager may return a null namespace list. The
        /// lifecycle handle must preserve that shape rather than rejecting a
        /// manager the master routing layer accepts.
        /// </summary>
        [Test]
        public void NodeManagerRegistrationAllowsNullNamespaceUris()
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager.Setup(manager => manager.NamespaceUris).Returns((IEnumerable<string>)null);

            var registration = new NodeManagerRegistration(
                Guid.NewGuid(),
                1,
                nodeManager.Object);

            Assert.That(registration.NamespaceUris.IsNull, Is.True);
        }

        /// <summary>
        /// A custom-routed manager with no namespace list must keep its place in
        /// the master manager list across reload and removal.
        /// </summary>
        [Test]
        public async Task NullNamespaceNodeManagerCanReloadAndRemoveAsync()
        {
            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(namespaceUri: null);
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(factory => factory.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);

            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(originalFactory.Object, null)
                .ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(namespaceUri: null);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(factory => factory.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);

            NodeManagerRegistration replacement = await m_server.NodeManagerLifecycle
                .ReloadAsync(original, replacementFactory.Object, null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(original.NamespaceUris.IsNull, Is.True);
                Assert.That(replacement.NamespaceUris.IsNull, Is.True);
                Assert.That(replacement.Id, Is.EqualTo(original.Id));
                Assert.That(replacement.Generation, Is.EqualTo(2));
                Assert.That(
                    m_server.CurrentInstance.NodeManager.AsyncNodeManagers.Contains(
                        replacementManager.Object),
                    Is.True);
            });

            await m_server.NodeManagerLifecycle
                .RemoveAsync(replacement, null)
                .ConfigureAwait(false);

            Assert.That(
                m_server.CurrentInstance.NodeManager.AsyncNodeManagers.Contains(
                    replacementManager.Object),
                Is.False);
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
                .AddRuntimeNodeSetAsync(CreateOptions(kModelNamespaceUri, kFirstRegistrationValue), null)
                .ConfigureAwait(false);

            ArrayOf<NodeManagerRegistration> snapshotAfterFirstAdd =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(
                snapshotAfterFirstAdd.Count,
                Is.EqualTo(m_startupRegistrationIds.Count + 1));

            NodeManagerRegistration second = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(kSecondModelNamespaceUri, kSecondRegistrationValue), null)
                .ConfigureAwait(false);

            // The earlier snapshot variable must remain frozen at its original contents.
            Assert.That(
                snapshotAfterFirstAdd.Count,
                Is.EqualTo(m_startupRegistrationIds.Count + 1));
            Assert.That(
                snapshotAfterFirstAdd.Find(registration =>
                    registration.Id == first.Id),
                Is.SameAs(first));

            ArrayOf<NodeManagerRegistration> snapshotAfterSecondAdd =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(
                snapshotAfterSecondAdd.Count,
                Is.EqualTo(m_startupRegistrationIds.Count + 2));

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
            Assert.That(
                registrationsAfterSnapshotReads.Count,
                Is.EqualTo(m_startupRegistrationIds.Count + 2));
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
        /// must be rejected by Reload, ShadowReload, and Remove with the provider's
        /// ownership-mismatch message, without invoking a replacement factory and without
        /// changing the current generation's registration, routing, value, or namespace
        /// state.
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
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.StaleGeneration)]
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.ForeignId)]
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.ForeignNodeManager)]
        public async Task RegistrationIdentityMismatchIsRejectedWithoutChangingCurrentGenerationAsync(
            LifecycleOperation operation,
            MismatchKind mismatchKind)
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            // Establish a genuinely current generation (2) and a genuinely stale handle
            // (the original, now-superseded generation-1 handle).
            NodeManagerRegistration current = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2), null)
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

            switch (operation)
            {
                case LifecycleOperation.Reload:
                {
                    var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

                    Assert.That(
                        async () => await m_server.NodeManagerLifecycle
                            .ReloadAsync(mismatched, replacementFactory.Object, null)
                            .ConfigureAwait(false),
                        Throws.InvalidOperationException.With.Message.Contains(expectedMessage));

                    replacementFactory.Verify(
                        f => f.CreateAsync(
                            It.IsAny<IServerInternal>(),
                            It.IsAny<ApplicationConfiguration>(),
                            It.IsAny<CancellationToken>()),
                        Times.Never);
                    break;
                }
                case LifecycleOperation.ShadowReload:
                {
                    var replacementFactory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

                    Assert.That(
                        async () => await m_server.NodeManagerLifecycle
                            .ShadowReloadAsync(mismatched, replacementFactory.Object)
                            .ConfigureAwait(false),
                        Throws.InvalidOperationException.With.Message.Contains(expectedMessage));

                    replacementFactory.Verify(
                        f => f.CreateAsync(
                            It.IsAny<IServerInternal>(),
                            It.IsAny<ApplicationConfiguration>(),
                            It.IsAny<CancellationToken>()),
                        Times.Never);
                    break;
                }
                default:
                    Assert.That(
                        async () => await m_server.NodeManagerLifecycle
                            .RemoveAsync(mismatched, null)
                            .ConfigureAwait(false),
                        Throws.InvalidOperationException.With.Message.Contains(expectedMessage));
                    break;
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
                    .AddAsync(factory.Object, null)
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

        [Test]
        public void AddAsyncFromAnExecutingRequestRejectsWithoutInvokingFactory()
        {
            IServerInternal server = m_server.CurrentInstance;
            var factory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

            using var requestLifetime = new RequestLifetime();
            using OperationContext callerContext = CreateExecutingRequest(server, requestLifetime);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, callerContext, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "cannot run from an OPC UA request callback"));
            factory.Verify(
                candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReloadAndRemoveFromAnExecutingRequestAreRejectedAsync()
        {
            // Reload and Remove look up the registration to evaluate its per-registration opt-in
            // before the request-callback guard runs, so the target must be a real, owned and
            // non-opted-in registration for the guard - not the staleness check - to reject it.
            IServerInternal server = m_server.CurrentInstance;
            var replacement = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(
                    CreateGenerationOptions(generation: 1),
                    null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            using (var requestLifetime = new RequestLifetime())
            using (OperationContext callerContext = CreateExecutingRequest(server, requestLifetime))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        async () => await m_server.NodeManagerLifecycle
                            .ReloadAsync(
                                registration,
                                replacement.Object,
                                callerContext,
                                CancellationToken.None)
                            .ConfigureAwait(false),
                        Throws.InvalidOperationException.With.Message.Contains(
                            "cannot run from an OPC UA request callback"));
                    Assert.That(
                        async () => await m_server.NodeManagerLifecycle
                            .RemoveAsync(registration, callerContext, CancellationToken.None)
                            .ConfigureAwait(false),
                        Throws.InvalidOperationException.With.Message.Contains(
                            "cannot run from an OPC UA request callback"));
                });
            }

            replacement.Verify(
                candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, null, CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AddAsyncFromAContextThatIsNotAnExecutingRequestIsAllowedAsync()
        {
            // An internal operation carries a context of its own that was never enrolled as a
            // request. It cannot wait for itself, so the lifecycle operation must proceed. The
            // ambient marker this guard replaced could not tell the two apart.
            using var requestLifetime = new RequestLifetime();
            using OperationContext callerContext = CreateRequestContext(requestLifetime);

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(
                    CreateGenerationOptions(generation: 1),
                    callerContext,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(registration, Is.Not.Null);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, callerContext, CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AddAsyncAfterTheRequestCompletedIsAllowedAsync()
        {
            // The guard is bound to the request that is executing, not to the flow that served
            // it, so a context whose scope has been disposed no longer blocks the lifecycle.
            IServerInternal server = m_server.CurrentInstance;
            using var requestLifetime = new RequestLifetime();
            OperationContext callerContext = CreateExecutingRequest(server, requestLifetime);
            callerContext.Dispose();

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(
                    CreateGenerationOptions(generation: 1),
                    callerContext,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(registration, Is.Not.Null);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, callerContext, CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        public void LifecycleCallFromANodeManagerCallbackContextIsRejected()
        {
            // A NodeManager or Method callback receives an ISystemContext that was copied for
            // the request. Handing that operation to the lifecycle is the explicit replacement
            // for the ambient marker, so it must be both reachable and rejected.
            IServerInternal server = m_server.CurrentInstance;
            var factory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);

            using var requestLifetime = new RequestLifetime();
            using OperationContext callerContext = CreateExecutingRequest(server, requestLifetime);
            ISystemContext callbackContext = server.DefaultSystemContext.Copy(callerContext);

            Assert.That(callbackContext.GetOperationContext(), Is.SameAs(callerContext));
            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, callbackContext.GetOperationContext(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "cannot run from an OPC UA request callback"));
        }

        /// <summary>
        /// Creates a context that is enrolled as an executing request, the way
        /// <see cref="StandardServer"/> enrols a validated request. Disposing the context
        /// completes the request.
        /// </summary>
        private static OperationContext CreateExecutingRequest(
            IServerInternal server,
            RequestLifetime requestLifetime)
        {
            OperationContext context = CreateRequestContext(requestLifetime);
            context.AttachRequestScope(server.RequestManager.EnterRequestScope(context));
            return context;
        }

        private static OperationContext CreateRequestContext(RequestLifetime requestLifetime)
        {
            return new OperationContext(
                new RequestHeader { RequestHandle = 1, TimeoutHint = 0 },
                null,
                RequestType.Read,
                requestLifetime);
        }

        [Test]
        public async Task OptedInRuntimeNodeSetCanBeAddedFromRequestScopeAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var context = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            RuntimeNodeSetOptions options = CreateGenerationOptions(generation: 1);
            options.AllowLifecycleFromRequestCallback = true;
            NodeManagerRegistration registration = null;

            try
            {
                using IDisposable requestScope =
                    server.RequestManager.EnterRequestScope(context);
                registration = await m_server.NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(options, context)
                    .ConfigureAwait(false);

                Assert.That(registration, Is.Not.Null);
                Assert.That(registration.Generation, Is.EqualTo(1));
            }
            finally
            {
                server.RequestManager.RequestCompleted(context);
            }

            if (registration is not null)
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task OptedInRuntimeNodeSetCanBeReloadedAndRemovedFromRequestScopeAsync()
        {
            RuntimeNodeSetOptions initialOptions = CreateGenerationOptions(generation: 1);
            initialOptions.AllowLifecycleFromRequestCallback = true;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(initialOptions, null)
                .ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var context = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);

            try
            {
                using IDisposable requestScope =
                    server.RequestManager.EnterRequestScope(context);
                RuntimeNodeSetOptions replacement = CreateGenerationOptions(generation: 2);
                replacement.AllowLifecycleFromRequestCallback = true;
                registration = await m_server.NodeManagerLifecycle
                    .ShadowReloadRuntimeNodeSetAsync(registration, replacement)
                    .ConfigureAwait(false);
                Assert.That(registration.Generation, Is.EqualTo(2));

                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, context)
                    .ConfigureAwait(false);
            }
            finally
            {
                server.RequestManager.RequestCompleted(context);
            }

            bool remainsRegistered = false;
            foreach (NodeManagerRegistration candidate
                in m_server.NodeManagerLifecycle.Registrations)
            {
                remainsRegistered |= candidate.Id == registration.Id;
            }
            Assert.That(remainsRegistered, Is.False);
        }

        [Test]
        public async Task SimultaneousCallbackSafeAddsExcludePeerLifecycleWaitersAsync()
        {
            const string NamespaceUriA =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackAddA";
            const string NamespaceUriB =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackAddB";
            IServerInternal server = m_server.CurrentInstance;
            Mock<IAsyncNodeManager> managerA = CreateLifecycleNodeManager(NamespaceUriA);
            Mock<IAsyncNodeManager> managerB = CreateLifecycleNodeManager(NamespaceUriB);
            var firstFactoryEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFactory = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRequestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factoryA = new CallbackSafeNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return managerA.Object;
                });
            var factoryB = new CallbackSafeNodeManagerFactory(
                [NamespaceUriB],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(managerB.Object));

            Task<NodeManagerRegistration> addA = RunLifecycleCallbackRequestAsync(
                server,
                () => m_server.NodeManagerLifecycle.AddAsync(factoryA, null).AsTask());
            await firstFactoryEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Task<NodeManagerRegistration> addB = RunLifecycleCallbackRequestAsync(
                server,
                () => m_server.NodeManagerLifecycle.AddAsync(factoryB, null).AsTask(),
                secondRequestEntered);
            await secondRequestEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            try
            {
                await server.RequestManager
                    .WaitForCurrentRequestsAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            finally
            {
                releaseFirstFactory.TrySetResult(true);
            }

            NodeManagerRegistration[] registrations = await Task
                .WhenAll(addA, addB)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(factoryA.CreateCount, Is.EqualTo(1));
            Assert.That(factoryB.CreateCount, Is.EqualTo(1));

            foreach (NodeManagerRegistration registration in registrations)
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SimultaneousCallbackSafeReloadsExcludePeerLifecycleWaitersAsync()
        {
            const string NamespaceUriA =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackReloadA";
            const string NamespaceUriB =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackReloadB";
            IServerInternal server = m_server.CurrentInstance;
            Mock<IAsyncNodeManager> originalManagerA =
                CreateLifecycleNodeManager(NamespaceUriA);
            Mock<IAsyncNodeManager> originalManagerB =
                CreateLifecycleNodeManager(NamespaceUriB);
            originalManagerA
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            originalManagerB
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            NodeManagerRegistration originalA = await m_server.NodeManagerLifecycle
                .AddAsync(
                    new CallbackSafeNodeManagerFactory(
                        [NamespaceUriA],
                        (_, _, _) => new ValueTask<IAsyncNodeManager>(
                            originalManagerA.Object)), null)
                .ConfigureAwait(false);
            NodeManagerRegistration originalB = await m_server.NodeManagerLifecycle
                .AddAsync(
                    new CallbackSafeNodeManagerFactory(
                        [NamespaceUriB],
                        (_, _, _) => new ValueTask<IAsyncNodeManager>(
                            originalManagerB.Object)), null)
                .ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManagerA =
                CreateLifecycleNodeManager(NamespaceUriA);
            Mock<IAsyncNodeManager> replacementManagerB =
                CreateLifecycleNodeManager(NamespaceUriB);
            var firstFactoryEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFactory = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRequestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var replacementFactoryA = new CallbackSafeNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return replacementManagerA.Object;
                });
            var replacementFactoryB = new CallbackSafeNodeManagerFactory(
                [NamespaceUriB],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(
                    replacementManagerB.Object));

            Task<NodeManagerRegistration> reloadA = RunLifecycleCallbackRequestAsync(
                server,
                () => m_server.NodeManagerLifecycle
                    .ReloadAsync(originalA, replacementFactoryA, null)
                    .AsTask());
            await firstFactoryEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Task<NodeManagerRegistration> reloadB = RunLifecycleCallbackRequestAsync(
                server,
                () => m_server.NodeManagerLifecycle
                    .ReloadAsync(originalB, replacementFactoryB, null)
                    .AsTask(),
                secondRequestEntered);
            await secondRequestEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            try
            {
                await server.RequestManager
                    .WaitForCurrentRequestsAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            finally
            {
                releaseFirstFactory.TrySetResult(true);
            }

            NodeManagerRegistration[] replacements = await Task
                .WhenAll(reloadA, reloadB)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(replacementFactoryA.CreateCount, Is.EqualTo(1));
            Assert.That(replacementFactoryB.CreateCount, Is.EqualTo(1));

            foreach (NodeManagerRegistration replacement in replacements)
            {
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(replacement, null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ShutdownWaitsForSimultaneousCallbackSafeLifecycleOperationsAsync()
        {
            const string NamespaceUriA =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackShutdownA";
            const string NamespaceUriB =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:CallbackShutdownB";
            IServerInternal server = m_server.CurrentInstance;
            Mock<IAsyncNodeManager> managerA = CreateLifecycleNodeManager(NamespaceUriA);
            Mock<IAsyncNodeManager> managerB = CreateLifecycleNodeManager(NamespaceUriB);
            managerA.As<IDisposable>();
            managerB.As<IDisposable>();
            var firstFactoryEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirstFactory = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRequestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factoryA = new CallbackSafeNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return managerA.Object;
                });
            var factoryB = new CallbackSafeNodeManagerFactory(
                [NamespaceUriB],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(managerB.Object));
            var lifecycle = new NodeManagerLifecycle(m_server);
            try
            {
                Task<NodeManagerRegistration> addA = RunLifecycleCallbackRequestAsync(
                    server,
                    () => lifecycle.AddAsync(factoryA, null).AsTask());
                await firstFactoryEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Task<NodeManagerRegistration> addB = RunLifecycleCallbackRequestAsync(
                    server,
                    () => lifecycle.AddAsync(factoryB, null).AsTask(),
                    secondRequestEntered);
                await secondRequestEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await server.RequestManager
                    .WaitForCurrentRequestsAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                Task shutdown = lifecycle.BeginShutdownAsync(server).AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                releaseFirstFactory.TrySetResult(true);

                await shutdown
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await AssertLifecycleOperationStopsForShutdownAsync(addA)
                    .ConfigureAwait(false);
                await AssertLifecycleOperationStopsForShutdownAsync(addB)
                    .ConfigureAwait(false);
                Assert.That(
                    factoryB.CreateCount,
                    Is.Zero,
                    "The queued callback operation must observe shutdown before creating a manager.");

                await lifecycle.CompleteShutdownAsync(server).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
            }
            finally
            {
                releaseFirstFactory.TrySetResult(true);
                lifecycle.Dispose();
            }
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
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
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
                    .ReloadAsync(original, replacementFactory.Object, null)
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
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
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
                    .ReloadAsync(original, replacementFactory.Object, null)
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
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
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
                    .ReloadAsync(original, replacementFactory.Object, null)
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
        /// Reload transfers a compatible monitored item to the replacement generation
        /// without changing its object identity or publishing a transient bad status.
        /// </summary>
        [Test]
        public async Task ReloadAsyncRetainsCompatibleMonitoredItemAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                    .ConfigureAwait(false);
            var subscription = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                .GetSubscriptions()
                .Single(candidate => candidate.Id == subscriptionId);
            IMonitoredItem originalItem = subscription
                .GetMonitoredItemsSnapshot(original.NodeManager)
                .Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (MonitoredItemNotification initial, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(initial.ClientHandle, Is.EqualTo(1));
                Assert.That(initial.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(initial.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));

                NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2), null)
                    .ConfigureAwait(false);

                IMonitoredItem reloadedItem = subscription
                    .GetMonitoredItemsSnapshot(reloaded.NodeManager)
                    .Single();
                Assert.That(ReferenceEquals(reloadedItem, originalItem), Is.True);
                Assert.That(reloadedItem.Id, Is.EqualTo(monitoredItemId));

                (MonitoredItemNotification current, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(current.ClientHandle, Is.EqualTo(1));
                Assert.That(current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reload waits for an in-flight monitored-item mutation before transferring ownership.
        /// </summary>

        /// <summary>
        /// Reload detaches a dropped NodeId, publishes BadNodeIdUnknown once, and recovers
        /// the same monitored item when a later generation restores the compatible node.
        /// </summary>
        [Test]
        public async Task ReloadAsyncDroppedNodeRecoversSameMonitoredItemAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                    .ConfigureAwait(false);
            var subscription = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                .GetSubscriptions()
                .Single(candidate => candidate.Id == subscriptionId);
            IMonitoredItem originalItem = subscription
                .GetMonitoredItemsSnapshot(original.NodeManager)
                .Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (_, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);

                NodeManagerRegistration dropped = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(original, CreateDroppedGenerationOptions(), null)
                    .ConfigureAwait(false);

                IMonitoredItem detachedItem = subscription
                    .GetRecoverableMonitoredItemsSnapshot([valueNodeId])
                    .Single();
                Assert.That(ReferenceEquals(detachedItem, originalItem), Is.True);

                (MonitoredItemNotification bad, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(bad.ClientHandle, Is.EqualTo(1));
                Assert.That(bad.Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                NodeManagerRegistration recovered = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(dropped, CreateGenerationOptions(generation: 2), null)
                    .ConfigureAwait(false);

                IMonitoredItem recoveredItem = subscription
                    .GetMonitoredItemsSnapshot(recovered.NodeManager)
                    .Single();
                Assert.That(ReferenceEquals(recoveredItem, originalItem), Is.True);

                (MonitoredItemNotification current, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Remove preserves the subscription and monitored item, publishes BadNodeIdUnknown,
        /// recovers on a later add, and permits client-side deletion of the same item id.
        /// </summary>
        [Test]
        public async Task RemoveAsyncPublishesBadRecoversAndClientDeletesSameItemAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                    .ConfigureAwait(false);
            var subscription = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                .GetSubscriptions()
                .Single(candidate => candidate.Id == subscriptionId);
            IMonitoredItem originalItem = subscription
                .GetMonitoredItemsSnapshot(original.NodeManager)
                .Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (_, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);

                await m_server.NodeManagerLifecycle.RemoveAsync(original, null).ConfigureAwait(false);

                IMonitoredItem detachedItem = subscription
                    .GetRecoverableMonitoredItemsSnapshot([valueNodeId])
                    .Single();
                Assert.That(ReferenceEquals(detachedItem, originalItem), Is.True);

                (MonitoredItemNotification bad, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(bad.Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                NodeManagerRegistration added = await m_server.NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 2), null)
                    .ConfigureAwait(false);
                IMonitoredItem recoveredItem = subscription
                    .GetMonitoredItemsSnapshot(added.NodeManager)
                    .Single();
                Assert.That(ReferenceEquals(recoveredItem, originalItem), Is.True);

                (MonitoredItemNotification current, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                DeleteMonitoredItemsResponse deleteResponse = await m_server
                    .DeleteMonitoredItemsAsync(
                        m_secureChannelContext,
                        requestHeader,
                        subscriptionId,
                        [monitoredItemId],
                        RequestLifetime.None).ConfigureAwait(false);
                Assert.That(deleteResponse.Results, Is.EqualTo([StatusCodes.Good]));
                Assert.That(
                    subscription.GetMonitoredItemsSnapshot(added.NodeManager),
                    Is.Empty);
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// DeleteNodes publishes BadNodeIdUnknown and AddNodes recovers the same item.
        /// </summary>
        [Test]
        public async Task DeleteNodesThenAddNodesRecoversSameMonitoredItemAsync()
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken _) => new ValueTask<IAsyncNodeManager>(
                        new NodeManagementLifecycleNodeManager(
                            server,
                            configuration,
                            m_logger,
                            kModelNamespaceUri)));
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                    .ConfigureAwait(false);
            var subscription = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                .GetSubscriptions()
                .Single(candidate => candidate.Id == subscriptionId);
            IMonitoredItem originalItem = subscription
                .GetMonitoredItemsSnapshot(registration.NodeManager)
                .Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (_, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);

                RequestHeader requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                DeleteNodesResponse deleteResponse = await m_server.DeleteNodesAsync(
                    m_secureChannelContext,
                    requestHeader,
                    [new DeleteNodesItem
                    {
                        NodeId = valueNodeId,
                        DeleteTargetReferences = true
                    }],
                    RequestLifetime.None).ConfigureAwait(false);
                Assert.That(deleteResponse.Results, Is.EqualTo([StatusCodes.Good]));

                (MonitoredItemNotification bad, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(bad.Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                var attributes = new VariableAttributes
                {
                    SpecifiedAttributes =
                        (uint)NodeAttributesMask.DisplayName |
                        (uint)NodeAttributesMask.Value |
                        (uint)NodeAttributesMask.DataType |
                        (uint)NodeAttributesMask.ValueRank |
                        (uint)NodeAttributesMask.AccessLevel,
                    DisplayName = new LocalizedText(kValueBrowseName),
                    Value = new Variant(kGeneration2Value),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead
                };
                requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                AddNodesResponse addResponse = await m_server.AddNodesAsync(
                    m_secureChannelContext,
                    requestHeader,
                    [new AddNodesItem
                    {
                        ParentNodeId = rootNodeId,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        RequestedNewNodeId = valueNodeId,
                        BrowseName = new QualifiedName(kValueBrowseName, ns),
                        NodeClass = NodeClass.Variable,
                        NodeAttributes = new ExtensionObject(attributes)
                    }],
                    RequestLifetime.None).ConfigureAwait(false);
                Assert.That(addResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(addResponse.Results[0].AddedNodeId, Is.EqualTo(valueNodeId));

                IMonitoredItem recoveredItem = subscription
                    .GetMonitoredItemsSnapshot(registration.NodeManager)
                    .Single();
                Assert.That(ReferenceEquals(recoveredItem, originalItem), Is.True);
                Assert.That(recoveredItem.Id, Is.EqualTo(monitoredItemId));

                (MonitoredItemNotification current, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Unlike Reload and Remove, ShadowReload must succeed while the current
        /// generation owns an active reporting monitored item: the switch is committed
        /// and every new service request (here, Read) is atomically routed to the
        /// replacement generation, while the existing monitored item keeps being serviced
        /// by the retired (but not yet destroyed) current generation, including for a
        /// fresh value pushed directly on that retired generation's own node after the
        /// switch. Once the owning subscription is deleted, a later lifecycle operation
        /// opportunistically completes retired-generation cleanup and disposes the old
        /// generation's address space, without the lifecycle provider ever deleting the
        /// client's subscription itself.
        /// </summary>
        [Test]
        public async Task ShadowReloadAsyncKeepsActiveMonitoredItemAliveThenDisposesRetiredGenerationAfterDrainAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            const uint clientHandle = 1;

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) = await CreateSubscriptionWithMonitoredItemAsync(
                services,
                valueNodeId).ConfigureAwait(false);

            // Drain the initial data-change sample delivered on monitored-item creation so
            // the later publish loop only observes the value pushed after the switch.
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            (_, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);

            try
            {
                NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                    .ShadowReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2))
                    .ConfigureAwait(false);

                Assert.That(reloaded.Id, Is.EqualTo(original.Id));
                Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));
                Assert.That(ReferenceEquals(reloaded.NodeManager, original.NodeManager), Is.False);

                // New service requests must be atomically routed to the replacement; the
                // retired generation must no longer be reachable through routing.
                Assert.That(
                    master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, reloaded.NodeManager)),
                    Is.EqualTo(1));
                Assert.That(
                    master.NamespaceManagers[ns].Any(m => ReferenceEquals(m, original.NodeManager)),
                    Is.False);

                DataValue valueAfterSwitch = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(valueAfterSwitch.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(valueAfterSwitch.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                // The retired generation's own node must still be present, still owned by
                // the original manager instance, and unaffected by the switch.
                var originalValueState = (BaseVariableState)originalManager.Find(valueNodeId)!;
                Assert.That(originalValueState, Is.Not.Null);
                Assert.That(originalValueState.Value, Is.EqualTo(kGeneration1Value));

                ISubscription subscription = server.SubscriptionManager
                    .GetSubscriptions()
                    .Single(s => s.Id == subscriptionId);
                var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
                Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.True);
                Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);

                // Simulate an internal (device-driven) value push directly on the retired
                // generation's own node: it must still reach the existing monitored item.
                const int pushedValue = 777;
                originalValueState.Value = pushedValue;
                originalValueState.Timestamp = DateTimeUtc.Now;
                originalValueState.StatusCode = StatusCodes.Good;
                originalValueState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await originalValueState
                    .ClearChangeMasksAsync(server.DefaultSystemContext, includeChildren: false)
                    .ConfigureAwait(false);

                DataValue? pushedNotification;
                (pushedNotification, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements,
                    clientHandle).ConfigureAwait(false);
                Assert.That(pushedNotification, Is.Not.Null);
                Assert.That(pushedNotification!.Value.WrappedValue.GetInt32(), Is.EqualTo(pushedValue));

                // The replacement generation's own value must remain unaffected by the
                // push made directly on the retired generation.
                DataValue valueAfterPush = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(valueAfterPush.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            // With the owning subscription gone, a later lifecycle operation
            // opportunistically finishes retired-generation cleanup: the old generation's
            // own address space is torn down (DeleteAddressSpaceAsync empties its
            // PredefinedNodes) without the lifecycle provider ever deleting the client's
            // (already independently deleted) subscription itself.
            NodeManagerRegistration current = m_server.NodeManagerLifecycle.Registrations
                .Find(r => r.Id == original.Id);
            Assert.That(current, Is.Not.Null);
            await m_server.NodeManagerLifecycle.RemoveAsync(current, null).ConfigureAwait(false);

            Assert.That(originalManager.Find(valueNodeId), Is.Null);
            Assert.That(
                CountMatches(m_server.NodeManagerLifecycle.Registrations, r => r.Id == original.Id),
                Is.Zero);
        }

        /// <summary>
        /// Immediate reload switches new service requests to the replacement, queues
        /// BadNodeIdUnknown for every data monitored item owned by the prior generation,
        /// and disposes that generation without waiting for the subscription to drain.
        /// </summary>
        [Test]
        public async Task ImmediateReloadAsyncReportsBadNodeIdUnknownAndDisposesPriorGenerationAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            const uint clientHandle = 1;

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle).ConfigureAwait(false);
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            (_, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);

            RequestHeader queueHeader = m_requestHeader;
            queueHeader.Timestamp = DateTimeUtc.Now;
            ModifyMonitoredItemsResponse queueResponse = await services
                .ModifyMonitoredItemsAsync(
                    queueHeader,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    [
                        new MonitoredItemModifyRequest
                        {
                            MonitoredItemId = monitoredItemId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = clientHandle,
                                SamplingInterval = 0,
                                QueueSize = 5,
                                DiscardOldest = true
                            }
                        }
                    ])
                .ConfigureAwait(false);
            Assert.That(queueResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            try
            {
                NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                    .ImmediateReloadRuntimeNodeSetAsync(
                        original,
                        CreateGenerationOptions(generation: 2))
                    .ConfigureAwait(false);

                DataValue current = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(current.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(current.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                ISubscription subscription = server.SubscriptionManager
                    .GetSubscriptions()
                    .Single(s => s.Id == subscriptionId);
                var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
                Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
                Assert.That(originalManager.Find(valueNodeId), Is.Null,
                    "The prior generation must be disposed before immediate reload returns.");

                DataValue? retiredNotification;
                (retiredNotification, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements,
                    clientHandle).ConfigureAwait(false);
                Assert.That(retiredNotification, Is.Not.Null);
                Assert.That(
                    retiredNotification!.Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));

                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                ModifyMonitoredItemsResponse modifyResponse = await services
                    .ModifyMonitoredItemsAsync(
                        header,
                        subscriptionId,
                        TimestampsToReturn.Both,
                        [
                            new MonitoredItemModifyRequest
                            {
                                MonitoredItemId = monitoredItemId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = clientHandle,
                                    SamplingInterval = 0,
                                    QueueSize = 2,
                                    DiscardOldest = true
                                }
                            }
                        ])
                    .ConfigureAwait(false);
                Assert.That(
                    modifyResponse.Results[0].StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                SetMonitoringModeResponse samplingResponse = await services
                    .SetMonitoringModeAsync(
                        header,
                        subscriptionId,
                        MonitoringMode.Sampling,
                        [monitoredItemId])
                    .ConfigureAwait(false);
                Assert.That(samplingResponse.Results[0], Is.EqualTo(StatusCodes.Good));

                header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                SetMonitoringModeResponse modeResponse = await services
                    .SetMonitoringModeAsync(
                        header,
                        subscriptionId,
                        MonitoringMode.Reporting,
                        [monitoredItemId])
                    .ConfigureAwait(false);
                Assert.That(modeResponse.Results[0], Is.EqualTo(StatusCodes.Good));

                DataValue? resumedNotification;
                (resumedNotification, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements,
                    clientHandle).ConfigureAwait(false);
                Assert.That(resumedNotification, Is.Not.Null);
                Assert.That(
                    resumedNotification!.Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                DeleteMonitoredItemsResponse deleteResponse = await services
                    .DeleteMonitoredItemsAsync(header, subscriptionId, [monitoredItemId])
                    .ConfigureAwait(false);
                Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(subscription.MonitoredItemCount, Is.Zero);
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// After a ShadowReload, an ownership-sensitive data monitored item still owned by
        /// the retired generation must be dispatched to that generation for Modify,
        /// SetMonitoringMode (disable/re-enable), and Delete - not to the visible
        /// replacement generation that now serves the same namespace. Each operation must
        /// succeed (a <c>BadMonitoredItemIdInvalid</c> would prove the ownership defect,
        /// where the same-namespace replacement claims but cannot service the retired item),
        /// new Reads must be routed to the replacement, notifications pushed on the retired
        /// generation's own node must keep flowing, and once the final item drains via Delete
        /// the retired generation must be disposed promptly - without any further lifecycle
        /// operation.
        /// </summary>
        [Test]
        public async Task ShadowReloadedDataMonitoredItemIsModifiableToggleableAndDeletableOnRetiredGenerationThenDrainDisposesItAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            const uint clientHandle = 1;

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(services, valueNodeId, clientHandle)
                    .ConfigureAwait(false);

            // Drain the initial data-change sample delivered on monitored-item creation.
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            (_, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2))
                .ConfigureAwait(false);

            // The existing item stays owned by the retired generation; the replacement owns none.
            ISubscription subscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(s => s.Id == subscriptionId);
            var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.True);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);

            // New Reads are routed to the replacement generation.
            DataValue afterSwitch = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(afterSwitch.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(afterSwitch.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            // Old notifications continue: a value pushed on the retired node still arrives.
            await PushRetiredValueAsync(server, originalManager, valueNodeId, 4242).ConfigureAwait(false);
            DataValue? pushed;
            (pushed, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);
            Assert.That(pushed!.Value.WrappedValue.GetInt32(), Is.EqualTo(4242));

            // (1) Modify the retired-owned item - must be routed to the retired generation.
            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            ArrayOf<MonitoredItemModifyRequest> itemsToModify =
            [
                new MonitoredItemModifyRequest
                {
                    MonitoredItemId = monitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = clientHandle,
                        SamplingInterval = 0,
                        QueueSize = 5,
                        DiscardOldest = true
                    }
                }
            ];
            ModifyMonitoredItemsResponse modifyResponse = await services
                .ModifyMonitoredItemsAsync(header, subscriptionId, TimestampsToReturn.Both, itemsToModify)
                .ConfigureAwait(false);
            Assert.That(modifyResponse.Results.Count, Is.EqualTo(1));
            Assert.That(modifyResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good),
                "Modify of a shadow-retired data monitored item must be routed to its owning " +
                "(retired) generation and succeed.");

            // (2) Disable then re-enable the retired-owned item.
            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            SetMonitoringModeResponse disableResponse = await services
                .SetMonitoringModeAsync(header, subscriptionId, MonitoringMode.Disabled, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(disableResponse.Results.Count, Is.EqualTo(1));
            Assert.That(disableResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Disabling a shadow-retired data monitored item must be routed to its owning generation.");

            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            SetMonitoringModeResponse enableResponse = await services
                .SetMonitoringModeAsync(header, subscriptionId, MonitoringMode.Reporting, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(enableResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Re-enabling a shadow-retired data monitored item must be routed to its owning generation.");

            // The re-enabled item still delivers a fresh value pushed on the retired node.
            await PushRetiredValueAsync(server, originalManager, valueNodeId, 5353).ConfigureAwait(false);
            (pushed, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);
            Assert.That(pushed!.Value.WrappedValue.GetInt32(), Is.EqualTo(5353));

            // (3) Delete the retired-owned item - must be routed to the retired generation,
            // draining it. Without owner-based routing the same-namespace replacement claims
            // the item and returns BadMonitoredItemIdInvalid, so the retired item never drains.
            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            DeleteMonitoredItemsResponse deleteResponse = await services
                .DeleteMonitoredItemsAsync(header, subscriptionId, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
            Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Delete of a shadow-retired data monitored item must be routed to its owning generation.");
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);

            // The replacement generation is unaffected and still serves Reads.
            DataValue afterDrain = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(afterDrain.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            // Prompt cleanup: with the final item drained, the retired generation is disposed
            // WITHOUT any further lifecycle operation - its own address space is torn down.
            await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Deleting the final event monitored item owned by a shadow-retired generation must
        /// promptly dispose that generation without requiring another lifecycle operation.
        /// </summary>
        [Test]
        public async Task ShadowReloadedEventMonitoredItemDeletionDrainsRetiredGenerationAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateEventGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(services, rootNodeId)
                    .ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadRuntimeNodeSetAsync(
                    original,
                    CreateEventGenerationOptions(generation: 2))
                .ConfigureAwait(false);

            ISubscription subscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(s => s.Id == subscriptionId);
            var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(1));
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.True);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);
            Assert.That(originalManager.Find(rootNodeId), Is.Not.Null);

            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            DeleteMonitoredItemsResponse deleteResponse = await services
                .DeleteMonitoredItemsAsync(header, subscriptionId, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
            Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(subscription.MonitoredItemCount, Is.Zero);
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);

            await AssertRetiredGenerationDisposedAsync(originalManager, rootNodeId)
                .ConfigureAwait(false);

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// The background pass triggered by deleting a shadow generation's final item must
        /// not hold the lifecycle semaphore while waiting for an active request. Otherwise
        /// an opted-in lifecycle call made by that request waits for the semaphore while the
        /// drain waits for the request, forming a circular wait.
        /// </summary>
        [Test]
        public async Task ShadowDrainDoesNotDeadlockCallbackSafeLifecycleCallAsync()
        {
            const string DrainProbeNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DrainProbe";
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadRuntimeNodeSetAsync(
                    original,
                    CreateGenerationOptions(generation: 2))
                .ConfigureAwait(false);

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            NodeManagerRegistration probeRegistration = null;
            IDisposable requestScope =
                server.RequestManager.EnterRequestScope(callbackContext);
            try
            {
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);

                // Give the queued background pass a deterministic opportunity to reach its
                // request drain before this request enters the lifecycle API.
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

                RuntimeNodeSetOptions probeOptions = CreateOptions(
                    DrainProbeNamespaceUri,
                    value: 303);
                probeOptions.AllowLifecycleFromRequestCallback = true;
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                try
                {
                    probeRegistration = await m_server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(probeOptions, callbackContext, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Assert.Fail(
                        "The callback-safe lifecycle call deadlocked with the background " +
                        "retired-generation request drain.");
                }
            }
            finally
            {
                requestScope.Dispose();
            }

            Assert.That(probeRegistration, Is.Not.Null);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(probeRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(reloaded, null)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// A shadow-retired manager that still owns a data monitored item remains in the
        /// session identity-change fan-out and receives the unsubscribe for all-events
        /// monitored items that existed when it was retired. It is not routed for new
        /// all-events subscriptions or ordinary services.
        /// </summary>
        [Test]
        public async Task ShadowRetiredManagerKeepsLifecycleNotificationsUntilDrainedAsync()
        {
            TrackingLifecycleNodeManager originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => originalManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint existingEventSubscriptionId, uint existingEventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            Assert.That(originalManager.AllEventsSubscribeCount, Is.EqualTo(1));

            TrackingLifecycleNodeManager replacementManager = null;
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateTrackingNodeManagementFactory(
                        kGeneration2Value,
                        manager => replacementManager = manager))
                .ConfigureAwait(false);

            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, originalManager)),
                Is.False);
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, replacementManager)),
                Is.True);

            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            int activationCount = originalManager.SessionActivatedCount;
            await master
                .SessionActivatedAsync(
                    new OperationContext(session, DiagnosticsMasks.None),
                    session.Id,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                originalManager.SessionActivatedCount,
                Is.EqualTo(activationCount + 1),
                "Identity-change notification must invalidate the retired manager's " +
                "monitored-item permission cache.");

            (uint newEventSubscriptionId, uint newEventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            Assert.That(
                originalManager.AllEventsSubscribeCount,
                Is.EqualTo(1),
                "A shadow-retired manager must not receive new all-events subscriptions.");

            await DeleteMonitoredItemAsync(
                services,
                existingEventSubscriptionId,
                existingEventMonitoredItemId).ConfigureAwait(false);
            Assert.That(originalManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

            await DeleteMonitoredItemAsync(
                services,
                newEventSubscriptionId,
                newEventMonitoredItemId).ConfigureAwait(false);
            Assert.That(
                originalManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "The retired manager was never subscribed to the post-reload item.");

            await DeleteMonitoredItemAsync(
                services,
                dataSubscriptionId,
                dataMonitoredItemId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(
                originalManager,
                valueNodeId).ConfigureAwait(false);

            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, existingEventSubscriptionId)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, newEventSubscriptionId)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(reloaded, null)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// If prompt cleanup suspends a graceful retiree and its request drain fails, a
        /// monitored item that appears during the drain keeps the retiree alive. Its session
        /// and retained all-events notifications must be restored until the item drains.
        /// </summary>
        [Test]
        public async Task FailedRetiredDrainRestoresNotificationsForLateMonitoredItemAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, uint eventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            Assert.That(retiredManager.AllEventsSubscribeCount, Is.EqualTo(1));

            NodeManagerRegistration replacement = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateNodeManagementFactory(
                        kGeneration2Value,
                        includeEuRange: false))
                .ConfigureAwait(false);

            ISubscription dataSubscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(subscription => subscription.Id == dataSubscriptionId);
            var lateItem = new Mock<IMonitoredItem>();
            lateItem.SetupGet(item => item.Id).Returns(uint.MaxValue - 1);
            lateItem.SetupGet(item => item.NodeManager).Returns(original.NodeManager);
            lateItem.SetupGet(item => item.IsDurable).Returns(false);

            var requestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task heldRequest = HoldRequestAsync(server, requestEntered, releaseRequest);
            await requestEntered.Task.ConfigureAwait(false);

            TimeSpan originalDrainTimeout = server.RequestManager.RequestDrainTimeout;
            bool lateItemAdded = false;
            try
            {
                server.RequestManager.RequestDrainTimeout = TimeSpan.FromSeconds(2);
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);

                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                AddSyntheticMonitoredItem(
                    (Subscription)dataSubscription,
                    lateItem.Object);
                lateItemAdded = true;

                await WaitForRetiredNotificationsResumedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                int unsubscribeCount = retiredManager.AllEventsUnsubscribeCount;
                await DeleteMonitoredItemAsync(
                    services,
                    eventSubscriptionId,
                    eventMonitoredItemId).ConfigureAwait(false);
                Assert.That(
                    retiredManager.AllEventsUnsubscribeCount,
                    Is.EqualTo(unsubscribeCount + 1),
                    "The retained all-events snapshot must resume deletion fan-out.");
            }
            finally
            {
                if (lateItemAdded)
                {
                    RemoveSyntheticMonitoredItem(
                        (Subscription)dataSubscription,
                        lateItem.Object);
                }
                server.RequestManager.RequestDrainTimeout = originalDrainTimeout;
                releaseRequest.TrySetResult(true);
                await heldRequest.ConfigureAwait(false);
            }

            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(replacement, null).ConfigureAwait(false);

            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                valueNodeId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Successful final retirement must not unsubscribe the deleted item twice.");
        }

        /// <summary>
        /// A direct retired-generation cleanup must release the lifecycle semaphore for its
        /// request drain. A callback-safe reload can then win while that drain waits for the
        /// callback request; the original Remove must revalidate its stale handle rather than
        /// destroying the winning generation.
        /// </summary>
        [Test]
        public async Task DirectRetiredDrainAllowsCallbackReloadAndRejectsStaleRemoveAsync()
        {
            const string TargetNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DrainReloadWinner";
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration retiree = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort retireeNs = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var retireeValueId = new NodeId(kValueNodeId, retireeNs);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    retireeValueId,
                    clientHandle: 1).ConfigureAwait(false);
            NodeManagerRegistration retireeReplacement =
                await m_server.NodeManagerLifecycle
                    .ShadowReloadAsync(
                        retiree,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .ConfigureAwait(false);
            ((IDynamicNodeManagerHost)master)
                .SetRetiredGenerationDrainObserver(null);

            RuntimeNodeSetOptions targetOptions = CreateOptions(
                TargetNamespaceUri,
                value: 401);
            targetOptions.AllowLifecycleFromRequestCallback = true;
            NodeManagerRegistration target = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(targetOptions, null)
                .ConfigureAwait(false);
            ushort targetNs = (ushort)server.NamespaceUris.GetIndex(TargetNamespaceUri);
            var targetValueId = new NodeId(kValueNodeId, targetNs);

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            Task removeTask = null;
            NodeManagerRegistration winner = null;
            IDisposable requestScope =
                server.RequestManager.EnterRequestScope(callbackContext);
            try
            {
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);

                removeTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .RemoveAsync(target, null)
                        .AsTask());
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                RuntimeNodeSetOptions replacementOptions = CreateOptions(
                    TargetNamespaceUri,
                    value: 402);
                replacementOptions.AllowLifecycleFromRequestCallback = true;
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                winner = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(
                        target,
                        replacementOptions,
                        callbackContext,
                        timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                requestScope.Dispose();
            }

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await removeTask.ConfigureAwait(false));
            Assert.That(
                exception.Message,
                Is.EqualTo(
                    "The registration is stale or is not owned by this lifecycle provider."));
            NodeManagerRegistration currentWinner =
                m_server.NodeManagerLifecycle.Registrations.Find(
                    registration => registration.Id == target.Id);
            Assert.That(currentWinner, Is.SameAs(winner));
            Assert.That(
                ((AsyncCustomNodeManager)winner.NodeManager).Find(targetValueId),
                Is.Not.Null,
                "The stale Remove must not destroy the reload winner.");
            DataValue winnerValue = await ReadValueAsync(targetValueId).ConfigureAwait(false);
            Assert.That(winnerValue.WrappedValue.GetInt32(), Is.EqualTo(402));

            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                retireeValueId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(winner, null).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retireeReplacement, null)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// A reload waiting for direct retired cleanup must resolve its registration only
        /// after that drain. If a callback-safe Remove wins meanwhile, the stale reload must
        /// not invoke its replacement factory or resurrect the removed registration.
        /// </summary>
        [Test]
        public async Task RetiredDrainRevalidatesBeforeReloadAfterCallbackRemoveAsync()
        {
            const string TargetNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DrainRemoveWinner";
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration retiree = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort retireeNs = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var retireeValueId = new NodeId(kValueNodeId, retireeNs);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    retireeValueId,
                    clientHandle: 1).ConfigureAwait(false);
            NodeManagerRegistration retireeReplacement =
                await m_server.NodeManagerLifecycle
                    .ShadowReloadAsync(
                        retiree,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .ConfigureAwait(false);
            ((IDynamicNodeManagerHost)master)
                .SetRetiredGenerationDrainObserver(null);

            RuntimeNodeSetOptions targetOptions = CreateOptions(
                TargetNamespaceUri,
                value: 501);
            targetOptions.AllowLifecycleFromRequestCallback = true;
            NodeManagerRegistration target = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(targetOptions, null)
                .ConfigureAwait(false);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            int factoryCalls = 0;
            replacementFactory
                .Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref factoryCalls))
                .Throws(new InvalidOperationException(
                    "A stale reload must not invoke its replacement factory."));

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            Task<NodeManagerRegistration> reloadTask = null;
            IDisposable requestScope =
                server.RequestManager.EnterRequestScope(callbackContext);
            try
            {
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);

                reloadTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .ReloadAsync(target, replacementFactory.Object, null)
                        .AsTask());
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(target, callbackContext, timeout.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                requestScope.Dispose();
            }

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await reloadTask.ConfigureAwait(false));
            Assert.That(
                exception.Message,
                Is.EqualTo(
                    "The registration is stale or is not owned by this lifecycle provider."));
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(
                    registration => registration.Id == target.Id),
                Is.Null);

            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                retireeValueId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retireeReplacement, null)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Once final cleanup suspends event-delete fan-out, deletion of a pre-retirement
        /// all-events item remains recorded in the retired manager's snapshot and is
        /// unsubscribed during finalization. A post-retirement item is never added to that
        /// snapshot and therefore receives neither subscribe nor unsubscribe callbacks.
        /// </summary>
        [Test]
        public async Task SuspendedRetiredEventSnapshotFinalizesDeletedExistingItemOnlyAsync()
        {
            const string ProbeNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:SnapshotProbe";
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint existingSubscriptionId, uint existingMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateNodeManagementFactory(
                        kGeneration2Value,
                        includeEuRange: false))
                .ConfigureAwait(false);
            ((IDynamicNodeManagerHost)master)
                .SetRetiredGenerationDrainObserver(null);
            (uint postSubscriptionId, uint postMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            Assert.That(retiredManager.AllEventsSubscribeCount, Is.EqualTo(1));

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            Task<NodeManagerRegistration> probeTask = null;
            IDisposable requestScope =
                server.RequestManager.EnterRequestScope(callbackContext);
            try
            {
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);

                RuntimeNodeSetOptions probeOptions = CreateOptions(
                    ProbeNamespaceUri,
                    value: 601);
                probeTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(probeOptions, null)
                        .AsTask());
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                await DeleteMonitoredItemAsync(
                    services,
                    existingSubscriptionId,
                    existingMonitoredItemId).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(
                    services,
                    postSubscriptionId,
                    postMonitoredItemId).ConfigureAwait(false);
                Assert.That(
                    retiredManager.AllEventsUnsubscribeCount,
                    Is.Zero,
                    "Suspended delete fan-out must leave finalization to the retained snapshot.");
            }
            finally
            {
                requestScope.Dispose();
            }

            NodeManagerRegistration probe = await probeTask.ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                valueNodeId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Only the pre-retirement subscription belongs to the retained snapshot.");

            await m_server.NodeManagerLifecycle.RemoveAsync(probe, null).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, existingSubscriptionId)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, postSubscriptionId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// An all-events deletion that already owns the monitored-item mutation gate completes
        /// before shadow retirement snapshots subscriptions. The retired manager receives that
        /// unsubscribe exactly once and final disposal does not replay it.
        /// </summary>
        [Test]
        public async Task InFlightAllEventsDeleteIsNotRetainedOrUnsubscribedTwiceAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, uint eventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            var unsubscribeEntered =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var allowUnsubscribe =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            retiredManager.AllEventsCallback = async (
                IEventMonitoredItem monitoredItem,
                bool unsubscribe,
                CancellationToken cancellationToken) =>
            {
                if (unsubscribe && monitoredItem.Id == eventMonitoredItemId)
                {
                    unsubscribeEntered.TrySetResult(true);
                    await allowUnsubscribe.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            };

            Task deleteTask = DeleteMonitoredItemAsync(
                services,
                eventSubscriptionId,
                eventMonitoredItemId);
            await unsubscribeEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            var replacementBound =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            Task<NodeManagerRegistration> reloadTask = m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateTrackingNodeManagementFactory(
                        kGeneration2Value,
                        manager =>
                        {
                            manager.AllEventsCallback = (
                                IEventMonitoredItem monitoredItem,
                                bool unsubscribe,
                                CancellationToken _) =>
                            {
                                if (!unsubscribe &&
                                    monitoredItem.Id == eventMonitoredItemId)
                                {
                                    replacementBound.TrySetResult(true);
                                }
                                return default;
                            };
                        }))
                .AsTask();
            try
            {
                await replacementBound.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(
                    reloadTask.IsCompleted,
                    Is.False,
                    "Retirement must wait for the in-flight monitored-item mutation.");
            }
            finally
            {
                allowUnsubscribe.TrySetResult(true);
            }
            await deleteTask.ConfigureAwait(false);
            NodeManagerRegistration reloaded = await reloadTask.ConfigureAwait(false);
            Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

            await DeleteMonitoredItemAsync(
                services,
                dataSubscriptionId,
                dataMonitoredItemId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                valueNodeId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Finalization must not replay an unsubscribe completed before retirement.");

            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Filter modification and ConditionRefresh keep reaching a shadow-retired manager
        /// only for all-events items in its retirement snapshot. Post-retirement items are
        /// excluded, and final cleanup removes the retired manager from both fan-outs.
        /// </summary>
        [Test]
        public async Task RetiredAllEventsSnapshotReceivesModifyAndConditionRefreshUntilCleanupAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint existingSubscriptionId, uint existingMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateNodeManagementFactory(
                        kGeneration2Value,
                        includeEuRange: false))
                .ConfigureAwait(false);
            (uint postSubscriptionId, uint postMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            Assert.That(retiredManager.AllEventsSubscribeCount, Is.EqualTo(1));
            await ModifyEventMonitoredItemAsync(
                services,
                existingSubscriptionId,
                existingMonitoredItemId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsSubscribeCount,
                Is.EqualTo(2),
                "The retained manager must receive filter changes for its snapshotted item.");
            await ModifyEventMonitoredItemAsync(
                services,
                postSubscriptionId,
                postMonitoredItemId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsSubscribeCount,
                Is.EqualTo(2),
                "A post-retirement item must not be added to the retained snapshot.");

            IList<IEventMonitoredItem> eventItems =
                server.EventManager.GetMonitoredItems();
            IEventMonitoredItem existingItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == existingMonitoredItemId);
            IEventMonitoredItem postItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == postMonitoredItemId);
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            await master
                .ConditionRefreshAsync(
                    new OperationContext(session, DiagnosticsMasks.None),
                    [existingItem, postItem],
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(retiredManager.ConditionRefreshCount, Is.EqualTo(1));
            Assert.That(
                retiredManager.LastConditionRefreshMonitoredItemIds,
                Is.EqualTo(new[] { existingMonitoredItemId }),
                "ConditionRefresh must use the retired manager's exact item snapshot.");

            await DeleteMonitoredItemAsync(
                services,
                dataSubscriptionId,
                dataMonitoredItemId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                valueNodeId).ConfigureAwait(false);
            Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

            await ModifyEventMonitoredItemAsync(
                services,
                existingSubscriptionId,
                existingMonitoredItemId).ConfigureAwait(false);
            await ModifyEventMonitoredItemAsync(
                services,
                postSubscriptionId,
                postMonitoredItemId).ConfigureAwait(false);
            await master
                .ConditionRefreshAsync(
                    new OperationContext(session, DiagnosticsMasks.None),
                    [existingItem, postItem],
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(retiredManager.AllEventsSubscribeCount, Is.EqualTo(2));
            Assert.That(
                retiredManager.ConditionRefreshCount,
                Is.EqualTo(1),
                "A finalized retired manager must receive no further condition refresh.");

            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, existingSubscriptionId)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, postSubscriptionId)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionRefreshSnapshotLeasesLaterManagersBeforeDispatchAsync()
        {
            const string BlockingNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshBlocking";
            const string ReloadedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshReloaded";
            TrackingLifecycleNodeManager blockingManager = null;
            TrackingLifecycleNodeManager reloadedManager = null;
            NodeManagerRegistration blockingRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        701,
                        manager => blockingManager = manager,
                        BlockingNamespaceUri), null)
                    .ConfigureAwait(false);
            NodeManagerRegistration originalReloaded =
                await m_server.NodeManagerLifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        702,
                        manager => reloadedManager = manager,
                        ReloadedNamespaceUri), null)
                    .ConfigureAwait(false);

            var dispatchEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatch = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            blockingManager.ConditionRefreshCallback = async (_, ct) =>
            {
                dispatchEntered.TrySetResult(true);
                await releaseDispatch.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            bool callbackAfterDispose = false;
            reloadedManager.ConditionRefreshCallback = (_, _) =>
            {
                callbackAfterDispose = reloadedManager.DisposeCount > 0;
                return default;
            };

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            Task refreshTask = master
                .ConditionRefreshAsync(
                    new OperationContext(session, DiagnosticsMasks.None),
                    [],
                    CancellationToken.None)
                .AsTask();
            await dispatchEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Task<NodeManagerRegistration> reloadTask = m_server.NodeManagerLifecycle
                .ReloadAsync(
                    originalReloaded,
                    CreateTrackingNodeManagementFactory(
                        703,
                        _ => { },
                        ReloadedNamespaceUri), null)
                .AsTask();
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                Assert.That(
                    reloadTask.IsCompleted,
                    Is.False,
                    "Reload must wait for the lease captured for a later refresh target.");
                Assert.That(reloadedManager.DisposeCount, Is.Zero);
            }
            finally
            {
                releaseDispatch.TrySetResult(true);
            }

            await refreshTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            NodeManagerRegistration replacement = await reloadTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(reloadedManager.ConditionRefreshCount, Is.EqualTo(1));
            Assert.That(callbackAfterDispose, Is.False);
            Assert.That(reloadedManager.DisposeCount, Is.EqualTo(1));

            await m_server.NodeManagerLifecycle.RemoveAsync(blockingRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(replacement, null)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionRefreshWorkerLeaseDelaysRetiredGenerationCleanupAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, _) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            var refreshEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefresh = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            retiredManager.ConditionRefreshCallback = async (_, ct) =>
            {
                refreshEntered.TrySetResult(true);
                await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                eventSubscriptionId);
            await refreshEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            NodeManagerRegistration reloaded = null;
            try
            {
                reloaded = await m_server.NodeManagerLifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .ConfigureAwait(false);
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                Assert.That(
                    retiredManager.DeleteAddressSpaceCount,
                    Is.Zero,
                    "Final cleanup must wait for the worker's retained dispatch lease.");
                Assert.That(
                    retiredManager.Find(valueNodeId),
                    Is.Not.Null,
                    "The retired address space must remain alive while refresh dispatch is active.");
            }
            finally
            {
                releaseRefresh.TrySetResult(true);
            }

            await AssertRetiredGenerationDisposedAsync(retiredManager, valueNodeId)
                .ConfigureAwait(false);
            Assert.That(retiredManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
            if (reloaded is not null)
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConditionRefreshWorkerLifecycleCallDoesNotDeadlockFinalizationAsync()
        {
            const string ProbeNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshLifecycleProbe";
            const string TriggerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshCleanupTrigger";
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, _) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateNodeManagementFactory(
                        kGeneration2Value,
                        includeEuRange: false))
                .ConfigureAwait(false);
            ((IDynamicNodeManagerHost)master)
                .SetRetiredGenerationDrainObserver(null);

            var dispatchEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var startLifecycle = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycleCallStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var probeFactoryEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycleCompleted =
                new TaskCompletionSource<NodeManagerRegistration>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> probeManager =
                CreateLifecycleNodeManager(ProbeNamespaceUri);
            var probeFactory = new CallbackSafeNodeManagerFactory(
                [ProbeNamespaceUri],
                (_, _, _) =>
                {
                    probeFactoryEntered.TrySetResult(true);
                    return new ValueTask<IAsyncNodeManager>(probeManager.Object);
                });
            retiredManager.ConditionRefreshCallback = async (_, ct) =>
            {
                dispatchEntered.TrySetResult(true);
                await startLifecycle.Task.WaitAsync(ct).ConfigureAwait(false);
                lifecycleCallStarted.TrySetResult(true);
                try
                {
                    NodeManagerRegistration probe = await m_server.NodeManagerLifecycle
                        .AddAsync(probeFactory, null, ct)
                        .ConfigureAwait(false);
                    lifecycleCompleted.TrySetResult(probe);
                }
                catch (Exception ex)
                {
                    lifecycleCompleted.TrySetException(ex);
                    throw;
                }
            };

            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                eventSubscriptionId);
            await dispatchEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await DeleteMonitoredItemAsync(
                services,
                dataSubscriptionId,
                dataMonitoredItemId).ConfigureAwait(false);
            Task<NodeManagerRegistration> cleanupTrigger =
                RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .AddAsync(CreateTrackingNodeManagementFactory(
                            705,
                            _ => { },
                            TriggerNamespaceUri), null)
                        .AsTask());
            await WaitForRetiredNotificationsSuspendedAsync(
                master,
                retiredManager,
                session).ConfigureAwait(false);

            startLifecycle.TrySetResult(true);
            await lifecycleCallStarted.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await probeFactoryEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            NodeManagerRegistration probeRegistration = await lifecycleCompleted.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(retiredManager, valueNodeId)
                .ConfigureAwait(false);
            NodeManagerRegistration triggerRegistration = await cleanupTrigger
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await m_server.NodeManagerLifecycle.RemoveAsync(probeRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(triggerRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionRefreshWorkerIdleShutdownCompletesAsync()
        {
            m_requestHeader = null;
            await m_server.ShutdownInternalsAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            AssertServerInternalsDisposed();
            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CompleteShutdownWaitsForRetiredConditionRefreshDispatchAsync()
        {
            var lifecycle = new NodeManagerLifecycle(m_server);
            var releaseRefresh = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                TrackingLifecycleNodeManager retiredManager = null;
                NodeManagerRegistration original = await lifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => retiredManager = manager), null)
                    .ConfigureAwait(false);

                IServerInternal server = m_server.CurrentInstance;
                ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
                var valueNodeId = new NodeId(kValueNodeId, ns);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint dataSubscriptionId, uint dataMonitoredItemId) =
                    await CreateSubscriptionAndMonitoredItemAsync(
                        services,
                        valueNodeId,
                        clientHandle: 1).ConfigureAwait(false);
                (uint eventSubscriptionId, _) =
                    await CreateSubscriptionAndEventMonitoredItemAsync(
                        services,
                        ObjectIds.Server).ConfigureAwait(false);
                await lifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .ConfigureAwait(false);

                var refreshEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                retiredManager.ConditionRefreshCallback = async (_, ct) =>
                {
                    refreshEntered.TrySetResult(true);
                    await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                server.SubscriptionManager.ConditionRefresh(
                    new OperationContext(session, DiagnosticsMasks.None),
                    eventSubscriptionId);
                await refreshEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                await lifecycle.BeginShutdownAsync(server).ConfigureAwait(false);
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);
                Task completeShutdown = lifecycle
                    .CompleteShutdownAsync(server)
                    .AsTask();
                try
                {
                    Assert.That(
                        completeShutdown.IsCompleted,
                        Is.False,
                        "Shutdown teardown must wait for the retained worker dispatch.");
                    Assert.That(retiredManager.DisposeCount, Is.Zero);
                }
                finally
                {
                    releaseRefresh.TrySetResult(true);
                }

                await completeShutdown
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(retiredManager.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations, Is.Empty);

                await DeleteSubscriptionAsync(services, dataSubscriptionId)
                    .ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, eventSubscriptionId)
                    .ConfigureAwait(false);
            }
            finally
            {
                releaseRefresh.TrySetResult(true);
                lifecycle.Dispose();
            }
        }

        [Test]
        public async Task CancelledCompleteShutdownRemainsRetryableAfterDisposeAsync()
        {
            var lifecycle = new NodeManagerLifecycle(m_server);
            var releaseRefresh = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                TrackingLifecycleNodeManager retiredManager = null;
                TrackingLifecycleNodeManager replacementManager = null;
                NodeManagerRegistration original = await lifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => retiredManager = manager), null)
                    .ConfigureAwait(false);

                IServerInternal server = m_server.CurrentInstance;
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
                var valueNodeId = new NodeId(kValueNodeId, ns);
                (uint dataSubscriptionId, uint dataMonitoredItemId) =
                    await CreateSubscriptionAndMonitoredItemAsync(
                        services,
                        valueNodeId,
                        clientHandle: 1).ConfigureAwait(false);
                (uint eventSubscriptionId, _) =
                    await CreateSubscriptionAndEventMonitoredItemAsync(
                        services,
                        ObjectIds.Server).ConfigureAwait(false);
                await lifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateTrackingNodeManagementFactory(
                            kGeneration2Value,
                            manager => replacementManager = manager))
                    .ConfigureAwait(false);

                var refreshEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                retiredManager.ConditionRefreshCallback = async (_, ct) =>
                {
                    refreshEntered.TrySetResult(true);
                    await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                server.SubscriptionManager.ConditionRefresh(
                    new OperationContext(session, DiagnosticsMasks.None),
                    eventSubscriptionId);
                await refreshEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                await lifecycle.BeginShutdownAsync(server).ConfigureAwait(false);
                using (var cts = new CancellationTokenSource())
                {
                    Task cancelledShutdown = lifecycle
                        .CompleteShutdownAsync(server, cts.Token)
                        .AsTask();
                    await Task.Delay(TimeSpan.FromMilliseconds(100))
                        .ConfigureAwait(false);
                    Assert.That(
                        cancelledShutdown.IsCompleted,
                        Is.False,
                        "Shutdown must be waiting for the in-flight refresh dispatch.");
                    cts.Cancel();
                    Assert.That(
                        async () => await cancelledShutdown.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>());
                }

                lifecycle.Dispose();
                releaseRefresh.TrySetResult(true);
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);
                await lifecycle
                    .CompleteShutdownAsync(server)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                Assert.That(lifecycle.Registrations, Is.Empty);
                Assert.That(retiredManager.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementManager.DisposeCount, Is.EqualTo(1));

                await DeleteSubscriptionAsync(services, dataSubscriptionId)
                    .ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, eventSubscriptionId)
                    .ConfigureAwait(false);
            }
            finally
            {
                releaseRefresh.TrySetResult(true);
                lifecycle.Dispose();
            }
        }

        [TestCase(RetainedNotificationDispatchKind.Session)]
        [TestCase(RetainedNotificationDispatchKind.Modify)]
        [TestCase(RetainedNotificationDispatchKind.EventDelete)]
        public async Task RetiredNotificationFinalizationWaitsForInFlightDispatchAsync(
            RetainedNotificationDispatchKind dispatchKind)
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, uint dataMonitoredItemId) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, uint eventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateNodeManagementFactory(
                        kGeneration2Value,
                        includeEuRange: false))
                .ConfigureAwait(false);

            var dispatchEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatch = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            Task dispatchTask;
            switch (dispatchKind)
            {
                case RetainedNotificationDispatchKind.Session:
                    retiredManager.SessionActivatedCallback = async ct =>
                    {
                        dispatchEntered.TrySetResult(true);
                        await releaseDispatch.Task.WaitAsync(ct).ConfigureAwait(false);
                    };
                    dispatchTask = master
                        .SessionActivatedAsync(
                            new OperationContext(session, DiagnosticsMasks.None),
                            session.Id,
                            CancellationToken.None)
                        .AsTask();
                    break;
                case RetainedNotificationDispatchKind.Modify:
                    retiredManager.AllEventsCallback = async (
                        monitoredItem,
                        unsubscribe,
                        ct) =>
                    {
                        if (!unsubscribe &&
                            monitoredItem.Id == eventMonitoredItemId)
                        {
                            dispatchEntered.TrySetResult(true);
                            await releaseDispatch.Task.WaitAsync(ct).ConfigureAwait(false);
                        }
                    };
                    dispatchTask = ModifyEventMonitoredItemAsync(
                        services,
                        eventSubscriptionId,
                        eventMonitoredItemId);
                    break;
                case RetainedNotificationDispatchKind.EventDelete:
                    retiredManager.AllEventsCallback = async (
                        monitoredItem,
                        unsubscribe,
                        ct) =>
                    {
                        if (unsubscribe &&
                            monitoredItem.Id == eventMonitoredItemId)
                        {
                            dispatchEntered.TrySetResult(true);
                            await releaseDispatch.Task.WaitAsync(ct).ConfigureAwait(false);
                        }
                    };
                    dispatchTask = DeleteMonitoredItemAsync(
                        services,
                        eventSubscriptionId,
                        eventMonitoredItemId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dispatchKind));
            }

            await dispatchEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Task finalizeTask = ((IDynamicNodeManagerHost)master)
                .FinalizeRetiredGenerationNotificationsAsync(
                    retiredManager,
                    CancellationToken.None)
                .AsTask();
            try
            {
                Assert.That(
                    finalizeTask.IsCompleted,
                    Is.False,
                    "Finalization must wait for the captured retained dispatch lease.");
            }
            finally
            {
                releaseDispatch.TrySetResult(true);
            }
            await dispatchTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await finalizeTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await DeleteMonitoredItemAsync(
                services,
                dataSubscriptionId,
                dataMonitoredItemId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(retiredManager, valueNodeId)
                .ConfigureAwait(false);
            if (dispatchKind != RetainedNotificationDispatchKind.EventDelete)
            {
                await DeleteMonitoredItemAsync(
                    services,
                    eventSubscriptionId,
                    eventMonitoredItemId).ConfigureAwait(false);
            }
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
        }

        [Test]
        public async Task RetiredOwnedEventConditionRefreshExcludesReplacementItemAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint retiredMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    rootNodeId).ConfigureAwait(false);
            TrackingLifecycleNodeManager replacementManager = null;
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateTrackingNodeManagementFactory(
                        kGeneration2Value,
                        manager => replacementManager = manager))
                .ConfigureAwait(false);
            uint replacementMonitoredItemId = await CreateEventMonitoredItemAsync(
                services,
                subscriptionId,
                rootNodeId,
                clientHandle: 2).ConfigureAwait(false);

            IList<IEventMonitoredItem> eventItems =
                server.EventManager.GetMonitoredItems();
            IEventMonitoredItem retiredItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == retiredMonitoredItemId);
            IEventMonitoredItem replacementItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == replacementMonitoredItemId);
            Assert.That(retiredItem.NodeManager, Is.SameAs(retiredManager));
            Assert.That(replacementItem.NodeManager, Is.SameAs(replacementManager));

            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                subscriptionId);
            await WaitForConditionRefreshCountAsync(retiredManager, 1)
                .ConfigureAwait(false);

            Assert.That(
                retiredManager.LastConditionRefreshMonitoredItemIds,
                Is.EqualTo(new[] { retiredMonitoredItemId }),
                "The retired generation must receive its owned item, but not the " +
                "replacement generation's post-retirement item.");

            await DeleteMonitoredItemAsync(
                services,
                subscriptionId,
                retiredMonitoredItemId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(retiredManager, rootNodeId)
                .ConfigureAwait(false);
            await DeleteMonitoredItemAsync(
                services,
                subscriptionId,
                replacementMonitoredItemId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeWaitsForAddRequestDrainBeforeDisposingInternalsAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DisposeAddDrain";
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(NamespaceUri);
            int disposeCount = 0;
            manager.As<IDisposable>()
                .Setup(disposable => disposable.Dispose())
                .Callback(() => Interlocked.Increment(ref disposeCount));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);

            IServerInternal server = m_server.CurrentInstance;
            var requestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task requestTask = HoldRequestAsync(
                server,
                requestEntered,
                releaseRequest);
            await requestEntered.Task.ConfigureAwait(false);
            Task<NodeManagerRegistration> addTask = RunWithoutExecutionContext(
                () => m_server.NodeManagerLifecycle.AddAsync(factory.Object, null).AsTask());
            await WaitForRegistrationAsync(
                m_server.NodeManagerLifecycle,
                manager.Object).ConfigureAwait(false);

            m_requestHeader = null;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            Assert.That(addTask.IsCompleted, Is.False);
            Assert.That(disposeTask.IsCompleted, Is.False);
            Assert.That(
                Volatile.Read(ref disposeCount),
                Is.Zero,
                "Dispose must not tear down a manager while Add is draining requests.");

            releaseRequest.TrySetResult(true);
            await requestTask.ConfigureAwait(false);
            await AssertLifecycleOperationDidNotUseDisposedServicesAsync(addTask)
                .ConfigureAwait(false);
            await WaitForConditionAsync(() => Volatile.Read(ref disposeCount) == 1)
                .ConfigureAwait(false);
            await disposeTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeWaitsForReloadRequestDrainBeforeDisposingInternalsAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DisposeReloadDrainServer";
            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(NamespaceUri);
            int originalDisposeCount = 0;
            originalManager.As<IDisposable>()
                .Setup(disposable => disposable.Dispose())
                .Callback(() => Interlocked.Increment(ref originalDisposeCount));
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(NamespaceUri);
            int replacementDisposeCount = 0;
            replacementManager.As<IDisposable>()
                .Setup(disposable => disposable.Dispose())
                .Callback(() => Interlocked.Increment(ref replacementDisposeCount));
            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(originalFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var requestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task requestTask = HoldRequestAsync(
                server,
                requestEntered,
                releaseRequest);
            await requestEntered.Task.ConfigureAwait(false);
            Task<NodeManagerRegistration> reloadTask = RunWithoutExecutionContext(
                () => m_server.NodeManagerLifecycle
                    .ReloadAsync(original, replacementFactory.Object, null)
                    .AsTask());
            await WaitForRegistrationAsync(
                m_server.NodeManagerLifecycle,
                original.Id,
                replacementManager.Object).ConfigureAwait(false);

            m_requestHeader = null;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            Assert.That(reloadTask.IsCompleted, Is.False);
            Assert.That(disposeTask.IsCompleted, Is.False);
            Assert.That(Volatile.Read(ref originalDisposeCount), Is.Zero);
            Assert.That(
                Volatile.Read(ref replacementDisposeCount),
                Is.Zero,
                "Dispose must not tear down either reload generation during the drain.");

            releaseRequest.TrySetResult(true);
            await requestTask.ConfigureAwait(false);
            await AssertLifecycleOperationDidNotUseDisposedServicesAsync(reloadTask)
                .ConfigureAwait(false);
            await WaitForConditionAsync(
                    () => Volatile.Read(ref originalDisposeCount) == 1 &&
                        Volatile.Read(ref replacementDisposeCount) == 1)
                .ConfigureAwait(false);
            await disposeTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeWaitsForRemoveRequestDrainBeforeDisposingInternalsAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DisposeRemoveDrainServer";
            Mock<IAsyncNodeManager> manager = CreateLifecycleNodeManager(NamespaceUri);
            int disposeCount = 0;
            manager.As<IDisposable>()
                .Setup(disposable => disposable.Dispose())
                .Callback(() => Interlocked.Increment(ref disposeCount));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(manager.Object);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            var requestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task requestTask = HoldRequestAsync(
                server,
                requestEntered,
                releaseRequest);
            await requestEntered.Task.ConfigureAwait(false);
            Task removeTask = RunWithoutExecutionContext(
                () => m_server.NodeManagerLifecycle.RemoveAsync(registration, null).AsTask());
            await WaitForNodeManagerVisibilityAsync(
                master,
                manager.Object,
                visible: false).ConfigureAwait(false);

            m_requestHeader = null;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            Assert.That(removeTask.IsCompleted, Is.False);
            Assert.That(disposeTask.IsCompleted, Is.False);
            Assert.That(
                Volatile.Read(ref disposeCount),
                Is.Zero,
                "Dispose must not tear down the removed manager during its request drain.");

            releaseRequest.TrySetResult(true);
            await requestTask.ConfigureAwait(false);
            await AssertLifecycleOperationDidNotUseDisposedServicesAsync(removeTask)
                .ConfigureAwait(false);
            await WaitForConditionAsync(() => Volatile.Read(ref disposeCount) == 1)
                .ConfigureAwait(false);
            await disposeTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionRefreshWorkerCompletesBeforeServerDeletesAddressSpacesAsync()
        {
            TrackingLifecycleNodeManager manager = null;
            await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    created => manager = created), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) = await CreateSubscriptionAndEventMonitoredItemAsync(
                services,
                ObjectIds.Server).ConfigureAwait(false);
            var refreshEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefresh = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.ConditionRefreshCallback = async (_, ct) =>
            {
                refreshEntered.TrySetResult(true);
                await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken);
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                subscriptionId);
            await refreshEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Task shutdownTask = m_server.ShutdownInternalsAsync().AsTask();
            try
            {
                Assert.That(shutdownTask.IsCompleted, Is.False);
                Assert.That(
                    manager.DeleteAddressSpaceCount,
                    Is.Zero,
                    "The real ConditionRefresh worker must exit before address-space deletion.");
                Assert.That(manager.DisposeCount, Is.Zero);
            }
            finally
            {
                releaseRefresh.TrySetResult(true);
            }

            m_requestHeader = null;
            await shutdownTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(manager.DisposeCount, Is.EqualTo(1));
            AssertServerInternalsDisposed();
            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task MasterNodeManagerShutdownAggregatesAndCheckpointsPerManagerAsync()
        {
            TrackingLifecycleNodeManager firstManager = null;
            TrackingLifecycleNodeManager successfulManager = null;
            TrackingLifecycleNodeManager lastManager = null;
            await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    811,
                    created => firstManager = created,
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownCheckpoint1"), null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    812,
                    created => successfulManager = created,
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownCheckpoint2"), null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    813,
                    created => lastManager = created,
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownCheckpoint3"), null)
                .ConfigureAwait(false);

            firstManager.DeleteAddressSpaceFailuresRemaining = 1;
            lastManager.DeleteAddressSpaceFailuresRemaining = 1;
            IServerInternal server = m_server.CurrentInstance;
            AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                async () => await server.NodeManager.ShutdownAsync().ConfigureAwait(false));

            Assert.That(failure.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(firstManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(successfulManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(lastManager.DeleteAddressSpaceCount, Is.EqualTo(1));

            await server.NodeManager.ShutdownAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            Assert.That(firstManager.DeleteAddressSpaceCount, Is.EqualTo(2));
            Assert.That(
                successfulManager.DeleteAddressSpaceCount,
                Is.EqualTo(1),
                "A manager that completed cleanup must not be called again.");
            Assert.That(lastManager.DeleteAddressSpaceCount, Is.EqualTo(2));

            m_requestHeader = null;
            await m_server.ShutdownInternalsAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(firstManager.DeleteAddressSpaceCount, Is.EqualTo(2));
            Assert.That(successfulManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(lastManager.DeleteAddressSpaceCount, Is.EqualTo(2));
            AssertServerInternalsDisposed();
            await FinishShutdownTestAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Shutdown intent acquired while Remove is draining waits for that operation, rejects
        /// new work, and prevents removal rollback from republishing the manager. Disposing
        /// after shutdown intent defers semaphore disposal through CompleteShutdown.
        /// </summary>
        [Test]
        public async Task ShutdownThenDisposeDuringRemoveDrainDoesNotRepublishAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShutdownRemoveDrain";
            Mock<IAsyncNodeManager> nodeManager =
                CreateLifecycleNodeManager(NamespaceUri);
            Mock<IDisposable> disposable = nodeManager.As<IDisposable>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);
            var lifecycle = new NodeManagerLifecycle(m_server);
            IDisposable requestScope = null;
            try
            {
                NodeManagerRegistration registration = await lifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                var callbackContext = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext: null,
                    RequestType.Call,
                    RequestLifetime.None);
                requestScope = server.RequestManager.EnterRequestScope(callbackContext);
                Task removeTask = RunWithoutExecutionContext(
                    () => lifecycle.RemoveAsync(registration, null).AsTask());
                await WaitForNodeManagerVisibilityAsync(
                    master,
                    nodeManager.Object,
                    visible: false).ConfigureAwait(false);

                Task shutdownTask = lifecycle.BeginShutdownAsync(server).AsTask();
                Assert.That(
                    async () => await lifecycle
                        .AddAsync(factory.Object, null)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(
                        "shutting down"));
                Assert.That(
                    shutdownTask.IsCompleted,
                    Is.False,
                    "BeginShutdown must wait for the active Remove across its drain window.");

                lifecycle.Dispose();
                requestScope.Dispose();
                requestScope = null;

                Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await removeTask.ConfigureAwait(false));
                await shutdownTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, nodeManager.Object)),
                    Is.False,
                    "Shutdown-aware removal recovery must not republish the manager.");

                await lifecycle.CompleteShutdownAsync(server).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
                nodeManager.Verify(
                    value => value.DeleteAddressSpaceAsync(CancellationToken.None),
                    Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                requestScope?.Dispose();
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// Dispose acquired first while Reload is draining marks shutdown intent but defers
        /// semaphore disposal. BeginShutdown can join the in-flight operation, and Reload
        /// reports its already committed generation without failing on a disposed semaphore.
        /// </summary>
        [Test]
        public async Task DisposeThenShutdownDuringReloadDrainDefersSemaphoreDisposalAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DisposeReloadDrain";
            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(NamespaceUri);
            Mock<IDisposable> originalDisposable =
                originalManager.As<IDisposable>();
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(NamespaceUri);
            Mock<IDisposable> replacementDisposable =
                replacementManager.As<IDisposable>();
            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);
            var lifecycle = new NodeManagerLifecycle(m_server);
            IDisposable requestScope = null;
            try
            {
                NodeManagerRegistration original = await lifecycle
                    .AddAsync(originalFactory.Object, null)
                    .ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                var callbackContext = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext: null,
                    RequestType.Call,
                    RequestLifetime.None);
                requestScope = server.RequestManager.EnterRequestScope(callbackContext);
                Task<NodeManagerRegistration> reloadTask = RunWithoutExecutionContext(
                    () => lifecycle
                        .ReloadAsync(original, replacementFactory.Object, null)
                        .AsTask());
                await WaitForRegistrationAsync(
                    lifecycle,
                    original.Id,
                    replacementManager.Object).ConfigureAwait(false);

                lifecycle.Dispose();
                Task shutdownTask = lifecycle.BeginShutdownAsync(server).AsTask();
                Assert.That(
                    shutdownTask.IsCompleted,
                    Is.False,
                    "BeginShutdown must join a Reload that was active when Dispose ran.");

                requestScope.Dispose();
                requestScope = null;
                NodeManagerReloadCommittedException exception =
                    Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                        async () => await reloadTask.ConfigureAwait(false));
                Assert.That(exception.InnerException, Is.TypeOf<ObjectDisposedException>());
                await shutdownTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, originalManager.Object)),
                    Is.False);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, replacementManager.Object)),
                    Is.True);

                await lifecycle.CompleteShutdownAsync(server).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
                originalDisposable.Verify(value => value.Dispose(), Times.Once);
                replacementDisposable.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                requestScope?.Dispose();
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// A background retired-generation drain is part of the operation-lifetime barrier.
        /// BeginShutdown rejects new lifecycle work and waits until that drain leaves its
        /// request window before final retired-generation cleanup.
        /// </summary>
        [Test]
        public async Task BeginShutdownWaitsForBackgroundRetiredDrainAsync()
        {
            TrackingLifecycleNodeManager retiredManager = null;
            var lifecycle = new NodeManagerLifecycle(m_server);
            IDisposable requestScope = null;
            try
            {
                NodeManagerRegistration original = await lifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        kGeneration1Value,
                        manager => retiredManager = manager), null)
                    .ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
                var valueNodeId = new NodeId(kValueNodeId, ns);
                var services = new ServerTestServices(m_server, m_secureChannelContext);
                (uint subscriptionId, uint monitoredItemId) =
                    await CreateSubscriptionAndMonitoredItemAsync(
                        services,
                        valueNodeId,
                        clientHandle: 1).ConfigureAwait(false);
                await lifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .ConfigureAwait(false);

                var callbackContext = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext: null,
                    RequestType.Call,
                    RequestLifetime.None);
                requestScope = server.RequestManager.EnterRequestScope(callbackContext);
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken);
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager,
                    session).ConfigureAwait(false);

                Task shutdownTask = lifecycle.BeginShutdownAsync(server).AsTask();
                Assert.That(
                    async () => await lifecycle
                        .AddAsync(CreateNodeManagementFactory(
                            value: 3,
                            includeEuRange: false), null)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(
                        "shutting down"));
                Assert.That(
                    shutdownTask.IsCompleted,
                    Is.False,
                    "BeginShutdown must wait for the claimed background drain.");

                requestScope.Dispose();
                requestScope = null;
                await shutdownTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await AssertRetiredGenerationDisposedAsync(
                    retiredManager,
                    valueNodeId).ConfigureAwait(false);

                await DeleteSubscriptionAsync(services, subscriptionId)
                    .ConfigureAwait(false);
                await lifecycle.CompleteShutdownAsync(server).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
            }
            finally
            {
                requestScope?.Dispose();
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// After a ShadowReload, a subscription that still owns a data monitored item created
        /// on the retired generation must transfer to another session with that item routed to
        /// the retired generation. The item remains owned by the retired generation after the
        /// transfer and keeps delivering values pushed on the retired generation's own node.
        /// </summary>
        [Test]
        public async Task ShadowReloadedDataMonitoredItemSurvivesSubscriptionTransferOnRetiredGenerationAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            const uint clientHandle = 1;

            var servicesA = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, _) =
                await CreateSubscriptionAndMonitoredItemAsync(servicesA, valueNodeId, clientHandle)
                    .ConfigureAwait(false);

            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            (_, acknowledgements) = await PublishForDataChangeAsync(
                servicesA,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadRuntimeNodeSetAsync(original, CreateGenerationOptions(generation: 2))
                .ConfigureAwait(false);

            ISubscription subscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(s => s.Id == subscriptionId);
            var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.True);

            // Session B activates on a secured channel so the subscription (owned by an
            // anonymous identity) can be transferred to it: the server only permits an
            // anonymous-identity transfer over a Sign/SignAndEncrypt channel, and only
            // when both Sessions report the same client ApplicationUri.
            (RequestHeader headerB, SecureChannelContext channelB) = await m_server
                .CreateAndActivateSessionAsync(
                    $"{TestContext.CurrentContext.Test.Name}_SessionB",
                    useSecurity: true,
                    clientApplicationUri: kClientApplicationUri)
                .ConfigureAwait(false);
            try
            {
                var servicesB = new ServerTestServices(m_server, channelB);

                headerB.Timestamp = DateTimeUtc.Now;
                TransferSubscriptionsResponse transferResponse = await servicesB
                    .TransferSubscriptionsAsync(headerB, [subscriptionId], sendInitialValues: false)
                    .ConfigureAwait(false);
                Assert.That(transferResponse.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsGood(transferResponse.Results[0].StatusCode), Is.True,
                    "Transfer of a subscription owning a shadow-retired data monitored item must succeed.");

                // The item remains owned by the retired generation after the transfer.
                Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.True);
                Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);

                // The transferred item keeps delivering values pushed on the retired node,
                // proving the transfer was routed to the retired generation that owns it.
                await PushRetiredValueAsync(server, originalManager, valueNodeId, 6464).ConfigureAwait(false);
                (DataValue? pushed, _) = await PublishForDataChangeOnSessionAsync(
                    servicesB,
                    headerB,
                    subscriptionId,
                    default,
                    clientHandle).ConfigureAwait(false);
                Assert.That(pushed!.Value.WrappedValue.GetInt32(), Is.EqualTo(6464));

                headerB.Timestamp = DateTimeUtc.Now;
                ArrayOf<uint> subscriptionIds = [subscriptionId];
                DeleteSubscriptionsResponse deleteResponse = await servicesB
                    .DeleteSubscriptionsAsync(headerB, subscriptionIds)
                    .ConfigureAwait(false);
                Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                headerB.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(channelB, headerB, true, RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            // The retired generation drains once its subscription is gone; prompt cleanup tears
            // down its address space without any further lifecycle operation.
            await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);
        }

        /// <summary>
        /// When the replacement factory throws during ShadowReload's preparation phase
        /// (before the routing switch is ever committed), the sentinel exception must
        /// propagate unchanged and the current generation must remain fully active:
        /// registration, routing, and value state are entirely unaffected, exactly as for
        /// a fail-closed Reload failure.
        /// </summary>
        [Test]
        public async Task ShadowReloadAsyncWhenReplacementFactoryThrowsKeepsCurrentManagerAsync()
        {
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
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
                    .ShadowReloadAsync(original, replacementFactory.Object)
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
        /// When the replacement's structural commit succeeds but a subsequent rollback
        /// attempt during a later, unrelated failure also fails, ShadowReload must behave
        /// exactly like Reload: the replacement generation is retained live and reported
        /// from <see cref="INodeManagerLifecycle.Registrations"/> for retry or removal,
        /// both underlying failures are reported, and once the transient failures are
        /// cleared a subsequent Remove of the retained registration (and its owner)
        /// completes cleanly.
        /// </summary>
        [Test]
        public async Task ShadowReloadAsyncWhenReplacementRollbackFailsRetainsReplacementGenerationAsync()
        {
            const string OwnerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShadowReloadRollbackOwner";
            const string ReloadedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ShadowReloadRollbackRetained";
            const string AddReferencesFailure =
                "Replacement AddReferencesAsync failed.";
            const string DeleteReferenceFailure =
                "Replacement rollback DeleteReferenceAsync failed.";

            Mock<IAsyncNodeManager> ownerManager =
                CreateLifecycleNodeManager(OwnerNamespaceUri);
            Mock<IDisposable> ownerDisposable = ownerManager.As<IDisposable>();
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerManager.Object);
            NodeManagerRegistration ownerRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ownerNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                OwnerNamespaceUri);
            var ownerSourceId = new NodeId(2101, ownerNamespaceIndex);
            object ownerHandle = new();
            int deleteReferenceCalls = 0;
            bool failRollbackDelete = true;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    ownerHandle,
                    ReferenceTypeIds.HasComponent,
                    false,
                    ObjectIds.Server,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    deleteReferenceCalls++;
                    if (failRollbackDelete && deleteReferenceCalls >= 2)
                    {
                        return new ValueTask<ServiceResult>(
                            Task.FromException<ServiceResult>(
                                new SentinelException(DeleteReferenceFailure)));
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(ReloadedNamespaceUri);
            Mock<IDisposable> originalDisposable =
                originalManager.As<IDisposable>();
            originalManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);
            NodeManagerRegistration originalRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(originalFactory.Object, null)
                    .ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(ReloadedNamespaceUri);
            Mock<IDisposable> replacementDisposable =
                replacementManager.As<IDisposable>();
            replacementManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            bool failReplacementAddReferences = true;
            replacementManager
                .Setup(manager => manager.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => failReplacementAddReferences
                    ? new ValueTask(Task.FromException(
                        new SentinelException(AddReferencesFailure)))
                    : default);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);

            NodeManagerReloadCommittedException exception =
                Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(
                            originalRegistration,
                            replacementFactory.Object)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("replacement generation was retained"));
            Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
            string[] failureMessages = [.. ((AggregateException)exception.InnerException!)
                .Flatten()
                .InnerExceptions
                .Select(failure => failure.Message)];
            Assert.That(failureMessages, Does.Contain(AddReferencesFailure));
            Assert.That(failureMessages, Does.Contain(DeleteReferenceFailure));

            NodeManagerRegistration retainedRegistration =
                m_server.NodeManagerLifecycle.Registrations.Find(registration =>
                    ReferenceEquals(registration.NodeManager, replacementManager.Object));
            Assert.That(retainedRegistration, Is.Not.Null);
            Assert.That(retainedRegistration.Id, Is.EqualTo(originalRegistration.Id));
            Assert.That(
                retainedRegistration.Generation,
                Is.EqualTo(originalRegistration.Generation + 1));
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, originalManager.Object)),
                Is.False);
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, replacementManager.Object)),
                Is.True);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Never);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failReplacementAddReferences = false;
            failRollbackDelete = false;
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retainedRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(ownerRegistration, null)
                .ConfigureAwait(false);

            originalManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Once);
            replacementManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Once);
            ownerDisposable.Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
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
                .AddAsync(factory.Object, null)
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
                .RemoveAsync(registration, null)
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
                    GetNonStartupRegistrations();
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
                GetNonStartupRegistrations();
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
            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<NodeManagerAlreadyRegisteredException>());
            nodeManager.Verify(
                manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            nodeManagerAsDisposable.Verify(d => d.Dispose(), Times.Never);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, null)
                .ConfigureAwait(false);

            nodeManager.Verify(
                m => m.DeleteAddressSpaceAsync(CancellationToken.None),
                Times.Exactly(2));
            factory.Verify(
                f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
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

        [Test]
        public async Task RemoveAsyncDoesNotRepeatDestroyedAddressSpaceWhenReferenceCleanupRetriesAsync()
        {
            const string OwnerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveReferenceOwner";
            const string RemovedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveReferenceRetry";
            const string ExpectedMessage = "Post-destroy reference removal failed.";
            Mock<IAsyncNodeManager> ownerManager =
                CreateLifecycleNodeManager(OwnerNamespaceUri);
            Mock<IDisposable> ownerDisposable = ownerManager.As<IDisposable>();
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerManager.Object);
            NodeManagerRegistration ownerRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ownerNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                OwnerNamespaceUri);
            var ownerSourceId = new NodeId(2001, ownerNamespaceIndex);
            object ownerHandle = new();
            int deleteReferenceCalls = 0;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    ownerHandle,
                    ReferenceTypeIds.HasComponent,
                    false,
                    It.IsAny<ExpandedNodeId>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    int call = Interlocked.Increment(ref deleteReferenceCalls);
                    return call == 2
                        ? new ValueTask<ServiceResult>(
                            Task.FromException<ServiceResult>(
                                new SentinelException(ExpectedMessage)))
                        : new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            TrackingLifecycleNodeManager removedManager = null;
            NodeManagerRegistration removedRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        808,
                        created => removedManager = created,
                        RemovedNamespaceUri,
                        ownerSourceId), null)
                    .ConfigureAwait(false);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .RemoveAsync(removedRegistration, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage));
            Assert.That(removedManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(removedManager.DisposeCount, Is.Zero);
            Assert.That(deleteReferenceCalls, Is.EqualTo(2));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(candidate =>
                    ReferenceEquals(candidate, removedRegistration)),
                Is.SameAs(removedRegistration));

            await m_server.NodeManagerLifecycle
                .RemoveAsync(removedRegistration, null)
                .ConfigureAwait(false);

            Assert.That(
                removedManager.DeleteAddressSpaceCount,
                Is.EqualTo(1),
                "Retrying post-destroy reference cleanup must not delete the address space again.");
            Assert.That(removedManager.DisposeCount, Is.EqualTo(1));
            Assert.That(deleteReferenceCalls, Is.EqualTo(3));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(candidate =>
                    ReferenceEquals(candidate, removedRegistration)),
                Is.Null);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(ownerRegistration, null)
                .ConfigureAwait(false);
            ownerDisposable.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        [Category("NodeManagerLifecycleEvents")]
        public async Task RemoveAsyncCheckpointsDestroyWhenDisposeRetriesAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveDisposeRetry";
            TrackingLifecycleNodeManager manager = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    807,
                    created => manager = created,
                    NamespaceUri), null)
                .ConfigureAwait(false);
            manager.DisposeFailuresRemaining = 1;

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
                .CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    monitoredItems)
                .ConfigureAwait(false);
            Assert.That(createItemsResponse.Results.Count, Is.EqualTo(1));
            Assert.That(createItemsResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            ArrayOf<SubscriptionAcknowledgement> acknowledgements = default;
            try
            {
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;

                Assert.That(
                    async () => await m_server.NodeManagerLifecycle
                        .RemoveAsync(registration, null)
                        .ConfigureAwait(false),
                    Throws.TypeOf<SentinelException>()
                        .With.Message.EqualTo("Dispose failed."));

                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                int unsubscribeCountAfterFailure =
                    manager.AllEventsUnsubscribeCount;
                Assert.That(unsubscribeCountAfterFailure, Is.EqualTo(1));
                Assert.That(
                    GetNonStartupRegistrations(),
                    Has.Count.EqualTo(1));
                Assert.That(
                    GetNonStartupRegistrations()[0],
                    Is.SameAs(registration));
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, manager)),
                    Is.False,
                    "A failed disposal must not republish the removed NodeManager.");

                await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, null)
                    .ConfigureAwait(false);

                Assert.That(
                    manager.DeleteAddressSpaceCount,
                    Is.EqualTo(1),
                    "A completed destroy stage must not run again on Remove retry.");
                Assert.That(manager.DisposeCount, Is.EqualTo(2));
                Assert.That(
                    manager.AllEventsUnsubscribeCount,
                    Is.EqualTo(unsubscribeCountAfterFailure),
                    "A removal retry must not repeat all-events unsubscription.");
                Assert.That(GetNonStartupRegistrations(), Is.Empty);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, manager)),
                    Is.False);

                EventFieldList removeEvent;
                (removeEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(removeEvent, "A live NodeManager was removed.");
            }
            finally
            {
                requestHeader = m_requestHeader;
                requestHeader.Timestamp = DateTimeUtc.Now;
                await services
                    .DeleteSubscriptionsAsync(requestHeader, [subscriptionId])
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public Task AddAsyncWhenPreparationAndCleanupFailReportsEveryFailureAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:PreparationCleanupFailure";
            const string CreateFailure = "CreateAddressSpaceAsync failed.";
            const string DeleteFailure = "DeleteAddressSpaceAsync failed.";
            const string DisposeFailure = "Dispose failed.";

            Mock<IAsyncNodeManager> nodeManager = CreateLifecycleNodeManager(NamespaceUri);
            nodeManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new SentinelException(CreateFailure));
            nodeManager
                .Setup(manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()))
                .Throws(new SentinelException(DeleteFailure));
            Mock<IDisposable> disposable = nodeManager.As<IDisposable>();
            disposable
                .Setup(manager => manager.Dispose())
                .Throws(new SentinelException(DisposeFailure));

            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);

            AggregateException exception = Assert.ThrowsAsync<AggregateException>(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false));

            string[] failureMessages = [.. exception
                .Flatten()
                .InnerExceptions
                .Select(failure => failure.Message)];
            Assert.That(failureMessages, Does.Contain(CreateFailure));
            Assert.That(failureMessages, Does.Contain(DeleteFailure));
            Assert.That(failureMessages, Does.Contain(DisposeFailure));
            Assert.That(GetNonStartupRegistrations(), Is.Empty);

            var master = (MasterNodeManager)m_server.CurrentInstance.NodeManager;
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, nodeManager.Object)),
                Is.False);
            nodeManager.Verify(
                manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            nodeManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            disposable.Verify(manager => manager.Dispose(), Times.Once);
            return Task.CompletedTask;
        }

        [Test]
        public async Task RemoveAsyncWhenSessionUnbindingFailsRepublishesManagerAndAllowsRetryAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:UnbindingFailure";
            const string ExpectedMessage = "SessionClosingAsync failed.";
            bool failSessionClosing = false;

            Mock<IAsyncNodeManager> nodeManager = CreateLifecycleNodeManager(NamespaceUri);
            nodeManager
                .Setup(manager => manager.SessionClosingAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, bool, CancellationToken>(
                    (_, _, _, _) => failSessionClosing
                        ? new ValueTask(Task.FromException(
                            new SentinelException(ExpectedMessage)))
                        : default);
            Mock<IDisposable> disposable = nodeManager.As<IDisposable>();

            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(factory.Object, null)
                .ConfigureAwait(false);

            Mock<IAsyncNodeManager> peerManager =
                CreateLifecycleNodeManager(NamespaceUri);
            var peerFactory = new Mock<IAsyncNodeManagerFactory>();
            peerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(peerManager.Object);
            NodeManagerRegistration peerRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(peerFactory.Object, null)
                    .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            int namespaceIndex = server.NamespaceUris.GetIndex(NamespaceUri);
            int managerPosition = IndexOfReference(
                master.AsyncNodeManagers,
                nodeManager.Object);
            int routePosition = IndexOfReference(
                master.NamespaceManagers[namespaceIndex],
                nodeManager.Object);
            failSessionClosing = true;

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .RemoveAsync(registration, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<SentinelException>()
                    .With.Message.EqualTo(ExpectedMessage));

            Assert.That(
                GetNonStartupRegistrations(),
                Has.Count.EqualTo(2));
            Assert.That(
                GetNonStartupRegistrations().Find(candidate =>
                    candidate.Id == registration.Id),
                Is.SameAs(registration));
            Assert.That(
                IndexOfReference(master.AsyncNodeManagers, nodeManager.Object),
                Is.EqualTo(managerPosition));
            Assert.That(
                IndexOfReference(
                    master.NamespaceManagers[namespaceIndex],
                    nodeManager.Object),
                Is.EqualTo(routePosition));
            Assert.That(
                master.AsyncNodeManagers.Count(manager =>
                    ReferenceEquals(manager, nodeManager.Object)),
                Is.EqualTo(1));
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.True);
            Assert.That(
                master.NamespaceManagers[namespaceIndex].Count(manager =>
                    ReferenceEquals(manager, nodeManager.Object)),
                Is.EqualTo(1));
            nodeManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()),
                Times.Never);
            disposable.Verify(manager => manager.Dispose(), Times.Never);

            failSessionClosing = false;
            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, null)
                .ConfigureAwait(false);

            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, nodeManager.Object)),
                Is.False);
            Assert.That(
                master.NamespaceManagers[namespaceIndex].Any(manager =>
                    ReferenceEquals(manager, peerManager.Object)),
                Is.True);
            nodeManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            disposable.Verify(manager => manager.Dispose(), Times.Once);

            await m_server.NodeManagerLifecycle
                .RemoveAsync(peerRegistration, null)
                .ConfigureAwait(false);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);
        }

        [Test]
        public async Task ReloadAsyncWhenRetiredManagerCleanupFailsKeepsReplacementAndRetriesCleanupAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RetiredCleanupFailure";
            const string ExpectedMessage = "Retired SessionClosingAsync failed.";
            bool failRetiredSessionClosing = false;

            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(NamespaceUri);
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            originalManager
                .Setup(manager => manager.SessionClosingAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, bool, CancellationToken>(
                    (_, _, _, _) => failRetiredSessionClosing
                        ? new ValueTask(Task.FromException(
                            new SentinelException(ExpectedMessage)))
                        : default);
            Mock<IDisposable> originalDisposable = originalManager.As<IDisposable>();

            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);

            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(originalFactory.Object, null)
                .ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(NamespaceUri);
            Mock<IDisposable> replacementDisposable =
                replacementManager.As<IDisposable>();
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);

            failRetiredSessionClosing = true;
            NodeManagerReloadCommittedException exception =
                Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .ReloadAsync(original, replacementFactory.Object, null)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("replacement NodeManager is live"));
            Assert.That(exception.InnerException, Is.TypeOf<SentinelException>());
            Assert.That(exception.InnerException!.Message, Is.EqualTo(ExpectedMessage));
            Assert.That(exception.Registration.Id, Is.EqualTo(original.Id));
            Assert.That(exception.Registration.Generation, Is.EqualTo(original.Generation + 1));

            ArrayOf<NodeManagerRegistration> registrations =
                GetNonStartupRegistrations();
            Assert.That(registrations, Has.Count.EqualTo(1));
            NodeManagerRegistration replacement = registrations[0];
            Assert.That(replacement.Id, Is.EqualTo(original.Id));
            Assert.That(replacement.Generation, Is.EqualTo(original.Generation + 1));
            Assert.That(replacement.NodeManager, Is.SameAs(replacementManager.Object));

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            int namespaceIndex = server.NamespaceUris.GetIndex(NamespaceUri);
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, originalManager.Object)),
                Is.False);
            Assert.That(
                master.AsyncNodeManagers.Count(manager =>
                    ReferenceEquals(manager, replacementManager.Object)),
                Is.EqualTo(1));
            Assert.That(
                master.NamespaceManagers[namespaceIndex].Any(manager =>
                    ReferenceEquals(manager, replacementManager.Object)),
                Is.True);
            originalManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()),
                Times.Never);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Never);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failRetiredSessionClosing = false;
            await m_server.NodeManagerLifecycle
                .RemoveAsync(replacement, null)
                .ConfigureAwait(false);

            originalManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Once);
            replacementManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);
        }

        [Test]
        public async Task AddAsyncWhenPublicationRollbackFailsRetainsLiveRegistrationAsync()
        {
            const string OwnerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:AddRollbackOwner";
            const string RetainedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:AddRollbackRetained";
            const string AddReferencesFailure = "AddReferencesAsync failed.";
            const string DeleteReferenceFailure = "DeleteReferenceAsync failed.";

            Mock<IAsyncNodeManager> ownerManager =
                CreateLifecycleNodeManager(OwnerNamespaceUri);
            Mock<IDisposable> ownerDisposable = ownerManager.As<IDisposable>();
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerManager.Object);
            NodeManagerRegistration ownerRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ownerNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                OwnerNamespaceUri);
            var ownerSourceId = new NodeId(1001, ownerNamespaceIndex);
            object ownerHandle = new();
            bool failDeleteReference = true;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    ownerHandle,
                    ReferenceTypeIds.HasComponent,
                    false,
                    ObjectIds.Server,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(() => failDeleteReference
                    ? new ValueTask<ServiceResult>(
                        Task.FromException<ServiceResult>(
                            new SentinelException(DeleteReferenceFailure)))
                    : new ValueTask<ServiceResult>(ServiceResult.Good));

            Mock<IAsyncNodeManager> retainedManager =
                CreateLifecycleNodeManager(RetainedNamespaceUri);
            Mock<IDisposable> retainedDisposable =
                retainedManager.As<IDisposable>();
            retainedManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            bool failAddReferences = true;
            retainedManager
                .Setup(manager => manager.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => failAddReferences
                    ? new ValueTask(Task.FromException(
                        new SentinelException(AddReferencesFailure)))
                    : default);

            var retainedFactory = new Mock<IAsyncNodeManagerFactory>();
            retainedFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(retainedManager.Object);

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddAsync(retainedFactory.Object, null)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("published generation was retained"));
            Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
            string[] failureMessages = [.. ((AggregateException)exception.InnerException!)
                .Flatten()
                .InnerExceptions
                .Select(failure => failure.Message)];
            Assert.That(failureMessages, Does.Contain(AddReferencesFailure));
            Assert.That(failureMessages, Does.Contain(DeleteReferenceFailure));

            NodeManagerRegistration retainedRegistration =
                m_server.NodeManagerLifecycle.Registrations.Find(registration =>
                    ReferenceEquals(registration.NodeManager, retainedManager.Object));
            Assert.That(retainedRegistration, Is.Not.Null);
            Assert.That(retainedRegistration.Generation, Is.EqualTo(1));
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, retainedManager.Object)),
                Is.True);
            int retainedNamespaceIndex =
                server.NamespaceUris.GetIndex(RetainedNamespaceUri);
            Assert.That(
                master.NamespaceManagers[retainedNamespaceIndex].Any(manager =>
                    ReferenceEquals(manager, retainedManager.Object)),
                Is.True);
            retainedDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failAddReferences = false;
            failDeleteReference = false;
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retainedRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(ownerRegistration, null)
                .ConfigureAwait(false);

            retainedManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            retainedDisposable.Verify(manager => manager.Dispose(), Times.Once);
            ownerDisposable.Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
        }

        [Test]
        public async Task ReloadAsyncWhenReplacementRollbackFailsRetainsReplacementGenerationAsync()
        {
            const string OwnerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ReloadRollbackOwner";
            const string ReloadedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ReloadRollbackRetained";
            const string AddReferencesFailure =
                "Replacement AddReferencesAsync failed.";
            const string DeleteReferenceFailure =
                "Replacement rollback DeleteReferenceAsync failed.";

            Mock<IAsyncNodeManager> ownerManager =
                CreateLifecycleNodeManager(OwnerNamespaceUri);
            Mock<IDisposable> ownerDisposable = ownerManager.As<IDisposable>();
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerManager.Object);
            NodeManagerRegistration ownerRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ushort ownerNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                OwnerNamespaceUri);
            var ownerSourceId = new NodeId(2001, ownerNamespaceIndex);
            object ownerHandle = new();
            int deleteReferenceCalls = 0;
            bool failRollbackDelete = true;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    ownerHandle,
                    ReferenceTypeIds.HasComponent,
                    false,
                    ObjectIds.Server,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    deleteReferenceCalls++;
                    if (failRollbackDelete && deleteReferenceCalls >= 2)
                    {
                        return new ValueTask<ServiceResult>(
                            Task.FromException<ServiceResult>(
                                new SentinelException(DeleteReferenceFailure)));
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(ReloadedNamespaceUri);
            Mock<IDisposable> originalDisposable =
                originalManager.As<IDisposable>();
            originalManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            originalManager
                .As<INodeManagerReloadParticipant>()
                .Setup(participant => participant.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
            var originalFactory = new Mock<IAsyncNodeManagerFactory>();
            originalFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalManager.Object);
            NodeManagerRegistration originalRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(originalFactory.Object, null)
                    .ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(ReloadedNamespaceUri);
            Mock<IDisposable> replacementDisposable =
                replacementManager.As<IDisposable>();
            replacementManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            bool failReplacementAddReferences = true;
            replacementManager
                .Setup(manager => manager.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => failReplacementAddReferences
                    ? new ValueTask(Task.FromException(
                        new SentinelException(AddReferencesFailure)))
                    : default);
            var replacementFactory = new Mock<IAsyncNodeManagerFactory>();
            replacementFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(replacementManager.Object);

            NodeManagerReloadCommittedException exception =
                Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .ReloadAsync(
                            originalRegistration,
                            replacementFactory.Object, null)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("replacement generation was retained"));
            Assert.That(exception.InnerException, Is.TypeOf<AggregateException>());
            string[] failureMessages = [.. ((AggregateException)exception.InnerException!)
                .Flatten()
                .InnerExceptions
                .Select(failure => failure.Message)];
            Assert.That(failureMessages, Does.Contain(AddReferencesFailure));
            Assert.That(failureMessages, Does.Contain(DeleteReferenceFailure));

            NodeManagerRegistration retainedRegistration =
                m_server.NodeManagerLifecycle.Registrations.Find(registration =>
                    ReferenceEquals(registration.NodeManager, replacementManager.Object));
            Assert.That(retainedRegistration, Is.Not.Null);
            Assert.That(retainedRegistration.Id, Is.EqualTo(originalRegistration.Id));
            Assert.That(
                retainedRegistration.Generation,
                Is.EqualTo(originalRegistration.Generation + 1));
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, originalManager.Object)),
                Is.False);
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, replacementManager.Object)),
                Is.True);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Never);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failReplacementAddReferences = false;
            failRollbackDelete = false;
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retainedRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(ownerRegistration, null)
                .ConfigureAwait(false);

            originalManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Once);
            replacementManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Once);
            ownerDisposable.Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
        }

        [Test]
        public async Task RetainedReplacementRunsPostCommitRepairInsteadOfRollbackAsync()
        {
            const string OwnerNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RetainedRepairOwner";
            const string ReplacedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RetainedRepair";
            const string AddReferencesFailure = "Retained replacement publication failed.";
            const string DeleteReferenceFailure = "Retained replacement rollback failed.";
            const string RepairFailure = "Retained replacement monitored-item repair failed.";

            Mock<IAsyncNodeManager> ownerManager =
                CreateLifecycleNodeManager(OwnerNamespaceUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ownerFactory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerManager.Object);
            NodeManagerRegistration ownerRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(ownerFactory.Object, null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            var host = (IDynamicNodeManagerHost)server.NodeManager;
            ushort ownerNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                OwnerNamespaceUri);
            var ownerSourceId = new NodeId(3001, ownerNamespaceIndex);
            object ownerHandle = new();
            int deleteReferenceCalls = 0;
            bool failRollbackDelete = true;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    ownerHandle,
                    ReferenceTypeIds.HasComponent,
                    false,
                    ObjectIds.Server,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    deleteReferenceCalls++;
                    if (failRollbackDelete && deleteReferenceCalls >= 2)
                    {
                        return new ValueTask<ServiceResult>(
                            Task.FromException<ServiceResult>(
                                new SentinelException(DeleteReferenceFailure)));
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            Mock<IAsyncNodeManager> currentManager =
                CreateLifecycleNodeManager(ReplacedNamespaceUri);
            currentManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            PreparedNodeManager current = await host
                .PrepareAsync(currentManager.Object)
                .ConfigureAwait(false);
            await host.PublishAsync(current).ConfigureAwait(false);
            await host.CommitAsync(current).ConfigureAwait(false);

            Mock<IAsyncNodeManager> replacementManager =
                CreateLifecycleNodeManager(ReplacedNamespaceUri);
            replacementManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    IDictionary<NodeId, IList<IReference>>,
                    CancellationToken>((externalReferences, _) => externalReferences[ownerSourceId] =
                    [
                        new NodeStateReference(
                            ReferenceTypeIds.HasComponent,
                            false,
                            ObjectIds.Server)
                    ])
                .Returns(default(ValueTask));
            bool failReplacementAddReferences = true;
            replacementManager
                .Setup(manager => manager.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => failReplacementAddReferences
                    ? new ValueTask(Task.FromException(
                        new SentinelException(AddReferencesFailure)))
                    : default);
            PreparedNodeManager replacement = await host
                .PrepareAsync(replacementManager.Object)
                .ConfigureAwait(false);
            await host
                .ReplaceAsync(currentManager.Object, replacement)
                .ConfigureAwait(false);

            using var monitoredItem = new MonitoredItem(
                server,
                currentManager.Object,
                new object(),
                subscriptionId: 1,
                id: 3001,
                itemToMonitor: new ReadValueId
                {
                    NodeId = new NodeId("RetainedRepair", ownerNamespaceIndex),
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 1,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
            var itemLifecycle = (IDetachableMonitoredItem)monitoredItem;
            int repairCalls = 0;
            int rollbackCalls = 0;

            try
            {
                AggregateException exception = Assert.ThrowsAsync<AggregateException>(
                    async () => await host
                        .CommitAsync(
                            replacement,
                            beforeCommit: () =>
                            {
                                itemLifecycle.Detach(server);
                                return default;
                            },
                            afterCommit: () =>
                            {
                                repairCalls++;
                                itemLifecycle.Rebind(replacementManager.Object, new object());
                                return new ValueTask(Task.FromException(
                                    new SentinelException(RepairFailure)));
                            },
                            rollbackCommit: () =>
                            {
                                rollbackCalls++;
                                itemLifecycle.Rebind(currentManager.Object, new object());
                                return default;
                            })
                        .ConfigureAwait(false));

                string[] failureMessages = [.. exception
                    .Flatten()
                    .InnerExceptions
                    .Select(failure => failure.Message)];
                Assert.Multiple(() =>
                {
                    Assert.That(replacement.Published, Is.True);
                    Assert.That(repairCalls, Is.EqualTo(1));
                    Assert.That(rollbackCalls, Is.Zero);
                    Assert.That(monitoredItem.NodeManager, Is.SameAs(replacementManager.Object));
                    Assert.That(itemLifecycle.IsDetached, Is.False);
                    Assert.That(failureMessages, Does.Contain(AddReferencesFailure));
                    Assert.That(failureMessages, Does.Contain(DeleteReferenceFailure));
                    Assert.That(failureMessages, Does.Contain(RepairFailure));
                });
            }
            finally
            {
                failReplacementAddReferences = false;
                failRollbackDelete = false;
                if (replacement.Published)
                {
                    await host
                        .UnpublishAsync(replacementManager.Object)
                        .ConfigureAwait(false);
                }
                await host
                    .DestroyAddressSpaceAsync(replacementManager.Object)
                    .ConfigureAwait(false);
                await host
                    .DestroyAddressSpaceAsync(currentManager.Object)
                    .ConfigureAwait(false);
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(ownerRegistration, null)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public Task LifecycleArgumentGuardsRejectNullInputsAsync()
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            IAsyncNodeManagerFactory asyncFactory =
                Mock.Of<IAsyncNodeManagerFactory>();
            INodeManagerFactory syncFactory = Mock.Of<INodeManagerFactory>();

            ArgumentNullException exception =
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await lifecycle
                        .AddAsync((IAsyncNodeManagerFactory)null!, null)
                        .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("factory"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .AddAsync((INodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("factory"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(
                        null!,
                        (IAsyncNodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(
                        null!,
                        (INodeManagerFactory)null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(null!, asyncFactory, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadAsync(null!, syncFactory, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(
                        null!,
                        (IAsyncNodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(
                        null!,
                        (INodeManagerFactory)null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(null!, asyncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ShadowReloadAsync(null!, syncFactory)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .RemoveAsync(null!, null)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .BeginShutdownAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("server"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .CompleteShutdownAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("server"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// A commit that fails after the NodeManager's external references have
        /// been retained must not leave those references behind.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>CommitAddAsync</c> records the NodeManager's external references in
        /// the dynamic-reference table and then replays the retained references
        /// of every other registration into it. That replay runs arbitrary
        /// NodeManager code, so it can throw, and the rollback that follows takes
        /// the NodeManager back out of the routing table. If the recorded
        /// references stayed behind, the master would hold a NodeManager it does
        /// not route.
        /// </para>
        /// <para>
        /// That state is not merely a leak. <c>PublishAsync</c> treats presence in
        /// the table as "already registered", so the instance could never be
        /// registered again; and every later add replays the dead references into
        /// the NodeManager being added, which is the opposite of what the replay
        /// exists to do.
        /// </para>
        /// </remarks>
        [Test]
        public async Task FailedCommitDoesNotRetainExternalReferencesForARolledBackNodeManagerAsync()
        {
            var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:FailedCommitRetention");

            // The first call is the commit publishing this NodeManager's own
            // external references; the replay that follows is the one that must
            // fail, because that is the only point after the references are
            // recorded at which anything can throw.
            int addReferencesCalls = 0;
            candidate
                .Setup(manager => manager.AddReferencesAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (++addReferencesCalls > 1)
                    {
                        throw new InvalidOperationException(
                            "Injected replay failure.");
                    }
                    return default;
                });

            PreparedNodeManager prepared = await host
                .PrepareAsync(candidate.Object)
                .ConfigureAwait(false);
            await host.PublishAsync(prepared).ConfigureAwait(false);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await host
                    .CommitAsync(prepared, null, null, null)
                    .ConfigureAwait(false));
            Assert.That(addReferencesCalls, Is.GreaterThan(1),
                "The replay must have been reached, otherwise this test proves nothing.");

            // Probing PublishAsync with the same instance is what reads the
            // dynamic-reference table directly: it rejects a NodeManager that is
            // already recorded there.
            var probe = new PreparedNodeManager(candidate.Object, []);
            Assert.DoesNotThrowAsync(
                async () => await host.PublishAsync(probe).ConfigureAwait(false),
                "The rolled-back NodeManager must not still be recorded as registered.");
        }

        [Test]
        public async Task DynamicHostGuardsRejectInvalidArgumentsAndStateAsync()
        {
            var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
            var coordinator =
                (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:HostGuards");
            var prepared = new PreparedNodeManager(
                candidate.Object,
                []);

            ArgumentNullException exception =
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await host
                        .PrepareAsync(null!)
                        .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .PublishAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("prepared"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .ReplaceAsync(null!, prepared)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("current"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .UnpublishAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .DestroyAddressSpaceAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .RemoveDestroyedExternalReferencesAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = Assert.Throws<ArgumentNullException>(
                () => host.Release(null!));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));


            Assert.That(
                async () => await host
                    .CommitAsync(prepared)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "has not been staged"));

            Mock<IAsyncNodeManager> unowned = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:Unowned");
            Assert.That(
                async () => await host
                    .ReplaceAsync(unowned.Object, prepared)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "not owned"));
            Assert.That(
                async () => await host
                    .UnpublishAsync(unowned.Object)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "not owned"));
            Assert.DoesNotThrow(() => host.Release(unowned.Object));

            Mock<IAsyncNodeManager> destroyCandidate =
                CreateLifecycleNodeManager(
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:Destroy");
            await host
                .DestroyAddressSpaceAsync(destroyCandidate.Object)
                .ConfigureAwait(false);
            await host
                .RemoveDestroyedExternalReferencesAsync(destroyCandidate.Object)
                .ConfigureAwait(false);
            destroyCandidate.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);

            await host.PublishAsync(prepared).ConfigureAwait(false);
            Assert.That(
                async () => await host
                    .PublishAsync(prepared)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "already been staged"));

            await host.RollbackAsync(prepared).ConfigureAwait(false);
            candidate.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);

            Mock<IAsyncNodeManager> owned = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:Owned");
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(owned.Object);
            NodeManagerRegistration registration = await m_server
                .NodeManagerLifecycle
                .AddAsync(factory.Object, null)
                .ConfigureAwait(false);
            var duplicate = new PreparedNodeManager(owned.Object, []);
            Assert.That(
                async () => await host
                    .PublishAsync(duplicate)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "already registered"));

            Mock<IAsyncNodeManager> replacement = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:StagedReplacement");
            var stagedReplacement = new PreparedNodeManager(
                replacement.Object,
                [])
            {
                Staged = true
            };
            Assert.That(
                async () => await host
                    .ReplaceAsync(owned.Object, stagedReplacement)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "already been staged"));

            await m_server.NodeManagerLifecycle
                .RemoveAsync(registration, null)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task IndependentLifecycleShutdownReleasesRegistrationsAndRejectsNewOperationsAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:IndependentShutdown";
            Mock<IAsyncNodeManager> nodeManager =
                CreateLifecycleNodeManager(NamespaceUri);
            Mock<IDisposable> disposable = nodeManager.As<IDisposable>();
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);
            using var lifecycle = new NodeManagerLifecycle(m_server);

            NodeManagerRegistration registration = await lifecycle
                .AddAsync(factory.Object, null)
                .ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, registration.NodeManager)),
                Is.True);

            await lifecycle
                .BeginShutdownAsync(server)
                .ConfigureAwait(false);

            Assert.That(
                async () => await lifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "shutting down"));

            await lifecycle
                .CompleteShutdownAsync(server)
                .ConfigureAwait(false);

            Assert.That(lifecycle.Registrations, Is.Empty);
            Assert.That(
                master.AsyncNodeManagers.Any(manager =>
                    ReferenceEquals(manager, registration.NodeManager)),
                Is.False);
            disposable.Verify(value => value.Dispose(), Times.Once);
        }


        [Test]
        public Task AddAsyncCleansFailedSessionActivationAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:ActivationFailure";
            const string ExpectedMessage = "SessionActivatedAsync failed.";
            var nodeManager = new Mock<IAsyncNodeManager>();
            var syncNodeManager = new Mock<INodeManager>();
            nodeManager
                .Setup(manager => manager.NamespaceUris)
                .Returns([NamespaceUri]);
            nodeManager
                .Setup(manager => manager.SyncNodeManager)
                .Returns(syncNodeManager.Object);
            nodeManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            nodeManager
                .Setup(manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            nodeManager
                .Setup(manager => manager.SessionActivatedAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(
                    Task.FromException(
                        new SentinelException(ExpectedMessage))));
            nodeManager
                .Setup(manager => manager.SessionClosingAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(nodeManager.Object);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<SentinelException>()
                    .With.Message.EqualTo(ExpectedMessage));

            nodeManager.Verify(
                manager => manager.SessionClosingAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    false,
                    CancellationToken.None),
                Times.AtLeastOnce);
            nodeManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            Assert.That(
                GetNonStartupRegistrations(),
                Is.Empty);
            return Task.CompletedTask;
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

                // Add: registers a brand-new namespace URI.
                int namespaceCountBeforeAdd = server.NamespaceUris.Count;
                uint urisVersionBeforeAdd = await ReadUrisVersionAsync().ConfigureAwait(false);

                NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), null)
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

                // Reload (same URI): must not change the namespace table.
                int namespaceCountBeforeReload = server.NamespaceUris.Count;
                uint urisVersionBeforeReload = urisVersionAfterAdd;

                registration = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(registration, CreateGenerationOptions(generation: 2), null)
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

                // Remove: must not change the namespace table either.
                int namespaceCountBeforeRemove = server.NamespaceUris.Count;
                uint urisVersionBeforeRemove = urisVersionAfterReload;

                await m_server.NodeManagerLifecycle.RemoveAsync(registration, null).ConfigureAwait(false);

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
        private async Task<(uint SubscriptionId, uint MonitoredItemId)>
            CreateSubscriptionWithMonitoredItemAsync(
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

            return (subscriptionId, createItemsResponse.Results[0].MonitoredItemId);
        }

        private async Task<(
            MonitoredItemNotification Notification,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements)> PublishForDataChangeAsync(
                ServerTestServices services,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            const int MaxPublishAttempts = 20;
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

                foreach (ExtensionObject notificationData in
                    response.NotificationMessage.NotificationData)
                {
                    if (notificationData.TryGetValue(
                        out DataChangeNotification dataChangeNotification) &&
                        dataChangeNotification.MonitoredItems.Count > 0)
                    {
                        return (dataChangeNotification.MonitoredItems[0], acknowledgements);
                    }
                }
            }

            Assert.Fail("No data-change notification was published.");
            return default;
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

        private async Task DeleteMonitoredItemAsync(
            ServerTestServices services,
            uint subscriptionId,
            uint monitoredItemId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            DeleteMonitoredItemsResponse response = await services
                .DeleteMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        private async Task ModifyEventMonitoredItemAsync(
            ServerTestServices services,
            uint subscriptionId,
            uint monitoredItemId)
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.Message));
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            ModifyMonitoredItemsResponse response = await services
                .ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    [
                        new MonitoredItemModifyRequest
                        {
                            MonitoredItemId = monitoredItemId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 0,
                                Filter = new ExtensionObject(eventFilter),
                                QueueSize = 2,
                                DiscardOldest = true
                            }
                        }
                    ])
                .ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private static async Task WaitForNodeManagerVisibilityAsync(
            MasterNodeManager master,
            IAsyncNodeManager nodeManager,
            bool visible)
        {
            const int MaxAttempts = 100;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, nodeManager)) == visible)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.That(
                master.AsyncNodeManagers.Any(candidate =>
                    ReferenceEquals(candidate, nodeManager)),
                Is.EqualTo(visible));
        }

        private static async Task WaitForRegistrationAsync(
            INodeManagerLifecycle lifecycle,
            Guid registrationId,
            IAsyncNodeManager nodeManager)
        {
            const int MaxAttempts = 100;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                NodeManagerRegistration registration = lifecycle.Registrations.Find(
                    candidate => candidate.Id == registrationId);
                if (registration is not null &&
                    ReferenceEquals(registration.NodeManager, nodeManager))
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.That(
                lifecycle.Registrations.Find(candidate =>
                    candidate.Id == registrationId)?.NodeManager,
                Is.SameAs(nodeManager));
        }

        private static async Task WaitForRegistrationAsync(
            INodeManagerLifecycle lifecycle,
            IAsyncNodeManager nodeManager)
        {
            await WaitForConditionAsync(
                    () => lifecycle.Registrations.Find(candidate =>
                        ReferenceEquals(candidate.NodeManager, nodeManager)) is not null)
                .ConfigureAwait(false);
        }

        private static Task HoldRequestAsync(
            IServerInternal server,
            TaskCompletionSource<bool> entered,
            TaskCompletionSource<bool> release)
        {
            return RunWithoutExecutionContext(async () =>
            {
                var context = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext: null,
                    RequestType.Call,
                    RequestLifetime.None);
                using IDisposable requestScope =
                    server.RequestManager.EnterRequestScope(context);
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });
        }

        private static async Task AssertLifecycleOperationDidNotUseDisposedServicesAsync<T>(
            Task<T> operation)
        {
            try
            {
                await operation
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Assert.That(ex, Is.Not.InstanceOf<ObjectDisposedException>());
                Assert.That(ex.ToString(), Does.Not.Contain(nameof(ObjectDisposedException)));
            }
        }

        private static async Task AssertLifecycleOperationDidNotUseDisposedServicesAsync(
            Task operation)
        {
            try
            {
                await operation
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Assert.That(ex, Is.Not.InstanceOf<ObjectDisposedException>());
                Assert.That(ex.ToString(), Does.Not.Contain(nameof(ObjectDisposedException)));
            }
        }

        private static async Task WaitForConditionAsync(Func<bool> condition)
        {
            const int MaxAttempts = 400;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.That(condition(), Is.True);
        }

        private void AssertServerInternalsDisposed()
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => _ = m_server.CurrentInstance);
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
        }

        private async Task FinishShutdownTestAsync()
        {
            m_requestHeader = null;
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_fixture = null;
            }
            m_server = null;
        }

        private static Task RunWithoutExecutionContext(Func<Task> action)
        {
            Task task;
            using (ExecutionContext.SuppressFlow())
            {
                task = Task.Run(action);
            }
            return task;
        }

        private static Task<T> RunWithoutExecutionContext<T>(Func<Task<T>> action)
        {
            Task<T> task;
            using (ExecutionContext.SuppressFlow())
            {
                task = Task.Run(action);
            }
            return task;
        }

        private static Task<T> RunLifecycleCallbackRequestAsync<T>(
            IServerInternal server,
            Func<Task<T>> operation,
            TaskCompletionSource<bool> requestEntered = null)
        {
            return RunWithoutExecutionContext(async () =>
            {
                var context = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext: null,
                    RequestType.Call,
                    RequestLifetime.None);
                using IDisposable requestScope =
                    server.RequestManager.EnterRequestScope(context);
                requestEntered?.TrySetResult(true);
                return await operation().ConfigureAwait(false);
            });
        }

        private static async Task AssertLifecycleOperationStopsForShutdownAsync(
            Task<NodeManagerRegistration> operation)
        {
            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await operation
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false));
            Assert.That(
                exception.ToString(),
                Does.Contain("shutting down"));
        }

        private static async Task WaitForRetiredNotificationsSuspendedAsync(
            MasterNodeManager master,
            TrackingLifecycleNodeManager retiredManager,
            ISession session)
        {
            const int MaxAttempts = 100;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int before = retiredManager.SessionActivatedCount;
                await master
                    .SessionActivatedAsync(
                        new OperationContext(session, DiagnosticsMasks.None),
                        session.Id,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (retiredManager.SessionActivatedCount == before)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.Fail(
                "The direct retired-generation cleanup did not suspend notifications.");
        }

        private static async Task WaitForRetiredNotificationsResumedAsync(
            MasterNodeManager master,
            TrackingLifecycleNodeManager retiredManager,
            ISession session)
        {
            int activationCount = retiredManager.SessionActivatedCount;
            const int MaxAttempts = 200;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                await master
                    .SessionActivatedAsync(
                        new OperationContext(session, DiagnosticsMasks.None),
                        session.Id,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (retiredManager.SessionActivatedCount > activationCount)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.Fail(
                "The failed retired-generation drain did not restore notifications.");
        }

        private static void AddSyntheticMonitoredItem(
            Subscription subscription,
            IMonitoredItem monitoredItem)
        {
            Lock subscriptionLock = GetSubscriptionLock(subscription);
            lock (subscriptionLock)
            {
                GetSubscriptionMonitoredItems(subscription).Add(
                    monitoredItem.Id,
                    new LinkedListNode<IMonitoredItem>(monitoredItem));
            }
        }

        private static void RemoveSyntheticMonitoredItem(
            Subscription subscription,
            IMonitoredItem monitoredItem)
        {
            Lock subscriptionLock = GetSubscriptionLock(subscription);
            lock (subscriptionLock)
            {
                GetSubscriptionMonitoredItems(subscription).Remove(monitoredItem.Id);
            }
        }

        private static Dictionary<uint, LinkedListNode<IMonitoredItem>>
            GetSubscriptionMonitoredItems(Subscription subscription)
        {
            FieldInfo field = typeof(Subscription).GetField(
                "m_monitoredItems",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Dictionary<uint, LinkedListNode<IMonitoredItem>>)field.GetValue(subscription);
        }

        private static Lock GetSubscriptionLock(Subscription subscription)
        {
            FieldInfo field = typeof(Subscription).GetField(
                "m_lock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Lock)field.GetValue(subscription);
        }

        private static async Task WaitForConditionRefreshCountAsync(
            TrackingLifecycleNodeManager nodeManager,
            int expectedCount)
        {
            const int MaxAttempts = 100;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (nodeManager.ConditionRefreshCount >= expectedCount)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
            }

            Assert.That(nodeManager.ConditionRefreshCount, Is.GreaterThanOrEqualTo(expectedCount));
        }

        /// <summary>
        /// Creates a subscription with a single reporting data monitored item on
        /// <paramref name="nodeId"/> and returns both the subscription id and the
        /// server-assigned monitored item id (needed to target Modify, SetMonitoringMode,
        /// and Delete at that specific item).
        /// </summary>
        private async Task<(uint SubscriptionId, uint MonitoredItemId)>
            CreateSubscriptionAndMonitoredItemAsync(
                ServerTestServices services,
                NodeId nodeId,
                uint clientHandle)
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
                        ClientHandle = clientHandle,
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

            return (subscriptionId, createItemsResponse.Results[0].MonitoredItemId);
        }

        /// <summary>
        /// Creates a subscription with one reporting event monitored item on the supplied
        /// event notifier and returns both server-assigned identifiers.
        /// </summary>
        private async Task<(uint SubscriptionId, uint MonitoredItemId)>
            CreateSubscriptionAndEventMonitoredItemAsync(
                ServerTestServices services,
                NodeId nodeId)
        {
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse subscriptionResponse = await services
                .CreateSubscriptionAsync(requestHeader, 100, 100, 10, 0, true, 0)
                .ConfigureAwait(false);
            uint subscriptionId = subscriptionResponse.SubscriptionId;

            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            ArrayOf<MonitoredItemCreateRequest> monitoredItems =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        Filter = new ExtensionObject(eventFilter),
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

            return (subscriptionId, createItemsResponse.Results[0].MonitoredItemId);
        }

        private async Task<uint> CreateEventMonitoredItemAsync(
            ServerTestServices services,
            uint subscriptionId,
            NodeId nodeId,
            uint clientHandle)
        {
            var eventFilter = new EventFilter();
            eventFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                QualifiedName.From(BrowseNames.EventType));
            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateMonitoredItemsResponse response = await services
                .CreateMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    [
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = nodeId,
                                AttributeId = Attributes.EventNotifier
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = clientHandle,
                                SamplingInterval = 0,
                                Filter = new ExtensionObject(eventFilter),
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        }
                    ])
                .ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return response.Results[0].MonitoredItemId;
        }

        /// <summary>
        /// Pushes a fresh value directly onto a retired generation's own node, simulating an
        /// internal (device-driven) update so tests can prove the retired generation still
        /// services its existing monitored items after a ShadowReload switch.
        /// </summary>
        private static async Task PushRetiredValueAsync(
            IServerInternal server,
            AsyncCustomNodeManager retiredManager,
            NodeId nodeId,
            int value)
        {
            var state = (BaseVariableState)retiredManager.Find(nodeId)!;
            state.Value = value;
            state.Timestamp = DateTimeUtc.Now;
            state.StatusCode = StatusCodes.Good;
            state.UpdateChangeMasks(NodeStateChangeMasks.Value);
            await state
                .ClearChangeMasksAsync(server.DefaultSystemContext, includeChildren: false)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Polls (bounded) until the retired generation's own address space has been torn
        /// down (its <c>PredefinedNodes</c> emptied), proving the retired generation is
        /// disposed promptly once its last monitored item drains - without any further
        /// lifecycle operation being invoked by the test.
        /// </summary>
        private static async Task AssertRetiredGenerationDisposedAsync(
            AsyncCustomNodeManager retiredManager,
            NodeId nodeId)
        {
            const int MaxAttempts = 50;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                if (retiredManager.Find(nodeId) is null)
                {
                    return;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.That(
                retiredManager.Find(nodeId),
                Is.Null,
                "The shadow-retired generation must be disposed promptly once its last monitored " +
                "item drains, without any further lifecycle operation.");
        }

        /// <summary>
        /// Publishes on <paramref name="subscriptionId"/> using the given session's request
        /// header in a bounded loop until a <see cref="DataChangeNotification"/> carrying
        /// <paramref name="clientHandle"/> arrives. Used to prove a transferred monitored item
        /// keeps being serviced by the (retired) generation that owns it, from a second session.
        /// </summary>
        private async Task<(DataValue? Value, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishForDataChangeOnSessionAsync(
                ServerTestServices services,
                RequestHeader sessionRequestHeader,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                uint clientHandle)
        {
            const int MaxPublishAttempts = 20;
            DataValue? value = null;

            for (int attempt = 0; attempt < MaxPublishAttempts && value is null; attempt++)
            {
                sessionRequestHeader.Timestamp = DateTimeUtc.Now;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                PublishResponse response = await services
                    .PublishAsync(sessionRequestHeader, acknowledgements, timeoutCts.Token)
                    .ConfigureAwait(false);

                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));

                acknowledgements = response.AvailableSequenceNumbers.ToArrayOf(
                    sequenceNumber => new SubscriptionAcknowledgement
                    {
                        SubscriptionId = subscriptionId,
                        SequenceNumber = sequenceNumber
                    });

                if (response.NotificationMessage is { } message)
                {
                    foreach (ExtensionObject notificationData in message.NotificationData)
                    {
                        if (notificationData.TryGetValue(out DataChangeNotification dcn))
                        {
                            foreach (MonitoredItemNotification item in dcn.MonitoredItems)
                            {
                                if (item.ClientHandle == clientHandle)
                                {
                                    value = item.Value;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(
                value,
                Is.Not.Null,
                $"No data-change notification for client handle {clientHandle} on subscription " +
                $"{subscriptionId} arrived within {MaxPublishAttempts} bounded publish attempts.");
            return (value, acknowledgements);
        }

        /// <summary>
        /// Publishes on <paramref name="subscriptionId"/> in a bounded loop, acknowledging
        /// previously delivered sequence numbers on each call, until a
        /// <see cref="DataChangeNotification"/> carrying <paramref name="clientHandle"/>
        /// arrives. Used to prove that a monitored item keeps being serviced (by whichever
        /// NodeManager generation owns it) after a live lifecycle switch.
        /// </summary>
        private async Task<(DataValue? Value, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishForDataChangeAsync(
                ServerTestServices services,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                uint clientHandle)
        {
            const int MaxPublishAttempts = 20;
            DataValue? value = null;

            for (int attempt = 0; attempt < MaxPublishAttempts && value is null; attempt++)
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

                if (response.NotificationMessage is { } message)
                {
                    foreach (ExtensionObject notificationData in message.NotificationData)
                    {
                        if (notificationData.TryGetValue(out DataChangeNotification dcn))
                        {
                            foreach (MonitoredItemNotification item in dcn.MonitoredItems)
                            {
                                if (item.ClientHandle == clientHandle)
                                {
                                    value = item.Value;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(
                value,
                Is.Not.Null,
                $"No data-change notification for client handle {clientHandle} on subscription " +
                $"{subscriptionId} arrived within {MaxPublishAttempts} bounded publish attempts.");
            return (value, acknowledgements);
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
        private static RuntimeNodeSetOptions CreateOptions(
            string namespaceUri,
            int value,
            bool subscribeToEvents = false)
        {
            string xml = BuildNodeSetXml(namespaceUri, subscribeToEvents);

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

        private static RuntimeNodeSetOptions CreateEventGenerationOptions(int generation)
        {
            return CreateOptions(
                kModelNamespaceUri,
                generation == 1 ? kGeneration1Value : kGeneration2Value,
                subscribeToEvents: true);
        }

        private static RuntimeNodeSetOptions CreateDroppedGenerationOptions()
        {
            string xml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{kModelNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kModelNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={kRootNodeId}" BrowseName="1:{kRootBrowseName}">
                    <DisplayName>{kRootBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;

            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "NodeManagerLifecycleTests-Dropped",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [kModelNamespaceUri])
                ]
            };
        }

        /// <summary>
        /// Builds a synthetic NodeSet2 document with a root object organized under Objects
        /// and a readable Int32 value variable. The scalar variable carries no
        /// <c>&lt;Value&gt;</c> element; see the defect note on <see cref="CreateOptions"/>
        /// for why its concrete value is instead wired through the fluent
        /// <c>Configure</c> callback.
        /// </summary>
        private static string BuildNodeSetXml(
            string namespaceUri,
            bool subscribeToEvents = false)
        {
            string eventNotifier = subscribeToEvents ? " EventNotifier=\"1\"" : string.Empty;
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
                  <UAObject NodeId="ns=1;i={kRootNodeId}" BrowseName="1:{kRootBrowseName}"{eventNotifier}>
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
        private Task<DataValue> ReadValueAsync(
            NodeId nodeId,
            uint attributeId = Attributes.Value)
        {
            return ReadValueAsync(nodeId, m_requestHeader, attributeId);
        }

        private async Task<DataValue> ReadValueAsync(
            NodeId nodeId,
            RequestHeader requestHeader,
            uint attributeId = Attributes.Value)
        {
            ArrayOf<ReadValueId> readIds =
                [new ReadValueId { NodeId = nodeId, AttributeId = attributeId }];

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

        private ArrayOf<NodeManagerRegistration> GetNonStartupRegistrations()
        {
            var registrations = new List<NodeManagerRegistration>();
            m_server.NodeManagerLifecycle.Registrations.ForEach(registration =>
            {
                if (!m_startupRegistrationIds.Contains(registration.Id))
                {
                    registrations.Add(registration);
                }
            });
            return new ArrayOf<NodeManagerRegistration>(registrations.ToArray());
        }

        private static int IndexOfReference(
            IReadOnlyList<IAsyncNodeManager> managers,
            IAsyncNodeManager target)
        {
            for (int ii = 0; ii < managers.Count; ii++)
            {
                if (ReferenceEquals(managers[ii], target))
                {
                    return ii;
                }
            }
            return -1;
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

        private static Mock<IAsyncNodeManager> CreateLifecycleNodeManager(
            string namespaceUri)
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            var syncNodeManager = new Mock<INodeManager>();
            if (namespaceUri is null)
            {
                nodeManager
                    .Setup(manager => manager.NamespaceUris)
                    .Returns((IEnumerable<string>)null);
            }
            else
            {
                nodeManager
                    .Setup(manager => manager.NamespaceUris)
                    .Returns([namespaceUri]);
            }
            nodeManager
                .Setup(manager => manager.SyncNodeManager)
                .Returns(syncNodeManager.Object);
            nodeManager
                .Setup(manager => manager.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            nodeManager
                .Setup(manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return nodeManager;
        }

        private IAsyncNodeManagerFactory CreateNodeManagementFactory(
            int value,
            bool includeEuRange)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken _) => new ValueTask<IAsyncNodeManager>(
                        new NodeManagementLifecycleNodeManager(
                            server,
                            configuration,
                            m_logger,
                            kModelNamespaceUri,
                            value,
                            includeEuRange)));
            return factory.Object;
        }

        private IAsyncNodeManagerFactory CreateTrackingNodeManagementFactory(
            int value,
            Action<TrackingLifecycleNodeManager> onCreated,
            string namespaceUri = kModelNamespaceUri,
            NodeId? externalParentId = null)
        {
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory
                .Setup(candidate => candidate.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal server,
                    ApplicationConfiguration configuration,
                    CancellationToken _) =>
                {
                    var manager = new TrackingLifecycleNodeManager(
                        server,
                        configuration,
                        m_logger,
                        namespaceUri,
                        value,
                        externalParentId);
                    onCreated(manager);
                    return new ValueTask<IAsyncNodeManager>(manager);
                });
            return factory.Object;
        }

        private sealed class CallbackSafeNodeManagerFactory :
            IAsyncNodeManagerFactory,
            IRequestCallbackSafeNodeManagerFactory
        {
            public CallbackSafeNodeManagerFactory(
                ArrayOf<string> namespaceUris,
                Func<
                    IServerInternal,
                    ApplicationConfiguration,
                    CancellationToken,
                    ValueTask<IAsyncNodeManager>> create)
            {
                NamespacesUris = namespaceUris;
                m_create = create;
            }

            public ArrayOf<string> NamespacesUris { get; }

            public bool AllowLifecycleFromRequestCallback => true;

            public int CreateCount => Volatile.Read(ref m_createCount);

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_createCount);
                return m_create(server, configuration, cancellationToken);
            }

            private readonly Func<
                IServerInternal,
                ApplicationConfiguration,
                CancellationToken,
                ValueTask<IAsyncNodeManager>> m_create;
            private int m_createCount;
        }

        private class NodeManagementLifecycleNodeManager :
            AsyncCustomNodeManager,
            INodeManagerReloadParticipant
        {
            private readonly int m_value;
            private readonly bool m_includeEuRange;
            private readonly NodeId? m_externalParentId;

            public NodeManagementLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                string namespaceUri,
                int value = kGeneration1Value,
                bool includeEuRange = false,
                NodeId? externalParentId = null)
                : base(server, configuration, logger, namespaceUri)
            {
                m_value = value;
                m_includeEuRange = includeEuRange;
                m_externalParentId = externalParentId;
            }

            public override bool AllowNodeManagement => true;

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                ushort namespaceIndex = NamespaceIndexes[0];
                NodeId externalParentId =
                    m_externalParentId ?? ObjectIds.ObjectsFolder;
                NodeId externalReferenceTypeId = m_externalParentId is null
                    ? ReferenceTypeIds.Organizes
                    : ReferenceTypeIds.HasComponent;
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId(kRootNodeId, namespaceIndex),
                    BrowseName = new QualifiedName(kRootBrowseName, namespaceIndex),
                    DisplayName = new LocalizedText(kRootBrowseName),
                    ReferenceTypeId = externalReferenceTypeId,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                root.AddReference(
                    externalReferenceTypeId,
                    true,
                    externalParentId);

                var variable = new BaseDataVariableState(root)
                {
                    NodeId = new NodeId(kValueNodeId, namespaceIndex),
                    BrowseName = new QualifiedName(kValueBrowseName, namespaceIndex),
                    DisplayName = new LocalizedText(kValueBrowseName),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = m_value
                };
                if (m_includeEuRange)
                {
                    var euRange = new PropertyState(variable)
                    {
                        NodeId = new NodeId(kEuRangeNodeId, namespaceIndex),
                        BrowseName = QualifiedName.From(BrowseNames.EURange),
                        DisplayName = new LocalizedText(BrowseNames.EURange),
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        DataType = DataTypeIds.Range,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentRead,
                        UserAccessLevel = AccessLevels.CurrentRead,
                        Value = new Variant(
                            new ExtensionObject(
                                new Opc.Ua.Range
                                {
                                    Low = 0,
                                    High = 100
                                }))
                    };
                    variable.AddChild(euRange);
                }
                root.AddChild(variable);
                await AddPredefinedNodeAsync(
                    SystemContext,
                    root,
                    cancellationToken).ConfigureAwait(false);
                MasterNodeManager.CreateExternalReference(
                    externalReferences,
                    externalParentId,
                    externalReferenceTypeId,
                    false,
                    root.NodeId);
            }

            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }
        }

        private sealed class LifecycleTestServer : ReferenceServer
        {
            public LifecycleTestServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public ValueTask ShutdownInternalsAsync(
                CancellationToken cancellationToken = default)
            {
                return base.OnServerStoppingAsync(cancellationToken);
            }
        }

        private sealed class TrackingLifecycleNodeManager :
            NodeManagementLifecycleNodeManager
        {
            private int m_sessionActivatedCount;
            private int m_allEventsSubscribeCount;
            private int m_allEventsUnsubscribeCount;
            private int m_conditionRefreshCount;
            private int m_readCount;
            private int m_deleteAddressSpaceCount;
            private int m_deleteAddressSpaceFailuresRemaining;
            private int m_disposeCount;
            private int m_disposeFailuresRemaining;
            private uint[] m_lastConditionRefreshMonitoredItemIds = [];

            public TrackingLifecycleNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                string namespaceUri,
                int value,
                NodeId? externalParentId = null)
                : base(
                    server,
                    configuration,
                    logger,
                    namespaceUri,
                    value,
                    includeEuRange: false,
                    externalParentId: externalParentId)
            {
            }

            public int SessionActivatedCount =>
                Volatile.Read(ref m_sessionActivatedCount);

            public int AllEventsSubscribeCount =>
                Volatile.Read(ref m_allEventsSubscribeCount);

            public int AllEventsUnsubscribeCount =>
                Volatile.Read(ref m_allEventsUnsubscribeCount);

            public int ConditionRefreshCount =>
                Volatile.Read(ref m_conditionRefreshCount);

            public int ReadCount =>
                Volatile.Read(ref m_readCount);

            public int DeleteAddressSpaceCount =>
                Volatile.Read(ref m_deleteAddressSpaceCount);

            public int DisposeCount =>
                Volatile.Read(ref m_disposeCount);

            public int DeleteAddressSpaceFailuresRemaining
            {
                get => Volatile.Read(ref m_deleteAddressSpaceFailuresRemaining);
                set => Volatile.Write(ref m_deleteAddressSpaceFailuresRemaining, value);
            }

            public int DisposeFailuresRemaining
            {
                get => Volatile.Read(ref m_disposeFailuresRemaining);
                set => Volatile.Write(ref m_disposeFailuresRemaining, value);
            }

            public uint[] LastConditionRefreshMonitoredItemIds =>
                Volatile.Read(ref m_lastConditionRefreshMonitoredItemIds);

            public Func<
                IEventMonitoredItem,
                bool,
                CancellationToken,
                ValueTask> AllEventsCallback { get; set; }

            public Func<CancellationToken, ValueTask> SessionActivatedCallback { get; set; }

            public Func<
                IList<IEventMonitoredItem>,
                CancellationToken,
                ValueTask> ConditionRefreshCallback { get; set; }

            public Func<CancellationToken, ValueTask> ReadCallback { get; set; }

            public override async ValueTask ReadAsync(
                OperationContext context,
                double maxAge,
                ArrayOf<ReadValueId> nodesToRead,
                IList<DataValue> values,
                IList<ServiceResult> errors,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_readCount);
                if (ReadCallback is not null)
                {
                    await ReadCallback(cancellationToken).ConfigureAwait(false);
                }
                await base.ReadAsync(
                        context,
                        maxAge,
                        nodesToRead,
                        values,
                        errors,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            public override async ValueTask SessionActivatedAsync(
                OperationContext context,
                NodeId sessionId,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_sessionActivatedCount);
                if (SessionActivatedCallback is not null)
                {
                    await SessionActivatedCallback(cancellationToken).ConfigureAwait(false);
                }
                await base.SessionActivatedAsync(
                        context,
                        sessionId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            public override async ValueTask<ServiceResult> SubscribeToAllEventsAsync(
                OperationContext context,
                uint subscriptionId,
                IEventMonitoredItem monitoredItem,
                bool unsubscribe,
                CancellationToken cancellationToken = default)
            {
                if (unsubscribe)
                {
                    Interlocked.Increment(ref m_allEventsUnsubscribeCount);
                }
                else
                {
                    Interlocked.Increment(ref m_allEventsSubscribeCount);
                }

                if (AllEventsCallback is not null)
                {
                    await AllEventsCallback(
                        monitoredItem,
                        unsubscribe,
                        cancellationToken).ConfigureAwait(false);
                }

                return await base
                    .SubscribeToAllEventsAsync(
                        context,
                        subscriptionId,
                        monitoredItem,
                        unsubscribe,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            public override async ValueTask<ServiceResult> ConditionRefreshAsync(
                OperationContext context,
                IList<IEventMonitoredItem> monitoredItems,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_conditionRefreshCount);
                Interlocked.Exchange(
                    ref m_lastConditionRefreshMonitoredItemIds,
                    [.. monitoredItems.Select(monitoredItem => monitoredItem.Id)]);
                if (ConditionRefreshCallback is not null)
                {
                    await ConditionRefreshCallback(monitoredItems, cancellationToken)
                        .ConfigureAwait(false);
                }
                return await base.ConditionRefreshAsync(
                        context,
                        monitoredItems,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            public override async ValueTask DeleteAddressSpaceAsync(
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteAddressSpaceCount);
                if (TryConsumeFailure(ref m_deleteAddressSpaceFailuresRemaining))
                {
                    throw new SentinelException("DeleteAddressSpaceAsync failed.");
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref m_disposeCount);
                    if (TryConsumeFailure(ref m_disposeFailuresRemaining))
                    {
                        throw new SentinelException("Dispose failed.");
                    }
                }
                base.Dispose(disposing);
            }

            private static bool TryConsumeFailure(ref int failuresRemaining)
            {
                int current = Volatile.Read(ref failuresRemaining);
                while (current > 0)
                {
                    int observed = Interlocked.CompareExchange(
                        ref failuresRemaining,
                        current - 1,
                        current);
                    if (observed == current)
                    {
                        return true;
                    }
                    current = observed;
                }
                return false;
            }
        }

        /// <summary>
        /// Identifies which live lifecycle operation is under test in the parameterized
        /// identity-mismatch test.
        /// </summary>
        public enum LifecycleOperation
        {
            Reload,
            Remove,
            ShadowReload
        }

        public enum RetainedNotificationDispatchKind
        {
            Session,
            Modify,
            EventDelete
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
