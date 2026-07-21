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

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Live lifecycle integration tests for runtime NodeSet-backed NodeManagers, exercising
    /// <see cref="RuntimeNodeSetLifecycleExtensions.AddRuntimeNodeSetAsync"/>,
    /// <see cref="RuntimeNodeSetLifecycleExtensions.ReloadRuntimeNodeSetAsync"/>, and
    /// <see cref="INodeManagerLifecycle.RemoveAsync"/> against a real, running
    /// <see cref="ReferenceServer"/>.
    /// </summary>
    /// <remarks>
    /// Each test starts a fresh <see cref="ServerFixture{ReferenceServer}"/> so that the
    /// namespace-table and routing-table baselines are predictable and unaffected by other
    /// tests' live add/reload/remove mutations.
    /// </remarks>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("RuntimeNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RuntimeNodeSetLifecycleTests
    {
        private const double kMaxAge = 10000;

        private const string kModelNamespaceUri =
            "urn:opcfoundation.org:Tests:RuntimeNodeSetLifecycle";

        private const uint kRootNodeId = 9000;
        private const uint kValueNodeId = 9001;
        private const uint kOriginalOnlyNodeId = 9002;
        private const uint kReplacementOnlyNodeId = 9003;
        private const string kRootBrowseName = "LifecycleRoot";
        private const string kValueBrowseName = "LifecycleValue";
        private const string kOriginalOnlyBrowseName = "OriginalOnly";
        private const string kReplacementOnlyBrowseName = "ReplacementOnly";
        private const int kGeneration1Value = 1;
        private const int kGeneration2Value = 2;
        private const int kOriginalOnlyValue = 100;
        private const int kReplacementOnlyValue = 200;

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
                nameof(RuntimeNodeSetLifecycleTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create().CreateLogger<RuntimeNodeSetLifecycleTests>();

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
        /// Adding a runtime NodeSet after startup must publish exactly one registration,
        /// route it into the master node manager's live snapshots exactly once, append the
        /// model namespace exactly once, and increment <c>UrisVersion</c> by one.
        /// </summary>
        [Test]
        public async Task AddRuntimeNodeSetAsyncPublishesRegistrationAndRoutingAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;

            int namespaceCountBefore = server.NamespaceUris.Count;
            IReadOnlyList<IAsyncNodeManager> managersBefore = master.AsyncNodeManagers;
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routesBefore =
                master.NamespaceManagers;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);

            Assert.That(server.CurrentState, Is.EqualTo(ServerState.Running));
            Assert.That(registration.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(registration.Generation, Is.EqualTo(1));
            Assert.That(registration.NodeManager, Is.InstanceOf<RuntimeNodeSetNodeManager>());
            Assert.That(registration.NamespaceUris.Count, Is.EqualTo(1));
            Assert.That(registration.NamespaceUris[0], Is.EqualTo(kModelNamespaceUri));

            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            Assert.That(CountMatches(registrations, r => r.Id == registration.Id), Is.EqualTo(1));

            IReadOnlyList<IAsyncNodeManager> managersAfter = master.AsyncNodeManagers;
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routesAfter =
                master.NamespaceManagers;

            Assert.That(
                managersBefore.Count(m => ReferenceEquals(m, registration.NodeManager)),
                Is.Zero,
                "The pre-add snapshot must be unaffected by the subsequent Add.");
            Assert.That(
                managersAfter.Count(m => ReferenceEquals(m, registration.NodeManager)),
                Is.EqualTo(1));
            Assert.That(managersAfter, Has.Count.EqualTo(managersBefore.Count + 1));

            int namespaceIndex = server.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0));
            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore + 1));

            Assert.That(routesBefore.ContainsKey(namespaceIndex), Is.False);
            Assert.That(routesAfter.ContainsKey(namespaceIndex), Is.True);
            Assert.That(routesAfter[namespaceIndex], Has.Count.EqualTo(1));
            Assert.That(
                ReferenceEquals(routesAfter[namespaceIndex][0], registration.NodeManager),
                Is.True);

            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore + 1));
        }

        [Test]
        public async Task PreparedRuntimeNodeSetRemainsHiddenUntilCommitAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            var host = (IDynamicNodeManagerHost)master;
            var factory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(generation: 1));
            IAsyncNodeManager nodeManager = await factory
                .CreateAsync(server, m_fixture.Config)
                .ConfigureAwait(false);
            PreparedNodeManager prepared = await host
                .PrepareAsync(nodeManager)
                .ConfigureAwait(false);

            try
            {
                await host.PublishAsync(prepared).ConfigureAwait(false);

                ushort ns = (ushort)server.NamespaceUris.GetIndex(
                    kModelNamespaceUri);
                var rootNodeId = new NodeId(kRootNodeId, ns);
                Assert.That(
                    master.AsyncNodeManagers.Any(manager =>
                        ReferenceEquals(manager, nodeManager)),
                    Is.False);
                Assert.That(
                    await master.FindNodeInAddressSpaceAsync(rootNodeId)
                        .ConfigureAwait(false),
                    Is.Null);

                await host.CommitAsync(prepared).ConfigureAwait(false);

                Assert.That(
                    master.AsyncNodeManagers.Any(manager =>
                        ReferenceEquals(manager, nodeManager)),
                    Is.True);
                Assert.That(
                    await master.FindNodeInAddressSpaceAsync(rootNodeId)
                        .ConfigureAwait(false),
                    Is.Not.Null);
            }
            finally
            {
                await host.RollbackAsync(prepared).ConfigureAwait(false);
                (nodeManager as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// Nodes added through a live runtime NodeSet must be directly findable, browsable
        /// from both the Objects folder and their parent, translatable via a relative browse
        /// path, and readable through the Read service with a concrete <c>Good</c> value.
        /// </summary>
        [Test]
        public async Task AddedRuntimeNodesAreFindableBrowsableReadableAndTranslatableAsync()
        {
            IServerInternal server = m_server.CurrentInstance;

            _ = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);

            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var valueNodeId = new NodeId(kValueNodeId, ns);

            // Direct find.
            NodeState rootNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(rootNodeId)
                .ConfigureAwait(false);
            NodeState valueNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(valueNodeId)
                .ConfigureAwait(false);

            Assert.That(rootNode, Is.Not.Null);
            Assert.That(rootNode.BrowseName.Name, Is.EqualTo(kRootBrowseName));
            Assert.That(valueNode, Is.Not.Null);
            Assert.That(valueNode.BrowseName.Name, Is.EqualTo(kValueBrowseName));

            // Browse Objects -> root.
            BrowseResponse objectsBrowse = await BrowseAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            Assert.That(objectsBrowse.Results.Count, Is.EqualTo(1));
            ArrayOf<ReferenceDescription> objectsReferences = objectsBrowse.Results[0].References;
            Assert.That(
                objectsReferences.Contains(r => r.BrowseName.Equals(new QualifiedName(kRootBrowseName, ns))),
                Is.True);

            // Browse root -> value.
            BrowseResponse rootBrowse = await BrowseAsync(rootNodeId).ConfigureAwait(false);
            Assert.That(rootBrowse.Results.Count, Is.EqualTo(1));
            ArrayOf<ReferenceDescription> rootReferences = rootBrowse.Results[0].References;
            Assert.That(
                rootReferences.Contains(r => r.BrowseName.Equals(new QualifiedName(kValueBrowseName, ns))),
                Is.True);

            // Translate ObjectsFolder/LifecycleRoot/LifecycleValue.
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var relativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(kRootBrowseName, ns)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(kValueBrowseName, ns)
                    }
                ]
            };
            ArrayOf<BrowsePath> browsePaths =
            [
                new BrowsePath { StartingNode = ObjectIds.ObjectsFolder, RelativePath = relativePath }
            ];

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            TranslateBrowsePathsToNodeIdsResponse translateResponse = await services
                .TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths)
                .ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(
                translateResponse.ResponseHeader,
                translateResponse.Results,
                browsePaths);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                translateResponse.DiagnosticInfos,
                browsePaths,
                translateResponse.ResponseHeader.StringTable,
                m_logger);

            Assert.That(translateResponse.Results.Count, Is.EqualTo(1));
            BrowsePathResult translateResult = translateResponse.Results[0];
            Assert.That(translateResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(translateResult.Targets.Count, Is.EqualTo(1));
            Assert.That(
                translateResult.Targets[0].TargetId,
                Is.EqualTo(new ExpandedNodeId(valueNodeId)));

            // Service read of the concrete Int32 value.
            DataValue dataValue = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(dataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dataValue.WrappedValue.GetInt32(), Is.EqualTo(kGeneration1Value));
        }

        [Test]
        public async Task AddedReferencesAreTrackedDeduplicatedAndRemovedAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);
            var runtimeManager =
                (RuntimeNodeSetNodeManager)registration.NodeManager;
            ushort namespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, namespaceIndex);
            var reference = new NodeStateReference(
                ReferenceTypeIds.HasComponent,
                false,
                ObjectIds.Server);
            var references = new Dictionary<NodeId, IList<IReference>>
            {
                [rootNodeId] = [reference]
            };

            await runtimeManager
                .AddReferencesAsync(references)
                .ConfigureAwait(false);
            await runtimeManager
                .AddReferencesAsync(references)
                .ConfigureAwait(false);

            Dictionary<NodeId, IList<IReference>> trackedReferences =
                runtimeManager.GetAddedReferences();
            Assert.That(trackedReferences.ContainsKey(rootNodeId), Is.True);
            Assert.That(trackedReferences[rootNodeId], Has.Count.EqualTo(1));
            Assert.That(
                trackedReferences[rootNodeId][0].TargetId,
                Is.EqualTo(new ExpandedNodeId(ObjectIds.Server)));

            object sourceHandle = await runtimeManager
                .GetManagerHandleAsync(rootNodeId)
                .ConfigureAwait(false);
            Assert.That(sourceHandle, Is.Not.Null);
            ServiceResult deleteResult = await runtimeManager
                .DeleteReferenceAsync(
                    sourceHandle,
                    ReferenceTypeIds.HasComponent,
                    isInverse: false,
                    targetId: ObjectIds.Server,
                    deleteBidirectional: false)
                .ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(deleteResult), Is.True);
            Assert.That(runtimeManager.GetAddedReferences(), Is.Empty);
        }

        [Test]
        public async Task PrepareReloadAsyncReturnsDroppedReferencesForRemovedNodesAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);
            var originalManager =
                (RuntimeNodeSetNodeManager)registration.NodeManager;
            ushort namespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                kModelNamespaceUri);
            var originalOnlyNodeId = new NodeId(
                kOriginalOnlyNodeId,
                namespaceIndex);
            var reference = new NodeStateReference(
                ReferenceTypeIds.HasComponent,
                false,
                ObjectIds.Server);
            await originalManager
                .AddReferencesAsync(
                    new Dictionary<NodeId, IList<IReference>>
                    {
                        [originalOnlyNodeId] = [reference]
                    })
                .ConfigureAwait(false);

            var factory = new RuntimeNodeSetNodeManagerFactory(
                CreateOptions(generation: 2));
            IAsyncNodeManager replacement = await factory
                .CreateAsync(server, m_fixture.Config)
                .ConfigureAwait(false);
            var host = (IDynamicNodeManagerHost)server.NodeManager;
            PreparedNodeManager prepared = await host
                .PrepareAsync(replacement)
                .ConfigureAwait(false);

            try
            {
                ArrayOf<LocalReference> droppedReferences =
                    await originalManager
                        .PrepareReloadAsync(replacement)
                        .ConfigureAwait(false);

                Assert.That(droppedReferences, Has.Count.EqualTo(1));
                Assert.That(
                    droppedReferences[0].SourceId,
                    Is.EqualTo(ObjectIds.Server));
                Assert.That(
                    droppedReferences[0].ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
                Assert.That(droppedReferences[0].IsInverse, Is.True);
                Assert.That(
                    droppedReferences[0].TargetId,
                    Is.EqualTo(originalOnlyNodeId));
            }
            finally
            {
                await host.RollbackAsync(prepared).ConfigureAwait(false);
                (replacement as IDisposable)?.Dispose();
            }
        }

        [Test]
        public Task RuntimeNodeSetLifecycleExtensionsRejectNullArgumentsAsync()
        {
            INodeManagerLifecycle lifecycle = m_server.NodeManagerLifecycle;
            var options = new RuntimeNodeSetOptions();
            Mock<IAsyncNodeManager> nodeManager = new();
            nodeManager
                .Setup(manager => manager.NamespaceUris)
                .Returns(["urn:opcfoundation.org:Tests:RuntimeNodeSet:NullGuards"]);
            var registration = new NodeManagerRegistration(
                Guid.NewGuid(),
                1,
                nodeManager.Object);

            ArgumentNullException exception =
                Assert.ThrowsAsync<ArgumentNullException>(
                    async () => await RuntimeNodeSetLifecycleExtensions
                        .AddRuntimeNodeSetAsync(
                            null!,
                            options)
                        .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .AddRuntimeNodeSetAsync(null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("options"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await RuntimeNodeSetLifecycleExtensions
                    .ReloadRuntimeNodeSetAsync(
                        null!,
                        registration,
                        options)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("lifecycle"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadRuntimeNodeSetAsync(
                        null!,
                        options)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("registration"));

            exception = Assert.ThrowsAsync<ArgumentNullException>(
                async () => await lifecycle
                    .ReloadRuntimeNodeSetAsync(
                        registration,
                        null!)
                    .ConfigureAwait(false));
            Assert.That(exception.ParamName, Is.EqualTo("replacement"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// The existing embedded complex-types NodeSet test resource can also be added to a
        /// running server and its imported nodes can be found and read through the service.
        /// </summary>
        [Test]
        public async Task ExistingRuntimeNodeSetResourceCanBeAddedAndReadAfterStartupAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var options = new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "ServerComplexTypesTestModel",
                        _ => new ValueTask<Stream>(RuntimeNodeSetTestServer.OpenTestStream()),
                        [RuntimeNodeSetTestServer.NamespaceUri])
                ],
                DefaultNamespaceUri = RuntimeNodeSetTestServer.NamespaceUri
            };

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(options)
                .ConfigureAwait(false);

            ushort ns = (ushort)server.NamespaceUris.GetIndex(RuntimeNodeSetTestServer.NamespaceUri);
            var pointValueId = new NodeId(RuntimeNodeSetTestServer.PointValueVariable, ns);

            Assert.That(registration.NamespaceUris.Count, Is.EqualTo(1));
            Assert.That(
                registration.NamespaceUris[0],
                Is.EqualTo(RuntimeNodeSetTestServer.NamespaceUri));
            NodeState pointValue = await server.NodeManager
                .FindNodeInAddressSpaceAsync(pointValueId)
                .ConfigureAwait(false);
            Assert.That(pointValue, Is.Not.Null);

            DataValue browseName = await ReadValueAsync(pointValueId, Attributes.BrowseName)
                .ConfigureAwait(false);
            Assert.That(browseName.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                browseName.WrappedValue.GetQualifiedName(),
                Is.EqualTo(new QualifiedName("PointValue", ns)));
        }

        [Test]
        public async Task AddRuntimeNodeSetAsyncWithDuplicateNodeIdsRollsBackWithoutPublishingAsync()
        {
            const uint DuplicateNodeId = 9100;
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            int managerCountBefore = master.AsyncNodeManagers.Count;
            string xml = BuildDuplicateNodeSetXml(DuplicateNodeId);

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(CreateRawOptions(
                            "DuplicateNodeIds",
                            kModelNamespaceUri,
                            xml))
                        .ConfigureAwait(false));

            Assert.That(exception.Message, Does.Contain("Duplicate NodeId"));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            Assert.That(master.AsyncNodeManagers, Has.Count.EqualTo(managerCountBefore));

            int namespaceIndex = server.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0));
            NodeState duplicateNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(
                    new NodeId(DuplicateNodeId, (ushort)namespaceIndex))
                .ConfigureAwait(false);
            Assert.That(duplicateNode, Is.Null);
        }

        [Test]
        public async Task AddRuntimeNodeSetAsyncWithNodeInUnownedNamespaceRollsBackWithoutPublishingAsync()
        {
            const string ExternalNamespaceUri =
                "urn:opcfoundation.org:Tests:RuntimeNodeSetLifecycle:External";
            const uint ExternalNodeId = 9200;
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            int managerCountBefore = master.AsyncNodeManagers.Count;
            string xml = BuildUnownedNamespaceNodeSetXml(
                ExternalNamespaceUri,
                ExternalNodeId);

            InvalidOperationException exception =
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_server.NodeManagerLifecycle
                        .AddRuntimeNodeSetAsync(CreateRawOptions(
                            "UnownedNamespace",
                            kModelNamespaceUri,
                            xml))
                        .ConfigureAwait(false));

            Assert.That(exception.Message, Does.Contain("not owned"));
            Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
            Assert.That(master.AsyncNodeManagers, Has.Count.EqualTo(managerCountBefore));

            int externalNamespaceIndex =
                server.NamespaceUris.GetIndex(ExternalNamespaceUri);
            Assert.That(externalNamespaceIndex, Is.GreaterThan(0));
            NodeState externalNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(
                    new NodeId(ExternalNodeId, (ushort)externalNamespaceIndex))
                .ConfigureAwait(false);
            Assert.That(externalNode, Is.Null);
        }

        /// <summary>
        /// When complex-type factory loading is disabled, reload compatibility must fall
        /// back to the published address-space definition and reject an incompatible
        /// replacement without disturbing the live generation.
        /// </summary>
        [Test]
        public async Task ReloadRuntimeNodeSetAsyncRejectsIncompatibleDataTypeWhenComplexTypeLoadingDisabledAsync()
        {
            m_server.LoadComplexTypes = false;

            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateComplexTypeOptions(incompatibleDefinition: false))
                .ConfigureAwait(false);
            Guid originalId = original.Id;
            long originalGeneration = original.Generation;
            IAsyncNodeManager originalNodeManager = original.NodeManager;

            int namespaceIndex = server.NamespaceUris.GetIndex(
                RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0));
            ushort ns = (ushort)namespaceIndex;
            var testPointId = new NodeId(RuntimeNodeSetTestServer.TestPointDataType, ns);
            var binaryEncodingId = new NodeId(
                RuntimeNodeSetTestServer.TestPointBinaryEncoding,
                ns);
            var pointValueId = new NodeId(RuntimeNodeSetTestServer.PointValueVariable, ns);
            var expandedTestPointId = NodeId.ToExpandedNodeId(
                testPointId,
                server.NamespaceUris);

            Assert.That(
                server.Factory.TryGetEncodeableType(expandedTestPointId, out _),
                Is.False,
                "The compatibility check must use the published address-space definition.");

            string expectedMessage =
                $"DataType '{testPointId}' has an incompatible definition. " +
                "Runtime DataType definitions are immutable for the server lifetime.";
            await Assert.ThatAsync(
                () => m_server.NodeManagerLifecycle
                    .ReloadRuntimeNodeSetAsync(
                        original,
                        CreateComplexTypeOptions(incompatibleDefinition: true))
                    .AsTask(),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.EqualTo(expectedMessage)).ConfigureAwait(false);

            ArrayOf<NodeManagerRegistration> registrations =
                m_server.NodeManagerLifecycle.Registrations;
            Assert.That(registrations, Has.Count.EqualTo(1));
            NodeManagerRegistration current = registrations[0];
            Assert.That(current, Is.SameAs(original));
            Assert.That(current.Id, Is.EqualTo(originalId));
            Assert.That(current.Generation, Is.EqualTo(originalGeneration));
            Assert.That(current.NodeManager, Is.SameAs(originalNodeManager));

            IReadOnlyList<IAsyncNodeManager> managers = master.AsyncNodeManagers;
            Assert.That(
                managers.Count(manager => ReferenceEquals(manager, originalNodeManager)),
                Is.EqualTo(1));
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                master.NamespaceManagers;
            Assert.That(routes.ContainsKey(namespaceIndex), Is.True);
            Assert.That(routes[namespaceIndex], Has.Count.EqualTo(1));
            Assert.That(routes[namespaceIndex][0], Is.SameAs(originalNodeManager));

            NodeState testPoint = await server.NodeManager
                .FindNodeInAddressSpaceAsync(testPointId)
                .ConfigureAwait(false);
            NodeState binaryEncoding = await server.NodeManager
                .FindNodeInAddressSpaceAsync(binaryEncodingId)
                .ConfigureAwait(false);
            NodeState pointValue = await server.NodeManager
                .FindNodeInAddressSpaceAsync(pointValueId)
                .ConfigureAwait(false);
            Assert.That(testPoint, Is.Not.Null);
            Assert.That(binaryEncoding, Is.Not.Null);
            Assert.That(pointValue, Is.Not.Null);

            DataValue testPointBrowseName = await ReadValueAsync(
                testPointId,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(testPointBrowseName.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                testPointBrowseName.WrappedValue.GetQualifiedName(),
                Is.EqualTo(new QualifiedName("TestPoint", ns)));

            DataValue pointValueBrowseName = await ReadValueAsync(
                pointValueId,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(pointValueBrowseName.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(
                pointValueBrowseName.WrappedValue.GetQualifiedName(),
                Is.EqualTo(new QualifiedName("PointValue", ns)));

            Assert.That(
                server.Factory.TryGetEncodeableType(expandedTestPointId, out _),
                Is.False);
        }

        /// <summary>
        /// Reloading a compatible complex-type NodeSet must rebuild the active generation's
        /// subtype and encoding indexes after the retired generation removes its types.
        /// </summary>
        [Test]
        public async Task ReloadRuntimeNodeSetAsyncRestoresTypeTreeAfterOldGenerationRetiresAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateComplexTypeOptions(incompatibleDefinition: false))
                .ConfigureAwait(false);

            int namespaceIndex = server.NamespaceUris.GetIndex(
                RuntimeNodeSetTestServer.NamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThan(0));
            ushort ns = (ushort)namespaceIndex;
            var testPointId = new NodeId(RuntimeNodeSetTestServer.TestPointDataType, ns);
            var binaryEncodingId = new NodeId(
                RuntimeNodeSetTestServer.TestPointBinaryEncoding,
                ns);
            var expandedTestPointId = NodeId.ToExpandedNodeId(
                testPointId,
                server.NamespaceUris);
            var expandedBinaryEncodingId = NodeId.ToExpandedNodeId(
                binaryEncodingId,
                server.NamespaceUris);
            IAsyncNodeManager originalNodeManager = original.NodeManager;
            NodeState originalTestPoint = await server.NodeManager
                .FindNodeInAddressSpaceAsync(testPointId)
                .ConfigureAwait(false);
            Assert.That(originalTestPoint, Is.InstanceOf<DataTypeState>());
            Assert.That(
                server.TypeTree.FindSuperType(testPointId),
                Is.EqualTo(DataTypeIds.Structure));
            Assert.That(
                server.TypeTree.FindSubTypes(DataTypeIds.Structure).ToList(),
                Does.Contain(testPointId));
            Assert.That(server.TypeTree.IsEncodingOf(binaryEncodingId, testPointId), Is.True);
            Assert.That(
                server.TypeTree.FindDataTypeId(binaryEncodingId),
                Is.EqualTo(testPointId));
            Assert.That(
                server.Factory.TryGetEncodeableType(expandedTestPointId, out _),
                Is.True);
            Assert.That(
                server.Factory.TryGetEncodeableType(expandedBinaryEncodingId, out _),
                Is.True);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(
                    original,
                    CreateComplexTypeOptions(incompatibleDefinition: false))
                .ConfigureAwait(false);

            Assert.That(reloaded.Id, Is.EqualTo(original.Id));
            Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));
            Assert.That(reloaded.NodeManager, Is.Not.SameAs(originalNodeManager));

            IReadOnlyList<IAsyncNodeManager> managers = master.AsyncNodeManagers;
            Assert.That(
                managers.Count(manager => ReferenceEquals(manager, originalNodeManager)),
                Is.Zero);
            Assert.That(
                managers.Count(manager => ReferenceEquals(manager, reloaded.NodeManager)),
                Is.EqualTo(1));

            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routes =
                master.NamespaceManagers;
            Assert.That(routes.ContainsKey(namespaceIndex), Is.True);
            Assert.That(routes[namespaceIndex], Has.Count.EqualTo(1));
            Assert.That(routes[namespaceIndex][0], Is.SameAs(reloaded.NodeManager));

            NodeState reloadedTestPoint = await server.NodeManager
                .FindNodeInAddressSpaceAsync(testPointId)
                .ConfigureAwait(false);
            Assert.That(reloadedTestPoint, Is.InstanceOf<DataTypeState>());
            Assert.That(reloadedTestPoint, Is.Not.SameAs(originalTestPoint));

            Assert.That(
                server.TypeTree.FindSuperType(testPointId),
                Is.EqualTo(DataTypeIds.Structure));
            Assert.That(
                server.TypeTree.FindSubTypes(DataTypeIds.Structure).ToList(),
                Does.Contain(testPointId));
            Assert.That(server.TypeTree.IsEncodingOf(binaryEncodingId, testPointId), Is.True);
            Assert.That(
                server.TypeTree.FindDataTypeId(binaryEncodingId),
                Is.EqualTo(testPointId));

            Assert.That(
                server.Factory.TryGetEncodeableType(expandedTestPointId, out _),
                Is.True);
            Assert.That(
                server.Factory.TryGetEncodeableType(expandedBinaryEncodingId, out _),
                Is.True);
        }

        /// <summary>
        /// Reloading a live registration must replace the generation and the NodeManager
        /// instance while preserving the registration <c>Id</c> and the model namespace index;
        /// only the replacement's nodes must be reachable afterwards.
        /// </summary>
        [Test]
        public async Task ReloadRuntimeNodeSetAsyncReplacesGenerationAndPreservesNamespaceIndexAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;

            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);

            int namespaceIndexBefore = server.NamespaceUris.GetIndex(kModelNamespaceUri);
            int namespaceCountBefore = server.NamespaceUris.Count;
            uint urisVersionBefore = await ReadUrisVersionAsync().ConfigureAwait(false);
            string[] namespaceArrayBefore = await ReadNamespaceArrayAsync().ConfigureAwait(false);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(original, CreateOptions(generation: 2))
                .ConfigureAwait(false);

            Assert.That(reloaded.Id, Is.EqualTo(original.Id));
            Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));
            Assert.That(reloaded.Generation, Is.EqualTo(2));
            Assert.That(reloaded.NodeManager, Is.InstanceOf<RuntimeNodeSetNodeManager>());
            Assert.That(ReferenceEquals(reloaded.NodeManager, original.NodeManager), Is.False);

            IReadOnlyList<IAsyncNodeManager> managersAfter = master.AsyncNodeManagers;
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routesAfter =
                master.NamespaceManagers;

            Assert.That(
                managersAfter.Count(m => ReferenceEquals(m, original.NodeManager)),
                Is.Zero,
                "The original generation's NodeManager must no longer be registered.");
            Assert.That(
                managersAfter.Count(m => ReferenceEquals(m, reloaded.NodeManager)),
                Is.EqualTo(1));

            Assert.That(routesAfter.ContainsKey(namespaceIndexBefore), Is.True);
            Assert.That(routesAfter[namespaceIndexBefore], Has.Count.EqualTo(1));
            Assert.That(
                ReferenceEquals(routesAfter[namespaceIndexBefore][0], reloaded.NodeManager),
                Is.True);

            // Same-URI reload must not change the namespace index, count, or UrisVersion.
            int namespaceIndexAfter = server.NamespaceUris.GetIndex(kModelNamespaceUri);
            Assert.That(namespaceIndexAfter, Is.EqualTo(namespaceIndexBefore));
            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountBefore));
            uint urisVersionAfter = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfter, Is.EqualTo(urisVersionBefore));
            string[] namespaceArrayAfter = await ReadNamespaceArrayAsync().ConfigureAwait(false);
            Assert.That(namespaceArrayAfter, Is.EqualTo(namespaceArrayBefore));

            ushort ns = (ushort)namespaceIndexAfter;
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var replacementOnlyNodeId = new NodeId(kReplacementOnlyNodeId, ns);
            var originalOnlyNodeId = new NodeId(kOriginalOnlyNodeId, ns);

            DataValue valueAfterReload = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(valueAfterReload.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(valueAfterReload.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

            NodeState replacementNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(replacementOnlyNodeId)
                .ConfigureAwait(false);
            Assert.That(replacementNode, Is.Not.Null);
            Assert.That(replacementNode.BrowseName.Name, Is.EqualTo(kReplacementOnlyBrowseName));
            DataValue replacementValue = await ReadValueAsync(replacementOnlyNodeId).ConfigureAwait(false);
            Assert.That(replacementValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(replacementValue.WrappedValue.GetInt32(), Is.EqualTo(kReplacementOnlyValue));

            NodeState originalOnlyNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(originalOnlyNodeId)
                .ConfigureAwait(false);
            Assert.That(originalOnlyNode, Is.Null);
            DataValue originalOnlyValue = await ReadValueAsync(originalOnlyNodeId).ConfigureAwait(false);
            Assert.That(originalOnlyValue.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Unlike Reload, ShadowReload must succeed while the current generation owns an
        /// active reporting monitored item. New Read and Browse requests must be
        /// atomically routed to the replacement generation as soon as the switch is
        /// committed, while the monitored item created before the switch keeps being
        /// serviced by the retired (but not yet destroyed) current generation, including
        /// for a value pushed directly on that retired generation's own node after the
        /// switch. Once the owning subscription is deleted, a later lifecycle operation
        /// completes retired-generation cleanup and disposes the old generation's address
        /// space.
        /// </summary>
        [Test]
        public async Task ShadowReloadRuntimeNodeSetAsyncRoutesNewRequestsToReplacementWhileOldMonitoredItemStaysActiveAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;

            NodeManagerRegistration original = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);

            ushort ns = (ushort)server.NamespaceUris.GetIndex(kModelNamespaceUri);
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var valueNodeId = new NodeId(kValueNodeId, ns);
            var replacementOnlyNodeId = new NodeId(kReplacementOnlyNodeId, ns);
            var originalManager = (AsyncCustomNodeManager)original.NodeManager;
            const uint clientHandle = 1;

            var services = new ServerTestServices(m_server, m_secureChannelContext);
            uint subscriptionId = await CreateSubscriptionWithMonitoredItemAsync(services, valueNodeId)
                .ConfigureAwait(false);

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
                    .ShadowReloadRuntimeNodeSetAsync(original, CreateOptions(generation: 2))
                    .ConfigureAwait(false);

                Assert.That(reloaded.Id, Is.EqualTo(original.Id));
                Assert.That(reloaded.Generation, Is.EqualTo(original.Generation + 1));
                Assert.That(ReferenceEquals(reloaded.NodeManager, original.NodeManager), Is.False);

                // New requests must be atomically routed to the replacement generation.
                Assert.That(
                    master.NamespaceManagers[ns].Count(m => ReferenceEquals(m, reloaded.NodeManager)),
                    Is.EqualTo(1));
                Assert.That(
                    master.NamespaceManagers[ns].Any(m => ReferenceEquals(m, original.NodeManager)),
                    Is.False);

                DataValue valueAfterSwitch = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
                Assert.That(valueAfterSwitch.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(valueAfterSwitch.WrappedValue.GetInt32(), Is.EqualTo(kGeneration2Value));

                BrowseResponse rootBrowse = await BrowseAsync(rootNodeId).ConfigureAwait(false);
                Assert.That(rootBrowse.Results.Count, Is.EqualTo(1));
                ArrayOf<ReferenceDescription> rootReferences = rootBrowse.Results[0].References;
                Assert.That(
                    rootReferences.Contains(
                        r => r.BrowseName.Equals(new QualifiedName(kReplacementOnlyBrowseName, ns))),
                    Is.True);
                Assert.That(
                    rootReferences.Contains(
                        r => r.BrowseName.Equals(new QualifiedName(kOriginalOnlyBrowseName, ns))),
                    Is.False);
                NodeState replacementNode = await server.NodeManager
                    .FindNodeInAddressSpaceAsync(replacementOnlyNodeId)
                    .ConfigureAwait(false);
                Assert.That(replacementNode, Is.Not.Null);

                // The retired generation's own node must still be present and unaffected.
                var originalValueState = (BaseVariableState)originalManager.Find(valueNodeId)!;
                Assert.That(originalValueState, Is.Not.Null);
                Assert.That(originalValueState.Value, Is.EqualTo(kGeneration1Value));

                // Simulate an internal (device-driven) value push directly on the retired
                // generation's own node: it must still reach the existing monitored item.
                const int pushedValue = 888;
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
            // own address space is torn down without ever deleting the client's
            // (already independently deleted) subscription itself.
            NodeManagerRegistration current = m_server.NodeManagerLifecycle.Registrations
                .Find(r => r.Id == original.Id);
            Assert.That(current, Is.Not.Null);
            await m_server.NodeManagerLifecycle.RemoveAsync(current).ConfigureAwait(false);

            Assert.That(originalManager.Find(valueNodeId), Is.Null);
            Assert.That(
                CountMatches(m_server.NodeManagerLifecycle.Registrations, r => r.Id == original.Id),
                Is.Zero);
        }

        /// <summary>
        /// Removing a live registration must unroute its NodeManager and unregister it,
        /// leaving its nodes unreachable through direct lookup, browse, read, and translate,
        /// while the model namespace URI stays permanently in the NamespaceArray.
        /// </summary>
        [Test]
        public async Task RemoveAsyncUnroutesNodesAndRetainsNamespaceUriAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;

            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddRuntimeNodeSetAsync(CreateOptions(generation: 1))
                .ConfigureAwait(false);

            int namespaceIndex = server.NamespaceUris.GetIndex(kModelNamespaceUri);
            int namespaceCountAfterAdd = server.NamespaceUris.Count;
            uint urisVersionAfterAdd = await ReadUrisVersionAsync().ConfigureAwait(false);
            string[] namespaceArrayAfterAdd = await ReadNamespaceArrayAsync().ConfigureAwait(false);

            ushort ns = (ushort)namespaceIndex;
            var rootNodeId = new NodeId(kRootNodeId, ns);
            var valueNodeId = new NodeId(kValueNodeId, ns);

            await m_server.NodeManagerLifecycle.RemoveAsync(registration).ConfigureAwait(false);

            // Registration and routing must be absent.
            ArrayOf<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations;
            Assert.That(CountMatches(registrations, r => r.Id == registration.Id), Is.Zero);

            IReadOnlyList<IAsyncNodeManager> managersAfterRemove = master.AsyncNodeManagers;
            IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> routesAfterRemove =
                master.NamespaceManagers;
            Assert.That(
                managersAfterRemove.Count(m => ReferenceEquals(m, registration.NodeManager)),
                Is.Zero);
            Assert.That(routesAfterRemove.ContainsKey(namespaceIndex), Is.False);

            // Direct lookup must no longer find the removed nodes.
            NodeState rootNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(rootNodeId)
                .ConfigureAwait(false);
            NodeState valueNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(valueNodeId)
                .ConfigureAwait(false);
            Assert.That(rootNode, Is.Null);
            Assert.That(valueNode, Is.Null);

            // Browse of the removed root NodeId itself must fail with BadNodeIdUnknown.
            BrowseResponse rootBrowse = await BrowseAsync(rootNodeId).ConfigureAwait(false);
            Assert.That(rootBrowse.Results.Count, Is.EqualTo(1));
            Assert.That(rootBrowse.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

            // The root must no longer be reachable from the Objects folder.
            BrowseResponse objectsBrowse = await BrowseAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            Assert.That(objectsBrowse.Results.Count, Is.EqualTo(1));
            ArrayOf<ReferenceDescription> objectsReferences = objectsBrowse.Results[0].References;
            Assert.That(
                objectsReferences.Contains(r => r.BrowseName.Equals(new QualifiedName(kRootBrowseName, ns))),
                Is.False);

            // Read must fail with BadNodeIdUnknown.
            DataValue valueAfterRemove = await ReadValueAsync(valueNodeId).ConfigureAwait(false);
            Assert.That(valueAfterRemove.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));

            // Translate must no longer resolve the browse path.
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var relativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(kRootBrowseName, ns)
                    },
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(kValueBrowseName, ns)
                    }
                ]
            };
            ArrayOf<BrowsePath> browsePaths =
            [
                new BrowsePath { StartingNode = ObjectIds.ObjectsFolder, RelativePath = relativePath }
            ];

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            TranslateBrowsePathsToNodeIdsResponse translateResponse = await services
                .TranslateBrowsePathsToNodeIdsAsync(requestHeader, browsePaths)
                .ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(
                translateResponse.ResponseHeader,
                translateResponse.Results,
                browsePaths);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                translateResponse.DiagnosticInfos,
                browsePaths,
                translateResponse.ResponseHeader.StringTable,
                m_logger);
            Assert.That(translateResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(translateResponse.Results[0].StatusCode), Is.True);

            // The model namespace URI must remain permanently in the NamespaceArray, and
            // UrisVersion must remain at its post-add value: removal never rewrites the
            // namespace table.
            Assert.That(server.NamespaceUris.GetIndex(kModelNamespaceUri), Is.EqualTo(namespaceIndex));
            Assert.That(server.NamespaceUris.Count, Is.EqualTo(namespaceCountAfterAdd));
            uint urisVersionAfterRemove = await ReadUrisVersionAsync().ConfigureAwait(false);
            Assert.That(urisVersionAfterRemove, Is.EqualTo(urisVersionAfterAdd));
            string[] namespaceArrayAfterRemove = await ReadNamespaceArrayAsync().ConfigureAwait(false);
            Assert.That(namespaceArrayAfterRemove, Is.EqualTo(namespaceArrayAfterAdd));
            Assert.That(namespaceArrayAfterRemove, Does.Contain(kModelNamespaceUri));
        }

        private static RuntimeNodeSetOptions CreateRawOptions(
            string sourceName,
            string modelNamespaceUri,
            string xml)
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        sourceName,
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [modelNamespaceUri])
                ],
                DefaultNamespaceUri = modelNamespaceUri
            };
        }

        private static string BuildDuplicateNodeSetXml(uint duplicateNodeId)
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{kModelNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kModelNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={duplicateNodeId}" BrowseName="1:DuplicateOne">
                    <DisplayName>DuplicateOne</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                  <UAObject NodeId="ns=1;i={duplicateNodeId}" BrowseName="1:DuplicateTwo">
                    <DisplayName>DuplicateTwo</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;
        }

        private static string BuildUnownedNamespaceNodeSetXml(
            string externalNamespaceUri,
            uint externalNodeId)
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{kModelNamespaceUri}</Uri>
                    <Uri>{externalNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{kModelNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=2;i={externalNodeId}" BrowseName="2:ExternalNode">
                    <DisplayName>ExternalNode</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;
        }

        /// <summary>
        /// Builds the <see cref="RuntimeNodeSetOptions"/> for a single in-memory source of
        /// the given generation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Independently confirmed production defect:</b> a standard single-level
        /// NodeSet2 <c>&lt;Value&gt;&lt;uax:Int32&gt;N&lt;/uax:Int32&gt;&lt;/Value&gt;</c>
        /// payload is imported as the CLR default (<c>0</c>) instead of <c>N</c> by
        /// <c>UANodeSetHelpers.Import</c> / the underlying <c>XmlDecoder</c> path. The XML
        /// below therefore carries no <c>&lt;Value&gt;</c> element for its scalar
        /// variables (leaving one in place would misleadingly imply it is honored), and the
        /// <see cref="RuntimeNodeSetOptions.Configure"/> callback seeds the concrete
        /// generation-specific <c>Int32</c> values through the supported post-import fluent
        /// <see cref="Server.Fluent.INodeManagerBuilder.Variable{TValue}(string)"/>
        /// hook instead. This keeps the Read-service assertions proving real, concrete,
        /// generation-specific values (1 then 2) end-to-end without weakening them, while
        /// routing around the defect at the test-authoring level. The defect itself is a
        /// pre-existing production issue and should be reported/fixed separately.
        /// </para>
        /// </remarks>
        private static RuntimeNodeSetOptions CreateOptions(int generation)
        {
            string xml = BuildNodeSetXml(generation);

            (string childBrowseName, int childValue) = generation == 1
                ? (kOriginalOnlyBrowseName, kOriginalOnlyValue)
                : (kReplacementOnlyBrowseName, kReplacementOnlyValue);
            int lifecycleValue = generation == 1 ? kGeneration1Value : kGeneration2Value;

            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        $"RuntimeNodeSetLifecycleTests-gen{generation}",
                        _ => new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [kModelNamespaceUri])
                ],
                Configure = builder =>
                {
                    // See the defect note above: seed the concrete, generation-specific
                    // Int32 values here rather than relying on the NodeSet2 <Value> import,
                    // which is defective for this scalar shape. Assigning through the
                    // resolved node's Value property (rather than an OnRead override) also
                    // establishes the Good status code a freshly imported, value-less
                    // variable otherwise lacks (BadWaitingForInitialData).
                    builder.Variable<int>($"{kRootBrowseName}/{kValueBrowseName}").Node.Value =
                        lifecycleValue;
                    builder.Variable<int>($"{kRootBrowseName}/{childBrowseName}").Node.Value =
                        childValue;
                }
            };
        }

        /// <summary>
        /// Creates runtime options backed by an in-memory copy of the embedded complex-type
        /// NodeSet, optionally changing the TestPoint definition incompatibly.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the embedded NodeSet does not contain the expected compatible field.
        /// </exception>
        private static RuntimeNodeSetOptions CreateComplexTypeOptions(
            bool incompatibleDefinition)
        {
            string xml;
            using (Stream stream = RuntimeNodeSetTestServer.OpenTestStream())
            using (var reader = new StreamReader(stream))
            {
                xml = reader.ReadToEnd();
            }

            if (incompatibleDefinition)
            {
                const string compatibleField =
                    "<Field Name=\"Name\" DataType=\"String\" />";
                const string incompatibleField =
                    "<Field Name=\"Name\" DataType=\"Int32\" />";
                int fieldIndex = xml.IndexOf(compatibleField, StringComparison.Ordinal);
                if (fieldIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The embedded TestPoint Name field was not found.");
                }
                xml = xml
                    .Remove(fieldIndex, compatibleField.Length)
                    .Insert(fieldIndex, incompatibleField);
            }

            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "ServerComplexTypesTestModel",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                        [RuntimeNodeSetTestServer.NamespaceUri])
                ],
                DefaultNamespaceUri = RuntimeNodeSetTestServer.NamespaceUri
            };
        }

        /// <summary>
        /// Builds a synthetic NodeSet2 document with a root object organized under Objects,
        /// a readable <c>LifecycleValue</c> Int32 variable, and a generation-specific extra
        /// variable (<c>OriginalOnly</c> in generation 1, <c>ReplacementOnly</c> in
        /// generation 2). Neither scalar variable carries a <c>&lt;Value&gt;</c> element;
        /// see the defect note on <see cref="CreateOptions"/> for why their concrete values
        /// are instead wired through the fluent <c>Configure</c> callback.
        /// </summary>
        private static string BuildNodeSetXml(int generation)
        {
            (uint childNodeId, string childBrowseName) = generation == 1
                ? (kOriginalOnlyNodeId, kOriginalOnlyBrowseName)
                : (kReplacementOnlyNodeId, kReplacementOnlyBrowseName);

            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
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
                      <Reference ReferenceType="i=35">ns=1;i={kValueNodeId}</Reference>
                      <Reference ReferenceType="i=35">ns=1;i={childNodeId}</Reference>
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
                  <UAVariable NodeId="ns=1;i={childNodeId}" BrowseName="1:{childBrowseName}"
                              ParentNodeId="ns=1;i={kRootNodeId}" DataType="i=6" ValueRank="-1"
                              AccessLevel="3" UserAccessLevel="3">
                    <DisplayName>{childBrowseName}</DisplayName>
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
        /// Reads the Server object's <c>NamespaceArray</c> variable.
        /// </summary>
        private async Task<string[]> ReadNamespaceArrayAsync()
        {
            DataValue value = await ReadValueAsync(VariableIds.Server_NamespaceArray).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            return value.WrappedValue.GetStringArray().ToArray();
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
        /// Browses a single node with the standard hierarchical-references template used
        /// throughout this fixture.
        /// </summary>
        private async Task<BrowseResponse> BrowseAsync(NodeId nodeId)
        {
            var services = new ServerTestServices(m_server, m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId([nodeId], template);

            RequestHeader requestHeader = m_requestHeader;
            requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await services
                .BrowseAsync(requestHeader, view: null, requestedMaxReferencesPerNode: 0, nodesToBrowse)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(response.ResponseHeader, response.Results, nodesToBrowse);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToBrowse,
                response.ResponseHeader.StringTable,
                m_logger);

            return response;
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
    }
}
