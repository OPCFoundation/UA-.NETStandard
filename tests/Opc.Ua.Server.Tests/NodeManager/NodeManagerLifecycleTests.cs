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
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public sealed partial class NodeManagerLifecycleTests
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
        private ArrayOf<NodeManagerRegistration> m_startupRegistrations;

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
                AppContext.BaseDirectory,
                "nl-" + Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<LifecycleTestServer>(t => new LifecycleTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create().CreateLogger<NodeManagerLifecycleTests>();
            m_startupRegistrationIds = [];
            m_startupRegistrations = m_server.NodeManagerLifecycle.Registrations;
            m_startupRegistrations.ForEach(
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
            // A fresh manager's published identity is generation 1; the handle must match it.
            const long generation = 1;
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
            nodeManager.Setup(manager => manager.NamespaceUris).Returns((IEnumerable<string>)null!);

            var registration = new NodeManagerRegistration(
                Guid.NewGuid(),
                1,
                nodeManager.Object);

            Assert.That(registration.NamespaceUris.IsNull, Is.True);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task EmptyRetiredGenerationDrainDoesNotBlockShutdownAsync()
        {
            NodeManagerLifecycle lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
            MethodInfo scheduleDrain = typeof(NodeManagerLifecycle).GetMethod(
                "ScheduleRetiredGenerationDrainCleanup",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new AssertionException("The retired-generation drain scheduler was not found.");

            scheduleDrain.Invoke(lifecycle, null);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await lifecycle.BeginShutdownAsync(m_server.CurrentInstance, timeout.Token)
                .AsTask()
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Reclamation never retains the publication writer while a manager closes sessions, deletes, or disposes.
        /// </summary>
        [TestCase("SessionClosing")]
        [TestCase("DeleteAddressSpace")]
        [TestCase("Dispose")]
        public async Task ReclaimerCallbacksCanPerformNestedLifecycleMutationsAsync(string stage)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            const string originalUri = kModelNamespaceUri + ":ReclaimerOriginal";
            const string nestedUri = kModelNamespaceUri + ":ReclaimerNested";
            Mock<IAsyncNodeManager> original = CreateLifecycleNodeManager(originalUri);
            Mock<IAsyncDisposable> disposal = original.As<IAsyncDisposable>();
            int callbacks = 0;
            async ValueTask MutateAsync()
            {
                Interlocked.Increment(ref callbacks);
                Mock<IAsyncNodeManager> nested = CreateLifecycleNodeManager(nestedUri);
                var nestedFactory = new Mock<IAsyncNodeManagerFactory>();
                nestedFactory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(nested.Object);
                NodeManagerRegistration added = await m_server.NodeManagerLifecycle.AddAsync(
                    nestedFactory.Object, null, timeout.Token).ConfigureAwait(false);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Find(value => value.Id == added.Id), Is.Not.Null);
                await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, added, timeout.Token).ConfigureAwait(false);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Find(value => value.Id == added.Id), Is.Null);
            }
            if (stage == "SessionClosing")
            {
                original.Setup(value => value.SessionClosingAsync(
                    It.IsAny<OperationContext>(), It.IsAny<NodeId>(), false, It.IsAny<CancellationToken>()))
                    .Returns(() => MutateAsync());
            }
            else if (stage == "DeleteAddressSpace")
            {
                original.Setup(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                    .Returns(() => MutateAsync());
            }
            else
            {
                disposal.Setup(value => value.DisposeAsync()).Returns(() => MutateAsync());
            }
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(original.Object);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory.Object, null, timeout.Token).ConfigureAwait(false);

            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, registration, timeout.Token)
                .ConfigureAwait(false);

            Assert.That(callbacks, Is.EqualTo(1));
            original.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            disposal.Verify(value => value.DisposeAsync(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
            Assert.That(((NodeManagerLifecycle)m_server.NodeManagerLifecycle).RetiredNodeManagerCount, Is.Zero);
        }

        /// <summary>
        /// A host failure after the routing swap but before the publication callback cannot lose retirement ownership.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task PostSwapHostFailureRetainsRegistrationAndRetirementOwnershipAsync(
            bool replace,
            bool notifyBeforeFailure)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var server = (ServerInternalData)m_server.CurrentInstance;
            IMasterNodeManager master = server.NodeManager;
            var host = (IDynamicNodeManagerHost)master;
            var batchHost = (IDynamicNodeManagerBatchHost)master;
            var proxy = new Mock<IMasterNodeManager>();
            Mock<IDynamicNodeManagerHost> dynamicProxy = proxy.As<IDynamicNodeManagerHost>();
            Mock<IDynamicNodeManagerBatchHost> batchProxy = proxy.As<IDynamicNodeManagerBatchHost>();
            proxy.SetupGet(value => value.AsyncNodeManagers).Returns(() => master.AsyncNodeManagers);
            proxy.SetupGet(value => value.ConfigurationNodeManager).Returns(master.ConfigurationNodeManager);
            proxy.SetupGet(value => value.DiagnosticsNodeManager).Returns(master.DiagnosticsNodeManager);
            proxy.SetupGet(value => value.CoreNodeManager).Returns(master.CoreNodeManager);
            dynamicProxy.SetupGet(value => value.GenerationReconciler).Returns(host.GenerationReconciler);
            dynamicProxy.Setup(value => value.PrepareAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns<IAsyncNodeManager, CancellationToken>((manager, ct) => host.PrepareAsync(manager, ct));
            dynamicProxy.Setup(value => value.AbortAsync(
                It.IsAny<PreparedNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns<PreparedNodeManager, CancellationToken>((prepared, ct) => host.AbortAsync(prepared, ct));
            dynamicProxy.Setup(value => value.DestroyAddressSpaceAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns<IAsyncNodeManager, CancellationToken>((manager, ct) => host.DestroyAddressSpaceAsync(manager, ct));
            dynamicProxy.Setup(value => value.RemoveDestroyedExternalReferencesAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns<IAsyncNodeManager, CancellationToken>(
                    (manager, ct) => host.RemoveDestroyedExternalReferencesAsync(manager, ct));
            dynamicProxy.Setup(value => value.FinalizeRetiredGenerationNotificationsAsync(
                It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                .Returns<IAsyncNodeManager, CancellationToken>(
                    (manager, ct) => host.FinalizeRetiredGenerationNotificationsAsync(manager, ct));
            batchProxy.SetupGet(value => value.RoutingRevision).Returns(() => batchHost.RoutingRevision);
            batchProxy.Setup(value => value.PrepareReferencesAsync(
                It.IsAny<ArrayOf<PreparedNodeManager>>(), It.IsAny<ArrayOf<IAsyncNodeManager>>(),
                It.IsAny<NodeManagerRoutingTable.RoutingSnapshot>(), It.IsAny<CancellationToken>()))
                .Returns<ArrayOf<PreparedNodeManager>, ArrayOf<IAsyncNodeManager>,
                    NodeManagerRoutingTable.RoutingSnapshot, CancellationToken>(
                    (candidates, removed, view, token) =>
                        batchHost.PrepareReferencesAsync(candidates, removed, view, token));
            batchProxy.Setup(value => value.ReleaseRetiringEventGeneration(It.IsAny<NodeManagerGeneration>()))
                .Callback<NodeManagerGeneration>(batchHost.ReleaseRetiringEventGeneration);
            bool failPublication = false;
            var failure = new InvalidOperationException("The host failed around its publication notification.");
            batchProxy.Setup(value => value.CommitBatchAsync(It.IsAny<NodeManagerBatchContext>()))
                .Returns<NodeManagerBatchContext>(context => batchHost.CommitBatchAsync(failPublication
                    ? context with
                    {
                        Published = async () =>
                        {
                            if (notifyBeforeFailure)
                            {
                                await context.Published().ConfigureAwait(false);
                            }
                            throw failure;
                        }
                    }
                    : context));
            PropertyInfo nodeManagerProperty = typeof(ServerInternalData).GetProperty(nameof(ServerInternalData.NodeManager))
                ?? throw new AssertionException("The bound master NodeManager property was not found.");
            // Adapt only the existing host notification seam; the real master still performs the routing swap.
            nodeManagerProperty.SetValue(server, proxy.Object);
            NodeManagerBatchResult? result = null;
            try
            {
                using var lifecycle = new NodeManagerLifecycle(m_server);
                int initialRegistrationCount = lifecycle.Registrations.Count;
                Mock<IAsyncNodeManager> original = CreateLifecycleNodeManager(kModelNamespaceUri + ":PostSwap");
                Mock<IDisposable> disposal = original.As<IDisposable>();
                original.As<INodeManagerReloadParticipant>().Setup(value => value.PrepareReloadAsync(
                    It.IsAny<IAsyncNodeManager>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ArrayOf<LocalReference>>([]));
                var originalFactory = new Mock<IAsyncNodeManagerFactory>();
                originalFactory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(original.Object);
                NodeManagerRegistration registration = await lifecycle.AddAsync(
                    originalFactory.Object, timeout.Token).ConfigureAwait(false);
                var replacement = new Mock<IAsyncNodeManagerFactory>();
                Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(kModelNamespaceUri + ":PostSwap");
                Mock<IDisposable> candidateDisposal = candidate.As<IDisposable>();
                replacement.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(candidate.Object);
                int applicationPublications = 0;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [replace
                        ? NodeManagerBatchChange.Replace(registration, replacement.Object)
                        : NodeManagerBatchChange.Remove(registration)], timeout.Token).ConfigureAwait(false);
                failPublication = true;

                result = await prepared.CommitAsync(_ => default,
                    () => applicationPublications++, timeout.Token).ConfigureAwait(false);
                await result.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(result.CleanupFailure, Is.Not.Null);
                Assert.That(result.CleanupFailure!.ToString(), Does.Contain(failure.Message));
                Assert.That(applicationPublications, Is.EqualTo(1));
                Assert.That(result.Revision, Is.EqualTo(batchHost.RoutingRevision.Revision));
                Assert.That(result.Slots, Has.Count.EqualTo(initialRegistrationCount + (replace ? 1 : 0)));
                Assert.That(lifecycle.Registrations,
                    Has.Count.EqualTo(initialRegistrationCount + (replace ? 1 : 0)));
                Assert.That(lifecycle.RetiredNodeManagerCount, Is.Zero);
                original.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
                disposal.Verify(value => value.Dispose(), Times.Once);
                if (replace)
                {
                    NodeManagerRegistration current = lifecycle.Registrations.ToList()
                        .Single(value => value.Id == registration.Id);
                    Assert.That(current.NodeManager, Is.SameAs(candidate.Object));
                    Assert.That(current.Id, Is.EqualTo(registration.Id));
                    Assert.That(current.Generation, Is.EqualTo(registration.Generation + 1));
                    Assert.That(result.Slots.Find(slot => slot.Id == registration.Id), Is.SameAs(current.Slot));
                    failPublication = false;
                    await RemoveAndRetireAsync(lifecycle, current, timeout.Token).ConfigureAwait(false);
                    candidateDisposal.Verify(value => value.Dispose(), Times.Once);
                }
            }
            finally
            {
                nodeManagerProperty.SetValue(server, master);
            }
        }

        /// <summary>
        /// Reference cleanup retains its resolved live owner until both lookup and the reference callback finish.
        /// </summary>
        [Test]
        public async Task ReclaimerReferenceLookupPinsOwnerAcrossConcurrentRetirementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var reclaimer = new GenerationReclaimer();
            const string ownerUri = kModelNamespaceUri + ":CleanupReferenceOwner";
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(ownerUri);
            int disposed = 0;
            owner.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => Interlocked.Increment(ref disposed));
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(owner.Object);
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle.AddAsync(
                factory.Object, timeout.Token).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var source = new NodeId(9900, (ushort)server.NamespaceUris.GetIndex(ownerUri));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            object handle = new();
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    return handle;
                });
            int referenceCallbacks = 0;
            bool callbackAfterDisposal = false;
            owner.Setup(value => value.DeleteReferenceAsync(
                handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                It.IsAny<CancellationToken>())).Returns(() =>
                {
                    Interlocked.Increment(ref referenceCallbacks);
                    callbackAfterDisposal = Volatile.Read(ref disposed) != 0;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var retiredManager = new Mock<IAsyncNodeManager>();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(retiredManager.Object);
            generation.Gate.Retire();
            var retired = new GenerationReclaimer.RetiredGeneration(
                server, (IDynamicNodeManagerHost)server.NodeManager, generation,
                [new LocalReference(source, ReferenceTypeIds.HasComponent, false, new NodeId(9901))],
                needsDetachment: true);
            reclaimer.Enqueue([retired]);
            reclaimer.Activate([retired]);
            reclaimer.Schedule();
            NodeManagerBatchResult? removal = null;
            try
            {
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await using IPreparedNodeManagerBatch prepared = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration)], timeout.Token).ConfigureAwait(false);
                removal = await prepared.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);

                Assert.That(removal.Retirement.IsCompleted, Is.False,
                    "The reference cleanup still owns an in-flight call into this generation.");
                Assert.That(Volatile.Read(ref disposed), Is.Zero);
                owner.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                release.TrySetResult(true);
                await reclaimer.CompleteShutdownAsync(timeout.Token).ConfigureAwait(false);
                if (removal is not null)
                {
                    await removal.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            Assert.That(referenceCallbacks, Is.EqualTo(1));
            Assert.That(callbackAfterDisposal, Is.False);
            Assert.That(Volatile.Read(ref disposed), Is.EqualTo(1));
        }

        /// <summary>
        /// Destroyed-reference callbacks may publish dependencies without holding a shared host publication semaphore.
        /// </summary>
        [Test]
        public async Task DestroyedReferenceCallbackCanPublishNestedLifecycleMutationAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            const string ownerUri = kModelNamespaceUri + ":DestroyedReferenceOwner";
            Mock<IAsyncNodeManager> owner = CreateLifecycleNodeManager(ownerUri);
            var ownerFactory = new Mock<IAsyncNodeManagerFactory>();
            ApplicationConfiguration? configuration = null;
            ownerFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>())).Returns((
                    IServerInternal _, ApplicationConfiguration supplied, CancellationToken _) =>
                {
                    configuration = supplied;
                    return new ValueTask<IAsyncNodeManager>(owner.Object);
                });
            await m_server.NodeManagerLifecycle.AddAsync(ownerFactory.Object, timeout.Token).ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var source = new NodeId(9902, (ushort)server.NamespaceUris.GetIndex(ownerUri));
            object handle = new();
            owner.Setup(value => value.GetManagerHandleAsync(source, It.IsAny<CancellationToken>()))
                .ReturnsAsync(handle);
            owner.Setup(value => value.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), handle, It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeMetadata(handle, source));
            Mock<IAsyncNodeManager> dependency = CreateLifecycleNodeManager(
                kModelNamespaceUri + ":DestroyedReferenceDependency");
            var dependencyFactory = new Mock<IAsyncNodeManagerFactory>();
            dependencyFactory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(dependency.Object);
            NodeManagerRegistration? added = null;
            owner.Setup(value => value.DeleteReferenceAsync(
                handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                It.IsAny<CancellationToken>())).Returns(async () =>
                {
                    added = await m_server.NodeManagerLifecycle.AddAsync(
                        dependencyFactory.Object, timeout.Token).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            using var destroyed = new NodeManagementLifecycleNodeManager(
                server, configuration!, m_logger, kModelNamespaceUri + ":DestroyedReferenceSource");
            destroyed.GetRemovedExternalReferences().Add(
                new LocalReference(source, ReferenceTypeIds.HasComponent, false, new NodeId(9903)));

            await ((IDynamicNodeManagerHost)server.NodeManager).RemoveDestroyedExternalReferencesAsync(
                destroyed, timeout.Token).ConfigureAwait(false);

            Assert.That(added, Is.Not.Null);
            Assert.That(CountMatches(m_server.NodeManagerLifecycle.Registrations,
                registration => ReferenceEquals(registration, added)), Is.EqualTo(1));
            Assert.That(destroyed.GetRemovedExternalReferences(), Is.Empty);
            owner.Verify(value => value.DeleteReferenceAsync(
                handle, ReferenceTypeIds.HasComponent, false, It.IsAny<ExpandedNodeId>(), false,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A custom-routed manager with no namespace list must keep its place in
        /// the master manager list across reload and removal.
        /// </summary>
        [Test]
        public async Task NullNamespaceNodeManagerCanReloadAndRemoveAsync()
        {
            Mock<IAsyncNodeManager> originalManager =
                CreateLifecycleNodeManager(namespaceUri: null!);
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
                CreateLifecycleNodeManager(namespaceUri: null!);
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
                    m_server.CurrentInstance.NodeManager.AsyncNodeManagers.ToArray(),
                    Does.Contain(replacementManager.Object));
            });

            await m_server.NodeManagerLifecycle
                .RemoveAsync(replacement, null)
                .ConfigureAwait(false);

            Assert.That(
                m_server.CurrentInstance.NodeManager.AsyncNodeManagers.ToArray(),
                Does.Not.Contain(replacementManager.Object));
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
        [TestCase(LifecycleOperation.Reload, MismatchKind.SameIdDifferentGeneration)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.StaleGeneration)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.ForeignId)]
        [TestCase(LifecycleOperation.Remove, MismatchKind.SameIdDifferentGeneration)]
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.StaleGeneration)]
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.ForeignId)]
        [TestCase(LifecycleOperation.ShadowReload, MismatchKind.SameIdDifferentGeneration)]
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
                    1,
                    new Mock<IAsyncNodeManager>().Object),
                MismatchKind.SameIdDifferentGeneration => new NodeManagerRegistration(
                    current.Id,
                    1,
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

        /// <summary>
        /// Request callbacks use ordinary factories through canonical and compatibility entry points.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task AddFromExecutingRequestDoesNotRequireFactoryOptInAsync(int entryPoint)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            TrackingLifecycleNodeManager? manager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kGeneration1Value, created => manager = created);
            using var requestLifetime = new RequestLifetime();
            using OperationContext callerContext = CreateExecutingRequest(server, requestLifetime);
            NodeManagerRegistration registration;
            if (entryPoint == 0)
            {
                await using IPreparedNodeManagerBatch prepared = await m_server.NodeManagerLifecycle
                    .PrepareAsync([NodeManagerBatchChange.Add(factory)], timeout.Token).ConfigureAwait(false);
                NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(result.CleanupFailure, Is.Null);
                registration = result.Registrations[0];
            }
            else
            {
                IOperationContext? context = entryPoint == 1
                    ? callerContext
                    : server.DefaultSystemContext.Copy(callerContext).GetOperationContext();
                Assert.That(context, Is.SameAs(callerContext));
                registration = await m_server.NodeManagerLifecycle
                    .AddAsync(factory, context, timeout.Token).ConfigureAwait(false);
            }
            Assert.That(registration.NodeManager, Is.SameAs(RequireLifecycleValue(manager)));
            Assert.That(registration.Identity.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
            await m_server.NodeManagerLifecycle.RemoveAsync(registration, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// A callback may replace and remove its own pinned generation without awaiting its drain.
        /// </summary>
        [Test]
        public async Task ReloadAndRemoveFromExecutingRequestDoNotWaitForOwnViewAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IServerInternal server = m_server.CurrentInstance;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 1), timeout.Token).ConfigureAwait(false);
            NodeManagerBatchResult replaced;
            NodeManagerBatchResult removed;

            using (var requestLifetime = new RequestLifetime())
            using (OperationContext callerContext = CreateExecutingRequest(server, requestLifetime))
            {
                callerContext.PinRouting(GetRoutingTable((MasterNodeManager)server.NodeManager));
                await using IPreparedNodeManagerBatch replacement = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(
                        registration, new RuntimeNodeSetNodeManagerFactory(CreateGenerationOptions(generation: 2)))],
                    timeout.Token).ConfigureAwait(false);
                replaced = await replacement.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                NodeManagerRegistration current = replaced.Registrations[0];
                Assert.That(replaced.CleanupFailure, Is.Null);
                Assert.That(current.Id, Is.EqualTo(registration.Id));
                Assert.That(current.Generation, Is.EqualTo(2));
                Assert.That(replaced.Retirement.IsCompleted, Is.False);
                await using IPreparedNodeManagerBatch removal = await m_server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(current)], timeout.Token).ConfigureAwait(false);
                removed = await removal.CommitAsync(_ => default, timeout.Token).ConfigureAwait(false);
                Assert.That(removed.CleanupFailure, Is.Null);
                Assert.That(current.Slot.TryGetCurrent(out _), Is.False);
                Assert.That(replaced.Retirement.IsCompleted, Is.False);
            }

            await replaced.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
            await removed.Retirement.WaitAsync(timeout.Token).ConfigureAwait(false);
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
        public async Task RuntimeNodeSetCanBeAddedFromRequestScopeAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var context = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            RuntimeNodeSetOptions options = CreateGenerationOptions(generation: 1);
            NodeManagerRegistration? registration = null;

            try
            {
                using IDisposable requestScope =
                    server.RequestManager.EnterRequestScope(context);
                registration = await m_server.NodeManagerLifecycle
                    .AddAsync(
                        new RuntimeNodeSetNodeManagerFactory(options),
                        CancellationToken.None)
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
        public async Task RuntimeNodeSetCanBeReloadedAndRemovedFromRequestScopeAsync()
        {
            RuntimeNodeSetOptions initialOptions = CreateGenerationOptions(generation: 1);
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
        public async Task ConcurrentCallbackAddsRejectStalePreparationWithoutRequestExclusionAsync()
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
            var factoryA = new CountingNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return managerA.Object;
                });
            var factoryB = new CountingNodeManagerFactory(
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

            NodeManagerRegistration registration;
            try
            {
                Assert.That(server.RequestManager.TrackedRequestCount, Is.GreaterThanOrEqualTo(1));
                registration = await addB.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(releaseFirstFactory.Task.IsCompleted, Is.False);
                Assert.That(server.RequestManager.TrackedRequestCount, Is.EqualTo(1),
                    "The first request is still executing its factory; writer suspension must not exclude it.");
            }
            finally
            {
                releaseFirstFactory.TrySetResult(true);
            }

            await Assert.ThatAsync(
                () => addA.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(server.RequestManager.TrackedRequestCount, Is.Zero);
            Assert.That(factoryA.CreateCount, Is.EqualTo(1));
            Assert.That(factoryB.CreateCount, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(registration));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList()
                .Any(value => ReferenceEquals(value.NodeManager, managerA.Object)), Is.False);
            await m_server.NodeManagerLifecycle.RemoveAsync(registration, null).ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentCallbackReloadsRejectStalePreparationWithoutRequestExclusionAsync()
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
                    new CountingNodeManagerFactory(
                        [NamespaceUriA],
                        (_, _, _) => new ValueTask<IAsyncNodeManager>(
                            originalManagerA.Object)), null)
                .ConfigureAwait(false);
            NodeManagerRegistration originalB = await m_server.NodeManagerLifecycle
                .AddAsync(
                    new CountingNodeManagerFactory(
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
            var replacementFactoryA = new CountingNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return replacementManagerA.Object;
                });
            var replacementFactoryB = new CountingNodeManagerFactory(
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

            NodeManagerRegistration replacement;
            try
            {
                Assert.That(server.RequestManager.TrackedRequestCount, Is.GreaterThanOrEqualTo(1));
                replacement = await reloadB.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(releaseFirstFactory.Task.IsCompleted, Is.False);
                Assert.That(server.RequestManager.TrackedRequestCount, Is.EqualTo(1),
                    "The first request is still executing its factory; writer suspension must not exclude it.");
            }
            finally
            {
                releaseFirstFactory.TrySetResult(true);
            }

            await Assert.ThatAsync(
                () => reloadA.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.InstanceOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(server.RequestManager.TrackedRequestCount, Is.Zero);
            Assert.That(replacementFactoryA.CreateCount, Is.EqualTo(1));
            Assert.That(replacementFactoryB.CreateCount, Is.EqualTo(1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(originalA));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(), Does.Contain(replacement));
            Assert.That(replacement.Id, Is.EqualTo(originalB.Id));
            Assert.That(replacement.Generation, Is.EqualTo(originalB.Generation + 1));
            Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList()
                .Any(value => ReferenceEquals(value.NodeManager, replacementManagerA.Object)), Is.False);
            await m_server.NodeManagerLifecycle.RemoveAsync(replacement, null).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(originalA, null).ConfigureAwait(false);
        }

        [Test]
        public async Task ShutdownWaitsForSimultaneousCallbackLifecycleOperationsAsync()
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
            var secondFactoryEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factoryA = new CountingNodeManagerFactory(
                [NamespaceUriA],
                async (_, _, ct) =>
                {
                    firstFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return managerA.Object;
                });
            var factoryB = new CountingNodeManagerFactory(
                [NamespaceUriB],
                async (_, _, ct) =>
                {
                    secondFactoryEntered.TrySetResult(true);
                    await releaseFirstFactory.Task.WaitAsync(ct).ConfigureAwait(false);
                    return managerB.Object;
                });
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
                await secondFactoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

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
                    Is.EqualTo(1),
                    "Both admitted preparations must finish and abort before shutdown can dispose their owner.");

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
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, original.NodeManager)
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

                IMonitoredItem reloadedItem = GetOwnedMonitoredItems(subscription, reloaded.NodeManager)
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
        /// A prepared replacement migrates a compatible monitored item and restores the old owner if the
        /// durable decision is rejected.
        /// </summary>
        /// <param name="rejectDecision">Whether the durable decision rejects publication.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PreparedBatchMigratesOrRestoresCompatibleMonitoredItemAsync(bool rejectDecision)
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
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, original.NodeManager).Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (_, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);

                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [
                        NodeManagerBatchChange.Replace(
                            original,
                            new RuntimeNodeSetNodeManagerFactory(CreateGenerationOptions(generation: 2)))
                    ],
                    null).ConfigureAwait(false);
                if (rejectDecision)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await prepared.CommitAsync(
                            _ => new ValueTask(Task.FromException(new InvalidOperationException("Rejected."))))
                            .ConfigureAwait(false)).ConfigureAwait(false);
                }
                else
                {
                    NodeManagerBatchResult result = await prepared.CommitAsync(_ => default).ConfigureAwait(false);
                    NodeManagerRegistration replacement = result.Registrations[0];
                    IMonitoredItem migratedItem = GetOwnedMonitoredItems(subscription, replacement.NodeManager)
                        .Single();
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(result.CleanupFailure, Is.Null);
                        Assert.That(ReferenceEquals(migratedItem, originalItem), Is.True);
                        Assert.That(migratedItem.Id, Is.EqualTo(monitoredItemId));
                    }
                }

                NodeManagerRegistration current = m_server.NodeManagerLifecycle.Registrations
                    .Find(registration => registration.Id == original.Id);
                IMonitoredItem currentItem = GetOwnedMonitoredItems(subscription, current.NodeManager).Single();
                int expectedValue = rejectDecision ? kGeneration1Value : kGeneration2Value;
                if (rejectDecision)
                {
                    DataValue currentValue = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                    Assert.That(currentValue.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));
                    ArrayOf<WriteValue> writeValues =
                    [
                        new WriteValue
                        {
                            NodeId = valueNodeId,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(kGeneration1Value + kGeneration2Value)
                        }
                    ];
                    RequestHeader requestHeader = m_requestHeader;
                    requestHeader.Timestamp = DateTimeUtc.Now;
                    WriteResponse writeResponse = await m_server.WriteAsync(
                        m_secureChannelContext,
                        requestHeader,
                        writeValues,
                        RequestLifetime.None).ConfigureAwait(false);
                    Assert.That(writeResponse.Results[0], Is.EqualTo(StatusCodes.Good));
                    expectedValue = kGeneration1Value + kGeneration2Value;
                }
                (MonitoredItemNotification notification, acknowledgements) =
                    await PublishForDataChangeAsync(services, subscriptionId, acknowledgements).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(ReferenceEquals(currentItem, originalItem), Is.True);
                    Assert.That(currentItem.Id, Is.EqualTo(monitoredItemId));
                    Assert.That(notification.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(
                        notification.Value.WrappedValue.GetInt32(),
                        Is.EqualTo(expectedValue));
                }
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
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, original.NodeManager)
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

                Assert.That(subscription.ContainsMonitoredItem(originalItem), Is.True);
                Assert.That(dropped.Slot.UnresolvedItems.Keys, Does.Contain(originalItem));

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

                IMonitoredItem recoveredItem = GetOwnedMonitoredItems(subscription, recovered.NodeManager)
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
        /// Remove reports the lost node without adopting the old item on a later add.
        /// The client can recreate monitoring and delete its original terminal item.
        /// </summary>
        [Test]
        public async Task RemoveInvalidatesUntilClientRecreatesMonitoringAsync()
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
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, original.NodeManager)
                .Single();
            ArrayOf<SubscriptionAcknowledgement> acknowledgements = [];

            try
            {
                (_, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);

                await m_server.NodeManagerLifecycle.RemoveAsync(original, null).ConfigureAwait(false);

                Assert.That(subscription.ContainsMonitoredItem(originalItem), Is.True);
                Assert.That(originalItem.NodeManager, Is.Null);

                (MonitoredItemNotification bad, acknowledgements) =
                    await PublishForDataChangeAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                Assert.That(bad.Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                NodeManagerRegistration added = await m_server.NodeManagerLifecycle
                    .AddRuntimeNodeSetAsync(CreateGenerationOptions(generation: 2), null)
                    .ConfigureAwait(false);
                Assert.That(GetOwnedMonitoredItems(subscription, added.NodeManager), Is.Empty);
                Assert.That(((IMonitoredItemGenerationBinding)originalItem).IsTerminal, Is.True);
                Assert.That(original.Slot.UnresolvedItems, Is.Empty);
                Assert.That(originalItem.Id, Is.EqualTo(monitoredItemId));

                (uint recreatedSubscriptionId, uint recreatedItemId) =
                    await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId).ConfigureAwait(false);
                try
                {
                    (MonitoredItemNotification current, _) = await PublishForDataChangeAsync(
                        services, recreatedSubscriptionId, []).ConfigureAwait(false);
                    Assert.That(recreatedItemId, Is.Not.EqualTo(monitoredItemId));
                    Assert.That(current.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(current.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
                }
                finally
                {
                    await DeleteSubscriptionAsync(services, recreatedSubscriptionId).ConfigureAwait(false);
                }

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
                    GetOwnedMonitoredItems(subscription, added.NodeManager),
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
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, registration.NodeManager)
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
                        TypeDefinition = VariableTypeIds.BaseDataVariableType,
                        NodeAttributes = new ExtensionObject(attributes)
                    }],
                    RequestLifetime.None).ConfigureAwait(false);
                Assert.That(addResponse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(addResponse.Results[0].AddedNodeId, Is.EqualTo(valueNodeId));

                IMonitoredItem recoveredItem = GetOwnedMonitoredItems(subscription, registration.NodeManager)
                    .Single();
                Assert.That(ReferenceEquals(recoveredItem, originalItem), Is.True);
                Assert.That(recoveredItem.Id, Is.EqualTo(monitoredItemId));

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
        /// The legacy ShadowReload alias uses Migrate: the switch routes new service requests to
        /// the replacement and moves a compatible monitored item without changing its client
        /// subscription or monitored-item identity. The retired generation is not kept alive
        /// for future requests or notifications.
        /// </summary>
        [Test]
        public async Task ShadowReloadMigratesActiveMonitoredItemAndDisposesRetiredGenerationAsync()
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
            (uint subscriptionId, uint monitoredItemId) = await CreateSubscriptionWithMonitoredItemAsync(
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

                // The compatible item is now bound to the replacement generation, not retained
                // as a hidden dependency on the old address space.
                await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);
                Assert.That(originalManager.Find(valueNodeId), Is.Null);

                ISubscription subscription = server.SubscriptionManager
                    .GetSubscriptions()
                    .Single(s => s.Id == subscriptionId);
                var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
                Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
                Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.True);
                subscription.GetMonitoredItems(out ArrayOf<uint> serverHandles, out _);
                Assert.That(serverHandles.ToList(), Does.Contain(monitoredItemId));

                DataValue? migratedValue;
                (migratedValue, acknowledgements) = await PublishForDataChangeAsync(
                    services,
                    subscriptionId,
                    acknowledgements,
                    clientHandle).ConfigureAwait(false);
                Assert.That(migratedValue!.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                // A value push from the replacement reaches the migrated item.
                const int pushedValue = 777;
                var replacementManager = (AsyncCustomNodeManager)reloaded.NodeManager;
                var replacementValueState = (BaseVariableState)replacementManager.Find(valueNodeId)!;
                Assert.That(replacementValueState, Is.Not.Null);
                replacementValueState.Value = pushedValue;
                replacementValueState.Timestamp = DateTimeUtc.Now;
                replacementValueState.StatusCode = StatusCodes.Good;
                replacementValueState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await replacementValueState
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

                // The replacement generation serves the value that was pushed to its node.
                DataValue valueAfterPush = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(valueAfterPush.WrappedValue.GetInt32(), Is.EqualTo(pushedValue));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }

            // Removing the replacement can race an already-claimed background retirement
            // drain. Its completion does not join that independent cleanup operation.
            NodeManagerRegistration current = m_server.NodeManagerLifecycle.Registrations
                .Find(r => r.Id == original.Id);
            Assert.That(current, Is.Not.Null);
            await m_server.NodeManagerLifecycle.RemoveAsync(current, null).ConfigureAwait(false);

            Assert.That(
                CountMatches(m_server.NodeManagerLifecycle.Registrations, r => r.Id == original.Id),
                Is.Zero);
            await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);
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
                await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);
                Assert.That(originalManager.Find(valueNodeId), Is.Null,
                    "The prior generation must be disposed after its independent retirement completes.");

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
                    Is.EqualTo(StatusCodes.Good));

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
        /// After a ShadowReload, a compatible data monitored item is owned by the replacement
        /// generation. Modify, SetMonitoringMode, and Delete continue to use the same item ID,
        /// while the retired generation is disposed independently of subscription lifetime.
        /// </summary>
        [Test]
        public async Task ShadowReloadedDataMonitoredItemMigratesWithStableIdAsync()
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

            // The existing item has moved to the replacement generation.
            ISubscription subscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(s => s.Id == subscriptionId);
            var tracker = (ISubscriptionMonitoredItemLifecycle)subscription;
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.True);
            await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);

            // New Reads are routed to the replacement generation.
            DataValue afterSwitch = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(afterSwitch.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(afterSwitch.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            DataValue? migratedValue;
            (migratedValue, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);
            Assert.That(migratedValue!.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            // Notifications continue from the replacement node after migration.
            var replacementManager = (AsyncCustomNodeManager)reloaded.NodeManager;
            await PushRetiredValueAsync(server, replacementManager, valueNodeId, 4242).ConfigureAwait(false);
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
                "Modify of the migrated monitored item must preserve its client-visible identity.");

            // (2) Disable then re-enable the migrated item.
            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            SetMonitoringModeResponse disableResponse = await services
                .SetMonitoringModeAsync(header, subscriptionId, MonitoringMode.Disabled, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(disableResponse.Results.Count, Is.EqualTo(1));
            Assert.That(disableResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Disabling the migrated item must preserve its client-visible identity.");

            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            SetMonitoringModeResponse enableResponse = await services
                .SetMonitoringModeAsync(header, subscriptionId, MonitoringMode.Reporting, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(enableResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Re-enabling the migrated item must preserve its client-visible identity.");

            // The re-enabled item still delivers a fresh value pushed on the replacement node.
            await PushRetiredValueAsync(server, replacementManager, valueNodeId, 5353).ConfigureAwait(false);
            (pushed, acknowledgements) = await PublishForDataChangeAsync(
                services,
                subscriptionId,
                acknowledgements,
                clientHandle).ConfigureAwait(false);
            Assert.That(pushed!.Value.WrappedValue.GetInt32(), Is.EqualTo(5353));

            // (3) Delete the migrated item using its unchanged client-visible ID.
            header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            DeleteMonitoredItemsResponse deleteResponse = await services
                .DeleteMonitoredItemsAsync(header, subscriptionId, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
            Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good),
                "Delete of the migrated item must succeed on the replacement generation.");
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);

            // The replacement generation is unaffected and still serves Reads.
            DataValue afterDrain = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(afterDrain.WrappedValue.GetInt32(), Is.EqualTo(5353));

            // Prompt cleanup: with the final item drained, the retired generation is disposed
            // WITHOUT any further lifecycle operation - its own address space is torn down.
            await AssertRetiredGenerationDisposedAsync(originalManager, valueNodeId).ConfigureAwait(false);

            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// A compatible event monitored item moves to the replacement during ShadowReload;
        /// deleting it unsubscribes the replacement without retaining the old generation.
        /// </summary>
        [Test]
        public async Task ShadowReloadedEventMonitoredItemMigratesAndUnbindsReplacementAsync()
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
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.True);
            await AssertRetiredGenerationDisposedAsync(originalManager, rootNodeId).ConfigureAwait(false);
            Assert.That(originalManager.Find(rootNodeId), Is.Null);

            RequestHeader header = m_requestHeader;
            header.Timestamp = DateTimeUtc.Now;
            DeleteMonitoredItemsResponse deleteResponse = await services
                .DeleteMonitoredItemsAsync(header, subscriptionId, [monitoredItemId])
                .ConfigureAwait(false);
            Assert.That(deleteResponse.Results.Count, Is.EqualTo(1));
            Assert.That(deleteResponse.Results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(subscription.MonitoredItemCount, Is.Zero);
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.False);

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
        public async Task ShadowDrainDoesNotDeadlockCallbackLifecycleCallAsync()
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
            NodeManagerRegistration? probeRegistration = null;
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
                        "The callback lifecycle call deadlocked with the background " +
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
        /// A retired manager receives no new session or all-events fan-out. Retirement releases its
        /// original all-events binding exactly once, independently of subsequent item deletion.
        /// </summary>
        [Test]
        public async Task ShadowRetiredManagerReceivesNoNewLifecycleNotificationsAsync()
        {
            TrackingLifecycleNodeManager? originalManager = null;
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
            Assert.That(originalManager!.AllEventsSubscribeCount, Is.EqualTo(1));

            TrackingLifecycleNodeManager? replacementManager = null;
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
                .GetSession(m_requestHeader.AuthenticationToken)!;
            int activationCount = originalManager.SessionActivatedCount;
            using var activationContext = new OperationContext(session, DiagnosticsMasks.None);
            await master
                .SessionActivatedAsync(
                    activationContext,
                    session.Id,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(
                originalManager.SessionActivatedCount,
                Is.EqualTo(activationCount),
                "Identity-change notification must only reach live generations.");

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
            await AssertRetiredGenerationDisposedAsync(
                originalManager,
                valueNodeId).ConfigureAwait(false);
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
        /// Event deletion releases the recorded all-events binding of a retiring generation exactly once,
        /// reports a failed release to the client, and cleanup never replays the release.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RetiredAllEventsDeletionCompletesBookkeepingAndReleasesGenerationAsync(
            bool unsubscribeFails)
        {
            StatusCode unsubscribeStatus = unsubscribeFails ? StatusCodes.BadCommunicationError : StatusCodes.Good;
            TrackingLifecycleNodeManager? retiredManager = null;
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
                    services, valueNodeId, clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, uint eventMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services, ObjectIds.Server).ConfigureAwait(false);
            ISubscription eventSubscription = server.SubscriptionManager.GetSubscriptions()
                .Single(subscription => subscription.Id == eventSubscriptionId);
            Assert.That(
                original.Identity.Gate.TryEnter(out GenerationGate.Lease? admission),
                Is.True,
                "The test must hold a real admission so the retired generation stays retiring.");
            GenerationGate.Lease? heldAdmission = admission;
            Task<NodeManagerRegistration>? replacementTask = null;
            NodeManagerRegistration? replacement = null;
            try
            {
                replacementTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(
                            original,
                            CreateNodeManagementFactory(kGeneration2Value, includeEuRange: false))
                        .AsTask());
                await WaitForConditionAsync(
                        () => master.RetiringEventGenerations.Contains(original.Identity))
                    .ConfigureAwait(false);
                retiredManager!.AllEventsUnsubscribeResult = new ServiceResult(unsubscribeStatus);

                RequestHeader header = m_requestHeader;
                header.Timestamp = DateTimeUtc.Now;
                DeleteMonitoredItemsResponse response = await services
                    .DeleteMonitoredItemsAsync(header, eventSubscriptionId, [eventMonitoredItemId])
                    .ConfigureAwait(false);

                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0], Is.EqualTo(unsubscribeStatus));
                Assert.That(eventSubscription.MonitoredItemCount, Is.Zero);
                Assert.That(
                    server.EventManager.GetMonitoredItems().Any(item => item.Id == eventMonitoredItemId),
                    Is.False);
                Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

                heldAdmission!.Dispose();
                heldAdmission = null;
                replacement = await replacementTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                heldAdmission?.Dispose();
                if (replacementTask is not null)
                {
                    replacement ??= await replacementTask.ConfigureAwait(false);
                }
            }

            await DeleteMonitoredItemAsync(services, dataSubscriptionId, dataMonitoredItemId).ConfigureAwait(false);
            await retiredManager.DisposalCompleted.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(retiredManager.Find(valueNodeId), Is.Null);
            Assert.That(master.RetiringEventGenerations, Has.None.SameAs(original.Identity));
            Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1),
                "Cleanup must not replay a release for an already deleted item.");
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(replacement, null).ConfigureAwait(false);
        }

        /// <summary>
        /// If a real generation admission delays cleanup, a monitored item that appears after
        /// notification suspension keeps the retiree alive and restores its notification fan-out.
        /// </summary>
        [Test]
        public async Task RetiredDrainRestoresNotificationsForLateMonitoredItemAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
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
            Assert.That(retiredManager!.AllEventsSubscribeCount, Is.EqualTo(1));

            var replacementCreated =
                new TaskCompletionSource<TrackingLifecycleNodeManager>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            IAsyncNodeManagerFactory replacementFactory = CreateTrackingNodeManagementFactory(
                kGeneration2Value,
                manager => replacementCreated.TrySetResult(manager));
            Assert.That(
                original.Identity.Gate.TryEnter(out GenerationGate.Lease? admission),
                Is.True,
                "The test must hold a real admission before the generation is retired.");
            GenerationGate.Lease? heldAdmission = admission ??
                throw new AssertionException("The test must retain its successful generation admission.");
            Task<NodeManagerRegistration>? replacementTask = null;
            NodeManagerRegistration? replacement = null;

            ISubscription dataSubscription = server.SubscriptionManager
                .GetSubscriptions()
                .Single(subscription => subscription.Id == dataSubscriptionId);
            var lateItem = new Mock<IMonitoredItem>();
            lateItem.SetupGet(item => item.Id).Returns(uint.MaxValue - 1);
            lateItem.SetupGet(item => item.NodeManager).Returns(original.NodeManager);
            lateItem.SetupGet(item => item.IsDurable).Returns(false);

            bool lateItemAdded = false;
            try
            {
                replacementTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(original, replacementFactory)
                        .AsTask());
                TrackingLifecycleNodeManager replacementManager = await replacementCreated.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await WaitForRegistrationAsync(
                    m_server.NodeManagerLifecycle,
                    original.Id,
                    replacementManager).ConfigureAwait(false);

                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);

                // A late item still naming the retired manager does not suspend the retired generation's
                // bindings: the retiring generation keeps receiving deletion fan-out through its binding record.
                AddSyntheticMonitoredItem(
                    (Subscription)dataSubscription,
                    lateItem.Object);
                lateItemAdded = true;

                replacement = await replacementTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(retiredManager.DisposeCount, Is.Zero);

                int unsubscribeCount = retiredManager.AllEventsUnsubscribeCount;
                await DeleteMonitoredItemAsync(
                    services,
                    eventSubscriptionId,
                    eventMonitoredItemId).ConfigureAwait(false);
                Assert.That(
                    retiredManager.AllEventsUnsubscribeCount,
                    Is.EqualTo(unsubscribeCount + 1),
                    "The draining generation must release the deleted item's binding.");

                heldAdmission!.Dispose();
                heldAdmission = null;
            }
            finally
            {
                if (lateItemAdded)
                {
                    RemoveSyntheticMonitoredItem(
                        (Subscription)dataSubscription,
                        lateItem.Object);
                }
                heldAdmission?.Dispose();
                if (replacementTask is not null)
                {
                    replacement ??= await replacementTask.ConfigureAwait(false);
                }
            }

            Assert.That(replacement, Is.Not.Null);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(
                retiredManager,
                valueNodeId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(replacement!, null).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Successful final retirement must not unsubscribe the deleted item twice.");
        }

        /// <summary>
        /// A pending retired-generation drain must not block removal of an unrelated current
        /// registration, and a later callback reload must reject the now-stale registration.
        /// </summary>
        [Test]
        public async Task PendingRetiredDrainAllowsUnrelatedRemoveAndRejectsStaleReloadAsync()
        {
            const string TargetNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DrainReloadWinner";
            TrackingLifecycleNodeManager? retiredManager = null;
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
            NodeManagerRegistration target = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(targetOptions, null)
                .ConfigureAwait(false);

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            Task? removeTask = null;
            IDisposable requestScope =
                server.RequestManager.EnterRequestScope(callbackContext);
            try
            {
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);

                // The unrelated remove must not wait for the pending retired drain.
                removeTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .RemoveAsync(target, null)
                        .AsTask());
                await removeTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                RuntimeNodeSetOptions replacementOptions = CreateOptions(
                    TargetNamespaceUri,
                    value: 402);
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                InvalidOperationException exception =
                    await Assert.ThrowsAsync<InvalidOperationException>(
                        async () => await m_server.NodeManagerLifecycle
                            .ReloadRuntimeNodeSetAsync(
                                target,
                                replacementOptions,
                                callbackContext,
                                timeout.Token)
                            .ConfigureAwait(false)).ConfigureAwait(false) ??
                        throw new InvalidOperationException(
                            "Reload unexpectedly accepted a registration removed during retired cleanup.");
                Assert.That(
                    exception.Message,
                    Is.EqualTo(
                        "The registration is stale or is not owned by this lifecycle provider."));
            }
            finally
            {
                requestScope.Dispose();
            }

            await removeTask!.ConfigureAwait(false);
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(
                    registration => registration.Id == target.Id),
                Is.Null);

            await AssertRetiredGenerationDisposedAsync(
                retiredManager!,
                retireeValueId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retireeReplacement, null)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// A pending retired-generation drain must not block an unrelated reload factory for
        /// a registration in the current view.
        /// </summary>
        [Test]
        public async Task PendingRetiredDrainDoesNotBlockUnrelatedReloadFactoryAsync()
        {
            const string TargetNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DrainRemoveWinner";
            TrackingLifecycleNodeManager? retiredManager = null;
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
                    "An unrelated reload factory should run during retired cleanup."));

            var callbackContext = new OperationContext(
                new RequestHeader(),
                secureChannelContext: null,
                RequestType.Call,
                RequestLifetime.None);
            Task<NodeManagerRegistration>? reloadTask = null;
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
                    .GetSession(m_requestHeader.AuthenticationToken)!;
                await WaitForRetiredNotificationsSuspendedAsync(
                    master,
                    retiredManager!,
                    session!).ConfigureAwait(false);
            }
            finally
            {
                requestScope.Dispose();
            }

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await reloadTask.ConfigureAwait(false));
            Assert.That(
                exception.Message,
                Is.EqualTo(
                    "An unrelated reload factory should run during retired cleanup."));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(
                    registration => registration.Id == target.Id),
                Is.SameAs(target));

            await AssertRetiredGenerationDisposedAsync(
                retiredManager!,
                retireeValueId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(target, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle
                .RemoveAsync(retireeReplacement, null)
                .ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// While a retired generation is still draining, deleting a pre-reload all-events item releases its
        /// recorded binding exactly once, an item created against the replacement is never bound to or released
        /// on the retired generation, and cleanup replays nothing.
        /// </summary>
        [Test]
        public async Task RetiringGenerationReleasesOnlyRecordedEventBindingsAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
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
            (uint existingSubscriptionId, uint existingMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            Assert.That(
                original.Identity.Gate.TryEnter(out GenerationGate.Lease? admission),
                Is.True,
                "The test must hold a real admission so the retired generation stays retiring.");
            GenerationGate.Lease? heldAdmission = admission;
            Task<NodeManagerRegistration>? replacementTask = null;
            NodeManagerRegistration? reloaded = null;
            uint postSubscriptionId = 0;
            try
            {
                replacementTask = RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(
                            original,
                            CreateNodeManagementFactory(kGeneration2Value, includeEuRange: false))
                        .AsTask());
                await WaitForConditionAsync(
                        () => master.RetiringEventGenerations.Contains(original.Identity))
                    .ConfigureAwait(false);

                (postSubscriptionId, uint postMonitoredItemId) =
                    await CreateSubscriptionAndEventMonitoredItemAsync(
                        services,
                        ObjectIds.Server).ConfigureAwait(false);
                Assert.That(retiredManager!.AllEventsSubscribeCount, Is.EqualTo(1));

                await DeleteMonitoredItemAsync(
                    services,
                    existingSubscriptionId,
                    existingMonitoredItemId).ConfigureAwait(false);
                Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));
                await DeleteMonitoredItemAsync(
                    services,
                    postSubscriptionId,
                    postMonitoredItemId).ConfigureAwait(false);
                Assert.That(
                    retiredManager.AllEventsUnsubscribeCount,
                    Is.EqualTo(1),
                    "An item bound against the replacement must not be released on the retired generation.");

                heldAdmission!.Dispose();
                heldAdmission = null;
                reloaded = await replacementTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                heldAdmission?.Dispose();
                if (replacementTask is not null)
                {
                    reloaded ??= await replacementTask.ConfigureAwait(false);
                }
            }

            await AssertRetiredGenerationDisposedAsync(retiredManager, valueNodeId).ConfigureAwait(false);
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Cleanup must not replay a release that deletion already performed.");
            Assert.That(master.RetiringEventGenerations, Has.None.SameAs(original.Identity));

            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded!, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, existingSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, postSubscriptionId).ConfigureAwait(false);
        }

        /// <summary>
        /// An in-flight deletion is absent from replacement bindings while its original owner finishes
        /// exactly one unsubscribe. Retirement waits for that admitted callback, not replacement publication.
        /// </summary>
        [Test]
        public async Task InFlightAllEventsDeleteIsNotRetainedOrUnsubscribedTwiceAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
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
            retiredManager!.AllEventsCallback = async (
                monitoredItem,
                unsubscribe,
                cancellationToken) =>
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

            TrackingLifecycleNodeManager? replacementManager = null;
            Task<NodeManagerRegistration> reloadTask = m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateTrackingNodeManagementFactory(
                        kGeneration2Value,
                        manager => replacementManager = manager))
                .AsTask();
            NodeManagerRegistration reloaded;
            try
            {
                // The reload returns once the replacement is live; only the retired generation's drain waits
                // for the in-flight monitored-item mutation.
                reloaded = await reloadTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                AssertLifecycleValue(replacementManager);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(deleteTask.IsCompleted, Is.False);
                    Assert.That(replacementManager.SubscribedAllEventIds.ToList(), Is.Empty,
                        "The deleting item must never be rebound on the replacement generation.");
                    Assert.That(
                        retiredManager.DisposeCount,
                        Is.Zero,
                        "Retirement must wait for the in-flight monitored-item mutation.");
                }
            }
            finally
            {
                allowUnsubscribe.TrySetResult(true);
            }
            await deleteTask.ConfigureAwait(false);
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
        /// After a shadow reload the current view owns filter modification and ConditionRefresh for every
        /// all-events item, and the retired generation releases its recorded binding exactly once at cleanup.
        /// </summary>
        [Test]
        public async Task ShadowReloadRoutesModifyAndConditionRefreshToCurrentViewAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
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
            (uint existingSubscriptionId, uint existingMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            TrackingLifecycleNodeManager? replacementManager = null;
            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ShadowReloadAsync(
                    original,
                    CreateTrackingNodeManagementFactory(
                        kGeneration2Value,
                        manager => replacementManager = manager))
                .ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(retiredManager!, valueNodeId).ConfigureAwait(false);
            Assert.That(retiredManager!.AllEventsSubscribeCount, Is.EqualTo(1));
            Assert.That(
                retiredManager.AllEventsUnsubscribeCount,
                Is.EqualTo(1),
                "Cleanup must release the retired generation's recorded binding exactly once.");
            Assert.That(master.RetiringEventGenerations, Has.None.SameAs(original.Identity));

            (uint postSubscriptionId, uint postMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);
            int replacementSubscribes = replacementManager!.AllEventsSubscribeCount;
            await ModifyEventMonitoredItemAsync(
                services,
                existingSubscriptionId,
                existingMonitoredItemId).ConfigureAwait(false);
            await ModifyEventMonitoredItemAsync(
                services,
                postSubscriptionId,
                postMonitoredItemId).ConfigureAwait(false);
            Assert.That(replacementManager.AllEventsSubscribeCount, Is.EqualTo(replacementSubscribes + 2));

            IList<IEventMonitoredItem> eventItems = server.EventManager.GetMonitoredItems();
            IEventMonitoredItem existingItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == existingMonitoredItemId);
            IEventMonitoredItem postItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == postMonitoredItemId);
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken)!;
            using (var refreshContext = new OperationContext(session, DiagnosticsMasks.None))
            {
                await master
                    .ConditionRefreshAsync(
                        refreshContext,
                        [existingItem, postItem],
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            Assert.That(replacementManager.ConditionRefreshCount, Is.EqualTo(1));
            Assert.That(
                replacementManager.LastConditionRefreshMonitoredItemIds,
                Is.EquivalentTo(new[] { existingMonitoredItemId, postMonitoredItemId }));
            Assert.That(retiredManager.AllEventsSubscribeCount, Is.EqualTo(1));
            Assert.That(retiredManager.ConditionRefreshCount, Is.Zero);
            Assert.That(retiredManager.AllEventsUnsubscribeCount, Is.EqualTo(1));

            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, existingSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, postSubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ConditionRefreshSnapshotLeasesLaterManagersBeforeDispatchAsync()
        {
            const string BlockingNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshBlocking";
            const string ReloadedNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RefreshReloaded";
            TrackingLifecycleNodeManager? blockingManager = null;
            TrackingLifecycleNodeManager? reloadedManager = null;
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
            blockingManager!.ConditionRefreshCallback = async (_, ct) =>
            {
                dispatchEntered.TrySetResult(true);
                await releaseDispatch.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            bool callbackAfterDispose = false;
            reloadedManager!.ConditionRefreshCallback = (_, _) =>
            {
                callbackAfterDispose = reloadedManager.DisposeCount > 0;
                return default;
            };

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken)!;
            // The request context pins its routing view until the request is disposed, as the service layer does.
            var refreshContext = new OperationContext(session!, DiagnosticsMasks.None);
            Task refreshTask = master
                .ConditionRefreshAsync(
                    refreshContext,
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
            NodeManagerRegistration replacement;
            try
            {
                // The reload publishes its replacement without waiting; the lease captured for the later refresh
                // target keeps only the retired generation from being disposed.
                replacement = await reloadTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                Assert.That(
                    reloadedManager.DisposeCount,
                    Is.Zero,
                    "Retirement must wait for the lease captured for a later refresh target.");
            }
            finally
            {
                releaseDispatch.TrySetResult(true);
            }

            await refreshTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            refreshContext.Dispose();
            await WaitForConditionAsync(() => reloadedManager.DisposeCount == 1).ConfigureAwait(false);
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
            TrackingLifecycleNodeManager? retiredManager = null;
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
            retiredManager!.ConditionRefreshCallback = async (_, ct) =>
            {
                refreshEntered.TrySetResult(true);
                await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken)!;
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session!, DiagnosticsMasks.None),
                eventSubscriptionId);
            await refreshEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            NodeManagerRegistration? reloaded = null;
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
            TrackingLifecycleNodeManager? retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint dataSubscriptionId, _) =
                await CreateSubscriptionAndMonitoredItemAsync(
                    services,
                    valueNodeId,
                    clientHandle: 1).ConfigureAwait(false);
            (uint eventSubscriptionId, _) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    ObjectIds.Server).ConfigureAwait(false);

            var dispatchEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var startLifecycle = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycleCompleted =
                new TaskCompletionSource<NodeManagerRegistration>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IAsyncNodeManager> probeManager =
                CreateLifecycleNodeManager(ProbeNamespaceUri);
            var probeFactory = new CountingNodeManagerFactory(
                [ProbeNamespaceUri],
                (_, _, _) => new ValueTask<IAsyncNodeManager>(probeManager.Object));
            retiredManager!.ConditionRefreshCallback = async (_, ct) =>
            {
                dispatchEntered.TrySetResult(true);
                await startLifecycle.Task.WaitAsync(ct).ConfigureAwait(false);
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
                .GetSession(m_requestHeader.AuthenticationToken)!;
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                eventSubscriptionId);
            await dispatchEntered.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            // The reload publishes its replacement while the refresh still runs inside the old generation.
            NodeManagerRegistration reloaded = await RunWithoutExecutionContext(
                    () => m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(
                            original,
                            CreateNodeManagementFactory(
                                kGeneration2Value,
                                includeEuRange: false))
                        .AsTask())
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(retiredManager.DisposeCount, Is.Zero);

            // A lifecycle call from the in-flight refresh callback completes without deadlocking retirement.
            startLifecycle.TrySetResult(true);
            NodeManagerRegistration probeRegistration = await lifecycleCompleted.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            await AssertRetiredGenerationDisposedAsync(retiredManager, valueNodeId)
                .ConfigureAwait(false);

            await m_server.NodeManagerLifecycle.RemoveAsync(probeRegistration, null)
                .ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, dataSubscriptionId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, eventSubscriptionId).ConfigureAwait(false);
        }
        [Test]
        public async Task ConditionRefreshWorkerIdleShutdownCompletesAsync()
        {
            m_requestHeader = null!;
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
                TrackingLifecycleNodeManager? retiredManager = null;
                TrackingLifecycleNodeManager? replacementManager = null;
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
                var refreshEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                retiredManager!.ConditionRefreshCallback = async (_, ct) =>
                {
                    refreshEntered.TrySetResult(true);
                    await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken)!;
                server.SubscriptionManager.ConditionRefresh(
                    new OperationContext(session!, DiagnosticsMasks.None),
                    eventSubscriptionId);
                await refreshEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                await lifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateTrackingNodeManagementFactory(
                            kGeneration2Value,
                            manager => replacementManager = manager))
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
                    NodeManagerGeneration replacementGeneration =
                        NodeManagerGenerationRegistry.GetOrCreate(replacementManager!);
                    Assert.That(
                        replacementGeneration.Gate.State,
                        Is.EqualTo(NodeManagerGenerationState.Retiring));
                    Assert.That(
                        replacementGeneration.Gate.TryEnter(out GenerationGate.Lease? lateLease),
                        Is.False,
                        "Shutdown must close the replacement generation before draining it.");
                    Assert.That(lateLease, Is.Null);
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
                TrackingLifecycleNodeManager? retiredManager = null;
                TrackingLifecycleNodeManager? replacementManager = null;
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
                var refreshEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                retiredManager!.ConditionRefreshCallback = async (_, ct) =>
                {
                    refreshEntered.TrySetResult(true);
                    await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                ISession session = server.SessionManager
                    .GetSession(m_requestHeader.AuthenticationToken)!;
                server.SubscriptionManager.ConditionRefresh(
                    new OperationContext(session!, DiagnosticsMasks.None),
                    eventSubscriptionId);
                await refreshEntered.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                await lifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateTrackingNodeManagementFactory(
                            kGeneration2Value,
                            manager => replacementManager = manager))
                    .ConfigureAwait(false);
                // The data item migrated to the replacement. Shutdown drains generations independently, so the
                // unleased replacement is disposed before shutdown blocks on the retired generation; release the
                // item while its owner is still live.
                await DeleteMonitoredItemAsync(
                    services,
                    dataSubscriptionId,
                    dataMonitoredItemId).ConfigureAwait(false);

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
                await lifecycle
                    .CompleteShutdownAsync(server)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                Assert.That(lifecycle.Registrations, Is.Empty);
                Assert.That(retiredManager.DisposeCount, Is.EqualTo(1));
                Assert.That(replacementManager!.DisposeCount, Is.EqualTo(1));

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

        // Session activation is deduplicated by the generation's session binding record, so an already-bound
        // session reactivation never re-enters the manager and cannot hold the generation.
        [TestCase(RetainedNotificationDispatchKind.Modify)]
        [TestCase(RetainedNotificationDispatchKind.EventDelete)]
        public async Task RetiredGenerationDisposalWaitsForInFlightDispatchAsync(
            RetainedNotificationDispatchKind dispatchKind)
        {
            TrackingLifecycleNodeManager? retiredManager = null;
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

            var dispatchEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDispatch = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task dispatchTask;
            switch (dispatchKind)
            {
                case RetainedNotificationDispatchKind.Modify:
                    retiredManager!.AllEventsCallback = async (
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
                    retiredManager!.AllEventsCallback = async (
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
            NodeManagerRegistration reloaded;
            try
            {
                // The reload publishes while the dispatch still runs on the old generation; only that
                // generation's disposal waits for the dispatch's view pin.
                reloaded = await m_server.NodeManagerLifecycle
                    .ShadowReloadAsync(
                        original,
                        CreateNodeManagementFactory(
                            kGeneration2Value,
                            includeEuRange: false))
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                Assert.That(
                    retiredManager!.DisposeCount,
                    Is.Zero,
                    "Retirement must wait for the dispatch admitted on the old generation.");
            }
            finally
            {
                releaseDispatch.TrySetResult(true);
            }
            await dispatchTask
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

        /// <summary>
        /// Event items owned by a shadow-reloaded manager migrate to the replacement, which then receives
        /// ConditionRefresh for both the migrated and newer items; the retired manager receives none.
        /// </summary>
        [Test]
        public async Task ShadowReloadMigratesOwnedEventItemsForConditionRefreshAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    kGeneration1Value,
                    manager => retiredManager = manager), null)
                .ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            (uint subscriptionId, uint migratedMonitoredItemId) =
                await CreateSubscriptionAndEventMonitoredItemAsync(
                    services,
                    rootNodeId).ConfigureAwait(false);
            TrackingLifecycleNodeManager? replacementManager = null;
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

            IList<IEventMonitoredItem> eventItems = server.EventManager.GetMonitoredItems();
            IEventMonitoredItem migratedItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == migratedMonitoredItemId);
            IEventMonitoredItem replacementItem = eventItems.Single(
                monitoredItem => monitoredItem.Id == replacementMonitoredItemId);
            Assert.That(migratedItem.NodeManager, Is.SameAs(replacementManager));
            Assert.That(replacementItem.NodeManager, Is.SameAs(replacementManager));

            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken)!;
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session, DiagnosticsMasks.None),
                subscriptionId);
            await WaitForConditionRefreshCountAsync(replacementManager!, 1).ConfigureAwait(false);

            Assert.That(
                replacementManager!.LastConditionRefreshMonitoredItemIds,
                Is.EquivalentTo(new[] { migratedMonitoredItemId, replacementMonitoredItemId }));
            Assert.That(retiredManager!.ConditionRefreshCount, Is.Zero);

            await AssertRetiredGenerationDisposedAsync(retiredManager, rootNodeId).ConfigureAwait(false);
            await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            await m_server.NodeManagerLifecycle.RemoveAsync(reloaded, null).ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAfterAddWithInFlightRequestDisposesManagersOnceAsync()
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

            // Publication does not drain unrelated in-flight requests; only views pinning a retired generation do.
            await addTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            m_requestHeader = null!;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });


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
        public async Task DisposeAfterReloadWithInFlightRequestDisposesManagersOnceAsync()
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

            // Publication does not drain unrelated in-flight requests; only views pinning a retired generation do.
            await reloadTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            m_requestHeader = null!;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });


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
        public async Task DisposeAfterRemoveWithInFlightRequestDisposesManagersOnceAsync()
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

            // Publication does not drain unrelated in-flight requests; only views pinning a retired generation do.
            await removeTask
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            m_requestHeader = null!;
            Task disposeTask = RunWithoutExecutionContext(() =>
            {
                m_server.Dispose();
                return Task.CompletedTask;
            });

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
            TrackingLifecycleNodeManager? manager = null;
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
            manager!.ConditionRefreshCallback = async (_, ct) =>
            {
                refreshEntered.TrySetResult(true);
                await releaseRefresh.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            ISession session = server.SessionManager
                .GetSession(m_requestHeader.AuthenticationToken)!;
            server.SubscriptionManager.ConditionRefresh(
                new OperationContext(session!, DiagnosticsMasks.None),
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

            m_requestHeader = null!;
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
            TrackingLifecycleNodeManager? firstManager = null;
            TrackingLifecycleNodeManager? successfulManager = null;
            TrackingLifecycleNodeManager? lastManager = null;
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

            firstManager!.DeleteAddressSpaceFailuresRemaining = 1;
            lastManager!.DeleteAddressSpaceFailuresRemaining = 1;
            IServerInternal server = m_server.CurrentInstance;
            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(
                async () => await server.NodeManager.ShutdownAsync().ConfigureAwait(false));

            Assert.That(failure.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(firstManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(successfulManager!.DeleteAddressSpaceCount, Is.EqualTo(1));
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

            m_requestHeader = null!;
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
        /// Shutdown intent rejects new work without waiting for a removed generation's drain.
        /// CompleteShutdown waits for that generation, and disposal never republishes its route.
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
            GenerationGate.Lease? heldAdmission = null;
            try
            {
                NodeManagerRegistration registration = await lifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                Assert.That(registration.Identity.Gate.TryEnter(out heldAdmission), Is.True);
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
                await removeTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await shutdownTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                lifecycle.Dispose();
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, nodeManager.Object)),
                    Is.False,
                    "Shutdown-aware removal recovery must not republish the manager.");

                Task completion = lifecycle.CompleteShutdownAsync(server).AsTask();
                await Task.Delay(100).ConfigureAwait(false);
                Assert.That(completion.IsCompleted, Is.False);
                disposable.Verify(value => value.Dispose(), Times.Never);
                heldAdmission!.Dispose();
                heldAdmission = null;
                await completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
                nodeManager.Verify(
                    value => value.DeleteAddressSpaceAsync(CancellationToken.None),
                    Times.Once);
                disposable.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                heldAdmission?.Dispose();
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// Dispose while a reload's retired generation is still pinned marks shutdown intent but defers
        /// semaphore disposal. Shutdown still runs, and CompleteShutdown disposes the retired generation only
        /// after its admission is released.
        /// </summary>
        [Test]
        public async Task DisposeThenShutdownDuringRetiredDrainDefersSemaphoreDisposalAsync()
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
            GenerationGate.Lease? heldAdmission = null;
            try
            {
                NodeManagerRegistration original = await lifecycle
                    .AddAsync(originalFactory.Object, null)
                    .ConfigureAwait(false);
                IServerInternal server = m_server.CurrentInstance;
                var master = (MasterNodeManager)server.NodeManager;
                Assert.That(original.Identity.Gate.TryEnter(out heldAdmission), Is.True);
                NodeManagerRegistration replacement = await RunWithoutExecutionContext(
                    () => lifecycle
                        .ReloadAsync(original, replacementFactory.Object, null)
                        .AsTask())
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(replacement.NodeManager, Is.SameAs(replacementManager.Object));

                lifecycle.Dispose();
                await lifecycle.BeginShutdownAsync(server).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                originalDisposable.Verify(value => value.Dispose(), Times.Never);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, originalManager.Object)),
                    Is.False);
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, replacementManager.Object)),
                    Is.True);

                Task completeTask = lifecycle.CompleteShutdownAsync(server).AsTask();
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                Assert.That(completeTask.IsCompleted, Is.False);
                originalDisposable.Verify(value => value.Dispose(), Times.Never);
                heldAdmission!.Dispose();
                heldAdmission = null;
                await completeTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(lifecycle.Registrations, Is.Empty);
                originalDisposable.Verify(value => value.Dispose(), Times.Once);
                replacementDisposable.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                heldAdmission?.Dispose();
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// A background retired-generation drain waits only on its own generation. BeginShutdown rejects new
        /// lifecycle work without waiting for that drain, and CompleteShutdown waits until the held admission
        /// releases before the retired generation is disposed.
        /// </summary>
        [Test]
        public async Task CompleteShutdownWaitsForBackgroundRetiredDrainAsync()
        {
            TrackingLifecycleNodeManager? retiredManager = null;
            var replacementCreated =
                new TaskCompletionSource<TrackingLifecycleNodeManager>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            var lifecycle = new NodeManagerLifecycle(m_server);
            GenerationGate.Lease? heldAdmission = null;
            Task<NodeManagerRegistration>? reloadTask = null;
            Task? shutdownTask = null;
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
                Assert.That(
                    original.Identity.Gate.TryEnter(out heldAdmission),
                    Is.True,
                    "The shutdown test must hold a real generation admission.");
                IAsyncNodeManagerFactory replacementFactory = CreateTrackingNodeManagementFactory(
                    kGeneration2Value,
                    manager => replacementCreated.TrySetResult(manager));
                reloadTask = RunWithoutExecutionContext(
                    () => lifecycle.ShadowReloadAsync(
                        original,
                        replacementFactory).AsTask());
                TrackingLifecycleNodeManager replacementManager = await replacementCreated.Task
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await WaitForRegistrationAsync(
                    lifecycle,
                    original.Id,
                    replacementManager).ConfigureAwait(false);
                NodeManagerRegistration replacement = await reloadTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(replacement.NodeManager, Is.SameAs(replacementManager));
                await DeleteMonitoredItemAsync(
                    services,
                    subscriptionId,
                    monitoredItemId).ConfigureAwait(false);
                await DeleteSubscriptionAsync(services, subscriptionId)
                    .ConfigureAwait(false);

                shutdownTask = lifecycle.BeginShutdownAsync(server).AsTask();
                Assert.That(
                    async () => await lifecycle
                        .AddAsync(CreateNodeManagementFactory(
                            value: 3,
                            includeEuRange: false), null)
                        .ConfigureAwait(false),
                    Throws.InvalidOperationException.With.Message.Contains(
                        "shutting down"));
                await shutdownTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(retiredManager!.DisposeCount, Is.Zero);

                Task completeTask = lifecycle.CompleteShutdownAsync(server).AsTask();
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                Assert.That(
                    completeTask.IsCompleted,
                    Is.False,
                    "CompleteShutdown must wait for the admission held on the retired generation.");
                Assert.That(retiredManager.DisposeCount, Is.Zero);

                heldAdmission!.Dispose();
                heldAdmission = null;
                await completeTask
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                Assert.That(retiredManager.DisposeCount, Is.EqualTo(1));
                Assert.That(lifecycle.Registrations, Is.Empty);
            }
            finally
            {
                heldAdmission?.Dispose();
                if (reloadTask is not null && !reloadTask.IsCompleted)
                {
                    await reloadTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                if (shutdownTask is not null && !shutdownTask.IsCompleted)
                {
                    await shutdownTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                lifecycle.Dispose();
            }
        }

        /// <summary>
        /// After a ShadowReload, a migrated data monitored item transfers with its subscription
        /// and remains owned by the replacement generation.
        /// </summary>
        [Test]
        public async Task ShadowReloadedDataMonitoredItemTransfersWithSubscriptionToReplacementGenerationAsync()
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
            Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
            Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.True);

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

                Assert.That(tracker.HasMonitoredItems(original.NodeManager), Is.False);
                Assert.That(tracker.HasMonitoredItems(reloaded.NodeManager), Is.True);

                (DataValue? migratedInitialValue, ArrayOf<SubscriptionAcknowledgement> migratedAcks) =
                    await PublishForDataChangeOnSessionAsync(
                        servicesB,
                        headerB,
                        subscriptionId,
                        default,
                        clientHandle).ConfigureAwait(false);
                Assert.That(migratedInitialValue, Is.Not.Null);
                Assert.That(migratedInitialValue!.Value.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                var replacementManager = (AsyncCustomNodeManager)reloaded.NodeManager;
                var replacementState = (BaseVariableState)replacementManager.Find(valueNodeId)!;
                replacementState.Value = 6464;
                replacementState.Timestamp = DateTimeUtc.Now;
                replacementState.StatusCode = StatusCodes.Good;
                replacementState.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await replacementState
                    .ClearChangeMasksAsync(server.DefaultSystemContext, includeChildren: false)
                    .ConfigureAwait(false);
                (DataValue? pushed, _) = await PublishForDataChangeOnSessionAsync(
                    servicesB,
                    headerB,
                    subscriptionId,
                    migratedAcks,
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

            // Migration releases the old generation independently of the transferred subscription.
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
        /// A failed shadow replacement reconciliation preserves the committed generation without rollback.
        /// </summary>
        [Test]
        public async Task ShadowReloadAsyncReferenceFailureRetainsReplacementGenerationWithoutRollbackAsync()
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
                    if (failRollbackDelete && deleteReferenceCalls >= 1)
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
                await Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .ShadowReloadAsync(
                            originalRegistration,
                            replacementFactory.Object)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("replacement NodeManager is live"));
            Assert.That(exception.InnerException, Is.TypeOf<SentinelException>());
            Assert.That(exception.InnerException!.Message, Is.EqualTo(AddReferencesFailure));
            Assert.That(deleteReferenceCalls, Is.Zero,
                "A committed replacement must not roll back its serving references.");
            NodeManagerBatchResult committed = RequireLifecycleValue(exception.Result);
            await committed.Retirement.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(committed.Issues.ToList().Any(
                issue => issue.Stage == NodeManagerReconciliationStage.References && issue.IsRetryable), Is.True);

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
            originalDisposable.Verify(manager => manager.Dispose(), Times.Once);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failReplacementAddReferences = false;
            await committed.RetryReconciliationAsync().ConfigureAwait(false);
            Assert.That(committed.Issues.ToList(), Is.Empty);
            failRollbackDelete = false;
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, retainedRegistration).ConfigureAwait(false);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, ownerRegistration).ConfigureAwait(false);

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
        /// A failure from <c>DeleteAddressSpaceAsync</c> is reported as a committed removal,
        /// leaves the NodeManager unpublished, and is retried by a later lifecycle operation.
        /// </summary>
        /// <exception cref="SentinelException">
        /// Thrown by the injected address-space deletion failure.
        /// </exception>
        [Test]
        public async Task RemoveAsyncWhenDeleteAddressSpaceFailsReportsCommittedRemovalAndRetriesAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DeleteFailure";
            const string ExpectedMessage = "DeleteAddressSpaceAsync failed.";
            var disposalCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
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
            nodeManagerAsDisposable.Setup(manager => manager.Dispose())
                .Callback(() => disposalCompleted.TrySetResult(true));
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

            Task<NodeManagerBatchResult> firstRemoval = CommitChangeAsync(
                m_server.NodeManagerLifecycle, NodeManagerBatchChange.Remove(registration));
            NodeManagerBatchResult removalResult;
            try
            {
                Task deleteStartResult = await Task.WhenAny(
                    deleteStarted.Task,
                    Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                Assert.That(deleteStartResult, Is.SameAs(deleteStarted.Task));
                await deleteStarted.Task.ConfigureAwait(false);

                removalResult = await firstRemoval.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(removalResult.Retirement.IsCompleted, Is.False);
                Assert.That(
                    master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                    Is.Zero);
                Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);

                ArrayOf<NodeManagerRegistration> registrations =
                    GetNonStartupRegistrations();
                Assert.That(
                    CountMatches(registrations, item => item.Id == registration.Id),
                    Is.Zero);
            }
            finally
            {
                releaseDelete.TrySetResult(true);
            }

            await Assert.ThatAsync(
                () => removalResult.Retirement.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage)).ConfigureAwait(false);
            Assert.That(removalResult.Retirement.IsFaulted, Is.True);

            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.Zero);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);

            Assert.That(
                CountMatches(GetNonStartupRegistrations(), item => item.Id == registration.Id),
                Is.Zero);
            nodeManager.Verify(
                m => m.DeleteAddressSpaceAsync(CancellationToken.None),
                Times.Once);
            nodeManagerAsDisposable.Verify(d => d.Dispose(), Times.Never);

            const string RecoveryNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:DeleteFailureRecovery";
            var recoveryManager = new Mock<IAsyncNodeManager>();
            recoveryManager
                .Setup(m => m.NamespaceUris)
                .Returns([RecoveryNamespaceUri]);
            recoveryManager
                .Setup(m => m.SyncNodeManager)
                .Returns(new Mock<INodeManager>().Object);
            recoveryManager
                .Setup(m => m.CreateAddressSpaceAsync(
                    It.IsAny<IDictionary<NodeId, IList<IReference>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            recoveryManager
                .Setup(m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            var recoveryFactory = new Mock<IAsyncNodeManagerFactory>();
            recoveryFactory
                .Setup(f => f.CreateAsync(
                    It.IsAny<IServerInternal>(),
                    It.IsAny<ApplicationConfiguration>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(recoveryManager.Object);

            NodeManagerRegistration recoveryRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(recoveryFactory.Object, null)
                .ConfigureAwait(false);
            await disposalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            nodeManager.Verify(
                m => m.DeleteAddressSpaceAsync(CancellationToken.None),
                Times.Exactly(2));
            nodeManagerAsDisposable.Verify(d => d.Dispose(), Times.Once);
            Assert.That(
                CountMatches(m_server.NodeManagerLifecycle.Registrations, item => item.Id == registration.Id),
                Is.Zero);
            Assert.That(
                master.AsyncNodeManagers.Count(m => ReferenceEquals(m, nodeManager.Object)),
                Is.Zero);
            Assert.That(master.NamespaceManagers.ContainsKey(namespaceIndex), Is.False);

            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, recoveryRegistration).ConfigureAwait(false);
        }

        [Test]
        public async Task RemoveAsyncReportsCommittedReferenceCleanupFailureAndRetriesAsync()
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
            ownerManager.Setup(manager => manager.GetNodeMetadataAsync(
                It.IsAny<OperationContext>(), ownerHandle, It.IsAny<BrowseResultMask>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeMetadata(ownerHandle, ownerSourceId));
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

            TrackingLifecycleNodeManager? removedManager = null;
            NodeManagerRegistration removedRegistration =
                await m_server.NodeManagerLifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        808,
                        created => removedManager = created,
                        RemovedNamespaceUri,
                        ownerSourceId), null)
                    .ConfigureAwait(false);

            AssertLifecycleValue(removedManager);
            var lateOwnerSourceId = new NodeId(2002, ownerNamespaceIndex);
            ownerManager.Setup(manager => manager.GetManagerHandleAsync(
                lateOwnerSourceId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            NodeState root = RequireLifecycleValue(removedManager.Find(
                new NodeId(kRootNodeId, removedManager.NamespaceIndexes[0])));
            root.AddReference(ReferenceTypeIds.HasComponent, true, lateOwnerSourceId);

            NodeManagerBatchResult removal = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, NodeManagerBatchChange.Remove(removedRegistration))
                .ConfigureAwait(false);
            await Assert.ThatAsync(
                () => removal.Retirement.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage)).ConfigureAwait(false);
            Assert.That(removedManager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(removedManager.DisposeCount, Is.Zero);
            Assert.That(deleteReferenceCalls, Is.EqualTo(2));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(candidate =>
                    candidate.Id == removedRegistration.Id),
                Is.Null);

            const string RecoveryNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveReferenceRecovery";
            NodeManagerRegistration recoveryRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    809,
                    static _ => { },
                    RecoveryNamespaceUri), null)
                .ConfigureAwait(false);

            await removedManager.DisposalCompleted.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(
                removedManager.DeleteAddressSpaceCount,
                Is.EqualTo(1),
                "Retrying post-destroy reference cleanup must not delete the address space again.");
            Assert.That(removedManager.DisposeCount, Is.EqualTo(1));
            Assert.That(deleteReferenceCalls, Is.EqualTo(3));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.Find(candidate =>
                    candidate.Id == removedRegistration.Id),
                Is.Null);

            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, recoveryRegistration).ConfigureAwait(false);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, ownerRegistration).ConfigureAwait(false);
            ownerDisposable.Verify(value => value.Dispose(), Times.Once);
        }

        [Test]
        [Category("NodeManagerLifecycleEvents")]
        public async Task RemoveAsyncCheckpointsDestroyWhenDisposeRetriesAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveDisposeRetry";
            TrackingLifecycleNodeManager? manager = null;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    807,
                    created => manager = created,
                    NamespaceUri), null)
                .ConfigureAwait(false);
            manager!.DisposeFailuresRemaining = 1;

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

                NodeManagerBatchResult removal = await CommitChangeAsync(
                    m_server.NodeManagerLifecycle, NodeManagerBatchChange.Remove(registration))
                    .ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => removal.Retirement.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.TypeOf<SentinelException>().With.Message.EqualTo("Dispose failed.")).ConfigureAwait(false);

                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                int unsubscribeCountAfterFailure =
                    manager.AllEventsUnsubscribeCount;
                Assert.That(unsubscribeCountAfterFailure, Is.EqualTo(1));
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, manager)),
                    Is.False,
                    "A failed disposal must not republish the removed NodeManager.");

                EventFieldList? removeEvent;
                (removeEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(removeEvent!, "A live NodeManager was removed.");

                const string RecoveryNamespaceUri =
                    "urn:opcfoundation.org:Tests:NodeManagerLifecycle:RemoveDisposeRecovery";
                NodeManagerRegistration recoveryRegistration = await m_server.NodeManagerLifecycle
                    .AddAsync(CreateTrackingNodeManagementFactory(
                        810,
                        static _ => { },
                        RecoveryNamespaceUri), null)
                    .ConfigureAwait(false);

                await manager.DisposalCompleted.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                EventFieldList? addEvent;
                (addEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                        services,
                        subscriptionId,
                        acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(addEvent!, "A live NodeManager was added.");

                Assert.That(
                        manager.DeleteAddressSpaceCount,
                        Is.EqualTo(1),
                    "A completed destroy stage must not run again on Remove retry.");
                Assert.That(manager.DisposeCount, Is.EqualTo(2));
                Assert.That(
                    manager.AllEventsUnsubscribeCount,
                    Is.EqualTo(unsubscribeCountAfterFailure),
                    "A removal retry must not repeat all-events unsubscription.");
                Assert.That(GetNonStartupRegistrations(), Has.Count.EqualTo(1));
                Assert.That(
                    GetNonStartupRegistrations().Find(candidate =>
                        candidate.Id == recoveryRegistration.Id),
                    Is.SameAs(recoveryRegistration));
                Assert.That(
                    master.AsyncNodeManagers.Any(candidate =>
                        ReferenceEquals(candidate, manager)),
                    Is.False);
                await m_server.NodeManagerLifecycle
                    .RemoveAsync(recoveryRegistration, null)
                    .ConfigureAwait(false);
                Assert.That(GetNonStartupRegistrations(), Is.Empty);

                EventFieldList? recoveryRemoveEvent;
                (recoveryRemoveEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(recoveryRemoveEvent!, "A live NodeManager was removed.");
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
        public async Task AddAsyncWhenPreparationAndCleanupFailReportsEveryFailureAsync()
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

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(
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
        }

        [Test]
        public async Task RemoveAsyncWhenSessionUnbindingFailsReportsCommittedRemovalAndRetriesAsync()
        {
            const string NamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:UnbindingFailure";
            const string ExpectedMessage = "SessionClosingAsync failed.";
            bool failSessionClosing = false;
            var disposalCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

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
            disposable.Setup(manager => manager.Dispose())
                .Callback(() => disposalCompleted.TrySetResult(true));

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
            failSessionClosing = true;

            NodeManagerBatchResult removal = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, NodeManagerBatchChange.Remove(registration)).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => removal.Retirement.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage)).ConfigureAwait(false);

            Assert.That(
                GetNonStartupRegistrations(),
                Has.Count.EqualTo(1));
            Assert.That(
                GetNonStartupRegistrations().Find(candidate =>
                    candidate.Id == registration.Id),
                Is.Null);
            Assert.That(
                IndexOfReference(master.AsyncNodeManagers, nodeManager.Object),
                Is.EqualTo(-1));
            Assert.That(
                IndexOfReference(
                    master.NamespaceManagers[namespaceIndex],
                    nodeManager.Object),
                Is.EqualTo(-1));
            Assert.That(
                master.AsyncNodeManagers.Count(manager =>
                    ReferenceEquals(manager, nodeManager.Object)),
                Is.Zero);
            Assert.That(master.NamespaceManagers[namespaceIndex]
                .Any(manager => ReferenceEquals(manager, peerManager.Object)), Is.True);
            nodeManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    It.IsAny<CancellationToken>()),
                Times.Never);
            disposable.Verify(manager => manager.Dispose(), Times.Never);

            failSessionClosing = false;
            const string RecoveryNamespaceUri =
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:UnbindingRecovery";
            NodeManagerRegistration recoveryRegistration = await m_server.NodeManagerLifecycle
                .AddAsync(CreateTrackingNodeManagementFactory(
                    811,
                    static _ => { },
                    RecoveryNamespaceUri), null)
                .ConfigureAwait(false);

            await disposalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
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

            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, recoveryRegistration).ConfigureAwait(false);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, peerRegistration).ConfigureAwait(false);
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
            var disposalCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

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
            originalDisposable.Setup(manager => manager.Dispose())
                .Callback(() => disposalCompleted.TrySetResult(true));

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
            NodeManagerBatchResult result = await CommitChangeAsync(
                m_server.NodeManagerLifecycle, NodeManagerBatchChange.Replace(original, replacementFactory.Object))
                .ConfigureAwait(false);
            await Assert.ThatAsync(
                () => result.Retirement.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.TypeOf<SentinelException>().With.Message.EqualTo(ExpectedMessage)).ConfigureAwait(false);
            Assert.That(result.Registrations[0].Id, Is.EqualTo(original.Id));
            Assert.That(result.Registrations[0].Generation, Is.EqualTo(original.Generation + 1));

            ArrayOf<NodeManagerRegistration> registrations =
                GetNonStartupRegistrations();
            Assert.That(registrations, Has.Count.EqualTo(1));
            NodeManagerRegistration replacement = registrations[0];
            Assert.That(replacement, Is.SameAs(result.Registrations[0]));
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
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, replacement).ConfigureAwait(false);
            await disposalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

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
        public async Task AddAsyncReferenceFailureRetainsLiveRegistrationWithoutRollbackAsync()
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

            NodeManagerAddCommittedException exception =
                await Assert.ThrowsAsync<NodeManagerAddCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddAsync(retainedFactory.Object, null)
                        .ConfigureAwait(false)).ConfigureAwait(false) ??
                    throw new InvalidOperationException("AddAsync did not report its committed failure.");

            Assert.That(
                exception.Message,
                Does.Contain("published generation was retained"));
            Assert.That(exception.InnerException, Is.TypeOf<SentinelException>()
                .With.Message.EqualTo(AddReferencesFailure));
            NodeManagerBatchResult committed = RequireLifecycleValue(exception.Result);
            Assert.That(committed.Issues.ToList().Any(issue =>
                issue.Stage == NodeManagerReconciliationStage.References && issue.IsRetryable), Is.True);
            ownerManager.Verify(manager => manager.DeleteReferenceAsync(
                ownerHandle, ReferenceTypeIds.HasComponent, false, ObjectIds.Server, false,
                It.IsAny<CancellationToken>()), Times.Never,
                "A committed reference failure must not attempt rollback.");

            NodeManagerRegistration retainedRegistration = exception.Registration;
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
            await committed.RetryReconciliationAsync().ConfigureAwait(false);
            Assert.That(committed.Issues, Is.Empty);
            failDeleteReference = false;
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, retainedRegistration).ConfigureAwait(false);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, ownerRegistration).ConfigureAwait(false);

            retainedManager.Verify(
                manager => manager.DeleteAddressSpaceAsync(
                    CancellationToken.None),
                Times.Once);
            retainedDisposable.Verify(manager => manager.Dispose(), Times.Once);
            ownerDisposable.Verify(manager => manager.Dispose(), Times.Once);
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
        }

        [Test]
        public async Task ReloadAsyncReferenceFailureRetainsReplacementGenerationWithoutRollbackAsync()
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
            bool failRollbackDelete = true;
            ownerManager
                .Setup(manager => manager.GetManagerHandleAsync(
                    ownerSourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(ownerHandle));
            ownerManager
                .Setup(manager => manager.DeleteReferenceAsync(
                    It.IsAny<object>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<ExpandedNodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            // A committed replacement's new edge is reconciled, never rolled back.
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
                    if (failRollbackDelete)
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
                            ObjectIds.ObjectsFolder)
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
                await Assert.ThrowsAsync<NodeManagerReloadCommittedException>(
                    async () => await m_server.NodeManagerLifecycle
                        .ReloadAsync(
                            originalRegistration,
                            replacementFactory.Object, null)
                        .ConfigureAwait(false));

            Assert.That(
                exception.Message,
                Does.Contain("replacement generation was retained"));
            Assert.That(exception.InnerException, Is.TypeOf<SentinelException>()
                .With.Message.EqualTo(AddReferencesFailure));
            NodeManagerBatchResult committed = RequireLifecycleValue(exception.Result);
            Assert.That(committed.Issues.ToList().Any(issue =>
                issue.Stage == NodeManagerReconciliationStage.References && issue.IsRetryable), Is.True);
            ownerManager.Verify(manager => manager.DeleteReferenceAsync(
                ownerHandle, ReferenceTypeIds.HasComponent, false, ObjectIds.Server, false,
                It.IsAny<CancellationToken>()), Times.Never,
                "A committed reference failure must not attempt rollback.");

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
            // The reload committed at the view swap, so the original generation retires independently of the
            // failed post-commit reference reconciliation instead of being kept for a rollback.
            await committed.Retirement.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            originalDisposable.Verify(manager => manager.Dispose(), Times.Once);
            replacementDisposable.Verify(manager => manager.Dispose(), Times.Never);

            failReplacementAddReferences = false;
            await committed.RetryReconciliationAsync().ConfigureAwait(false);
            Assert.That(committed.Issues, Is.Empty);
            failRollbackDelete = false;
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, retainedRegistration).ConfigureAwait(false);
            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, ownerRegistration).ConfigureAwait(false);

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
            TrackingLifecycleNodeManager? originalManager = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => originalManager = manager), null)
                .ConfigureAwait(false);
            IServerInternal server = m_server.CurrentInstance;
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var valueNodeId = new NodeId(kValueNodeId, (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
            (uint subscriptionId, uint monitoredItemId) =
                await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId).ConfigureAwait(false);
            var subscription = (ISubscriptionMonitoredItemLifecycle)server.SubscriptionManager
                .GetSubscriptions().Single(candidate => candidate.Id == subscriptionId);
            IMonitoredItem originalItem = GetOwnedMonitoredItems(subscription, original.NodeManager).Single();
            var publicationFailure = new SentinelException("Committed replacement publication callback failed.");
            var readinessFailure = new SentinelException("Committed replacement readiness failed.");
            ReadinessLifecycleNodeManager? replacementManager = null;
            var factory = new Mock<IAsyncNodeManagerFactory>();
            factory.Setup(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((
                    IServerInternal candidateServer,
                    ApplicationConfiguration configuration,
                    CancellationToken _) =>
                {
                    replacementManager = new ReadinessLifecycleNodeManager(
                        candidateServer, configuration, m_logger, kGeneration2Value)
                    {
                        ReadinessCallback = _ => new ValueTask(Task.FromException(readinessFailure))
                    };
                    return new ValueTask<IAsyncNodeManager>(replacementManager);
                });
            try
            {
                var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
                await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Replace(original, factory.Object)], null).ConfigureAwait(false);
                int publicationCalls = 0;
                NodeManagerBatchResult result = await prepared.CommitAsync(
                    _ => default,
                    () =>
                    {
                        publicationCalls++;
                        Assert.That(prepared.IsCommitted, Is.True);
                        throw publicationFailure;
                    }).ConfigureAwait(false);
                NodeManagerRegistration replacement = result.Registrations[0];
                AssertLifecycleValue(originalManager);
                AssertLifecycleValue(replacementManager);
                IMonitoredItem migrated = GetOwnedMonitoredItems(subscription, replacement.NodeManager).Single();
                var failures = RequireLifecycleValue(result.CleanupFailure as AggregateException)
                    .Flatten().InnerExceptions;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(prepared.IsCommitted, Is.True);
                    Assert.That(publicationCalls, Is.EqualTo(1));
                    Assert.That(GetBranchRegistration(lifecycle, original.Id), Is.SameAs(replacement));
                    Assert.That(replacement.Generation, Is.EqualTo(original.Generation + 1));
                    Assert.That(migrated, Is.SameAs(originalItem));
                    Assert.That(migrated.Id, Is.EqualTo(monitoredItemId));
                    Assert.That(migrated.NodeManager, Is.SameAs(replacementManager));
                    Assert.That(((IMonitoredItemGenerationBinding)migrated).IsSourceUnavailable, Is.False);
                    Assert.That(replacementManager.ReadinessCount, Is.EqualTo(1));
                    Assert.That(replacementManager.ReadinessCompletedCount, Is.Zero);
                    Assert.That(failures, Is.EquivalentTo(new[] { publicationFailure, readinessFailure }));
                }
                await result.Retirement.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(originalManager.DeleteAddressSpaceCount, Is.EqualTo(1));
                    Assert.That(originalManager.DisposeCount, Is.EqualTo(1));
                    Assert.That(replacementManager.DeleteAddressSpaceCount, Is.Zero);
                    Assert.That(replacementManager.DisposeCount, Is.Zero);
                }
                DataValue currentValue = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(currentValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(currentValue.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));
            }
            finally
            {
                await DeleteSubscriptionAsync(services, subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task LifecycleArgumentGuardsRejectNullInputsAsync()
        {
            var lifecycle = (NodeManagerLifecycle)m_server.NodeManagerLifecycle;
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
        }

        /// <summary>
        /// Rejecting a prepared batch must not publish its external references or replay
        /// them into a later candidate using the same namespace and NodeIds.
        /// </summary>
        [Test]
        public async Task RejectedPreparedBatchDoesNotRetainExternalReferencesAsync()
        {
            TrackingLifecycleNodeManager? owner = null;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle.AddAsync(
                CreateTrackingNodeManagementFactory(kGeneration1Value, manager => owner = manager), null)
                .ConfigureAwait(false);
            AssertLifecycleValue(owner);
            IServerInternal server = m_server.CurrentInstance;
            var ownerRootId = new NodeId(kRootNodeId,
                (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri));
            ushort candidateNamespace = server.NamespaceUris.GetIndexOrAppend(kSecondModelNamespaceUri);
            var candidateRootId = new NodeId(kRootNodeId, candidateNamespace);
            NodeState ownerRoot = RequireLifecycleValue(owner.Find(ownerRootId));
            TrackingLifecycleNodeManager? rejected = null;
            var lifecycle = (INodeManagerBatchLifecycle)m_server.NodeManagerLifecycle;
            var decisionFailure = new SentinelException("Prepared reference publication rejected.");
            int referenceCallbacks = 0;
            NodeStateReferenceAdded? previous = ownerRoot.OnReferenceAdded;
            ownerRoot.OnReferenceAdded = (_, _, _, _) => referenceCallbacks++;
            try
            {
                await using (IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration2Value, manager => rejected = manager, kSecondModelNamespaceUri, ownerRootId))],
                    null).ConfigureAwait(false))
                {
                    int decisions = 0;
                    SentinelException? exception = await Assert.ThrowsAsync<SentinelException>(async () =>
                        await prepared.CommitAsync(_ =>
                        {
                            decisions++;
                            Assert.That(ownerRoot.ReferenceExists(
                                ReferenceTypeIds.HasComponent, false, candidateRootId), Is.False);
                            throw decisionFailure;
                        }).ConfigureAwait(false)).ConfigureAwait(false);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(exception, Is.SameAs(decisionFailure));
                        Assert.That(decisions, Is.EqualTo(1));
                        Assert.That(prepared.IsCommitted, Is.False);
                        Assert.That(referenceCallbacks, Is.Zero);
                        Assert.That(GetBranchRegistration(lifecycle, original.Id), Is.SameAs(original));
                        Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(1));
                    }
                }
                AssertLifecycleValue(rejected);
                Assert.That(rejected.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(rejected.DisposeCount, Is.EqualTo(1));

                TrackingLifecycleNodeManager? replacement = null;
                await using IPreparedNodeManagerBatch next = await lifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(CreateTrackingNodeManagementFactory(
                        kGeneration2Value, manager => replacement = manager, kSecondModelNamespaceUri))],
                    null).ConfigureAwait(false);
                NodeManagerBatchResult result = await next.CommitAsync(_ => default).ConfigureAwait(false);
                AssertLifecycleValue(replacement);
                NodeState replacementRoot = RequireLifecycleValue(replacement.Find(candidateRootId));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(next.IsCommitted, Is.True);
                    Assert.That(result.CleanupFailure, Is.Null);
                    Assert.That(result.Registrations[0].NodeManager, Is.SameAs(replacement));
                    Assert.That(GetBranchRegistrations(lifecycle), Has.Count.EqualTo(2));
                    Assert.That(referenceCallbacks, Is.Zero);
                    Assert.That(ownerRoot.ReferenceExists(
                        ReferenceTypeIds.HasComponent, false, candidateRootId), Is.False);
                    Assert.That(replacementRoot.ReferenceExists(
                        ReferenceTypeIds.HasComponent, true, ownerRootId), Is.False);
                }
            }
            finally
            {
                ownerRoot.OnReferenceAdded = previous;
            }
        }

        [Test]
        public async Task DynamicHostGuardsRejectInvalidArgumentsAndStateAsync()
        {
            var host = (IDynamicNodeManagerHost)m_server.CurrentInstance.NodeManager;
            Mock<IAsyncNodeManager> candidate = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:HostGuards");
            var prepared = new PreparedNodeManager(
                candidate.Object,
                []);

            ArgumentNullException exception =
                await Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await host
                        .PrepareAsync(null!)
                        .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .AbortAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("prepared"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .DestroyAddressSpaceAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await host
                    .RemoveDestroyedExternalReferencesAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            exception = Assert.Throws<ArgumentNullException>(
                () => host.Release(null!));
            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));

            var batchHost = (IDynamicNodeManagerBatchHost)m_server.CurrentInstance.NodeManager;
            var preparation = new ChangePreparer(m_server.CurrentInstance);
            int decisions = 0;
            var context = new NodeManagerBatchContext(
                Candidates: [prepared],
                Removed: [],
                TypeTree: preparation.TypeTree,
                OriginalTypes: preparation.OriginalTypes,
                TypeRevision: preparation.TypeRevision,
                Factory: preparation.Factory,
                OriginalFactory: preparation.OriginalFactory,
                FactoryRevision: preparation.FactoryRevision,
                ReadImages: [],
                DecideAsync: _ =>
                {
                    decisions++;
                    return default;
                },
                ViewPublished: () => Assert.Fail("An unprepared candidate must not be published."),
                Published: () =>
                {
                    Assert.Fail("An unprepared candidate must not complete publication.");
                    return default;
                },
                ReconcileBindingsAsync: () => default,
                DeferReferenceRemoval: (_, _) => Assert.Fail("An unprepared candidate must not retire references."),
                ReportCleanupFailure: _ => Assert.Fail("An unprepared candidate must fail before commit."),
                Preparation: preparation,
                CancellationToken: CancellationToken.None,
                Reconciliation: null!);
            await Assert.ThatAsync(async () => await batchHost.CommitBatchAsync(context).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("no prepared generation")).ConfigureAwait(false);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decisions, Is.Zero);
                Assert.That(prepared.Published, Is.False);
                Assert.That(prepared.Generation, Is.Null);
            }

            Mock<IAsyncNodeManager> unowned = CreateLifecycleNodeManager(
                "urn:opcfoundation.org:Tests:NodeManagerLifecycle:Unowned");
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

            await host.AbortAsync(prepared).ConfigureAwait(false);
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
                    .AbortAsync(duplicate)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains(
                    "cannot be aborted"));
            owned.Verify(manager => manager.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Never);
            duplicate.Published = true;
            await Assert.ThatAsync(() => host.AbortAsync(duplicate).AsTask(),
                Throws.InvalidOperationException.With.Message.Contains("already been published")).ConfigureAwait(false);

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
        public async Task AddAsyncReportsCommittedSessionActivationFailureAsync()
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

            NodeManagerAddCommittedException exception =
                await Assert.ThrowsAsync<NodeManagerAddCommittedException>(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory.Object, null)
                    .ConfigureAwait(false)).ConfigureAwait(false) ??
                    throw new InvalidOperationException("AddAsync did not report its committed failure.");
            Assert.That(exception.InnerException, Is.TypeOf<SentinelException>());
            Assert.That(exception.InnerException!.Message, Is.EqualTo(ExpectedMessage));
            Assert.That(
                m_server.NodeManagerLifecycle.Registrations.ToList(),
                Does.Contain(exception.Registration));
            Assert.That(exception.Registration.NodeManager, Is.SameAs(nodeManager.Object));

            await RemoveAndRetireAsync(m_server.NodeManagerLifecycle, exception.Registration).ConfigureAwait(false);

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
            Assert.That(GetNonStartupRegistrations(), Is.Empty);
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

                EventFieldList? addEvent;
                (addEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(addEvent!, "A live NodeManager was added.");

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBeforeAdd + 1));
                uint urisVersionAfterAdd = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfterAdd, Is.Not.EqualTo(urisVersionBeforeAdd));

                // Reload (same URI): must not change the namespace table.
                int namespaceCountBeforeReload = server.NamespaceUris.Count;
                uint urisVersionBeforeReload = urisVersionAfterAdd;

                registration = await m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(registration, CreateGenerationOptions(generation: 2), null)
                    .ConfigureAwait(false);

                EventFieldList? reloadEvent;
                (reloadEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(reloadEvent!, "A live NodeManager was reloaded.");

                Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBeforeReload));
                uint urisVersionAfterReload = await ReadUrisVersionAsync().ConfigureAwait(false);
                Assert.That(urisVersionAfterReload, Is.EqualTo(urisVersionBeforeReload));

                // Remove: must not change the namespace table either.
                int namespaceCountBeforeRemove = server.NamespaceUris.Count;
                uint urisVersionBeforeRemove = urisVersionAfterReload;

                await m_server.NodeManagerLifecycle.RemoveAsync(registration, null).ConfigureAwait(false);

                EventFieldList? removeEvent;
                (removeEvent, acknowledgements) = await PublishForModelChangeEventAsync(
                    services,
                    subscriptionId,
                    acknowledgements).ConfigureAwait(false);
                AssertModelChangeEvent(removeEvent!, "A live NodeManager was removed.");

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
                        out DataChangeNotification? dataChangeNotification) &&
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
            m_requestHeader = null!;
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
                m_fixture = null!;
            }
            m_server = null!;
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
            TaskCompletionSource<bool>? requestEntered = null)
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

        private static Task AssertLifecycleOperationStopsForShutdownAsync(
            Task<NodeManagerRegistration> operation)
        {
            return Assert.ThatAsync(
                () => operation.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.TypeOf<InvalidOperationException>()
                    .And
                    .Matches<InvalidOperationException>(
                        exception => exception is not null && exception.ToString().Contains("shutting down", StringComparison.Ordinal)));
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

        /// <summary>
        /// Captures exact generation ownership and independently verifies subscription membership.
        /// </summary>
        private static IMonitoredItem[] GetOwnedMonitoredItems(
            ISubscriptionMonitoredItemLifecycle subscription,
            IAsyncNodeManager nodeManager)
        {
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            return [.. generation.MonitoredItems.Keys.Where(subscription.ContainsMonitoredItem)];
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
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            return (Dictionary<uint, LinkedListNode<IMonitoredItem>>)field.GetValue(subscription)!;
        }

        private static Lock GetSubscriptionLock(Subscription subscription)
        {
            FieldInfo field = typeof(Subscription).GetField(
                "m_lock",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            return (Lock)field.GetValue(subscription)!;
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
        /// Explicitly joins teardown when a test asserts physical cleanup rather than publication alone.
        /// </summary>
        private static async Task RemoveAndRetireAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            CancellationToken cancellationToken = default)
        {
            NodeManagerBatchResult result = await CommitChangeAsync(
                lifecycle, NodeManagerBatchChange.Remove(registration), cancellationToken).ConfigureAwait(false);
            await result.Retirement.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes one change without joining its independently observable physical retirement.
        /// </summary>
        private static async Task<NodeManagerBatchResult> CommitChangeAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerBatchChange change,
            CancellationToken cancellationToken = default)
        {
            await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
                [change], cancellationToken).ConfigureAwait(false);
            NodeManagerBatchResult result = await prepared.CommitAsync(_ => default, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            return result;
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
                        if (notificationData.TryGetValue(out DataChangeNotification? dcn))
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
                        if (notificationData.TryGetValue(out DataChangeNotification? dcn))
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
        private async Task<(EventFieldList? EventFields, ArrayOf<SubscriptionAcknowledgement> Acknowledgements)>
            PublishForModelChangeEventAsync(
                ServerTestServices services,
                uint subscriptionId,
                ArrayOf<SubscriptionAcknowledgement> acknowledgements)
        {
            const int MaxPublishAttempts = 20;
            EventFieldList? eventFields = null;

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
                        if (notificationData.TryGetValue(out EventNotificationList? eventNotification) &&
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

        private ArrayOf<NodeManagerRegistration> GetBranchRegistrations(INodeManagerLifecycle? lifecycle = null)
        {
            lifecycle ??= m_server.NodeManagerLifecycle;
            ArrayOf<NodeManagerRegistration> registrations = lifecycle.Registrations;
            if (ReferenceEquals(lifecycle, m_server.NodeManagerLifecycle) && !lifecycle.IsShuttingDown)
            {
                foreach (NodeManagerRegistration startup in m_startupRegistrations)
                {
                    Assert.That(registrations.Find(registration => registration.Id == startup.Id),
                        Is.SameAs(startup), "Runtime publication must retain each adopted startup registration.");
                }
            }
            return registrations.ToList().Where(registration => !m_startupRegistrationIds.Contains(registration.Id))
                .ToArrayOf();
        }

        private NodeManagerRegistration GetBranchRegistration(INodeManagerLifecycle lifecycle, Guid id)
        {
            return RequireLifecycleValue(GetBranchRegistrations(lifecycle).Find(registration => registration.Id == id));
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
                    .Returns((IEnumerable<string>)null!);
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

        private sealed class CountingNodeManagerFactory :
            IAsyncNodeManagerFactory
        {
            public CountingNodeManagerFactory(
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
                                new Range
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

            /// <summary>
            /// Gets or sets an observer of explicitly admitted service requests.
            /// </summary>
            public Action<OperationContext>? RequestValidatedCallback { get; set; }

            public ValueTask ShutdownInternalsAsync(
                CancellationToken cancellationToken = default)
            {
                return OnServerStoppingAsync(cancellationToken);
            }

            /// <inheritdoc/>
            protected override ValueTask OnRequestValidatedAsync(OperationContext context)
            {
                RequestValidatedCallback?.Invoke(context);
                return base.OnRequestValidatedAsync(context);
            }
        }

        private sealed partial class TrackingLifecycleNodeManager :
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

            /// <summary>
            /// Creates a live generation with observable lifecycle callbacks and configurable cleanup failures.
            /// </summary>
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

            /// <summary>
            /// Gets the number of session-activation callbacks received by this generation.
            /// </summary>
            public int SessionActivatedCount =>
                Volatile.Read(ref m_sessionActivatedCount);

            public ArrayOf<NodeId> ActivatedSessionIds => [.. m_activatedSessionIds];

            public ArrayOf<NodeId> ClosedSessionIds => [.. m_closedSessionIds];

            public ArrayOf<uint> SubscribedAllEventIds => [.. m_subscribedAllEventIds];

            public ArrayOf<uint> UnsubscribedAllEventIds => [.. m_unsubscribedAllEventIds];

            public ServiceResult AllEventsSubscribeResult { get; set; } = ServiceResult.Good;

            public ServiceResult AllEventsUnsubscribeResult { get; set; } = ServiceResult.Good;

            /// <summary>
            /// Gets or sets a barrier before physical address-space deletion.
            /// </summary>
            public Func<CancellationToken, ValueTask>? BeforeDeleteAddressSpaceAsync { get; set; }

            /// <summary>
            /// Gets or sets a barrier during explicit reference-owner discovery.
            /// </summary>
            public Func<NodeId, CancellationToken, ValueTask>? ManagerHandleCallback { get; set; }

            /// <summary>
            /// Gets the number of all-events subscriptions dispatched to this generation.
            /// </summary>
            public int AllEventsSubscribeCount =>
                Volatile.Read(ref m_allEventsSubscribeCount);

            /// <summary>
            /// Gets all-events unsubscribe calls, including any erroneous finalization replay.
            /// </summary>
            public int AllEventsUnsubscribeCount =>
                Volatile.Read(ref m_allEventsUnsubscribeCount);

            /// <summary>
            /// Gets the number of condition-refresh callbacks received.
            /// </summary>
            public int ConditionRefreshCount =>
                Volatile.Read(ref m_conditionRefreshCount);

            /// <summary>
            /// Gets the number of read batches dispatched to this generation.
            /// </summary>
            public int ReadCount =>
                Volatile.Read(ref m_readCount);

            /// <summary>
            /// Gets address-space deletion attempts, including injected failures.
            /// </summary>
            public int DeleteAddressSpaceCount =>
                Volatile.Read(ref m_deleteAddressSpaceCount);

            /// <summary>
            /// Gets managed-disposal attempts, including injected failures.
            /// </summary>
            public int DisposeCount =>
                Volatile.Read(ref m_disposeCount);

            /// <summary>
            /// Gets the completion signal raised after asynchronous disposal of the retired manager finishes.
            /// </summary>
            public Task DisposalCompleted => m_disposalCompleted.Task;

            /// <summary>
            /// Gets or sets how many address-space deletion attempts must fail before cleanup succeeds.
            /// </summary>
            public int DeleteAddressSpaceFailuresRemaining
            {
                get => Volatile.Read(ref m_deleteAddressSpaceFailuresRemaining);
                set => Volatile.Write(ref m_deleteAddressSpaceFailuresRemaining, value);
            }

            /// <summary>
            /// Gets or sets how many managed-disposal attempts must fail before cleanup succeeds.
            /// </summary>
            public int DisposeFailuresRemaining
            {
                get => Volatile.Read(ref m_disposeFailuresRemaining);
                set => Volatile.Write(ref m_disposeFailuresRemaining, value);
            }

            /// <summary>
            /// Gets a snapshot of the item identifiers supplied to the last condition refresh.
            /// </summary>
            public uint[] LastConditionRefreshMonitoredItemIds =>
                Volatile.Read(ref m_lastConditionRefreshMonitoredItemIds);

            /// <summary>
            /// Gets or sets a barrier or observation callback for all-events subscription changes.
            /// </summary>
            public Func<
                IEventMonitoredItem,
                bool,
                CancellationToken,
                ValueTask> AllEventsCallback
            { get; set; } = null!;

            /// <summary>
            /// Gets or sets a callback executed before forwarding session activation.
            /// </summary>
            public Func<CancellationToken, ValueTask> SessionActivatedCallback { get; set; } = null!;

            /// <summary>
            /// Gets or sets a callback that receives the operation context of a session activation.
            /// </summary>
            public Func<OperationContext, CancellationToken, ValueTask> SessionActivatedContextCallback
            { get; set; } = null!;

            /// <summary>
            /// Gets or sets a callback that receives the operation context of an all-events subscription change.
            /// </summary>
            public Func<OperationContext, bool, CancellationToken, ValueTask> AllEventsContextCallback
            { get; set; } = null!;
            /// <summary>
            /// Gets or sets a callback observing or delaying the condition-refresh item batch.
            /// </summary>
            public Func<
                IList<IEventMonitoredItem>,
                CancellationToken,
                ValueTask> ConditionRefreshCallback
            { get; set; } = null!;

            /// <summary>
            /// Gets or sets a callback executed before the underlying read batch.
            /// </summary>
            public Func<OperationContext, CancellationToken, ValueTask> ReadCallback { get; set; } = null!;

            public NodeId ReadCallbackNodeId { get; set; }

            public Func<ServerSystemContext, NodeId, CancellationToken, ValueTask> ValidateNodeCallback
            { get; set; } = null!;

            public async ValueTask AddContinuationChildrenAsync(
                string prefix,
                CancellationToken cancellationToken)
            {
                ushort namespaceIndex = NamespaceIndexes[0];
                BaseObjectState root = RequireLifecycleValue(
                    Find(new NodeId(kRootNodeId, namespaceIndex)) as BaseObjectState);
                for (uint index = 0; index < 2; index++)
                {
                    string name = prefix + (index == 0 ? "First" : "Second");
                    var child = new BaseDataVariableState(root)
                    {
                        NodeId = new NodeId(8010 + index, namespaceIndex),
                        BrowseName = new QualifiedName(name, namespaceIndex),
                        DisplayName = LocalizedText.From(name),
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        AccessLevel = AccessLevels.CurrentRead,
                        UserAccessLevel = AccessLevels.CurrentRead,
                        Value = 42
                    };
                    root.AddChild(child);
                    await AddPredefinedNodeAsync(SystemContext, child, cancellationToken).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Counts reads and runs the configured observation callback before normal node-manager dispatch.
            /// </summary>
            public override async ValueTask ReadAsync(
                OperationContext context,
                double maxAge,
                ArrayOf<ReadValueId> nodesToRead,
                IList<DataValue> values,
                IList<ServiceResult> errors,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_readCount);
                if (ReadCallback is not null &&
                    (ReadCallbackNodeId.IsNull || nodesToRead.Contains(node => node.NodeId == ReadCallbackNodeId)))
                {
                    await ReadCallback(context, cancellationToken).ConfigureAwait(false);
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

            /// <summary>
            /// Counts session activation and runs its configured callback before forwarding to the base manager.
            /// </summary>
            public override async ValueTask SessionActivatedAsync(
                OperationContext context,
                NodeId sessionId,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_sessionActivatedCount);
                m_activatedSessionIds.Enqueue(sessionId);
                if (SessionActivatedContextCallback is not null)
                {
                    await SessionActivatedContextCallback(context, cancellationToken).ConfigureAwait(false);
                }
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

            public override ValueTask SessionClosingAsync(
                OperationContext context,
                NodeId sessionId,
                bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
            {
                m_closedSessionIds.Enqueue(sessionId);
                return base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken);
            }

            /// <summary>
            /// Tracks all-events subscription callbacks and can report a failure after completing an unsubscribe.
            /// </summary>
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
                    m_unsubscribedAllEventIds.Enqueue(monitoredItem.Id);
                }
                else
                {
                    Interlocked.Increment(ref m_allEventsSubscribeCount);
                    m_subscribedAllEventIds.Enqueue(monitoredItem.Id);
                }

                if (AllEventsContextCallback is not null)
                {
                    await AllEventsContextCallback(context, unsubscribe, cancellationToken).ConfigureAwait(false);
                }
                if (AllEventsCallback is not null)
                {
                    await AllEventsCallback(
                        monitoredItem,
                        unsubscribe,
                        cancellationToken).ConfigureAwait(false);
                }
                if (!unsubscribe && ServiceResult.IsBad(AllEventsSubscribeResult))
                {
                    return AllEventsSubscribeResult;
                }
                if (unsubscribe && ServiceResult.IsBad(AllEventsUnsubscribeResult))
                {
                    return AllEventsUnsubscribeResult;
                }

                ServiceResult result = await base
                    .SubscribeToAllEventsAsync(
                        context,
                        subscriptionId,
                        monitoredItem,
                        unsubscribe,
                        cancellationToken)
                    .ConfigureAwait(false);
                return unsubscribe && ServiceResult.IsGood(result) ? AllEventsUnsubscribeResult : result;
            }

            /// <summary>
            /// Records refreshed item identities and invokes the optional barrier before base refresh handling.
            /// </summary>
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

            /// <summary>
            /// Counts deletion attempts and injects configured failures before normal address-space teardown.
            /// </summary>
            /// <exception cref="SentinelException"></exception>
            public override async ValueTask DeleteAddressSpaceAsync(
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_deleteAddressSpaceCount);
                if (TryConsumeFailure(ref m_deleteAddressSpaceFailuresRemaining))
                {
                    throw new SentinelException("DeleteAddressSpaceAsync failed.");
                }
                if (BeforeDeleteAddressSpaceAsync is not null)
                {
                    await BeforeDeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
                }
                await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
            }

            protected override async ValueTask<NodeState> ValidateNodeAsync(
                ServerSystemContext context,
                NodeHandle handle,
                IDictionary<NodeId, NodeState> cache,
                CancellationToken cancellationToken = default)
            {
                if (ValidateNodeCallback is not null)
                {
                    await ValidateNodeCallback(context, handle.NodeId, cancellationToken).ConfigureAwait(false);
                }
                return await base.ValidateNodeAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Completes base disposal before signaling that the retired generation has released its resources.
            /// </summary>
            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync().ConfigureAwait(false);
                m_disposalCompleted.TrySetResult(true);
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

            private readonly ConcurrentQueue<NodeId> m_activatedSessionIds = new();
            private readonly ConcurrentQueue<NodeId> m_closedSessionIds = new();
            private readonly ConcurrentQueue<uint> m_subscribedAllEventIds = new();
            private readonly ConcurrentQueue<uint> m_unsubscribedAllEventIds = new();

            /// <summary>
            /// Signals when asynchronous disposal has completed for deterministic retirement assertions.
            /// </summary>
            private readonly TaskCompletionSource<bool> m_disposalCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            SameIdDifferentGeneration
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
