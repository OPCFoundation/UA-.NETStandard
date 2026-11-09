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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies canonical and secondary type table and factory publication ownership of
    /// <see cref="MasterNodeManager"/> on a live server.
    /// </summary>
    [TestFixture]
    [Category("MasterNodeManager")]
    [NonParallelizable]
    public sealed class MasterNodeManagerViewOwnershipTests
    {
        private const string kDerivedManagerNamespace = "urn:opcfoundation.org:Tests:DerivedMasterLookup";

        /// <summary>
        /// Candidate facades retain the host clock and private historian registry while gating host registries.
        /// </summary>
        [Test]
        public async Task CandidateServerPreservesClockAndSeparatesHistorianDeclarationsAsync()
        {
            var clock = new CandidateTimeProvider();
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry, clock));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var preparation = new ChangePreparer(server.CurrentInstance);
                using ChangePreparer.StagingServer staging = preparation.CreateServer();
                IServerInternal facade = staging;
                Assert.That(facade, Is.InstanceOf<ITimeProviderProvider>());
                Assert.That(((ITimeProviderProvider)facade).TimeProvider, Is.SameAs(clock));
                var historian = (IHistorianRegistryProvider)facade;
                var aliases = (IAliasNameStoreRegistryProvider)facade;
                Assert.That(facade, Is.InstanceOf<IConformanceUnitsManagerProvider>());
                var conformance = (IConformanceUnitsManagerProvider)facade;
                Assert.That(conformance.ConformanceUnitsManager, Is.Null);
                IHistorianProviderRegistry candidateHistorian = historian.HistorianRegistry;
                Assert.That(candidateHistorian, Is.InstanceOf<StagedHistorianProviderRegistry>());
                Assert.That(candidateHistorian,
                    Is.Not.SameAs(((IHistorianRegistryProvider)server.CurrentInstance).HistorianRegistry));
                Assert.That(
                    () => aliases.AliasNameStoreRegistry,
                    Throws.TypeOf<InvalidOperationException>());
                staging.Bind();
                Assert.That(conformance.ConformanceUnitsManager,
                    Is.Not.Null.And.SameAs(
                        ((IConformanceUnitsManagerProvider)server.CurrentInstance).ConformanceUnitsManager));
                Assert.That(
                    historian.HistorianRegistry,
                    Is.SameAs(candidateHistorian));
                Assert.That(
                    aliases.AliasNameStoreRegistry,
                    Is.SameAs(((IAliasNameStoreRegistryProvider)server.CurrentInstance).AliasNameStoreRegistry));
                staging.Detach();
                Assert.That(conformance.ConformanceUnitsManager, Is.Null);
                Assert.That(((ITimeProviderProvider)facade).TimeProvider, Is.SameAs(clock));
                Assert.That(historian.HistorianRegistry, Is.SameAs(candidateHistorian));
                Assert.That(
                    () => aliases.AliasNameStoreRegistry,
                    Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Identifies a host-owned clock independently of the system singleton.
        /// </summary>
        private sealed class CandidateTimeProvider : TimeProvider
        {
        }

        /// <summary>
        /// Verifies that a secondary manager on a running server does not own the shared tables and that
        /// its disposal leaves the canonical publication intact.
        /// </summary>
        [Test]
        public async Task SecondaryManagerDisposeKeepsCanonicalPublicationAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                TypeTable types = server.CurrentInstance.TypeTree;
                var factory = (EncodeableFactory)server.CurrentInstance.Factory;
                TypeTable servingImage = GetRoutingImage(server.CurrentInstance.NodeManager);
                Assert.That(servingImage, Is.Not.SameAs(types));
                Assert.That(servingImage.FindSuperType(ObjectTypeIds.FolderType),
                    Is.EqualTo(ObjectTypeIds.BaseObjectType));

                using (var secondary = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, Array.Empty<INodeManager>()))
                {
                    Assert.That(GetRoutingImage(secondary), Is.Not.SameAs(types));
                    Assert.That(GetRoutingImage(secondary).FindSuperType(ObjectTypeIds.FolderType),
                        Is.EqualTo(ObjectTypeIds.BaseObjectType));
                    Assert.That(server.CurrentInstance.Factory, Is.SameAs(factory));
                }

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(server.CurrentInstance.TypeTree, Is.SameAs(types));
                    Assert.That(server.CurrentInstance.Factory, Is.SameAs(factory));
                    Assert.That(types.FindSuperType(ObjectTypeIds.FolderType),
                        Is.EqualTo(ObjectTypeIds.BaseObjectType));
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a secondary manager cannot publish or select another preparation's private metadata.
        /// </summary>
        [Test]
        public async Task SecondaryManagerDoesNotSelectPrivateStagingMetadataAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                ushort ns = server.CurrentInstance.NamespaceUris.GetIndexOrAppend(
                    "urn:opcfoundation.org:Tests:MasterNodeManagerViewOwnership");
                var privateType = new NodeId(987654, ns);
                TypeTable canonicalImage = GetRoutingImage(server.CurrentInstance.NodeManager);
                using (var secondary = new MasterNodeManager(
                    server.CurrentInstance, fixture.Config, null, Array.Empty<INodeManager>()))
                {
                    var preparation = new ChangePreparer(server.CurrentInstance);
                    using ChangePreparer.StagingServer staging = preparation.CreateServer();
                    TypeTable image = staging.TypeTree;
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(image, Is.Not.SameAs(canonicalImage));
                        Assert.That(image, Is.Not.SameAs(server.CurrentInstance.TypeTree));
                        Assert.That(staging.DefaultSystemContext.Server, Is.SameAs(staging));
                        Assert.That(staging.DefaultSystemContext.Copy().TypeTable, Is.SameAs(image));
                    }
                    image.AddSubtype(privateType, ObjectTypeIds.BaseObjectType);
                    Assert.That(image.IsKnown(privateType), Is.True);
                    Assert.That(GetRoutingImage(secondary).IsKnown(privateType), Is.False);
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(canonicalImage.IsKnown(privateType), Is.False);
                    Assert.That(server.CurrentInstance.TypeTree.IsKnown(privateType), Is.False);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A derived master that inherits the default lookup still resolves through the captured request view.
        /// </summary>
        [Test]
        public async Task DerivedMasterWithoutLookupOverrideUsesCapturedRoutingAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager.SetupGet(manager => manager.NamespaceUris).Returns([kDerivedManagerNamespace]);
                using var master = new DerivedMasterNodeManager(
                    server.CurrentInstance, fixture.Config, nodeManager.Object);
                ushort namespaceIndex = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    kDerivedManagerNamespace);
                NodeId nodeId = new("DerivedMasterLookup", namespaceIndex);
                var expectedHandle = new object();
                nodeManager.Setup(manager => manager.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedHandle);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                var routing = (IRequestRouting)master;
                routing.PinRequest(context);

                (object? handle, IAsyncNodeManager? actualOwner) = await routing.GetManagerHandleAsync(
                    context, nodeId, CancellationToken.None).ConfigureAwait(false);

                Assert.That(handle, Is.SameAs(expectedHandle));
                Assert.That(actualOwner, Is.SameAs(nodeManager.Object));
                nodeManager.VerifyAll();
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A derived master's legacy handle lookup override remains in the contextual dispatch path.
        /// </summary>
        [Test]
        public async Task DerivedMasterLookupOverrideRemainsEffectiveAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager.SetupGet(manager => manager.NamespaceUris).Returns([kDerivedManagerNamespace]);
                var expectedHandle = new object();
                using var master = new OverridingMasterNodeManager(
                    server.CurrentInstance, fixture.Config, nodeManager.Object, expectedHandle);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                var routing = (IRequestRouting)master;
                routing.PinRequest(context);
                ushort namespaceIndex = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    kDerivedManagerNamespace);

                (object? handle, IAsyncNodeManager? owner) = await routing.GetManagerHandleAsync(
                    context, new NodeId("DerivedMasterOverride", namespaceIndex), CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(handle, Is.SameAs(expectedHandle));
                Assert.That(owner, Is.SameAs(nodeManager.Object));
                Assert.That(master.LookupCount, Is.EqualTo(1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A legacy lookup's captured context cannot retain its completed operation or superseded routing image.
        /// </summary>
        [Test]
        public async Task CompletedLegacyLookupDoesNotRetainOperationInCapturedContextAsync()
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry));
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                var manager = new Mock<IAsyncNodeManager>();
                manager.SetupGet(value => value.NamespaceUris).Returns([kDerivedManagerNamespace]);
                using var master = new OverridingMasterNodeManager(
                    server.CurrentInstance, fixture.Config, manager.Object, new object());
                ushort namespaceIndex = checked((ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    kDerivedManagerNamespace));
                (WeakReference<OperationContext> operation,
                    WeakReference<NodeManagerRoutingTable.RoutingSnapshot> snapshot) = await CaptureLegacyLookupAsync(
                    master, new NodeId("CapturedLookup", namespaceIndex)).ConfigureAwait(false);
                Assert.That(master.CapturedContext, Is.Not.Null);
                var additional = new Mock<IAsyncNodeManager>();
                additional.SetupGet(value => value.NamespaceUris).Returns([kDerivedManagerNamespace]);
                await master.RegisterNamespaceManagerAsync(kDerivedManagerNamespace, additional.Object)
                    .ConfigureAwait(false);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                Assert.That(operation.TryGetTarget(out _), Is.False);
                Assert.That(snapshot.TryGetTarget(out _), Is.False);
                GC.KeepAlive(master.CapturedContext);
                GC.KeepAlive(master);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that server shutdown does not replace canonical metadata handles.
        /// </summary>
        [Test]
        public async Task CanonicalDisposePreservesMetadataHandleIdentityAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            bool stopped = false;
            try
            {
                StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
                IServerInternal serving = server.CurrentInstance;
                TypeTable types = serving.TypeTree;
                var factory = (EncodeableFactory)serving.Factory;
                TypeTable servingImage = GetRoutingImage(serving.NodeManager);
                NodeId[] probes =
                [
                    ObjectTypeIds.FolderType,
                    ObjectTypeIds.BaseObjectType,
                    VariableTypeIds.BaseDataVariableType,
                    DataTypeIds.Int32
                ];
                var parents = new NodeId[probes.Length];
                for (int i = 0; i < probes.Length; i++)
                {
                    Assert.That(servingImage.IsKnown(probes[i]), Is.True);
                    parents[i] = servingImage.FindSuperType(probes[i]);
                }
                await fixture.StopAsync().ConfigureAwait(false);
                stopped = true;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(serving.TypeTree, Is.SameAs(types));
                    Assert.That(serving.Factory, Is.SameAs(factory));
                    Assert.That(factory.Fork(), Is.Not.SameAs(factory));
                    for (int i = 0; i < probes.Length; i++)
                    {
                        Assert.That(servingImage.FindSuperType(probes[i]), Is.EqualTo(parents[i]));
                        Assert.That(servingImage.IsKnown(probes[i]), Is.True);
                    }
                }
                NodeId added = new(987655, 1);
                types.AddSubtype(added, NodeId.Null);
                Assert.That(types.IsKnown(added), Is.True);
                Assert.That(servingImage.IsKnown(added), Is.False);
            }
            finally
            {
                if (!stopped)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Exercises the legacy virtual lookup path without overriding its default implementation.
        /// </summary>
        // TODO: Remove when CA1812 recognizes construction by the NUnit test methods.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "The test methods instantiate this private helper directly.")]
        private sealed class DerivedMasterNodeManager : MasterNodeManager
        {
            /// <summary>
            /// Initializes a derived master with the supplied server configuration.
            /// </summary>
            /// <param name="server">The server to own.</param>
            /// <param name="configuration">The server configuration.</param>
            /// <param name="manager">The additional manager in the initial routing view.</param>
            public DerivedMasterNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IAsyncNodeManager manager)
                : base(server, configuration, null, [manager], null)
            {
            }
        }

        /// <summary>
        /// Returns a controlled owner through a legacy virtual lookup override.
        /// </summary>
        // TODO: Remove when CA1812 recognizes construction by the NUnit test methods.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "The test methods instantiate this private helper directly.")]
        private sealed class OverridingMasterNodeManager : MasterNodeManager
        {
            /// <summary>
            /// Initializes a derived master with the supplied owner and handle.
            /// </summary>
            /// <param name="server">The server to own.</param>
            /// <param name="configuration">The server configuration.</param>
            /// <param name="manager">The additional manager in the initial routing view.</param>
            /// <param name="handle">The handle returned by the override.</param>
            public OverridingMasterNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IAsyncNodeManager manager,
                object handle)
                : base(server, configuration, null, [manager], null)
            {
                Manager = manager;
                Handle = handle;
            }

            /// <summary>
            /// Gets the number of legacy lookup calls.
            /// </summary>
            public int LookupCount { get; private set; }

            /// <summary>
            /// Gets the execution context captured by the latest override invocation.
            /// </summary>
            public ExecutionContext? CapturedContext { get; private set; }

            /// <inheritdoc/>
            public override ValueTask<(object? handle, IAsyncNodeManager? nodeManager)> GetManagerHandleAsync(
                NodeId nodeId,
                CancellationToken cancellationToken = default)
            {
                LookupCount++;
                CapturedContext = ExecutionContext.Capture();
                return new ValueTask<(object?, IAsyncNodeManager?)>((Handle, Manager));
            }

            /// <summary>
            /// The manager returned by the override.
            /// </summary>
            private IAsyncNodeManager Manager { get; }

            /// <summary>
            /// The handle returned by the override.
            /// </summary>
            private object Handle { get; }
        }

        /// <summary>
        /// Completes a scoped lookup without returning a strong reference to its service operation.
        /// </summary>
        /// <param name="master">The master whose legacy override captures the execution context.</param>
        /// <param name="nodeId">The node to resolve.</param>
        /// <returns>Weak references to the completed operation and its routing image.</returns>
        /// <exception cref="InvalidOperationException">The read did not capture routes.</exception>
        private static async Task<(WeakReference<OperationContext> Operation,
            WeakReference<NodeManagerRoutingTable.RoutingSnapshot> Snapshot)> CaptureLegacyLookupAsync(
            OverridingMasterNodeManager master,
            NodeId nodeId)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            var routing = (IRequestRouting)master;
            routing.PinRequest(context);
            await routing.GetManagerHandleAsync(context, nodeId, CancellationToken.None).ConfigureAwait(false);
            NodeManagerRoutingTable.RoutingSnapshot snapshot = context.RoutingSnapshot
                ?? throw new InvalidOperationException("The read did not capture routes.");
            return (new WeakReference<OperationContext>(context),
                new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(snapshot));
        }

        /// <summary>
        /// Captures the same immutable type image that a read operation receives.
        /// </summary>
        /// <param name="manager">The routing owner.</param>
        /// <returns>The captured type image.</returns>
        /// <exception cref="InvalidOperationException">The read operation did not capture a type image.</exception>
        private static TypeTable GetRoutingImage(IMasterNodeManager manager)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            ((IRequestRouting)manager).PinRequest(context);
            return context.RoutingSnapshot?.TypeTree
                ?? throw new InvalidOperationException("The read operation did not capture a type image.");
        }
    }
}
